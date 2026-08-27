using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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

    public static readonly StyledProperty<double> StartOffsetSecondsProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(StartOffsetSeconds));

    public static readonly StyledProperty<double> PlaybackPositionSecondsProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(PlaybackPositionSeconds));

    public static readonly StyledProperty<bool> IsPlaybackCursorVisibleProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsPlaybackCursorVisible));

    public static readonly StyledProperty<bool> IsGridSyncFlashVisibleProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsGridSyncFlashVisible));

    public static readonly StyledProperty<bool> IsHorizontalViewProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsHorizontalView));

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

    // 마디 000의 첫 박이 오디오의 몇 초 지점인지. OGG 앞의 무음이나 인코더 지연
    // 때문에 첫 박이 sample 0 에 있는 경우는 거의 없다. 이 값이 없으면 BPM 으로
    // 그 어긋남을 흡수할 수밖에 없고, 그러면 곡 뒤로 갈수록 격자가 음악과 벌어진다.
    public double StartOffsetSeconds
    {
        get => GetValue(StartOffsetSecondsProperty);
        set => SetValue(StartOffsetSecondsProperty, value);
    }

    public double PlaybackPositionSeconds
    {
        get => GetValue(PlaybackPositionSecondsProperty);
        set => SetValue(PlaybackPositionSecondsProperty, value);
    }

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
        AffectsRender<TimelineControlBase>(
            RowHeightProperty, VerticalZoomProperty, HorizontalZoomProperty, MeasureCountProperty,
            BeatSplitProperty, GridMeasureProperty, BpmProperty, DurationSecondsProperty,
            StartOffsetSecondsProperty,
            PlaybackPositionSecondsProperty, IsPlaybackCursorVisibleProperty, IsGridSyncFlashVisibleProperty,
            IsHorizontalViewProperty);

        AffectsMeasure<TimelineControlBase>(
            RowHeightProperty, VerticalZoomProperty, MeasureCountProperty,
            BeatSplitProperty, GridMeasureProperty, DurationSecondsProperty, IsHorizontalViewProperty);
    }

    // ---- 시간 축 변환 (모든 그리기·클릭 판정이 이 세 개만 쓴다) ----

    // 마디+박 위치 -> 오디오 시간(초)
    protected double MeasurePositionToSeconds(double measurePosition) =>
        StartOffsetSeconds + (measurePosition * 240.0 / Bpm);

    // 오디오 시간(초) -> 마디+박 위치
    protected double SecondsToMeasurePosition(double seconds) =>
        (seconds - StartOffsetSeconds) * Bpm / 240.0;

    // 오디오 시간(초) -> 타임라인 좌표(px)
    protected double SecondsToTPos(double seconds, double timelineLength)
    {
        var ratio = DurationSeconds > 0 ? seconds / DurationSeconds : 0.0;
        return IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
    }

    // 타임라인 좌표(px) -> 오디오 시간(초)
    protected double TPosToSeconds(double tPos, double timelineLength)
    {
        if (timelineLength <= 0)
            return 0.0;

        var ratio = IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength));
        return Math.Clamp(ratio, 0.0, 1.0) * DurationSeconds;
    }

    protected double GetTimelineHeight()
    {
        var spacingScale = GetGridSpacingScale();
        if (DurationSeconds > 0)
            return Math.Max(1.0, DurationSeconds * RowHeight * VerticalZoom * spacingScale / 2.0);

        return MeasureCount * RowHeight * VerticalZoom * spacingScale;
    }

    protected double GetGridSpacingScale() => Math.Max(1.0, BeatSplit / (double)Math.Max(1, GridMeasure));

    protected static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;

    protected static bool IsMeasureBeatLine(int index, int split, int gridMeasure)
    {
        if (gridMeasure <= 0 || split < gridMeasure || split % gridMeasure != 0)
            return false;

        return Mod(index, split / gridMeasure) == 0;
    }

    protected void DrawPlaybackCursor(DrawingContext context, double width, double height)
    {
        if (!IsPlaybackCursorVisible || DurationSeconds <= 0)
            return;

        var ratio = Math.Clamp(PlaybackPositionSeconds / DurationSeconds, 0, 1);
        var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 50, 255, 120)), 5);
        var cursorPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 255, 90)), 2);

        if (IsHorizontalView)
        {
            var x = ratio * width;
            context.DrawLine(glowPen, new Point(x, 0), new Point(x, height));
            context.DrawLine(cursorPen, new Point(x, 0), new Point(x, height));
        }
        else
        {
            var y = (1.0 - ratio) * height;
            context.DrawLine(glowPen, new Point(0, y), new Point(width, y));
            context.DrawLine(cursorPen, new Point(0, y), new Point(width, y));
        }
    }

    protected void DrawGridSyncFlash(DrawingContext context, double width, double height)
    {
        if (!IsGridSyncFlashVisible)
            return;

        context.FillRectangle(new SolidColorBrush(Color.FromArgb(36, 80, 255, 140)), new Rect(0, 0, width, height));
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(190, 90, 255, 150)), 2), new Rect(1, 1, width - 2, height - 2));
    }
}
