using System.Collections.Generic;
using System.Linq;
using bms_editer.Views.Controls;
using Xunit;

namespace bms_editer.Tests;

// 격자선 계산은 네 벌로 흩어져 있던 걸 한 곳으로 모은 자리라, 어긋나면
// 화면 전체가 조용히 틀어진다. 눈으로는 "좀 이상한데" 정도로만 보여서
// 좌표를 직접 확인해 둔다.
//
// EnumerateGridLines 는 protected 라 시험용 창구를 뚫어주는 껍데기를 쓴다.
public sealed class GridLineTests
{
    private sealed class Probe : TimelineControlBase
    {
        public IReadOnlyList<(double Position, string Kind, int Measure, double Seconds)> Lines(double length) =>
            EnumerateGridLines(length)
                .Select(line => (line.Position, line.Kind.ToString(), line.Measure, line.Seconds))
                .ToList();

        public IReadOnlyList<(double Position, string Kind, int Measure, double Seconds)> LinesWithCulling(double length, double minPos, double maxPos) =>
            EnumerateGridLines(length, minPos, maxPos)
                .Select(line => (line.Position, line.Kind.ToString(), line.Measure, line.Seconds))
                .ToList();

        public double TimelineLength() => GetTimelineHeight();
    }

    // 음원 없이 마디 기준으로 그릴 때. 마디 높이 × 마디 수 = 타임라인 길이.
    private static Probe MeasureModeProbe(int measureCount = 2, int beatSplit = 4)
    {
        return new Probe
        {
            DurationSeconds = 0,
            Bpm = 120,
            MeasureCount = measureCount,
            BeatSplit = beatSplit,
            GridMeasure = 4,
            RowHeight = 10,
            VerticalZoom = 1,
            IsHorizontalView = true,
        };
    }

    [Fact]
    public void 마디_기준_가로뷰는_0에서_끝까지_고르게_긋는다()
    {
        var probe = MeasureModeProbe();
        // BeatSplit 4 / GridMeasure 4 이면 간격 배율은 1이라 마디 높이 = RowHeight = 10
        var lines = probe.Lines(20.0);

        Assert.Equal(
            new[] { 0.0, 2.5, 5.0, 7.5, 10.0, 12.5, 15.0, 17.5, 20.0 },
            lines.Select(l => l.Position));
    }

    [Fact]
    public void 마지막_마디의_닫는_선까지_그린다()
    {
        var lines = MeasureModeProbe().Lines(20.0);

        // 예전 NoteGridControl 은 마지막 닫는 선을 빠뜨려서 파형 칸과 어긋났다.
        Assert.Equal(20.0, lines[^1].Position);
        Assert.Equal("Measure", lines[^1].Kind);
        Assert.Equal(2, lines[^1].Measure);
    }

    [Fact]
    public void 마디_시작과_박자와_보조선이_구분된다()
    {
        var probe = MeasureModeProbe(measureCount: 1, beatSplit: 8);
        probe.GridMeasure = 4;

        var kinds = probe.Lines(20.0).Select(l => l.Kind).ToArray();

        // 8분할 / 4박 = 두 칸마다 박자선
        Assert.Equal(
            new[] { "Measure", "SubBeat", "Beat", "SubBeat", "Beat", "SubBeat", "Beat", "SubBeat", "Measure" },
            kinds);
    }

    [Fact]
    public void 세로뷰는_아래가_0초라_위치가_뒤집힌다()
    {
        var probe = MeasureModeProbe();
        probe.IsHorizontalView = false;

        var lines = probe.Lines(20.0);

        Assert.Equal(20.0, lines[0].Position);
        Assert.Equal(0.0, lines[^1].Position);
    }

    [Fact]
    public void 음원이_있으면_초_단위로_긋고_길이를_넘지_않는다()
    {
        var probe = new Probe
        {
            DurationSeconds = 4.0,
            Bpm = 120,          // 마디 하나 = 240/120 = 2초
            BeatSplit = 4,      // 한 칸 = 0.5초
            GridMeasure = 4,
            IsHorizontalView = true,
        };

        var lines = probe.Lines(100.0);

        Assert.Equal(
            new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0 },
            lines.Select(l => l.Seconds));

