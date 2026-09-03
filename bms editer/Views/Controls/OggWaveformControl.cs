using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using bms_editer.Services;

namespace bms_editer.Views.Controls;

public sealed class WaveformScrubRequestedEventArgs : EventArgs
{
    public double Ratio { get; }
    public bool IsFinal { get; }

    public WaveformScrubRequestedEventArgs(double ratio, bool isFinal)
    {
        Ratio = ratio;
        IsFinal = isFinal;
    }
}

public sealed class OggWaveformControl : TimelineControlBase
{
    public static readonly StyledProperty<IReadOnlyList<float>?> PeaksProperty =
        AvaloniaProperty.Register<OggWaveformControl, IReadOnlyList<float>?>(nameof(Peaks));

    public static readonly StyledProperty<IReadOnlyList<float>?> RmsProperty =
        AvaloniaProperty.Register<OggWaveformControl, IReadOnlyList<float>?>(nameof(Rms));

    public static readonly StyledProperty<IReadOnlyList<OnsetMarker>?> OnsetsProperty =
        AvaloniaProperty.Register<OggWaveformControl, IReadOnlyList<OnsetMarker>?>(nameof(Onsets));

    // 버킷별 최대 진폭. 어택이 어디서 튀었는지를 그대로 보여준다.
    public IReadOnlyList<float>? Peaks
    {
        get => GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    // 버킷별 RMS. 피크 안쪽에 겹쳐 그려서 "소리의 몸통"을 나타낸다.
    public IReadOnlyList<float>? Rms
    {
        get => GetValue(RmsProperty);
        set => SetValue(RmsProperty, value);
    }

    public IReadOnlyList<OnsetMarker>? Onsets
    {
        get => GetValue(OnsetsProperty);
        set => SetValue(OnsetsProperty, value);
    }

    public event EventHandler<WaveformScrubRequestedEventArgs>? ScrubRequested;

    static OggWaveformControl()
    {
        AffectsRender<OggWaveformControl>(PeaksProperty, RmsProperty, OnsetsProperty);
    }

    public OggWaveformControl()
    {
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(15, 20, 20));
    private static readonly Pen CenterLinePen = new(Brushes.DimGray, 1);
    private static readonly Pen WavePen = new(new SolidColorBrush(Color.FromRgb(150, 170, 160)), 1);

    // 피크는 옅게 깔고 RMS 를 그 위에 진하게 겹친다. 지속음의 몸통 위로 어택만
    // 뿔처럼 솟아 보여서, 마디선을 어디에 맞춰야 하는지가 눈에 바로 들어온다.
    private static readonly IBrush PeakFillBrush = new SolidColorBrush(Color.FromArgb(110, 118, 158, 138));
    private static readonly IBrush RmsFillBrush = new SolidColorBrush(Color.FromArgb(210, 168, 208, 186));

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        var timelineLength = GetTimelineHeight();

        if (width <= 0 || height <= 0 || timelineLength <= 0)
            return;

        var waveformThickness = IsHorizontalView ? height : width;
        var center = waveformThickness / 2.0;

        context.FillRectangle(BackgroundBrush, new Rect(0, 0, width, height));

        if (IsHorizontalView)
        {
            context.DrawLine(CenterLinePen, new Point(0, center), new Point(width, center));
        }
        else
        {
            context.DrawLine(CenterLinePen, new Point(center, 0), new Point(center, height));
        }

        // 피크가 1.0 으로 정규화돼 있으므로 줌 1.0 이면 곡의 가장 큰 소리가
        // 반높이의 62% 를 채운다. 줌을 올려도 중심선 밖으로는 나가지 않게 자른다.
        var maxAmplitude = (center - 6) * 0.62 * Math.Clamp(HorizontalZoom, 0.1, 4.0);
        var amplitudeLimit = Math.Max(0.0, center - 2);
        var peaks = Peaks;

        if (peaks is { Count: > 1 })
        {
            EnsureWaveGeometry(peaks, Rms, timelineLength, center, maxAmplitude, amplitudeLimit, IsHorizontalView);

            if (_peakGeometry is not null)
                context.DrawGeometry(PeakFillBrush, null, _peakGeometry);

            if (_rmsGeometry is not null)
                context.DrawGeometry(RmsFillBrush, null, _rmsGeometry);

            DrawOnsetMarkers(context, waveformThickness, timelineLength, Onsets, IsHorizontalView);
        }
        else
        {
            // 음원을 닫으면 캐시해 둔 도형(수만 점짜리)도 같이 놓아준다.
            ReleaseWaveGeometry();

            var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
            for (var tPos = 0.0; tPos <= timelineLength; tPos += 2.0)
            {
                if (IsHorizontalView)
                {
                    var fMeasurePosition = rowHeight > 0 ? tPos / rowHeight : 0;
                    var ft = fMeasurePosition * 6.0;
                    var fAmplitude = Math.Abs(Math.Sin(ft) * 0.6 + Math.Sin(ft * 2.7 + 1.3) * 0.3 + Math.Sin(ft * 5.3) * 0.1);
                    var fHalf = fAmplitude * maxAmplitude;
                    context.DrawLine(WavePen, new Point(tPos, center - fHalf), new Point(tPos, center + fHalf));
                }
                else
                {
                    var measurePosition = rowHeight > 0 ? (timelineLength - tPos) / rowHeight : 0;
                    var t = measurePosition * 6.0;
                    var amplitude = Math.Abs(Math.Sin(t) * 0.6 + Math.Sin(t * 2.7 + 1.3) * 0.3 + Math.Sin(t * 5.3) * 0.1);
                    var half = amplitude * maxAmplitude;
                    context.DrawLine(WavePen, new Point(center - half, tPos), new Point(center + half, tPos));
                }
            }
        }

        DrawBeatGrid(context, waveformThickness, timelineLength, IsHorizontalView);
        DrawGridSyncFlash(context, width, height);

        // 재생 커서도 같은 시간축 위에 있어야 파형과 맞는다.
        DrawPlaybackCursor(
            context,
            IsHorizontalView ? timelineLength : width,
            IsHorizontalView ? height : timelineLength);
    }

