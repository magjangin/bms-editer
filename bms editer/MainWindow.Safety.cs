using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Threading;
using bms_editer.Services;
using bms_editer.ViewModels;
using bms_editer.Views;

namespace bms_editer;

// 안전장치: 자동 저장 타이머 · 예상하지 못한 예외 · 비정상 종료 뒤 복구. (알려진 문제 S-1·S-3)
//
// 앱이 EnableSafety 로 켜 줄 때만 동작한다. 테스트에서 띄우는 창은 켜지 않으므로
// 사용자의 %LocalAppData% 에 세션·자동 저장 파일을 만들지 않는다.
public partial class MainWindow
{
        public static readonly TimeSpan AutoSaveInterval = TimeSpan.FromMinutes(2);

        private DocumentSnapshotStore? _snapshots;
        private DispatcherTimer? _autoSaveTimer;
        private bool _isShowingErrorDialog;

        public void EnableSafety(DocumentSnapshotStore snapshots)
        {
            _snapshots = snapshots;
            if (DataContext is MainWindowViewModel vm)
                vm.Snapshots = snapshots;

            try
            {
                snapshots.BeginSession();
                snapshots.PruneOldDocuments();
            }
            catch (Exception ex)
            {
                Log(ex, "세션 시작");
            }

            _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveInterval };
            _autoSaveTimer.Tick += (_, _) => (DataContext as MainWindowViewModel)?.AutoSave();
            _autoSaveTimer.Start();

