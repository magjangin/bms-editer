using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
            Waveform.ScrubRequested += OnWaveformScrubRequested;
            Closing += OnWindowClosing;
            Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
            AddHandler(PointerPressedEvent, OnWindowPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
            UpdateEditorOrientation();
            UpdateWindowTitle();
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
                    break;
            }

            _isClosingConfirmed = true;
            Close();
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
            else if (e.PropertyName == nameof(MainWindowViewModel.PlaybackSpeed))
            {
                if (sender is MainWindowViewModel vm)
                    VideoPreview.SetPlaybackRate(vm.PlaybackSpeed);
            }
        }
    }