    private bool _isScrubbing;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || DurationSeconds <= 0)
            return;

        _isScrubbing = true;
        e.Pointer.Capture(this);
        RequestScrub(PositionAlongTimeline(point.Position), isFinal: false, e);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isScrubbing)
            return;

        RequestScrub(PositionAlongTimeline(e.GetCurrentPoint(this).Position), isFinal: false, e);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isScrubbing)
            return;

        _isScrubbing = false;
        e.Pointer.Capture(null);
        RequestScrub(PositionAlongTimeline(e.GetCurrentPoint(this).Position), isFinal: true, e);
    }

    private double PositionAlongTimeline(Point position) => IsHorizontalView ? position.X : position.Y;

    private void RequestScrub(double pos, bool isFinal, PointerEventArgs e)
    {
        if (DurationSeconds <= 0)
            return;

        var timelineLength = GetTimelineHeight();
        if (timelineLength <= 0)
            return;

        var ratio = IsHorizontalView ? Math.Clamp(pos / timelineLength, 0, 1) : Math.Clamp(1.0 - (pos / timelineLength), 0, 1);
        ScrubRequested?.Invoke(this, new WaveformScrubRequestedEventArgs(ratio, isFinal));
        e.Handled = true;
    }

    // 화면 블록 한 칸의 길이(px). 1px 이면 세로 줌을 끝까지 올렸을 때 한 칸이 약 3.9ms 라
    // 소스 버킷(2.5ms)과 균형이 맞는다.
    private const double BlockLength = 1.0;

    private const int MaxBlockCount = 200_000;

    // 파형 자체는 재생 커서가 움직여도 그대로다. 커서 때문에 초당 30번 다시 그리는데
    // 그때마다 수만 점짜리 도형을 새로 만들면 아무 의미 없이 GC 만 돈다.
    // 실제로 달라지는 값이 있을 때만 다시 만든다.
    private IReadOnlyList<float>? _geometryPeaks;
    private IReadOnlyList<float>? _geometryRms;
    private double _geometryTimelineLength;
    private double _geometryCenter;
    private double _geometryMaxAmplitude;
    private double _geometryAmplitudeLimit;
    private bool _geometryIsHorizontal;
    private Geometry? _peakGeometry;
    private Geometry? _rmsGeometry;

    private void EnsureWaveGeometry(
        IReadOnlyList<float> peaks,
        IReadOnlyList<float>? rms,
        double timelineLength,
        double center,
        double maxAmplitude,
        double amplitudeLimit,
        bool isHorizontal)
    {
        if (_peakGeometry is not null
            && ReferenceEquals(_geometryPeaks, peaks)
            && ReferenceEquals(_geometryRms, rms)
            && _geometryTimelineLength == timelineLength
            && _geometryCenter == center
            && _geometryMaxAmplitude == maxAmplitude
            && _geometryAmplitudeLimit == amplitudeLimit
            && _geometryIsHorizontal == isHorizontal)
            return;

        var blockCount = Math.Clamp((int)Math.Ceiling(timelineLength / BlockLength), 2, MaxBlockCount);

        _peakGeometry = BuildEnvelope(peaks, blockCount, timelineLength, center, maxAmplitude, amplitudeLimit, isHorizontal);
        _rmsGeometry = rms is { Count: > 1 }
            ? BuildEnvelope(rms, blockCount, timelineLength, center, maxAmplitude, amplitudeLimit, isHorizontal)
            : null;

        _geometryPeaks = peaks;
        _geometryRms = rms;
        _geometryTimelineLength = timelineLength;
        _geometryCenter = center;
        _geometryMaxAmplitude = maxAmplitude;
        _geometryAmplitudeLimit = amplitudeLimit;
        _geometryIsHorizontal = isHorizontal;
    }

    private void ReleaseWaveGeometry()
    {
        if (_peakGeometry is null && _rmsGeometry is null)
            return;

        _peakGeometry = null;
        _rmsGeometry = null;
        _geometryPeaks = null;
        _geometryRms = null;
    }

    // 위쪽(세로 뷰에서는 왼쪽) 가장자리를 앞으로 훑고 아래쪽 가장자리를 되짚어 닫은
    // 다각형 하나. 예전에는 블록마다 사각형을 하나씩 채워서, 5분 곡을 최대 줌으로 보면
    // 프레임마다 수만 개의 그리기 명령이 쌓였다.
    private static Geometry BuildEnvelope(
        IReadOnlyList<float> values,
        int blockCount,
        double timelineLength,
        double center,
        double maxAmplitude,
        double amplitudeLimit,
        bool isHorizontal)
    {
        var step = timelineLength / blockCount;
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < blockCount; i++)
            {
                var half = GetBlockAmplitude(values, i, blockCount, isHorizontal, maxAmplitude, amplitudeLimit);
                var point = ToPoint(i * step, center - half, isHorizontal);

                if (i == 0)
                    ctx.BeginFigure(point, isFilled: true);
                else
                    ctx.LineTo(point);
            }

            for (var i = blockCount - 1; i >= 0; i--)
            {
                var half = GetBlockAmplitude(values, i, blockCount, isHorizontal, maxAmplitude, amplitudeLimit);
                ctx.LineTo(ToPoint(i * step, center + half, isHorizontal));
            }

            ctx.EndFigure(true);
        }

        return geometry;
    }

    private static Point ToPoint(double alongTimeline, double acrossTimeline, bool isHorizontal) =>
        isHorizontal ? new Point(alongTimeline, acrossTimeline) : new Point(acrossTimeline, alongTimeline);

    private static double GetBlockAmplitude(
        IReadOnlyList<float> values,
        int blockIndex,
        int blockCount,
        bool isHorizontal,
        double maxAmplitude,
        double amplitudeLimit)
    {
        // 세로 뷰는 아래가 0초라 소스 순서를 뒤집는다.
        var sourceBlock = isHorizontal ? blockIndex : (blockCount - 1 - blockIndex);
        var (start, end) = OggPeakLoader.GetBlockSourceRange(sourceBlock, blockCount, values.Count);

        // 블록이 덮는 구간의 최댓값. 한 칸만 찍어 읽으면 줌을 줄였을 때 어택이 사라진다.
        var value = 0f;
        for (var i = start; i < end; i++)
            value = MathF.Max(value, values[i]);

        return Math.Min(value * maxAmplitude, amplitudeLimit);
    }

    private static readonly IPen[] OnsetPens = CreateOnsetPens();

    private static IPen[] CreateOnsetPens()
    {
        var pens = new IPen[146];
        for (var alpha = 25; alpha < pens.Length; alpha++)
            pens[alpha] = new Pen(new SolidColorBrush(Color.FromArgb((byte)alpha, 230, 230, 210)), 1);
        return pens;
    }

    private static IPen GetOnsetPen(byte alpha) => OnsetPens[Math.Clamp((int)alpha, 25, 145)];

    private void DrawOnsetMarkers(
        DrawingContext context,
        double thickness,
        double timelineLength,
        IReadOnlyList<OnsetMarker>? onsets,
        bool isHorizontal)
    {
        var durationSeconds = DurationSeconds;
        if (onsets is null || onsets.Count == 0 || timelineLength <= 0 || durationSeconds <= 0)
            return;

        var startOffset = isHorizontal ? 20.0 : 6.0;
        var endOffset = thickness - (isHorizontal ? 6.0 : 20.0);
        var center = thickness / 2.0;

        for (var i = 0; i < onsets.Count; i++)
        {
            var onset = onsets[i];
            var strength = onset.Strength;
            if (strength <= 0.01f)
                continue;

            var pen = GetOnsetPen((byte)Math.Clamp((int)((strength * 135f) + 10f), 25, 145));

            // 마커는 초로 들어온다. 격자·노트·재생 커서와 같은 변환을 쓴다.
            var tPos = ToTimelinePosition(Math.Clamp(onset.Seconds / durationSeconds, 0, 1), timelineLength);

            if (strength > 0.45f)
            {
                if (isHorizontal)
                    context.DrawLine(pen, new Point(tPos, startOffset), new Point(tPos, endOffset));
                else
                    context.DrawLine(pen, new Point(startOffset, tPos), new Point(endOffset, tPos));
            }
            else
            {
                var halfLen = (thickness * 0.28) * (strength / 0.45f);
                if (isHorizontal)
                    context.DrawLine(pen, new Point(tPos, center - halfLen), new Point(tPos, center + halfLen));
                else
                    context.DrawLine(pen, new Point(center - halfLen, tPos), new Point(center + halfLen, tPos));
            }
        }
    }

    private static readonly Typeface MeasureLabelTypeface = new("Inter, Arial, sans-serif");
    private readonly Dictionary<int, (string Text, FormattedText Formatted)> _measureLabelCache = new();

    private FormattedText GetOrCreateMeasureLabel(int measure, double seconds)
    {
        var text = $"#{measure:D3} ({seconds:F4}s)";
        if (_measureLabelCache.TryGetValue(measure, out var cached) && cached.Text == text)
            return cached.Formatted;

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            MeasureLabelTypeface,
            11.0,
            Brushes.LightGray);

        _measureLabelCache[measure] = (text, formatted);
        return formatted;
    }

    private void DrawBeatGrid(
        DrawingContext context,
        double thickness,
        double timelineLength,
        bool isHorizontal)
    {
        if (timelineLength <= 0 || Bpm <= 0)
            return;

        var subBeatPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 150, 160, 170)), 1);
        var beatPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 200, 210, 220)), 1);
        var measurePen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 1.5);

        foreach (var line in EnumerateGridLines(timelineLength))
        {
            var pen = line.Kind switch
            {
                GridLineKind.Measure => measurePen,
                GridLineKind.Beat => beatPen,
                _ => subBeatPen,
            };

            if (isHorizontal)
                context.DrawLine(pen, new Point(line.Position, 0), new Point(line.Position, thickness));
            else
                context.DrawLine(pen, new Point(0, line.Position), new Point(thickness, line.Position));

            if (line.Kind != GridLineKind.Measure)
                continue;

            // 마디 번호와 그 지점의 초. BPM 을 맞출 때 소리와 대조하는 기준이 된다.
            var label = GetOrCreateMeasureLabel(line.Measure, line.Seconds);

            if (isHorizontal)
                context.DrawText(label, new Point(line.Position + 3, 8));
            else
                context.DrawText(label, new Point(8, line.Position - label.Height - 2));
        }
    }
}
