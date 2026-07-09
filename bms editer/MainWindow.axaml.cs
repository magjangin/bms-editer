using System.Linq;
using System.ComponentModel;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using bms_editer.ViewModels;
using bms_editer.Views;
using bms_editer.Views.Controls;

namespace bms_editer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainWindowViewModel();
            DataContext = vm;
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
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsHorizontalView))
            {
                UpdateEditorOrientation();
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
}
