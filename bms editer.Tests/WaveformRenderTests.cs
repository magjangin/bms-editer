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

    // 어택이 전혀 없는 기준 그림용. 조용한 지속음만 깔린다.
    private static float[] BuildFlat(int count)
    {
        var values = new float[count];
        for (var i = 0; i < count; i++)
            values[i] = 0.05f;

        return values;
    }

    private static OggWaveformControl BuildControl(float[] peaks, double audioOffsetSeconds = 0)
    {
        return new OggWaveformControl
        {
            AudioOffsetSeconds = audioOffsetSeconds,
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

    // 창을 하나 띄워 실제로 그린 뒤 픽셀을 그대로 돌려준다.
    private static (byte[] Pixels, int Width, int Height) Capture(float[] values, double audioOffsetSeconds = 0)
    {
        var window = new Window
        {
            Width = Width,
            Height = Height,
            Content = BuildControl(values, audioOffsetSeconds),
        };

        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

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

        window.Close();
        return (buffer, pixelSize.Width, pixelSize.Height);
    }

    // 어택이 있는 그림과 없는 그림을 떠서 x 열마다 달라진 픽셀 수를 센다.
    //
    // 색으로 파형만 골라내려고도 해 봤는데, 격자선이 파형 위에 겹쳐 그려지는 데다
    // 마디선·박선·보조선이 저마다 다른 알파로 섞여서 어떤 임계를 잡아도 새어 들어왔다.
    // 두 번 그려서 빼면 "파형 때문에 달라진 자리"만 정확히 남는다.
    private static int[] MeasureChangedColumns(float[] withSpike, float[] flat, double audioOffsetSeconds = 0)
    {
        var (spikePixels, width, height) = Capture(withSpike, audioOffsetSeconds);
        var (flatPixels, _, _) = Capture(flat, audioOffsetSeconds);

        var changed = new int[width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;

                // 안티에일리어싱 때문에 1~2 정도는 늘 흔들린다.
                if (Math.Abs(spikePixels[offset + 1] - flatPixels[offset + 1]) > 8)
                    changed[x]++;
            }
        }

        return changed;
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

        var changed = MeasureChangedColumns(
            BuildSpike(bucketCount, spikeRatio, 0.05),
            BuildFlat(bucketCount));

        var peakColumn = IndexOfMax(changed);
        var expectedColumn = spikeRatio * Width;

        Assert.True(changed[peakColumn] > 0, "파형이 아예 그려지지 않았다");
        Assert.True(
            Math.Abs(peakColumn - expectedColumn) <= 3,
            $"어택이 {expectedColumn:F0}px 에 있어야 하는데 {peakColumn}px 에서 가장 두껍다");
    });

    [Fact]
    public void 조용한_구간과_어택이_뚜렷하게_구분된다() => RunOnUiThread(() =>
    {
        var changed = MeasureChangedColumns(
            BuildSpike(4000, 0.5, 0.05),
            BuildFlat(4000));

        // 어택이 있는 자리만 달라져야 한다. 조용한 구간은 두 그림이 같다.
        var loud = changed[IndexOfMax(changed)];
        var quiet = changed[10];

        Assert.True(loud > 20, $"어택 자리에서 바뀐 픽셀이 {loud}개뿐이다");
        Assert.Equal(0, quiet);
    });

    [Fact]
    public void 오프셋을_주면_파형이_그만큼_뒤로_간다() => RunOnUiThread(() =>
    {
        // 화면은 10초를 320px 에 담으므로 초당 32px 다. 0.5초를 밀면 16px 움직여야 한다.
        const double offsetSeconds = 0.5;
        const double pixelsPerSecond = Width / DurationSeconds;

        var spike = BuildSpike(4000, 0.4, 0.05);
        var flat = BuildFlat(4000);

        var before = IndexOfMax(MeasureChangedColumns(spike, flat));
        var after = IndexOfMax(MeasureChangedColumns(spike, flat, offsetSeconds));

        var movedPixels = after - before;
        var expectedPixels = offsetSeconds * pixelsPerSecond;

        Assert.True(
            Math.Abs(movedPixels - expectedPixels) <= 2,
            $"{expectedPixels:F0}px 움직여야 하는데 {movedPixels}px 움직였다");
    });
}
