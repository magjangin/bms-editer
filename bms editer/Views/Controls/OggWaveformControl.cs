using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using bms_editer.Services;

namespace bms_editer.Views.Controls;

// 노트 그리드 왼쪽에 배치되는 OGG 파형 칸.
// Peaks가 없으면 자리 표시용 파형을, 있으면 디코딩된 실제 피크 데이터를 그린다.
public sealed class OggWaveformControl : TimelineControlBase
{
    public static readonly StyledProperty<IReadOnlyList<float>?> PeaksProperty =
        AvaloniaProperty.Register<OggWaveformControl, IReadOnlyList<float>?>(nameof(Peaks));

    public static readonly StyledProperty<IReadOnlyList<float>?> OnsetsProperty =
        AvaloniaProperty.Register<OggWaveformControl, IReadOnlyList<float>?>(nameof(Onsets));

    public event EventHandler<WaveformScrubRequestedEventArgs>? ScrubRequested;

    // 디코딩된 OGG의 다운샘플링된 피크(0~1) 배열. 전체 트랙 길이를 그리드 전체 높이에 맞춰 그린다.
    public IReadOnlyList<float>? Peaks
    {
        get => GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    // 음량이 순간적으로 튀는 정도(0~1). 박자를 찾기 쉽도록 파형 위에 밝은 마커로 표시한다.
    public IReadOnlyList<float>? Onsets
    {
        get => GetValue(OnsetsProperty);
        set => SetValue(OnsetsProperty, value);
    }

    static OggWaveformControl()
    {
        AffectsRender<OggWaveformControl>(PeaksProperty, OnsetsProperty);
    }

    public OggWaveformControl()
    {
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var timelineLength = GetTimelineHeight();
        if (IsHorizontalView)
        {
            var height = double.IsInfinity(availableSize.Height) ? 220 : availableSize.Height;
            return new Size(timelineLength, height);
        }
        else
        {
            var width = double.IsInfinity(availableSize.Width) ? 220 : availableSize.Width;
            return new Size(width, timelineLength);
        }
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width > 0 ? Bounds.Width : 220;
        var height = Bounds.Height > 0 ? Bounds.Height : 220;

        // 시간축 길이는 공식에서 구한다. Bounds 에서 읽으면 부모가 늘려 잡았을 때
        // **그린 파형과 스크럽 좌표가 서로 다른 축을 보게 된다.**
        // (RequestScrub 은 원래부터 GetTimelineHeight() 를 쓰고 있었다)
        var timelineLength = GetTimelineHeight();
        var waveformThickness = IsHorizontalView ? height : width;

        context.FillRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 22)), new Rect(0, 0, width, height));

        var center = waveformThickness / 2;
        if (IsHorizontalView)
        {
            context.DrawLine(new Pen(Brushes.DimGray, 1), new Point(0, center), new Point(width, center));
        }
        else
        {
            context.DrawLine(new Pen(Brushes.DimGray, 1), new Point(center, 0), new Point(center, height));
        }

        var wavePen = new Pen(new SolidColorBrush(Color.FromRgb(150, 170, 160)), 1);
        var fillBrush = new SolidColorBrush(Color.FromArgb(125, 120, 150, 135));
        var maxAmplitude = (center - 8) * 0.58 * Math.Clamp(HorizontalZoom, 0.1, 4.0);
        var peaks = Peaks;

        TryGetVisibleTimelineRange(timelineLength, out var minPos, out var maxPos);

        if (peaks is { Count: > 1 })
        {
            DrawBlockWaveform(context, peaks, timelineLength, center, maxAmplitude, fillBrush, wavePen, IsHorizontalView, minPos, maxPos);
            DrawOnsetMarkers(context, waveformThickness, timelineLength, Onsets, IsHorizontalView, minPos, maxPos);
        }
        else
        {
            var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
            var startPos = Math.Max(0.0, Math.Floor(minPos / 2.0) * 2.0);
            var endPos = Math.Min(timelineLength, Math.Ceiling(maxPos / 2.0) * 2.0);

            for (var tPos = startPos; tPos <= endPos; tPos += 2.0)
            {
                if (IsHorizontalView)
                {
                    var fMeasurePosition = rowHeight > 0 ? tPos / rowHeight : 0;
                    var ft = fMeasurePosition * 6.0;
                    var fAmplitude = Math.Abs(Math.Sin(ft) * 0.6 + Math.Sin(ft * 2.7 + 1.3) * 0.3 + Math.Sin(ft * 5.3) * 0.1);
                    var fHalf = fAmplitude * maxAmplitude;
                    context.DrawLine(wavePen, new Point(tPos, center - fHalf), new Point(tPos, center + fHalf));
                }
                else
                {
                    var measurePosition = rowHeight > 0 ? (timelineLength - tPos) / rowHeight : 0;
                    var t = measurePosition * 6.0;
                    var amplitude = Math.Abs(Math.Sin(t) * 0.6 + Math.Sin(t * 2.7 + 1.3) * 0.3 + Math.Sin(t * 5.3) * 0.1);
                    var half = amplitude * maxAmplitude;
                    context.DrawLine(wavePen, new Point(center - half, tPos), new Point(center + half, tPos));
                }
            }
        }

        DrawBeatGrid(context, waveformThickness, timelineLength, IsHorizontalView, minPos, maxPos);
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
        if (!point.Properties.IsMiddleButtonPressed)
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

    // 버튼을 뗄 때 한 번만 실제로 재생을 옮긴다. 드래그 중에 매번 옮기면
    // 오디오 장치를 계속 여닫아 딸깍거린다. (알려진 문제 24번)
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

    private static void DrawBlockWaveform(
        DrawingContext context,
        IReadOnlyList<float> peaks,
        double timelineLength,
        double center,
        double maxAmplitude,
        IBrush fillBrush,
        IPen outlinePen,
        bool isHorizontal,
        double minPos,
        double maxPos)
    {
        var blockLength = 2.0;
        var displayPointCount = Math.Max(2, (int)Math.Ceiling(timelineLength / blockLength));

        var minIndex = Math.Max(0, (int)Math.Floor(minPos / blockLength) - 1);
        var maxIndex = Math.Min(displayPointCount, (int)Math.Ceiling(maxPos / blockLength) + 1);

        for (var i = minIndex; i < maxIndex; i++)
        {
            var tPos = i * blockLength;
            var sourceIndex = isHorizontal ? i : (displayPointCount - 1 - i);
            var half = GetDisplayPeak(peaks, sourceIndex, displayPointCount) * maxAmplitude;
            if (half < 0.5)
                continue;

            if (isHorizontal)
            {
                var rect = new Rect(tPos, center - half, Math.Max(1, blockLength - 0.35), half * 2);
                context.FillRectangle(fillBrush, rect);
                if (i % 3 == 0)
                    context.DrawLine(outlinePen, new Point(tPos, center - half), new Point(tPos, center + half));
            }
            else
            {
                var rect = new Rect(center - half, tPos, half * 2, Math.Max(1, blockLength - 0.35));
                context.FillRectangle(fillBrush, rect);
                if (i % 3 == 0)
                    context.DrawLine(outlinePen, new Point(center - half, tPos), new Point(center + half, tPos));
            }
        }
    }

    // 온셋 마커는 한 화면에 최대 2만 개가 그려진다. 색은 알파(25~145)만 달라지므로
    // 그 121가지를 미리 만들어 두고 나눠 쓴다. 예전에는 마커마다 Pen+Brush 를 새로 만들었다.
    private static readonly IPen[] OnsetPens = CreateOnsetPens();

    private static IPen[] CreateOnsetPens()
    {
        var pens = new IPen[146];
        for (var alpha = 25; alpha < pens.Length; alpha++)
            pens[alpha] = new Pen(new SolidColorBrush(Color.FromArgb((byte)alpha, 230, 230, 210)), 1);
        return pens;
    }

    private static IPen GetOnsetPen(byte alpha) => OnsetPens[Math.Clamp((int)alpha, 25, 145)];

    private static void DrawOnsetMarkers(
        DrawingContext context,
        double thickness,
        double timelineLength,
        IReadOnlyList<float>? onsets,
        bool isHorizontal,
        double minPos,
        double maxPos)
    {
        if (onsets is not { Count: > 1 })
            return;

        var count = onsets.Count;
        for (var i = 1; i < count; i++)
        {
            var strength = onsets[i];
            if (strength < 0.18)
                continue;

            // 버킷 -> 시각 규칙은 담은 쪽(OggPeakLoader)이 정한다. 여기서 다시 쓰지 않는다.
            //
            // 예전에는 여기만 i / (count - 1) 이라 곡 뒤로 갈수록 마커가 앞당겨졌다
            // (곡 끝에서 12~15ms). 온셋 마커는 격자를 맞추라고 있는 기준선이라,
            // 이게 어긋나면 그걸 믿고 맞춘 씽크가 통째로 틀어진다.
            var ratio = OggPeakLoader.GetBucketRatio(i, count);
            var pos = isHorizontal ? ratio * timelineLength : (1.0 - ratio) * timelineLength;
            if (pos < minPos - 20 || pos > maxPos + 20)
                continue;

            var alpha = (byte)Math.Clamp(25 + (strength * 120), 25, 145);
            var pen = GetOnsetPen(alpha);
            var half = (thickness * 0.10) + (strength * thickness * 0.18);
            var center = thickness / 2;

            if (isHorizontal)
            {
                context.DrawLine(pen, new Point(pos, center - half), new Point(pos, center + half));
            }
            else
            {
                context.DrawLine(pen, new Point(center - half, pos), new Point(center + half, pos));
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
        bool isHorizontal,
        double minPos,
        double maxPos)
    {
        if (timelineLength <= 0 || Bpm <= 0)
            return;

        var subBeatPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 150, 160, 170)), 1);
        var beatPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 200, 210, 220)), 1);
        var measurePen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 1.5);

        foreach (var line in EnumerateGridLines(timelineLength, minPos, maxPos))
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

    private static double GetDisplayPeak(IReadOnlyList<float> peaks, int displayIndex, int displayPointCount)
    {
        var start = (int)Math.Floor((double)displayIndex * peaks.Count / displayPointCount);
        var end = (int)Math.Ceiling((double)(displayIndex + 1) * peaks.Count / displayPointCount);
        start = Math.Clamp(start, 0, peaks.Count - 1);
        end = Math.Clamp(end, start + 1, peaks.Count);

        double sum = 0;
        var max = 0.0;
        for (var i = start; i < end; i++)
        {
            var value = peaks[i];
            sum += value;
            max = Math.Max(max, value);
        }

        var average = sum / (end - start);
        return (average * 0.55) + (max * 0.45);
    }

}

// IsFinal: 버튼을 뗀 순간인지. 드래그 중(false)에는 커서만 옮기고,
// 뗄 때(true) 한 번만 실제 재생 위치를 옮긴다.
public sealed class WaveformScrubRequestedEventArgs(double ratio, bool isFinal) : EventArgs
{
    public double Ratio { get; } = ratio;
    public bool IsFinal { get; } = isFinal;
}
