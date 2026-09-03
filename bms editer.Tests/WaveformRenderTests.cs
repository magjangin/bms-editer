using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using bms_editer.Services;
using bms_editer.Views.Controls;
using Xunit;

namespace bms_editer.Tests;

// 파형을 실제로 픽셀까지 그려 놓고, 소리가 튄 자리에 파형도 튀는지 재는 테스트.
//
// 계산이 맞아도 그리는 쪽에서 어긋나면 사용자가 보는 것은 틀린 그림이다.
// 여기서 잡는 것은 두 가지다.
//   * 어택이 엉뚱한 x 좌표에 그려지는 것 (시간축 변환이 격자와 어긋난 경우)
//   * 도형을 만드는 길이 통째로 막히는 것 (파형이 아예 안 나오는 경우)
// 블록마다 버킷을 몇 개씩 집계하는지는 픽셀로는 갈라낼 수 없어서
// WaveformTimeAxisTests 쪽에서 GetBlockSourceRange 로 따로 잰다.
public sealed class WaveformRenderTests
{
    private const double DurationSeconds = 10.0;
    private const double RowHeight = 16.0;
    private const int Width = 320;   // DurationSeconds * RowHeight * VerticalZoom * 4 / 2
    private const int Height = 220;

    private static void RunOnUiThread(Action body) =>
        HeadlessUnitTestSession
            .GetOrStartForAssembly(typeof(WaveformRenderTests).Assembly)
            .Dispatch(body, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    // 한 지점만 크게 튀는 피크 배열. 나머지는 조용한 지속음.
    private static float[] BuildSpike(int count, double spikeRatio, double spikeSeconds)
    {
        var values = new float[count];
        for (var i = 0; i < count; i++)
            values[i] = 0.05f;

        var spikeStart = (int)(spikeRatio * count);
        var spikeLength = Math.Max(1, (int)(spikeSeconds / DurationSeconds * count));
        for (var i = spikeStart; i < Math.Min(count, spikeStart + spikeLength); i++)
            values[i] = 1.0f;

        return values;
    }

    private static OggWaveformControl BuildControl(float[] peaks)
    {
        return new OggWaveformControl
        {
            Peaks = peaks,
            Rms = peaks,
            Onsets = Array.Empty<OnsetMarker>(),
            DurationSeconds = DurationSeconds,
            RowHeight = RowHeight,
            VerticalZoom = 1.0,
            HorizontalZoom = 1.0,
            BeatSplit = 16,
            GridMeasure = 4,
            Bpm = 141.0,
            IsHorizontalView = true,
        };
    }

    // 세로로 칠해진 파형의 두께를 x 열마다 잰다.
    private static int[] MeasureColumnThickness(Window window)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("프레임을 뜨지 못했다");

        var pixelSize = frame.PixelSize;
        var buffer = new byte[pixelSize.Width * pixelSize.Height * 4];

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(
                new PixelRect(0, 0, pixelSize.Width, pixelSize.Height),
                handle.AddrOfPinnedObject(),
                buffer.Length,
                pixelSize.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        var thickness = new int[pixelSize.Width];

        for (var y = 0; y < pixelSize.Height; y++)
        {
            for (var x = 0; x < pixelSize.Width; x++)
            {
                var offset = ((y * pixelSize.Width) + x) * 4;

                // 배경은 RGB(15,20,20), 파형은 초록빛이 도는 밝은 색이다.
                // 격자선은 회색이라 초록 성분이 붉은 성분보다 크지 않다.
                var blue = buffer[offset];
                var green = buffer[offset + 1];
                var red = buffer[offset + 2];

                if (green > 60 && green > red + 12 && green > blue + 12)
                    thickness[x]++;
            }
        }

        return thickness;
    }

    private static int IndexOfMax(int[] values)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best])
                best = i;
        }

        return best;
    }

    [Theory]
    // 소스 버킷이 화면 블록(320개)보다 많은 경우와 적은 경우를 모두 본다.
    // 둘 다 같은 자리에 그려져야 줌을 바꿔도 격자와의 관계가 유지된다.
    [InlineData(4000)]
    [InlineData(200)]
    public void 소리가_튄_자리에_파형도_튄다(int bucketCount) => RunOnUiThread(() =>
    {
        const double spikeRatio = 0.4;

        var window = new Window
        {
            Width = Width,
            Height = Height,
            Content = BuildControl(BuildSpike(bucketCount, spikeRatio, 0.05)),
        };

        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var thickness = MeasureColumnThickness(window);
        var peakColumn = IndexOfMax(thickness);
        var expectedColumn = spikeRatio * Width;

        Assert.True(thickness[peakColumn] > 0, "파형이 아예 그려지지 않았다");
        Assert.True(
            Math.Abs(peakColumn - expectedColumn) <= 3,
            $"어택이 {expectedColumn:F0}px 에 있어야 하는데 {peakColumn}px 에서 가장 두껍다");
    });

    [Fact]
    public void 조용한_구간과_어택이_뚜렷하게_구분된다() => RunOnUiThread(() =>
    {
        var window = new Window
        {
            Width = Width,
            Height = Height,
            Content = BuildControl(BuildSpike(4000, 0.5, 0.05)),
        };

        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var thickness = MeasureColumnThickness(window);

        // 조용한 구간(0.05)보다 어택 구간이 훨씬 두꺼워야 한다.
        var loud = thickness[IndexOfMax(thickness)];
        var quiet = thickness[10];

        Assert.True(loud > quiet * 4, $"어택 {loud}px, 조용한 구간 {quiet}px 로 차이가 나지 않는다");
    });
}
