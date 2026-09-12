using System;
using Avalonia;
using bms_editer.ViewModels;

namespace bms_editer;

public partial class MainWindow
{
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

        // 통계·검색 창에서 고른 노트가 있는 자리로 격자를 옮긴다.
        //
        // 이게 없으면 통계 창에서 키음 번호를 눌러도 화면에서는 아무 일도 안 일어난 것처럼
        // 보인다. 선택은 됐는데 그 노트들이 보이는 구간 밖에 있기 때문이다.
        //
        // 선택 직후에는 아직 레이아웃이 끝나지 않아 Viewport 가 0일 수 있으므로
        // 한 박자 뒤에 옮긴다.
        private void ScrollEditorToRatio(double ratio)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is not MainWindowViewModel vm)
                    return;

                var timelineLength = GetTimelineLength(vm);
                if (timelineLength <= 0)
                    return;

                var offset = EditorScrollViewer.Offset;

                if (vm.IsHorizontalView)
                {
                    var viewport = EditorScrollViewer.Viewport.Width;
                    if (viewport <= 0)
                        return;

                    var target = (ratio * timelineLength) - (viewport * 0.5);
                    var max = Math.Max(0, EditorSurface.Bounds.Width - viewport);
                    EditorScrollViewer.Offset = new Vector(Math.Clamp(target, 0, max), offset.Y);
                }
                else
                {
                    var viewport = EditorScrollViewer.Viewport.Height;
                    if (viewport <= 0)
                        return;

                    var target = ((1.0 - ratio) * timelineLength) - (viewport * 0.5);
                    var max = Math.Max(0, EditorSurface.Bounds.Height - viewport);
                    EditorScrollViewer.Offset = new Vector(offset.X, Math.Clamp(target, 0, max));
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
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