        // 마지막 선이 딱 곡 끝에 놓인다
        Assert.Equal(100.0, lines[^1].Position);
        Assert.All(lines, l => Assert.InRange(l.Position, 0.0, 100.0));
    }

    [Fact]
    public void 음원_모드에서_마디_번호가_올바르게_붙는다()
    {
        var probe = new Probe
        {
            DurationSeconds = 4.0,
            Bpm = 120,
            BeatSplit = 4,
            GridMeasure = 4,
            IsHorizontalView = true,
        };

        var measureLines = probe.Lines(100.0).Where(l => l.Kind == "Measure").ToArray();

        Assert.Equal(new[] { 0, 1, 2 }, measureLines.Select(l => l.Measure));
        Assert.Equal(new[] { 0.0, 2.0, 4.0 }, measureLines.Select(l => l.Seconds));
    }

    // ── 타임라인 길이 공식 ─────────────────────────────────────────────────────
    //
    // 이 식은 컨트롤 안(GetTimelineHeight)과 밖(창의 스크롤·스크럽 좌표)이 같이 쓴다.
    // 예전에는 밖에 복사본이 있어 한쪽만 바뀌면 스크럽한 자리와 소리가 어긋났다.

    [Theory]
    [InlineData(10.0, 0, 16.0, 8.0, 16, 4, 2560.0)]      // 음원 10초: 10 × 16 × 8 × 4 / 2
    [InlineData(0.0, 32, 16.0, 8.0, 16, 4, 16384.0)]     // 음원 없음: 32마디 × 16 × 8 × 4
    [InlineData(10.0, 0, 16.0, 8.0, 4, 4, 640.0)]        // 간격 배율 1
    [InlineData(10.0, 0, 16.0, 8.0, 2, 8, 640.0)]        // 배율은 1 밑으로 내려가지 않는다
    [InlineData(0.001, 0, 1.0, 1.0, 1, 1, 1.0)]          // 음원이 있으면 길이는 1 보다 작아지지 않는다
    public void 타임라인_길이_공식(
        double duration, int measureCount, double rowHeight, double zoom, int split, int gridMeasure, double expected)
    {
        Assert.Equal(
            expected,
            TimelineControlBase.ComputeTimelineLength(duration, measureCount, rowHeight, zoom, split, gridMeasure),
            9);
    }

    [Fact]
    public void 컨트롤이_그리는_길이와_공식이_같다()
    {
        var probe = new Probe
        {
            DurationSeconds = 183.7,
            MeasureCount = 40,
            RowHeight = 16,
            VerticalZoom = 6.5,
            BeatSplit = 24,
            GridMeasure = 4,
        };

        Assert.Equal(
            TimelineControlBase.ComputeTimelineLength(183.7, 40, 16, 6.5, 24, 4),
            probe.TimelineLength(),
            9);
    }

    [Fact]
    public void BeatSplit이_0이어도_터지지_않는다()
    {
        var probe = MeasureModeProbe(measureCount: 1);
        probe.BeatSplit = 0;

        var lines = probe.Lines(20.0);

        Assert.All(lines, l => Assert.Equal("Measure", l.Kind));
    }

    // ── 뷰포트 컬링 (Viewport Culling) ─────────────────────────────────────────

    [Fact]
    public void 뷰포트_컬링_적용시_지정한_범위_안의_격자선만_반환한다()
    {
        var probe = MeasureModeProbe(measureCount: 10, beatSplit: 4);
        var allLines = probe.Lines(100.0);
        var culledLines = probe.LinesWithCulling(100.0, 20.0, 40.0);

        Assert.True(culledLines.Count < allLines.Count);
        Assert.All(culledLines, l => Assert.InRange(l.Position, 19.5, 40.5));
    }

    [Fact]
    public void 음원_모드에서_뷰포트_컬링이_올바르게_동작한다()
    {
        var probe = new Probe
        {
            DurationSeconds = 10.0,
            Bpm = 120,
            BeatSplit = 4,
            GridMeasure = 4,
            IsHorizontalView = true,
        };

        var allLines = probe.Lines(1000.0);
        var culledLines = probe.LinesWithCulling(1000.0, 200.0, 500.0);

        Assert.True(culledLines.Count < allLines.Count);
        Assert.All(culledLines, l => Assert.InRange(l.Position, 199.5, 500.5));
    }

    [Fact]
    public void CalculateVisibleTimelineRange_가로_세로_버퍼_계산이_정확하다()
    {
        // 가로 뷰: X축 기준 [Left - buffer, Right + buffer]
        var rectH = new Avalonia.Rect(200, 50, 600, 300);
        var (minH, maxH) = TimelineControlBase.CalculateVisibleTimelineRange(rectH, 5000.0, isHorizontalView: true, buffer: 100.0);
        Assert.Equal(100.0, minH); // 200 - 100
        Assert.Equal(900.0, maxH); // (200 + 600) + 100

        // 세로 뷰: Y축 기준 [Top - buffer, Bottom + buffer]
        var rectV = new Avalonia.Rect(50, 400, 300, 500);
        var (minV, maxV) = TimelineControlBase.CalculateVisibleTimelineRange(rectV, 5000.0, isHorizontalView: false, buffer: 150.0);
        Assert.Equal(250.0, minV);  // 400 - 150
        Assert.Equal(1050.0, maxV); // (400 + 500) + 150

        // 경계 조건: 0 이하 클램프
        var rectZero = new Avalonia.Rect(0, 50, 100, 100);
        var (minZero, _) = TimelineControlBase.CalculateVisibleTimelineRange(rectZero, 5000.0, isHorizontalView: true, buffer: 200.0);
        Assert.Equal(0.0, minZero);

        // 비정상 Rect나 0 너비는 전체 범위 반환
        var (minEmpty, maxEmpty) = TimelineControlBase.CalculateVisibleTimelineRange(default, 5000.0, isHorizontalView: false);
        Assert.Equal(0.0, minEmpty);
        Assert.Equal(5000.0, maxEmpty);
    }

    [Fact]
    public void 대용량_차트에서_컬링_적용시_렌더_격자선_개수가_획기적으로_줄어든다_측정()
    {
        // 5분 곡, 200마디, 16분할 = 3,200개 격자선
        var probe = MeasureModeProbe(measureCount: 200, beatSplit: 16);
        var totalLength = probe.TimelineLength();

        var allLines = probe.Lines(totalLength);
        Assert.True(allLines.Count >= 3200);

        // 뷰포트 높이 800px에 해당하는 구간 [3000, 3800]만 조회
        var culledLines = probe.LinesWithCulling(totalLength, 3000.0, 3800.0);

        // 전체 3200개 중 화면에 보이는 약 320개 정도만 반환되어 연산량이 대폭 감소한다
        Assert.True(culledLines.Count < 400);
        Assert.True(culledLines.Count < allLines.Count * 0.15);
    }
}
