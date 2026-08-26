using System.Linq;
using System.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using bms_editer.Models;
using bms_editer.ViewModels;
using bms_editer.Views;
using bms_editer.Views.Controls;

namespace bms_editer;

public partial class MainWindow : Window
{
        private static readonly string[] BmsExtensions = { ".bms", ".bme", ".bml" };
        private static readonly string[] OggExtensions = { ".ogg" };
        private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".avi", ".mkv", ".ogv" };

        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainWindowViewModel();
            DataContext = vm;
            vm.ConfirmDiscardAsync = message => ConfirmWindow.ShowAsync(this, message, "새로 만들기");
            vm.PropertyChanged += OnViewModelPropertyChanged;
            Waveform.ScrubRequested += OnWaveformScrubRequested;
            UpdateEditorOrientation();
        }

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

            vm.LoadOgg(path);
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

            LoadFolderMedia(vm, folderPath);
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

            vm.LoadBms(path);
        }

        private async void OnSaveFileClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var path = vm.CurrentFilePath ?? await PickSavePathAsync(vm);
            if (path is null)
                return;

            vm.SaveBms(path);
        }

        private async void OnSaveFileAsClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var path = await PickSavePathAsync(vm);
            if (path is null)
                return;

            vm.SaveBms(path);
        }

        private async Task<string?> PickSavePathAsync(MainWindowViewModel vm)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "BMS 파일로 저장",
                DefaultExtension = "bms",
                SuggestedFileName = string.IsNullOrWhiteSpace(vm.Title) ? "chart" : vm.Title,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("BMS 차트 파일") { Patterns = new[] { "*.bms", "*.bme", "*.bml" } },
                },
            });

            return file?.TryGetLocalPath();
        }

        private static void LoadFolderMedia(MainWindowViewModel vm, string folderPath)
        {
            var bmsPath = FindBestFile(folderPath, BmsExtensions);
            if (bmsPath is not null)
                vm.LoadBms(bmsPath);

            var oggPath = FindBestFile(folderPath, OggExtensions);
            if (oggPath is not null)
                vm.LoadOgg(oggPath);

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

            vm.AddWav(path);
        }

        private void OnNoteGridKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (e.Key == Key.Delete)
            {
                vm.DeleteSelectedNotesCommand.Execute(null);
                e.Handled = true;
                return;
            }

            NoteMoveDirection? direction = e.Key switch
            {
                Key.Up => vm.IsHorizontalView ? NoteMoveDirection.LanePrevious : NoteMoveDirection.TimeForward,
                Key.Down => vm.IsHorizontalView ? NoteMoveDirection.LaneNext : NoteMoveDirection.TimeBackward,
                Key.Left => vm.IsHorizontalView ? NoteMoveDirection.TimeBackward : NoteMoveDirection.LanePrevious,
                Key.Right => vm.IsHorizontalView ? NoteMoveDirection.TimeForward : NoteMoveDirection.LaneNext,
                _ => null
            };

            if (direction is { } d)
            {
                vm.MoveSelectedNotesCommand.Execute(d);
                e.Handled = true;
            }
        }

        private void OnShowBulkEditClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var bulkEditWindow = new BulkEditWindow(new BulkEditViewModel(vm.SelectedNotes));
            bulkEditWindow.ShowDialog(this);
        }

        private void OnShowStatsClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var statsWindow = new NoteStatsWindow(new NoteStatsViewModel(vm.Chart, vm.WavList));
            statsWindow.ShowDialog(this);
        }

        private void OnWaveformScrubRequested(object? sender, WaveformScrubRequestedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.ScrubToRatio(e.Ratio);
        }

        private void OnEditorSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(EditorSurface);
            if (point.Properties.IsMiddleButtonPressed)
            {
                ScrubFromEditorSurface(e);
            }
            else if (IsNonMiddleMouseButtonPressed(point.Properties))
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.StopPlaybackAtCurrentPosition();
            }
        }

        private void OnEditorSurfacePointerMoved(object? sender, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint(EditorSurface);
            if (point.Properties.IsMiddleButtonPressed)
                ScrubFromEditorSurface(e);
        }

        private void ScrubFromEditorSurface(PointerEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || vm.OggDurationSeconds <= 0)
                return;

            var timelineLength = GetTimelineLength(vm);
            if (timelineLength <= 0)
                return;

            if (vm.IsHorizontalView)
            {
                var x = e.GetPosition(EditorSurface).X;
                var ratio = x / timelineLength;
                vm.ScrubToRatio(ratio);
            }
            else
            {
                var y = e.GetPosition(EditorSurface).Y;
                var ratio = 1.0 - (y / timelineLength);
                vm.ScrubToRatio(ratio);
            }
            e.Handled = true;
        }

        private static bool IsNonMiddleMouseButtonPressed(PointerPointProperties properties) =>
            properties.IsLeftButtonPressed ||
            properties.IsRightButtonPressed ||
            properties.IsXButton1Pressed ||
            properties.IsXButton2Pressed;

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
            else if (e.PropertyName == nameof(MainWindowViewModel.IsPlaying))
            {
                if (sender is not MainWindowViewModel vm)
                    return;

                if (vm.IsPlaying)
                    VideoPreview.PlayFrom(vm.PlaybackPositionSeconds);
                else
                    VideoPreview.PauseAt(vm.PlaybackPositionSeconds);
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
        }

        private void UpdateEditorOrientation()
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (vm.IsHorizontalView)
            {
                EditorSurface.Orientation = Avalonia.Layout.Orientation.Vertical;
                Waveform.Width = double.NaN;
                Waveform.Height = 220;
            }
            else
            {
                EditorSurface.Orientation = Avalonia.Layout.Orientation.Horizontal;
                Waveform.Width = 220;
                Waveform.Height = double.NaN;
            }
        }

        private void FollowPlaybackCursorIfNeeded()
        {
            if (DataContext is not MainWindowViewModel vm ||
                !vm.FollowPlaybackCursor ||
                !vm.IsPlaybackCursorVisible ||
                vm.OggDurationSeconds <= 0)
            {
                return;
            }

            var timelineLength = GetTimelineLength(vm);
            if (timelineLength <= 0)
                return;

            var offset = EditorScrollViewer.Offset;

            if (vm.IsHorizontalView)
            {
                if (EditorScrollViewer.Viewport.Width <= 0)
                    return;

                var cursorX = (vm.PlaybackPositionSeconds / vm.OggDurationSeconds) * timelineLength;
                var leftMargin = EditorScrollViewer.Viewport.Width * 0.25;
                var rightMargin = EditorScrollViewer.Viewport.Width * 0.75;
                var cursorInView = cursorX - offset.X;

                if (cursorInView < leftMargin || cursorInView > rightMargin)
                {
                    var targetX = cursorX - (EditorScrollViewer.Viewport.Width * 0.5);
                    var maxX = Math.Max(0, EditorSurface.Bounds.Width - EditorScrollViewer.Viewport.Width);
                    targetX = Math.Clamp(targetX, 0, maxX);
                    EditorScrollViewer.Offset = new Vector(targetX, offset.Y);
                }
            }
            else
            {
                if (EditorScrollViewer.Viewport.Height <= 0)
                    return;

                var cursorY = (1.0 - (vm.PlaybackPositionSeconds / vm.OggDurationSeconds)) * timelineLength;
                var topMargin = EditorScrollViewer.Viewport.Height * 0.25;
                var bottomMargin = EditorScrollViewer.Viewport.Height * 0.75;
                var cursorInView = cursorY - offset.Y;

                if (cursorInView < topMargin || cursorInView > bottomMargin)
                {
                    var targetY = cursorY - (EditorScrollViewer.Viewport.Height * 0.5);
                    var maxY = Math.Max(0, EditorSurface.Bounds.Height - EditorScrollViewer.Viewport.Height);
                    targetY = Math.Clamp(targetY, 0, maxY);
                    EditorScrollViewer.Offset = new Vector(offset.X, targetY);
                }
            }
        }

        private static double GetTimelineLength(MainWindowViewModel vm)
        {
            var spacingScale = Math.Max(1.0, vm.BeatSplit / (double)Math.Max(1, vm.GridMeasure));
            return vm.OggDurationSeconds > 0
                ? vm.OggDurationSeconds * vm.RowHeight * vm.VerticalZoom * spacingScale / 2.0
                : vm.MeasureCount * vm.RowHeight * vm.VerticalZoom * spacingScale;
        }
    }
