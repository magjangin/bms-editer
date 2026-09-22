using Avalonia.Input;
using bms_editer.ViewModels;
using bms_editer.Views.Controls;

namespace bms_editer;

public partial class MainWindow
{
        private void OnWaveformScrubRequested(object? sender, WaveformScrubRequestedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (e.IsFinal)
                vm.ScrubCommit(e.Ratio);
            else
                vm.ScrubPreview(e.Ratio);
        }

        private bool _isSurfaceScrubbing;

        private void OnEditorSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(EditorSurface);
            if (point.Properties.IsMiddleButtonPressed)
            {
                _isSurfaceScrubbing = true;
                e.Pointer.Capture(EditorSurface);
                ScrubFromEditorSurface(e, isFinal: false);
            }
            else if (IsNonMiddleMouseButtonPressed(point.Properties))
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.StopPlaybackAtCurrentPosition();
            }
        }

        private void OnEditorSurfacePointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isSurfaceScrubbing)
                ScrubFromEditorSurface(e, isFinal: false);
        }

        // 드래그 중에는 커서만 옮기고, 버튼을 뗄 때 한 번만 재생을 옮긴다. (알려진 문제 24번)
        private void OnEditorSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isSurfaceScrubbing)
                return;

            _isSurfaceScrubbing = false;
            e.Pointer.Capture(null);
            ScrubFromEditorSurface(e, isFinal: true);
        }

        private void ScrubFromEditorSurface(PointerEventArgs e, bool isFinal)
        {
            if (DataContext is not MainWindowViewModel vm || vm.OggDurationSeconds <= 0)
                return;

            var timelineLength = GetTimelineLength(vm);
            if (timelineLength <= 0)
                return;

            var position = e.GetPosition(EditorSurface);
            var timelineRatio = vm.IsHorizontalView
                ? position.X / timelineLength
                : 1.0 - (position.Y / timelineLength);

            // 누른 자리는 격자 위의 위치다. 음원은 오프셋만큼 밀려 있으니 음원 안의 위치로 되돌린다.
            var ratio = vm.TimelineRatioToAudioRatio(timelineRatio);

            if (isFinal)
                vm.ScrubCommit(ratio);
            else
                vm.ScrubPreview(ratio);

            e.Handled = true;
        }

        private static bool IsNonMiddleMouseButtonPressed(PointerPointProperties properties) =>
            properties.IsLeftButtonPressed ||
            properties.IsRightButtonPressed ||
            properties.IsXButton1Pressed ||
            properties.IsXButton2Pressed ||
            properties.PointerUpdateKind is PointerUpdateKind.LeftButtonPressed
                or PointerUpdateKind.RightButtonPressed
                or PointerUpdateKind.XButton1Pressed
                or PointerUpdateKind.XButton2Pressed;

        // 재생 중 마우스 우클릭(또는 좌클릭) 시 현재 위치에서 즉시 재생을 멈춘다.
        // Tunnel 단계에서 창 전체로 미리 가로채므로, 자식 컨트롤(노트 격자·파형 등)에서
        // 이벤트를 Handled 처리하거나 에디터 빈 영역을 클릭해도 안정적으로 즉각 정지한다.
        private void OnWindowPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || !vm.IsPlaying)
                return;

            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsMiddleButtonPressed)
                return;

            if (IsNonMiddleMouseButtonPressed(properties))
            {
                vm.StopPlaybackAtCurrentPosition();
            }
        }
    }
