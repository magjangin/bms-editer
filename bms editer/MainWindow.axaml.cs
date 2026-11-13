using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using bms_editer.ViewModels;
using bms_editer.Views;

namespace bms_editer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;
        vm.ConfirmAsync = message => ConfirmWindow.ShowAsync(this, message, "확인");
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.ScrollToRatioRequested += ScrollEditorToRatio;
        vm.PlaybackFailed += message => _ = ConfirmWindow.ShowMessageAsync(this, message, "재생 실패");
        Waveform.ScrubRequested += OnWaveformScrubRequested;
        Closing += OnWindowClosing;
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
        AddHandler(PointerPressedEvent, OnWindowPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        UpdateEditorOrientation();
        UpdateWindowTitle();
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        if (files is null || files.Length == 0)
            return;

        var first = files[0]!;
        if (Directory.Exists(first))
        {
            await LoadFolderMediaAsync(vm, first);
            return;
        }

        if (HasExtension(first, BmsExtensions))
        {
            if (!await vm.ConfirmDiscardIfNeededAsync("현재 작업 중인 내용이 모두 사라집니다.\n새 파일을 열까요?"))
                return;

            if (vm.LoadBms(first))
            {
                RememberFolder(first);
                var dir = Path.GetDirectoryName(first);
                if (!string.IsNullOrEmpty(dir))
                    await LoadCompanionMediaAsync(vm, dir);
            }
            else
            {
                await ConfirmWindow.ShowMessageAsync(this, $"차트를 열지 못했습니다.\n\n{vm.LastErrorMessage}", "열기 실패");
            }
        }
        else if (HasExtension(first, OggExtensions))
        {
            RememberFolder(first);
            if (await vm.LoadOggAsync(first) == OggLoadResult.Failed)
                await ConfirmWindow.ShowMessageAsync(this, $"OGG를 불러오지 못했습니다.\n\n{vm.LastErrorMessage}", "OGG 로드 실패");
        }
        else if (HasExtension(first, VideoExtensions))
        {
            RememberFolder(first);
            vm.LoadVideo(first);
        }
    }

    // 창을 닫을 때 저장 안 한 작업이 사라지지 않게 막는다.
    //
    // 새로 만들기·열기에는 확인창이 있었는데 정작 창 닫기에는 없었다.
    // Closing 은 await 할 수 없으므로, 일단 닫기를 취소하고 물어본 뒤
    // 사용자가 정말 닫겠다고 하면 그때 다시 Close() 한다.
    private bool _isClosingConfirmed;

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosingConfirmed || DataContext is not MainWindowViewModel vm || !vm.IsDirty)
            return;

        e.Cancel = true;

        var name = vm.CurrentFilePath is { } path ? Path.GetFileName(path) : "제목 없음";
        var choice = await ConfirmWindow.ShowThreeWayAsync(
            this,
            $"'{name}' 의 바뀐 내용을 저장하지 않았습니다.\n\n저장하고 닫을까요?",
            confirmText: "저장하고 닫기",
            alternateText: "저장 안 함",
            title: "종료");

        switch (choice)
        {
            case ConfirmChoice.Cancel:
                return;

            case ConfirmChoice.Confirm:
                var savePath = vm.CurrentFilePath ?? await PickSavePathAsync(vm);
                if (savePath is null)
                    return;

                if (!vm.SaveBms(savePath))
                {
                    await ConfirmWindow.ShowMessageAsync(
                        this, $"저장하지 못했습니다. 창을 닫지 않았습니다.\n\n{vm.LastErrorMessage}", "저장 실패");
                    return;
                }

                // 저장은 됐지만 알릴 것이 있으면(인코딩을 UTF-8 로 물렸다 등) 닫기 전에 보여준다.
                // 예전에는 이 경로에서만 경고가 조용히 사라졌다.
                if (vm.LastWarningMessage is { } warning)
                    await ConfirmWindow.ShowMessageAsync(this, warning, "저장 완료");
                break;
        }

        _isClosingConfirmed = true;
        Close();
    }

    // 재생 커서를 화면 프레임마다 옮긴다.
    //
    // 타이머만 쓰면 실측 30ms(33Hz) 간격이라 고주사율 화면에서는 프레임 대부분에서 커서가 멈춰 있었다.
    private bool _isPlaybackFrameLoopRunning;

    private void StartPlaybackFrameLoop()
    {
        if (_isPlaybackFrameLoopRunning)
            return;

        _isPlaybackFrameLoopRunning = true;
        RequestAnimationFrame(OnPlaybackFrame);
    }

    private void OnPlaybackFrame(TimeSpan _)
    {
        if (DataContext is MainWindowViewModel { IsPlaying: true } vm)
        {
            vm.RefreshPlaybackPosition();
            RequestAnimationFrame(OnPlaybackFrame);
            return;
        }

        _isPlaybackFrameLoopRunning = false;
    }

    private void UpdateWindowTitle()
    {
        if (DataContext is MainWindowViewModel vm)
            Title = vm.WindowTitle;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.PlaybackPositionSeconds))
        {
            FollowPlaybackCursorIfNeeded();
            if (sender is MainWindowViewModel vm)
                VideoPreview.SyncTo(vm.PlaybackPositionSeconds);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsHorizontalView))
        {
            UpdateEditorOrientation();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.WindowTitle))
        {
            UpdateWindowTitle();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsPlaying))
        {
            if (sender is not MainWindowViewModel vm)
                return;

            if (vm.IsPlaying)
            {
                VideoPreview.PlayFrom(vm.PlaybackPositionSeconds);
                StartPlaybackFrameLoop();
            }
            else
            {
                VideoPreview.PauseAt(vm.PlaybackPositionSeconds);
            }
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.VideoFilePath))
        {
            if (sender is not MainWindowViewModel vm)
                return;

            if (vm.VideoFilePath is { } videoPath)
                VideoPreview.LoadVideo(videoPath);
            else
                VideoPreview.ClearVideo();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.PlaybackSpeed))
        {
            if (sender is MainWindowViewModel vm)
                VideoPreview.SetPlaybackRate(vm.PlaybackSpeed);
        }
    }
}
