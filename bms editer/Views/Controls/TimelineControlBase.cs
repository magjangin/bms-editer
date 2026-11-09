using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using bms_editer.Services;

namespace bms_editer.Views.Controls;

public abstract class TimelineControlBase : Control
{
    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(RowHeight), 16.0);

    public static readonly StyledProperty<double> VerticalZoomProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(VerticalZoom), 1.0);

    public static readonly StyledProperty<double> HorizontalZoomProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(HorizontalZoom), 1.0);

    public static readonly StyledProperty<int> MeasureCountProperty =
        AvaloniaProperty.Register<TimelineControlBase, int>(nameof(MeasureCount), 32);

    public static readonly StyledProperty<int> BeatSplitProperty =
        AvaloniaProperty.Register<TimelineControlBase, int>(nameof(BeatSplit), 16);

    public static readonly StyledProperty<int> GridMeasureProperty =
        AvaloniaProperty.Register<TimelineControlBase, int>(nameof(GridMeasure), 4);

    public static readonly StyledProperty<double> BpmProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(Bpm), 120.0);

    public static readonly StyledProperty<double> DurationSecondsProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(DurationSeconds));

    public static readonly StyledProperty<double> PlaybackPositionSecondsProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(PlaybackPositionSeconds));

    public static readonly StyledProperty<double> AudioOffsetSecondsProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(AudioOffsetSeconds));

    public static readonly StyledProperty<bool> IsPlaybackCursorVisibleProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsPlaybackCursorVisible));

    public static readonly StyledProperty<bool> IsGridSyncFlashVisibleProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsGridSyncFlashVisible));

    public static readonly StyledProperty<bool> IsHorizontalViewProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsHorizontalView));

    public static readonly StyledProperty<ChartTimeline?> TimelineProperty =
        AvaloniaProperty.Register<TimelineControlBase, ChartTimeline?>(nameof(Timeline));

    // 마디 위치 <-> 시각 변환. 뷰모델이 넣어준다.
    // 비어 있으면 Bpm 하나만 쓰는 균일 시간축으로 대신한다(디자이너 미리보기 등).
    public ChartTimeline? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    // 실제로 쓸 시간축. BPM 이 바뀌면 균일 시간축도 따라 바뀌어야 해서 캐시해 둔다.
    private ChartTimeline? _fallbackTimeline;
    private double _fallbackTimelineBpm;

    protected ChartTimeline EffectiveTimeline
    {
        get
        {
            if (Timeline is { } timeline)
                return timeline;

            if (_fallbackTimeline is null || _fallbackTimelineBpm != Bpm)
            {
                _fallbackTimeline = ChartTimeline.Uniform(Bpm);
                _fallbackTimelineBpm = Bpm;
            }

            return _fallbackTimeline;
        }
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double VerticalZoom
    {
        get => GetValue(VerticalZoomProperty);
        set => SetValue(VerticalZoomProperty, value);
    }

    public double HorizontalZoom
    {
        get => GetValue(HorizontalZoomProperty);
        set => SetValue(HorizontalZoomProperty, value);
    }

    public int MeasureCount
    {
        get => GetValue(MeasureCountProperty);
        set => SetValue(MeasureCountProperty, value);
    }

    public int BeatSplit
    {
        get => GetValue(BeatSplitProperty);
        set => SetValue(BeatSplitProperty, value);
    }

    public int GridMeasure
    {
        get => GetValue(GridMeasureProperty);
        set => SetValue(GridMeasureProperty, value);
    }

    public double Bpm
    {
        get => GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    public double DurationSeconds
    {
        get => GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public double PlaybackPositionSeconds
    {
        get => GetValue(PlaybackPositionSecondsProperty);
        set => SetValue(PlaybackPositionSecondsProperty, value);
    }

    // 음원을 타임라인에서 뒤로 미는 양(초).
    // 격자와 노트는 차트 시간축 위에 그대로 두고, 음원에서 나온 것만(파형, 온셋, 재생 커서) 이 값만큼 민다.
    public double AudioOffsetSeconds
    {
        get => GetValue(AudioOffsetSecondsProperty);
        set => SetValue(AudioOffsetSecondsProperty, value);
    }

    public double AudioRatio(double audioSeconds) =>
        DurationSeconds <= 0 ? 0.0 : (audioSeconds + AudioOffsetSeconds) / DurationSeconds;

    public double AudioSecondsAtRatio(double ratio) =>
        (ratio * DurationSeconds) - AudioOffsetSeconds;

    public bool IsPlaybackCursorVisible
    {
        get => GetValue(IsPlaybackCursorVisibleProperty);
        set => SetValue(IsPlaybackCursorVisibleProperty, value);
    }

    public bool IsGridSyncFlashVisible
    {
        get => GetValue(IsGridSyncFlashVisibleProperty);
        set => SetValue(IsGridSyncFlashVisibleProperty, value);
    }

    public bool IsHorizontalView
    {
        get => GetValue(IsHorizontalViewProperty);
        set => SetValue(IsHorizontalViewProperty, value);
    }

    static TimelineControlBase()
    {
        // PlaybackPositionSeconds 와 IsPlaybackCursorVisible 은 일부러 뺐다. 커서는 PlaybackCursorLayer 가 그린다.
        AffectsRender<TimelineControlBase>(
            RowHeightProperty, VerticalZoomProperty, HorizontalZoomProperty, MeasureCountProperty,
            BeatSplitProperty, GridMeasureProperty, BpmProperty, DurationSecondsProperty,
            IsGridSyncFlashVisibleProperty,
            IsHorizontalViewProperty, TimelineProperty, AudioOffsetSecondsProperty);

        // HorizontalZoom 이 여기 빠져 있어서, 가로 줌을 바꾸면 다시 그려지기만 하고
        // 컨트롤 크기는 그대로였다. 레인이 잘리거나 오른쪽에 빈 자리가 남았다.
        AffectsMeasure<TimelineControlBase>(
            RowHeightProperty, VerticalZoomProperty, HorizontalZoomProperty, MeasureCountProperty,
            BeatSplitProperty, GridMeasureProperty, DurationSecondsProperty, IsHorizontalViewProperty);
    }

    protected double GetTimelineHeight() =>
        ComputeTimelineLength(DurationSeconds, MeasureCount, RowHeight, VerticalZoom, BeatSplit, GridMeasure);

    protected double GetGridSpacingScale() => ComputeGridSpacingScale(BeatSplit, GridMeasure);

    // 타임라인 길이(px)를 정하는 공식. **이 한 곳에만 둔다.**
    //
    // 컨트롤 밖(창의 스크롤·스크럽 좌표 계산)도 같은 값이 필요한데, 예전에는 그쪽이 식을 따로 베껴 갖고 있었다.
    // 식이 한쪽만 바뀌면 파형·격자는 멀쩡한데 스크럽한 자리와 소리가 어긋난다(알려진 문제 32번).
    public static double ComputeTimelineLength(
        double durationSeconds, int measureCount, double rowHeight, double verticalZoom, int beatSplit, int gridMeasure)
    {
        var spacingScale = ComputeGridSpacingScale(beatSplit, gridMeasure);
        if (durationSeconds > 0)
            return Math.Max(1.0, durationSeconds * rowHeight * verticalZoom * spacingScale / 2.0);

        return measureCount * rowHeight * verticalZoom * spacingScale;
    }

    public static double ComputeGridSpacingScale(int beatSplit, int gridMeasure) =>
        Math.Max(1.0, beatSplit / (double)Math.Max(1, gridMeasure));

    protected static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;

    protected static bool IsMeasureBeatLine(int index, int split, int gridMeasure)
    {
        if (gridMeasure <= 0 || split < gridMeasure || split % gridMeasure != 0)
            return false;

        return Mod(index, split / gridMeasure) == 0;
    }

    protected enum GridLineKind
    {
        SubBeat,
        Beat,
        Measure,
    }

    protected readonly record struct GridLine(double Position, GridLineKind Kind, int Measure, double Seconds);

    public const double DefaultViewportBuffer = 400.0;

    private ScrollViewer? _observedScrollViewer;

    // 마지막 Render 가 실제로 그려 둔 타임라인 구간. 처음에는 비어 있어서 무조건 그리게 된다.
    private double _renderedMinPos = 1.0;
    private double _renderedMaxPos;

    // 스크롤 시 자식 렌더링은 다시 호출되지 않으므로, 컬링 범위가 바뀌도록 직접 다시 그린다.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _observedScrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_observedScrollViewer is not null)
            _observedScrollViewer.ScrollChanged += OnObservedScrollChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_observedScrollViewer is not null)
            _observedScrollViewer.ScrollChanged -= OnObservedScrollChanged;
        _observedScrollViewer = null;
        base.OnDetachedFromVisualTree(e);
    }

    // 지금 보이는 구간이 이미 그려 둔 구간(뷰포트 + 버퍼) 안이면 다시 그릴 게 없다.
    //
    // 예전에는 스크롤 이벤트마다 노트·격자·파형을 전부 다시 기록했다. 휠을 굴리거나
    // 재생바를 따라가는 동안 프레임이 밀리는 주된 원인이었다. 버퍼를 벗어날 때만 다시 그린다.
    private void OnObservedScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var timelineLength = GetTimelineHeight();
        if (TryGetViewportInControl(out var viewport))
        {
            var (viewStart, viewEnd) = CalculateVisibleTimelineRange(viewport, timelineLength, IsHorizontalView, buffer: 0);
            if (viewStart >= _renderedMinPos && viewEnd <= _renderedMaxPos)
                return;
        }

        InvalidateVisual();
    }

    private bool TryGetViewportInControl(out Rect viewport)
    {
        viewport = default;

        var scrollViewer = _observedScrollViewer;
        if (scrollViewer is null || scrollViewer.Viewport.Width <= 0 || scrollViewer.Viewport.Height <= 0)
            return false;

        var p0 = scrollViewer.TranslatePoint(new Point(0, 0), this);
        var p1 = scrollViewer.TranslatePoint(new Point(scrollViewer.Viewport.Width, scrollViewer.Viewport.Height), this);
        if (!p0.HasValue || !p1.HasValue)
            return false;

        viewport = new Rect(
            new Point(Math.Min(p0.Value.X, p1.Value.X), Math.Min(p0.Value.Y, p1.Value.Y)),
            new Point(Math.Max(p0.Value.X, p1.Value.X), Math.Max(p0.Value.Y, p1.Value.Y)));
        return true;
    }

    // 뷰포트 사각 영역(컨트롤 로컬 좌표계 기준)에서 현재 화면에 보이는 타임라인 축 범위 [MinPos, MaxPos]를 구한다.
    // 뷰포트가 비정상적이거나 범위를 구할 수 없으면 전체 범위 [0, timelineLength]를 반환한다.
    public static (double MinPos, double MaxPos) CalculateVisibleTimelineRange(
        Rect viewportInControlCoordinates,
        double timelineLength,
        bool isHorizontalView,
        double buffer = DefaultViewportBuffer)
    {
        if (timelineLength <= 0)
            return (0, 0);

        if (viewportInControlCoordinates.Width <= 0 ||
            viewportInControlCoordinates.Height <= 0)
        {
            return (0, timelineLength);
        }

        double viewStart, viewEnd;
        if (isHorizontalView)
        {
            viewStart = viewportInControlCoordinates.Left;
            viewEnd = viewportInControlCoordinates.Right;
        }
        else
        {
            viewStart = viewportInControlCoordinates.Top;
            viewEnd = viewportInControlCoordinates.Bottom;
        }

        var minPos = Math.Max(0.0, viewStart - buffer);
        var maxPos = Math.Min(timelineLength, viewEnd + buffer);

        if (minPos > maxPos)
            return (0, timelineLength);

        return (minPos, maxPos);
    }

    // ScrollViewer 안에서 현재 화면에 보이는 타임라인 축 범위 [MinPos, MaxPos]를 구한다.
    // Render 가 부르는 자리라, 어디까지 그렸는지 여기서 기억해 둔다. (OnObservedScrollChanged 참고)
    protected (double MinPos, double MaxPos) GetVisibleTimelineRange(double timelineLength, double buffer = DefaultViewportBuffer)
    {
        if (timelineLength <= 0)
            return (0, 0);

        var range = TryGetViewportInControl(out var viewport)
            ? CalculateVisibleTimelineRange(viewport, timelineLength, IsHorizontalView, buffer)
            : (0.0, timelineLength);

        _renderedMinPos = range.Item1;
        _renderedMaxPos = range.Item2;
        return range;
    }

    // 타임라인에 그릴 격자선을 순서대로 내놓는다.
    // minPos / maxPos 가 주어지면 보이는 영역 밖의 격자선은 건너뛴다(뷰포트 컬링).
    protected IEnumerable<GridLine> EnumerateGridLines(
        double timelineLength,
        double minPos = double.NegativeInfinity,
        double maxPos = double.PositiveInfinity)
    {
        var split = Math.Max(1, BeatSplit);

        // 배경 음원이 있으면 화면 전체가 곡 길이를 뜻하므로 초 단위로 훑는다.
        if (DurationSeconds > 0 && Bpm > 0)
        {
            // 격자 칸의 시각은 Timeline 이 정한다. 예전에는 여기서 240/(BPM*split) 을
            // 직접 써서, BPM 이 바뀌거나 4/4가 아닌 마디가 있으면 그 뒤로 전부 어긋났다.
            var timeline = EffectiveTimeline;
            var horizontal = IsHorizontalView;

            // 예전에는 0번부터 곡 끝까지 전부 돌며 화면 밖을 건너뛰었다. 192분할이면 매 프레임
            // 수만 번의 시각 계산이 나갔다. 화면에 들어오는 첫 칸부터 시작해 화면을 지나면 멈춘다.
            for (var index = FirstVisibleGridIndex(timeline, timelineLength, minPos, maxPos, split); ; index++)
            {
                var seconds = timeline.SecondsAt((double)index / split);
                if (seconds > DurationSeconds)
                    yield break;

                var position = ToTimelinePosition(seconds / DurationSeconds, timelineLength);

                // 뒤 칸일수록 가로 뷰는 오른쪽, 세로 뷰는 위쪽으로 간다.
                if (horizontal ? position > maxPos + 0.5 : position < minPos - 0.5)
                    yield break;

                if (position < -0.5 || position > timelineLength + 0.5)
                    continue;

                if (position < minPos - 0.5 || position > maxPos + 0.5)
                    continue;

                yield return new GridLine(
                    position,
                    ClassifyGridLine(index, split),
                    index / split,
                    seconds);
            }
        }
        else
        {
            // 음원이 없으면 마디 높이가 곧 화면 높이다.
            var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
            var measureTimeline = EffectiveTimeline;
            var (firstMeasure, lastMeasure) = VisibleMeasureSpan(timelineLength, minPos, maxPos, rowHeight);

            // 마지막 마디의 닫는 선까지 그리려고 MeasureCount 까지 돈다.
            // 범위를 벗어나는 보조선은 아래 검사에서 걸러진다.
            for (var measure = firstMeasure; measure <= lastMeasure; measure++)
            {
                for (var beat = 0; beat < split; beat++)
                {
                    var measurePosition = measure + (beat / (double)split);
                    var offset = measurePosition * rowHeight;
                    var position = IsHorizontalView ? offset : timelineLength - offset;

                    // 곱셈 순서 차이로 끝 선이 반 픽셀쯤 넘칠 수 있어 여유를 둔다.
                    if (position < -0.5 || position > timelineLength + 0.5)
                        continue;

                    if (position < minPos - 0.5 || position > maxPos + 0.5)
                        continue;

                    yield return new GridLine(
                        position,
                        ClassifyGridLine(beat, split),
                        measure,
                        measureTimeline.SecondsAt(measurePosition));
                }
            }
        }
    }

    // 화면에 들어오는 가장 이른 격자 칸. 부동소수 오차에 대비해 한 칸 앞에서 시작한다.
    private int FirstVisibleGridIndex(ChartTimeline timeline, double timelineLength, double minPos, double maxPos, int split)
    {
        if (timelineLength <= 0 || DurationSeconds <= 0)
            return 0;

        var ratio = IsHorizontalView
            ? (minPos - 0.5) / timelineLength
            : 1.0 - ((maxPos + 0.5) / timelineLength);

        if (double.IsNaN(ratio) || ratio <= 0)
            return 0;

        var measurePosition = timeline.MeasurePositionAt(ratio * DurationSeconds);
        return Math.Max(0, (int)Math.Floor(measurePosition * split) - 1);
    }

    // 음원이 없을 때 화면에 걸치는 마디 범위. 앞뒤로 한 마디씩 여유를 둔다.
    private (int First, int Last) VisibleMeasureSpan(double timelineLength, double minPos, double maxPos, double rowHeight)
    {
        var measureCount = MeasureCount;
        if (rowHeight <= 0)
            return (0, measureCount);

        var earlyOffset = IsHorizontalView ? minPos - 0.5 : timelineLength - (maxPos + 0.5);
        var lateOffset = IsHorizontalView ? maxPos + 0.5 : timelineLength - (minPos - 0.5);

        var first = double.IsInfinity(earlyOffset)
            ? 0
            : (int)Math.Clamp(Math.Floor(earlyOffset / rowHeight) - 1, 0, measureCount);
        var last = double.IsInfinity(lateOffset)
            ? measureCount
            : (int)Math.Clamp(Math.Ceiling(lateOffset / rowHeight) + 1, 0, measureCount);

        return (first, last);
    }

    private GridLineKind ClassifyGridLine(int index, int split) =>
        Mod(index, split) == 0
            ? GridLineKind.Measure
            : IsMeasureBeatLine(index, split, GridMeasure)
                ? GridLineKind.Beat
                : GridLineKind.SubBeat;

    // 세로 뷰는 아래가 0초라 비율을 뒤집는다. 이 뒤집기가 여기저기 흩어져 있었다.
    protected double ToTimelinePosition(double ratio, double timelineLength) =>
        IsHorizontalView ? ratio * timelineLength : (1.0 - ratio) * timelineLength;

    // ── 재생 커서 ────────────────────────────────────────────────────────────
    //
    // 커서는 얇은 자식 층 하나에 그리고, 재생 중에는 그 층만 옮긴다.
    //
    // 예전에는 커서 위치가 AffectsRender 라서 재생 틱마다 노트·격자·파형·라벨을 전부 다시 기록했다.
    // 커서 선 두 줄을 옮기려고 수천 번의 그리기 호출을 다시 낸 셈이고, 곡이 빽빽할수록 재생이 끊겼다.
    // 층이 얇으니 화면에서 다시 칠할 영역도 커서가 지나간 띠뿐이다.
    private const double CursorLayerThickness = 8.0;

    private static readonly IPen CursorGlowPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 50, 255, 120)), 5).ToImmutable();
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 255, 90)), 2).ToImmutable();

    private sealed class PlaybackCursorLayer : Control
    {
        private bool _isHorizontal;

        public bool IsHorizontal
        {
            get => _isHorizontal;
            set
            {
                if (_isHorizontal == value)
                    return;

                _isHorizontal = value;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            var middle = CursorLayerThickness / 2;
            var (from, to) = IsHorizontal
                ? (new Point(middle, 0), new Point(middle, Bounds.Height))
                : (new Point(0, middle), new Point(Bounds.Width, middle));

            context.DrawLine(CursorGlowPen, from, to);
            context.DrawLine(CursorPen, from, to);
        }
    }

    private PlaybackCursorLayer? _cursorLayer;
    private TranslateTransform? _cursorTransform;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PlaybackPositionSecondsProperty
            || change.Property == IsPlaybackCursorVisibleProperty
            || change.Property == AudioOffsetSecondsProperty
            || change.Property == DurationSecondsProperty
            || change.Property == RowHeightProperty
            || change.Property == VerticalZoomProperty
            || change.Property == MeasureCountProperty
            || change.Property == BeatSplitProperty
            || change.Property == GridMeasureProperty
            || change.Property == IsHorizontalViewProperty)
        {
            UpdatePlaybackCursorLayer();
        }
    }

    // 커서가 처음 보일 때 층을 만든다. 재생하지 않는 컨트롤(테스트의 격자 계산용 등)은 층이 없다.
    private void UpdatePlaybackCursorLayer()
    {
        if (!IsPlaybackCursorVisible || DurationSeconds <= 0)
        {
            if (_cursorLayer is not null)
                _cursorLayer.IsVisible = false;
            return;
        }

        if (_cursorLayer is null || _cursorTransform is null)
        {
            _cursorTransform = new TranslateTransform();
            _cursorLayer = new PlaybackCursorLayer
            {
                IsHitTestVisible = false,
                RenderTransform = _cursorTransform,
                RenderTransformOrigin = RelativePoint.TopLeft,
            };

            VisualChildren.Add(_cursorLayer);
            InvalidateArrange();
        }

        var horizontal = IsHorizontalView;
        if (_cursorLayer.IsHorizontal != horizontal)
        {
            _cursorLayer.IsHorizontal = horizontal;
            InvalidateArrange();
        }

        // 재생 커서도 격자와 같은 타임라인 길이 위에 놓아야 파형·노트와 맞는다.
        var ratio = Math.Clamp(AudioRatio(PlaybackPositionSeconds), 0, 1);
        var position = ToTimelinePosition(ratio, GetTimelineHeight()) - (CursorLayerThickness / 2);

        _cursorTransform.X = horizontal ? position : 0;
        _cursorTransform.Y = horizontal ? 0 : position;
        _cursorLayer.IsVisible = true;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_cursorLayer is { } layer)
        {
            var size = layer.IsHorizontal
                ? new Size(CursorLayerThickness, finalSize.Height)
                : new Size(finalSize.Width, CursorLayerThickness);

            layer.Measure(size);
            layer.Arrange(new Rect(size));
        }

        return finalSize;
    }

    private static readonly IBrush SyncFlashBrush = new SolidColorBrush(Color.FromArgb(36, 80, 255, 140)).ToImmutable();
    private static readonly IPen SyncFlashPen = new Pen(new SolidColorBrush(Color.FromArgb(190, 90, 255, 150)), 2).ToImmutable();

    protected void DrawGridSyncFlash(DrawingContext context, double width, double height)
    {
        if (!IsGridSyncFlashVisible)
            return;

        context.FillRectangle(SyncFlashBrush, new Rect(0, 0, width, height));
        context.DrawRectangle(null, SyncFlashPen, new Rect(1, 1, width - 2, height - 2));
    }
}
