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

    // 버킷별 최대 진폭. 어택이 어디서 튀었는지를 담는다.
    public IReadOnlyList<float>? Peaks
    {
        get => GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    // 버킷별 RMS. 지속음의 몸통.
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
    private static readonly IBrush WaveFillBrush = new SolidColorBrush(Color.FromArgb(125, 120, 150, 135));

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

        var maxAmplitude = (center - 8) * 0.58 * Math.Clamp(HorizontalZoom, 0.1, 4.0);
        var peaks = Peaks;

        if (peaks is { Count: > 1 })
        {
            DrawBlockWaveform(context, peaks, Rms, timelineLength, center, maxAmplitude, IsHorizontalView);
            DrawOnsetMarkers(context, waveformThickness, timelineLength, Onsets, IsHorizontalView);
        }
        else
        {
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

    // 화면 블록 한 칸의 길이(px).
    private const double BlockLength = 2.0;

    private void DrawBlockWaveform(
        DrawingContext context,
        IReadOnlyList<float> peaks,
        IReadOnlyList<float>? rms,
        double timelineLength,
        double center,
        double maxAmplitude,
        bool isHorizontal)
    {
        var displayPointCount = Math.Max(2, (int)Math.Ceiling(timelineLength / BlockLength));
        var bucketShift = GetBucketShift(peaks.Count);

        for (var i = 0; i < displayPointCount; i++)
        {
            var tPos = i * BlockLength;
            var sourceIndex = isHorizontal ? i : (displayPointCount - 1 - i);
            var half = GetDisplayAmplitude(peaks, rms, sourceIndex, displayPointCount, bucketShift) * maxAmplitude;
            if (half < 0.5)
                continue;

            if (isHorizontal)
            {
                var rect = new Rect(tPos, center - half, Math.Max(1, BlockLength - 0.35), half * 2);
                context.FillRectangle(WaveFillBrush, rect);
                if (i % 3 == 0)
                    context.DrawLine(WavePen, new Point(tPos, center - half), new Point(tPos, center + half));
            }
            else
            {
                var rect = new Rect(center - half, tPos, half * 2, Math.Max(1, BlockLength - 0.35));
                context.FillRectangle(WaveFillBrush, rect);
                if (i % 3 == 0)
                    context.DrawLine(WavePen, new Point(center - half, tPos), new Point(center + half, tPos));
            }
        }
    }

    // 음원 오프셋을 버킷 수로 환산한다. 파형을 뒤로 밀면 같은 화면 자리에서
    // 그만큼 **앞쪽** 버킷을 읽어야 한다.
    private int GetBucketShift(int bucketCount)
    {
        var duration = DurationSeconds;
        if (duration <= 0 || bucketCount <= 0 || AudioOffsetSeconds == 0)
            return 0;

        return (int)Math.Round(AudioOffsetSeconds / duration * bucketCount);
    }

    // 블록이 덮는 구간의 값. 한 칸만 찍어 읽으면 줌을 줄였을 때 어택이 사라진다.
    //
    // 표시값은 RMS(몸통)와 피크(어택)를 섞은 하나의 진폭이다. 둘을 따로 겹쳐 그려 봤는데
    // 파형이 배경이 아니라 주인공이 되어 버려서, 정작 그 위에 겹치는 마디선과 마커가
    // 묻혔다. 섞는 비율은 v0.1.0 과 같다.
    private static double GetDisplayAmplitude(
        IReadOnlyList<float> peaks,
        IReadOnlyList<float>? rms,
        int blockIndex,
        int blockCount,
        int bucketShift)
    {
        var (start, end) = OggPeakLoader.GetBlockSourceRange(blockIndex, blockCount, peaks.Count);

        start -= bucketShift;
        end -= bucketShift;

        if (end <= 0 || start >= peaks.Count)
            return 0;

        start = Math.Max(0, start);
        end = Math.Min(peaks.Count, end);

        var peak = 0f;
        for (var i = start; i < end; i++)
            peak = MathF.Max(peak, peaks[i]);

        var body = 0f;
        if (rms is not null && rms.Count == peaks.Count)
        {
            for (var i = start; i < end; i++)
                body = MathF.Max(body, rms[i]);
        }

        return Math.Min(1.0, (body * 0.75) + (peak * 0.30));
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

            // 마커는 음원 시각으로 들어온다. 오프셋을 태워 격자와 같은 축에 올린다.
            var ratio = AudioRatio(onset.Seconds);
            if (ratio < 0 || ratio > 1)
                continue;

            var pen = GetOnsetPen((byte)Math.Clamp((int)((strength * 135f) + 10f), 25, 145));
            var tPos = ToTimelinePosition(ratio, timelineLength);

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
