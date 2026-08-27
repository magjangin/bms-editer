using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

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

        var timelineLength = IsHorizontalView ? width : height;
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

        if (peaks is { Count: > 1 })
        {
            DrawBlockWaveform(context, peaks, timelineLength, center, maxAmplitude, fillBrush, wavePen, IsHorizontalView);
            DrawOnsetMarkers(context, waveformThickness, timelineLength, Onsets, IsHorizontalView);
        }
        else
        {
            var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
            for (var tPos = 0.0; tPos < timelineLength; tPos += 2.0)
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

        DrawBeatGrid(context, waveformThickness, timelineLength, IsHorizontalView);
        DrawGridSyncFlash(context, width, height);
        DrawPlaybackCursor(context, width, height);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed)
        {
            var pos = IsHorizontalView ? point.Position.X : point.Position.Y;
            RequestScrub(pos, e);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed)
        {
            var pos = IsHorizontalView ? point.Position.X : point.Position.Y;
            RequestScrub(pos, e);
        }
    }

    private void RequestScrub(double pos, PointerEventArgs e)
    {
        if (DurationSeconds <= 0)
            return;

        var timelineLength = GetTimelineHeight();
        if (timelineLength <= 0)
            return;

        var ratio = IsHorizontalView ? Math.Clamp(pos / timelineLength, 0, 1) : Math.Clamp(1.0 - (pos / timelineLength), 0, 1);
        ScrubRequested?.Invoke(this, new WaveformScrubRequestedEventArgs(ratio));
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
        bool isHorizontal)
    {
        var blockLength = 2.0;
        var displayPointCount = Math.Max(2, (int)Math.Ceiling(timelineLength / blockLength));

        for (var i = 0; i < displayPointCount; i++)
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

    private static void DrawOnsetMarkers(DrawingContext context, double thickness, double timelineLength, IReadOnlyList<float>? onsets, bool isHorizontal)
    {
        if (onsets is not { Count: > 1 })
            return;

        var count = onsets.Count;
        var denom = count - 1;
        for (var i = 1; i < count; i++)
        {
            var strength = onsets[i];
            if (strength < 0.18)
                continue;

            var ratio = (double)i / denom;
            var alpha = (byte)Math.Clamp(25 + (strength * 120), 25, 145);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 230, 230, 210)), 1);
            var half = (thickness * 0.10) + (strength * thickness * 0.18);
            var center = thickness / 2;

            if (isHorizontal)
            {
                var x = ratio * timelineLength;
                context.DrawLine(pen, new Point(x, center - half), new Point(x, center + half));
            }
            else
            {
                var y = (1.0 - ratio) * timelineLength;
                context.DrawLine(pen, new Point(center - half, y), new Point(center + half, y));
            }
        }
    }

    private void DrawBeatGrid(DrawingContext context, double thickness, double timelineLength, bool isHorizontal)
    {
        if (timelineLength <= 0 || Bpm <= 0)
            return;

        var split = Math.Max(1, BeatSplit);
        var subBeatPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 150, 160, 170)), 1);
        var beatPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 200, 210, 220)), 1);
        var measurePen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 1.5);

        if (DurationSeconds > 0)
        {
            for (var index = 0; ; index++)
            {
                var seconds = MeasurePositionToSeconds((double)index / split);
                if (seconds > DurationSeconds)
                    return;

                // 오프셋이 양수면 마디 000이 오디오 시작보다 뒤에 있다.
                if (seconds < 0)
                    continue;

                var tPos = SecondsToTPos(seconds, timelineLength);

                var isMeasure = Mod(index, split) == 0;
                var pen = isMeasure
                    ? measurePen
                    : IsMeasureBeatLine(index, split, GridMeasure)
                        ? beatPen
                        : subBeatPen;

                if (isHorizontal)
                {
                    context.DrawLine(pen, new Point(tPos, 0), new Point(tPos, thickness));
                }
                else
                {
                    context.DrawLine(pen, new Point(0, tPos), new Point(thickness, tPos));
                }

                if (isMeasure)
                {
                    var measure = index / split;
                    var text = $"#{measure:D3} ({seconds:F4}s)";
                    var formattedText = new FormattedText(
                        text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter, Arial, sans-serif"),
                        11.0,
                        Brushes.LightGray);
                    
                    if (isHorizontal)
                    {
                        context.DrawText(formattedText, new Point(tPos + 3, 8));
                    }
                    else
                    {
                        context.DrawText(formattedText, new Point(8, tPos - formattedText.Height - 2));
                    }
                }
            }
        }
        else
        {
            var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
            var secondsPerMeasure = 240.0 / Bpm;

            for (var measure = 0; measure <= MeasureCount; measure++)
            {
                var seconds = measure * secondsPerMeasure;
                var tPos = isHorizontal ? (measure * rowHeight) : (timelineLength - measure * rowHeight);

                for (var beat = 0; beat < split; beat++)
                {
                    if (beat > 0)
                    {
                        var beatTPos = isHorizontal ? (tPos + (rowHeight * beat / split)) : (tPos - (rowHeight * beat / split));
                        if (beatTPos >= 0 && beatTPos <= timelineLength)
                        {
                            var pen = IsMeasureBeatLine(beat, split, GridMeasure) ? beatPen : subBeatPen;
                            if (isHorizontal)
                                context.DrawLine(pen, new Point(beatTPos, 0), new Point(beatTPos, thickness));
                            else
                                context.DrawLine(pen, new Point(0, beatTPos), new Point(thickness, beatTPos));
                        }
                    }
                }

                if (tPos >= 0 && tPos <= timelineLength)
                {
                    if (isHorizontal)
                        context.DrawLine(measurePen, new Point(tPos, 0), new Point(tPos, thickness));
                    else
                        context.DrawLine(measurePen, new Point(0, tPos), new Point(thickness, tPos));

                    var text = $"#{measure:D3} ({seconds:F4}s)";
                    var formattedText = new FormattedText(
                        text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter, Arial, sans-serif"),
                        11.0,
                        Brushes.LightGray);

                    if (isHorizontal)
                    {
                        context.DrawText(formattedText, new Point(tPos + 3, 8));
                    }
                    else
                    {
                        context.DrawText(formattedText, new Point(8, tPos - formattedText.Height - 2));
                    }
                }

                if (isHorizontal)
                    tPos += rowHeight;
                else
                    tPos -= rowHeight;
            }
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

public sealed class WaveformScrubRequestedEventArgs(double ratio) : EventArgs
{
    public double Ratio { get; } = ratio;
}
