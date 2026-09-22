using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using bms_editer.ViewModels;
using bms_editer.Views;

namespace bms_editer;

public partial class MainWindow
{
        private static readonly string[] BmsExtensions = { ".bms", ".bme", ".bml" };
        private static readonly string[] OggExtensions = { ".ogg" };
        private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".avi", ".mkv", ".ogv" };

        private async void OnLoadOggClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "OGG 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("OGG 오디오") { Patterns = new[] { "*.ogg" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            // 실패해도 이미 물려 있던 음원은 그대로 남는다. 사유만 알려준다.
            if (!await vm.LoadOggAsync(path))
                await ConfirmWindow.ShowMessageAsync(this, $"OGG를 불러오지 못했습니다.\n\n{vm.LastErrorMessage}", "OGG 로드 실패");
        }

        private async void OnLoadVideoClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "비디오 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("비디오 파일") { Patterns = new[] { "*.mp4", "*.webm", "*.mov", "*.avi", "*.mkv" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            vm.LoadVideo(path);
        }

        private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "BMS 폴더 선택",
                AllowMultiple = false,
            });

            var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (folderPath is null || !Directory.Exists(folderPath))
                return;

            // 확인은 **고를 것을 다 고른 뒤에** 받는다. 먼저 물으면, 선택을 취소해도
            // 이미 "버리시겠습니까"에 답한 뒤라 사용자만 헷갈린다.
            if (!await vm.ConfirmDiscardIfNeededAsync("현재 작업 중인 내용이 모두 사라집니다.\n폴더를 열까요?"))
                return;

            await LoadFolderMediaAsync(vm, folderPath);
        }

        private void OnClearVideoClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.ClearVideo();
        }

        private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "BMS 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("BMS 차트 파일") { Patterns = new[] { "*.bms", "*.bme", "*.bml" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            // 확인은 열 파일을 고른 뒤에 받는다. (폴더 열기와 같은 이유)
            if (!await vm.ConfirmDiscardIfNeededAsync("현재 작업 중인 내용이 모두 사라집니다.\n파일을 열까요?"))
                return;

            if (!vm.LoadBms(path))
                await ConfirmWindow.ShowMessageAsync(this, $"파일을 열지 못했습니다.\n\n{vm.LastErrorMessage}", "열기 실패");
        }

        private async void OnSaveFileClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var path = vm.CurrentFilePath ?? await PickSavePathAsync(vm);
            if (path is null)
                return;

            await SaveAndReportAsync(vm, path);
        }

        private async void OnSaveFileAsClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var path = await PickSavePathAsync(vm);
            if (path is null)
                return;

            await SaveAndReportAsync(vm, path);
        }

        // 저장은 조용히 실패하면 안 된다. 실패하면 사유까지 보여준다.
        // 저장은 됐지만 알려야 할 것(인코딩을 UTF-8로 물린 경우)도 여기서 보여준다.
        private async Task SaveAndReportAsync(MainWindowViewModel vm, string path)
        {
            if (!vm.SaveBms(path))
            {
                await ConfirmWindow.ShowMessageAsync(this, $"저장하지 못했습니다.\n\n{vm.LastErrorMessage}", "저장 실패");
                return;
            }

            if (vm.LastWarningMessage is { } warning)
                await ConfirmWindow.ShowMessageAsync(this, warning, "저장 완료");
        }

        private async Task<string?> PickSavePathAsync(MainWindowViewModel vm)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "BMS 파일로 저장",
                DefaultExtension = "bms",
                SuggestedFileName = ToSafeFileName(vm.Title),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("BMS 차트 파일") { Patterns = new[] { "*.bms", "*.bme", "*.bml" } },
                },
            });

            return file?.TryGetLocalPath();
        }

        // 곡 제목을 파일명으로 쓸 수 있게 다듬는다.
        //
        // 예전에는 제목을 그대로 넘겨서, `:` `?` `/` 같은 글자가 든 제목이면
        // 저장 대화상자가 걸렸다. 곡 제목에는 흔한 글자들이다.
        private static string ToSafeFileName(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "chart";

            var safe = new string(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();

            // 전부 걸러졌거나 점만 남은 경우.
            return safe.Trim('.').Length == 0 ? "chart" : safe;
        }

        private async Task LoadFolderMediaAsync(MainWindowViewModel vm, string folderPath)
        {
            var bmsPath = FindBestFile(folderPath, BmsExtensions);

            // 차트가 없는 폴더를 고르면 아무것도 건드리지 않는다.
            //
            // 예전에는 차트만 건너뛰고 음원·영상을 얹었다. 사용자는 새로 열렸다고 생각하는데
            // 화면의 차트는 옛것이고, CurrentFilePath 도 옛 파일을 가리켜서
            // 그대로 [저장]을 누르면 방금 연 폴더가 아니라 옛 차트가 덮어써졌다.
            if (bmsPath is null)
            {
                await ConfirmWindow.ShowMessageAsync(
                    this,
                    $"이 폴더에 BMS 차트가 없습니다.\n\n{folderPath}\n\n" +
                    "지금 작업 중인 차트를 그대로 두었습니다. 음원·영상도 바꾸지 않았습니다.",
                    "폴더 열기");
                return;
            }

            if (!vm.LoadBms(bmsPath))
            {
                await ConfirmWindow.ShowMessageAsync(this, $"차트를 열지 못했습니다.\n\n{vm.LastErrorMessage}", "열기 실패");
                return;
            }

            var oggPath = FindBestFile(folderPath, OggExtensions);
            if (oggPath is not null && !await vm.LoadOggAsync(oggPath))
                await ConfirmWindow.ShowMessageAsync(this, $"OGG를 불러오지 못했습니다.\n\n{vm.LastErrorMessage}", "OGG 로드 실패");

            var videoPath = FindBestFile(folderPath, VideoExtensions);
            if (videoPath is not null)
                vm.LoadVideo(videoPath);
            else
                vm.ClearVideo();
        }

        private static string? FindBestFile(string folderPath, IReadOnlyCollection<string> extensions)
        {
            var folderName = new DirectoryInfo(folderPath).Name;
            var candidates = Directory.EnumerateFiles(folderPath)
                .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
                return null;

            return candidates.FirstOrDefault(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), folderName, StringComparison.OrdinalIgnoreCase))
                ?? candidates[0];
        }

        private async void OnAddWavClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "WAV 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("WAV 오디오") { Patterns = new[] { "*.wav" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            // 예전에는 실패해도 아무 표시가 없어서, 키음이 한도를 넘으면 조용히 아무 일도 안 났다.
            if (!vm.AddWav(path))
                await ConfirmWindow.ShowMessageAsync(this, $"키음을 추가하지 못했습니다.\n\n{vm.LastErrorMessage}", "키음 추가 실패");
        }
    }
