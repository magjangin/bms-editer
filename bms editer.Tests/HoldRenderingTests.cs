using System.IO;
using Avalonia;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Views.Controls;
using Xunit;

namespace bms_editer.Tests;

// 홀드가 격자에 "머리에서 꼬리까지 한 줄로" 실제로 그려지는지 픽셀로 본다.
//
// 좌표 계산 함수만 테스트하면, 그리는 순서가 바뀌어 몸통이 노트에 덮이거나 바인딩이 빠져
// 아무것도 안 그려져도 통과한다. 그래서 컨트롤을 실제로 그려서 읽는다.
// 그림은 %TEMP%\bms-editer-tests\holds 에 남는다.
public sealed class HoldRenderingTests
{
    // RowHeight 16 × VerticalZoom 1 × (BeatSplit 16 / GridMeasure 4) = 마디당 64px. 레인 폭 40px.
    private const double PixelsPerMeasure = 64;
    private const double LaneWidth = 40;

    private static BmsNote Note(string lane, double at, string key = "01")
    {
        var measure = (int)System.Math.Floor(at);
        return new BmsNote { LaneId = lane, Measure = measure, Position = at - measure, WavKey = key };
    }

    private static NoteGridControl Grid(BmsNote[] notes, HoldLink[] links, bool horizontal = false) => new()
    {
        Lanes = LaneDefinition.CreateDefault(),
        Notes = notes,
        HoldLinks = links,
        MeasureCount = 4,
        RowHeight = 16,
        VerticalZoom = 1,
        HorizontalZoom = 1,
        LaneWidth = LaneWidth,
        BeatSplit = 16,
        GridMeasure = 4,
        Bpm = 120,
        IsHorizontalView = horizontal,
    };

    private static string Artifact(string name) => Path.Combine(HoldTestSupport.ArtifactDirectory("holds"), name);

    [Fact]
    public void 세로_보기에서_머리부터_꼬리까지_레인_가운데에_몸통이_그려진다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var head = Note("11", 1.0);
        var tail = Note("11", 3.0);
        var snapshot = GridSnapshot.Capture(Grid(new[] { head, tail }, new[] { new HoldLink(head, tail, "Hold") }));
        snapshot.Save(Artifact("vertical.png"));

        // 레인 11은 두 번째 칸이다. 세로 보기는 아래가 0마디라 머리가 아래(y=192), 꼬리가 위(y=64).
        const double laneCenter = LaneWidth * 1.5;

        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(laneCenter, 128), "Hold"), $"몸통 한가운데 {snapshot.At(laneCenter, 128)}");
        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(laneCenter, 72), "Hold"), "꼬리 바로 아래까지 이어져야 한다");
        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(laneCenter, 184), "Hold"), "머리 바로 위부터 시작해야 한다");

        // 몸통은 꼬리를 넘지 않고, 다른 레인에는 번지지 않는다.
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(laneCenter, 52), "Hold"), "꼬리 너머로 삐져나왔다");
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(laneCenter, 204), "Hold"), "머리 너머로 삐져나왔다");
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(LaneWidth * 3.5, 128), "Hold"), "옆 레인에 번졌다");
    });

    [Fact]
    public void 가로_보기에서도_몸통이_레인을_따라_그려진다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var head = Note("11", 1.0);
        var tail = Note("11", 3.0);
        var snapshot = GridSnapshot.Capture(Grid(new[] { head, tail }, new[] { new HoldLink(head, tail, "Hold") }, horizontal: true));
        snapshot.Save(Artifact("horizontal.png"));

        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(128, LaneWidth * 1.5), "Hold"));
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(128, LaneWidth * 3.5), "Hold"));
    });

    [Fact]
    public void BPM이_바뀌는_구간을_지나도_몸통_끝이_꼬리_노트와_만난다() => HoldTestSupport.RunOnUiThread(() =>
    {
        // 수용 조건 A-5. 음원이 있으면 위치는 마디가 아니라 시각을 따른다.
        // 2마디부터 BPM 이 두 배라, 3마디 꼬리는 마디 비례 자리(y=320)가 아니라 y=352 에 온다.
        var head = Note("11", 1.0);
        var tail = Note("11", 3.0);
        var grid = Grid(new[] { head, tail }, new[] { new HoldLink(head, tail, "Hold") });
        grid.DurationSeconds = 16;
        grid.Timeline = new ChartTimeline(120, new System.Collections.Generic.Dictionary<int, double>(), new[] { new BpmChange(2, 0, 240) });

        var snapshot = GridSnapshot.Capture(grid);
        snapshot.Save(Artifact("bpm-change.png"));

        const double laneCenter = LaneWidth * 1.5;
        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(laneCenter, 360), "Hold"), "꼬리 바로 아래까지 이어져야 한다");
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(laneCenter, 336), "Hold"), "몸통이 꼬리 노트 너머로 늘어났다");
    });

    [Fact]
    public void 채널을_건너는_짝은_선으로_잇는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        // 스타트레일 게이트처럼 게임이 여러 채널을 레인 하나로 모으는 경우.
        var open = Note("16", 1.0, "04");
        var close = Note("18", 3.0, "05");
        var snapshot = GridSnapshot.Capture(Grid(new[] { open, close }, new[] { new HoldLink(open, close, "Gate") }));
        snapshot.Save(Artifact("cross-lane.png"));

        // 16은 첫 칸(가운데 x=20, y=192), 18은 마지막 칸(가운데 x=260, y=64). 그 한가운데.
        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(140, 128), "Gate"), $"선 한가운데 {snapshot.At(140, 128)}");
    });

    [Fact]
    public void 몸통_사각형은_레인_가운데에서_머리부터_꼬리까지다()
    {
        var vertical = NoteGridControl.ComputeHoldBodyRect(40, 40, headPos: 192, tailPos: 64, isHorizontalView: false);
        var horizontal = NoteGridControl.ComputeHoldBodyRect(40, 40, headPos: 64, tailPos: 192, isHorizontalView: true);

        var width = 40 * NoteGridControl.HoldBodyWidthRatio;
        Assert.Equal(new Rect(40 + (40 - width) / 2, 64, width, 128), vertical);
        Assert.Equal(new Rect(64, 40 + (40 - width) / 2, 128, width), horizontal);
    }
}