            Opened += OnOpenedOfferRecovery;
            Closed += (_, _) =>
            {
                _autoSaveTimer.Stop();

                // 여기까지 왔으면 사용자가 닫기를 확정했다(저장했거나 버리기로 했다). 되살릴 것이 없다.
                try
                {
                    snapshots.EndSession();
                }
                catch (Exception ex)
                {
                    Log(ex, "세션 종료");
                }
            };
        }

        // App 이 UI 스레드에서 아무도 받지 않은 예외를 넘겨준다. 앱을 살려 뒀으면 true.
        //
        // 예전에는 예외 하나에 앱이 그대로 꺼지고 저장 안 한 작업이 통째로 사라졌다.
        // 이제 그 자리에서 비상 저장을 하고, 무슨 일이 있었는지 알린 뒤 계속 쓰게 한다.
        public bool HandleUnexpectedException(Exception exception, string? logPath)
        {
            if (DataContext is not MainWindowViewModel vm)
                return false;

            // 재생 타이머처럼 되풀이되는 곳에서 났을 수 있다. 먼저 끊는다.
            try
            {
                vm.StopPlaybackAtCurrentPosition();
            }
            catch (Exception ex)
            {
                Log(ex, "오류 뒤 재생 정지");
            }

            var snapshot = vm.EmergencySave();

            // 안내 창은 한 번에 하나만. 창이 떠 있는 동안 또 나면 기록만 남긴다.
            if (!_isShowingErrorDialog)
                Dispatcher.UIThread.Post(() => ShowUnexpectedError(exception, snapshot, logPath));

            return true;
        }

        private async void ShowUnexpectedError(Exception exception, SnapshotInfo? snapshot, string? logPath)
        {
            if (_isShowingErrorDialog)
                return;

            _isShowingErrorDialog = true;
            try
            {
                var saved = snapshot is not null
                    ? $"지금까지의 작업을 자동 저장해 두었습니다.\n{snapshot.SnapshotPath}"
                    : "저장 안 된 변경이 없어서 따로 저장하지 않았습니다.";
                var log = logPath is not null ? $"\n\n오류 기록: {logPath}" : string.Empty;

                var choice = await ConfirmWindow.ShowChoiceAsync(
                    this,
                    "예상하지 못한 오류로 방금 하던 동작이 중간에 멈췄습니다.\n\n" +
                    $"{exception.GetType().Name}: {exception.Message}\n\n" +
                    $"{saved}{log}\n\n" +
                    "계속 작업해도 되지만, 화면이 이상하면 저장하고 다시 켜 주세요.",
                    confirmText: "계속 작업",
                    alternateText: "끄기",
                    title: "오류");

                // 저장 안 된 변경이 있으면 늘 쓰던 "저장하고 닫을까요?" 가 뜬다.
                if (choice == ConfirmChoice.Alternate)
                    Close();
            }
            catch (Exception ex)
            {
                Log(ex, "오류 안내 창");
            }
            finally
            {
                _isShowingErrorDialog = false;
            }
        }

        // 지난번 실행이 저장 안 된 작업을 남기고 죽었으면 되살릴지 묻는다.
        private async void OnOpenedOfferRecovery(object? sender, EventArgs e)
        {
            Opened -= OnOpenedOfferRecovery;

            if (_snapshots is not { } snapshots || DataContext is not MainWindowViewModel vm)
                return;

            try
            {
                var abandoned = snapshots.FindAbandonedSessions();
                if (abandoned.Count > 0)
                    await OfferRecoveryAsync(vm, snapshots, abandoned);
            }
            catch (Exception ex)
            {
                Log(ex, "작업 복구");
            }
        }

        private async Task OfferRecoveryAsync(
            MainWindowViewModel vm,
            DocumentSnapshotStore snapshots,
            IReadOnlyList<AbandonedSession> abandoned)
        {
            // 여러 개면 가장 최근 것부터 하나씩. 나머지는 다음에 켤 때 이어서 묻는다.
            var session = abandoned[0];
            var snapshot = session.Snapshot;

            var name = snapshot.OriginalPath is { } original
                ? $"{Path.GetFileName(Path.GetDirectoryName(original))} / {Path.GetFileName(original)}"
                : "제목 없음";
            var more = abandoned.Count > 1
                ? $"\n\n(되살릴 작업이 {abandoned.Count - 1}개 더 있습니다. 다음에 켤 때 이어서 묻습니다.)"
                : string.Empty;

            var choice = await ConfirmWindow.ShowThreeWayAsync(
                this,
                "지난번에 bms editer 가 제대로 닫히지 않았습니다.\n" +
                "저장하지 않은 작업이 자동 저장돼 있습니다.\n\n" +
                $"파일: {name}\n" +
                $"자동 저장 시각: {snapshot.SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n\n" +
                "되살릴까요? 되살린 내용은 [저장]을 눌러야 원래 파일에 들어갑니다.\n\n" +
                "[복구 안 함]을 눌러도 자동 저장본은 자동 저장 폴더에 남습니다.\n" +
                "[취소]를 누르면 다음에 켤 때 다시 묻습니다." + more,
                confirmText: "복구",
                alternateText: "복구 안 함",
                title: "작업 복구");

            if (choice == ConfirmChoice.Cancel)
                return;

            snapshots.DismissSession(session);

            if (choice == ConfirmChoice.Alternate)
                return;

            if (!vm.RecoverFromSnapshot(snapshot))
            {
                await ConfirmWindow.ShowMessageAsync(
                    this,
                    $"자동 저장본을 열지 못했습니다.\n\n{vm.LastErrorMessage}\n\n" +
                    $"파일은 그대로 남아 있습니다. 곡 폴더에 복사해 넣고 직접 열어 보세요.\n{snapshot.SnapshotPath}",
                    "복구 실패");
                return;
            }

            // 되살린 상태를 이번 실행의 자동 저장본으로 곧바로 남긴다. 곧바로 또 죽어도 다시 되살릴 수 있게.
            vm.AutoSave();

            if (snapshot.OriginalPath is { } path && Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder))
                await LoadCompanionMediaAsync(vm, folder);
        }

        // 안전장치가 실패한 기록. 테스트가 켠 저장소면 그 안에 남아 사용자의 기록 폴더를 건드리지 않는다.
        private void Log(Exception exception, string source) =>
            ErrorLog.Write(_snapshots?.LogDirectory ?? AppDataPaths.Logs, exception, source);

        // 자동 저장본·저장 이력·오류 기록이 있는 폴더를 탐색기로 연다.
        private void OnOpenAppDataFolderClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.Root);
                Process.Start(new ProcessStartInfo { FileName = AppDataPaths.Root, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log(ex, "폴더 열기");
            }
        }
}
