using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Services.Holds;
using bms_editer.Views.Controls;
using Xunit;
using Xunit.Abstractions;

namespace bms_editer.Tests;

// 이 컴퓨터의 실제 게임 폴더에 있는 차트로 홀드 짝을 검증한다.
//
// 기대값을 손으로 적지 않는다. 게임 모드의 짝 맞추기 코드를 옮겨 둔 오라클(GameOracles)이
// 같은 파일을 **에디터 파서와 따로** 읽어 짝을 내고, 에디터 결과와 하나하나 비교한다.
// 그림은 %TEMP%\bms-editer-tests\real-holds 에, 차트별 요약은 그 아래 summary 에 남는다.
//
// 차트를 못 찾는 컴퓨터(CI 등)에서는 한 줄만 남기고 지나간다.
// BMS_EDITER_GAME_CHARTS="deflate=D:\Games\DEFLATE;..." 로 게임 폴더를 줄 수 있다.
public sealed class RealGameChartTests
{
    private const string NoCharts = "(이 컴퓨터에서 게임 차트를 찾지 못함)";

    // RowHeight 16 × VerticalZoom 1 × (BeatSplit 16 / GridMeasure 4) = 마디당 64px. 레인 폭 40px.
    private const double PixelsPerMeasure = 64;
    private const double LaneWidth = 40;

    private readonly ITestOutputHelper _output;

    public RealGameChartTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Charts()
    {
        var charts = GameChartCorpus.Charts;
        if (charts.Count == 0)
        {
            yield return new object[] { NoCharts, NoCharts, NoCharts };
            yield break;
        }

        foreach (var chart in charts)
            yield return new object[] { chart.ProfileId, chart.Game, chart.Path };
    }

    [Fact]
    public void 게임_차트_탐색_결과를_남긴다()
    {
        var charts = GameChartCorpus.Charts;
        var lines = charts.Select(c => $"{c.ProfileId}\t{c.Game}\t{c.Path}").ToArray();

        File.WriteAllLines(Path.Combine(HoldTestSupport.ArtifactDirectory("real-holds"), "discovered.tsv"), lines, Encoding.UTF8);
        _output.WriteLine($"찾은 차트 {charts.Count}개");
        foreach (var line in lines)
            _output.WriteLine(line);

        Assert.All(charts, c => Assert.NotNull(GameProfileCatalog.Default.Find(c.ProfileId)));
    }

    [Theory]
    [MemberData(nameof(Charts))]
    public void 에디터의_홀드_짝이_게임_모드_코드와_똑같다(string profileId, string game, string path)
    {
        if (Skipped(profileId))
            return;

        var parsed = BmsParser.Parse(path);
        var editor = HoldPairingEngine.Pair(parsed.Chart.Notes, HoldTestSupport.WavTexts(parsed), Profile(profileId));
        var oracle = GameOracles.Run(profileId, RawChart.Read(path));

        var editorPairs = Keys(editor);
        var oraclePairs = oracle.Pairs.OrderBy(k => k.ToString(), StringComparer.Ordinal).ToArray();
        var editorOrphanHeads = editor.Diagnostics.Count(d => d.Kind is HoldDiagnosticKind.OrphanHead or HoldDiagnosticKind.ReplacedHead);
        var editorOrphanTails = editor.Diagnostics.Count(d => d.Kind == HoldDiagnosticKind.OrphanTail);

        var summary =
            $"{game} / {SongName(path)} | 노트 {parsed.Chart.Notes.Count} | 짝 {editorPairs.Length} ({KindCounts(editor)}) | " +
            $"고아 시작 {editorOrphanHeads} · 고아 끝 {editorOrphanTails} | " +
            $"오라클 짝 {oraclePairs.Length} · 고아 {oracle.OrphanHeads}/{oracle.OrphanTails}";

        _output.WriteLine(summary);
        WriteSummary(profileId, path, summary, editor);

        Assert.True(oracle.HiddenFromEditor == 0,
            $"게임은 에디터가 편집하지 않는 채널에서 홀드 노트 {oracle.HiddenFromEditor}개를 읽습니다. 에디터 화면에는 보이지 않습니다.");
        Assert.Equal(oraclePairs, editorPairs);
        Assert.Equal(oracle.OrphanHeads, editorOrphanHeads);
        Assert.Equal(oracle.OrphanTails, editorOrphanTails);
    }

    [Theory]
    [MemberData(nameof(Charts))]
    public void 저장했다_다시_읽어도_홀드_짝이_그대로다(string profileId, string game, string path)
    {
        if (Skipped(profileId))
            return;

        // 게임 폴더에는 절대 쓰지 않는다. 차트를 임시 폴더로 복사해 그 옆에 저장한다.
        // 같은 폴더에 저장해야 #WAV 경로가 원문처럼 상대 경로로 남는다(파일명 규칙이 그 글자를 읽는다).
        var workDirectory = HoldTestSupport.ArtifactDirectory(Path.Combine("real-roundtrip", Guid.NewGuid().ToString("N")));
        var copy = Path.Combine(workDirectory, Path.GetFileName(path));
        File.Copy(path, copy);

        try
        {
            var profile = Profile(profileId);
            var parsed = BmsParser.Parse(copy);
            var before = HoldPairingEngine.Pair(parsed.Chart.Notes, HoldTestSupport.WavTexts(parsed), profile);

            var saved = Path.Combine(workDirectory, "saved.bms");
            var header = parsed.Chart.Header;
            var text = BmsWriter.Write(
                parsed.Chart, header.Title, header.Artist, header.Genre, header.Bpm,
                header.Player - 1, header.Rank, header.Level, parsed.WavItems, saved);
            File.WriteAllText(saved, text, parsed.Encoding);

            var reparsed = BmsParser.Parse(saved);
            var after = HoldPairingEngine.Pair(reparsed.Chart.Notes, HoldTestSupport.WavTexts(reparsed), profile);

            _output.WriteLine($"{game} / {SongName(path)}: 저장 전 짝 {before.Links.Count}, 저장 후 짝 {after.Links.Count}");

            // 프로파일은 고르지 않았으니 파일에 적히지 않는다(불변식 I-1).
            Assert.DoesNotContain("#BMSEDITER_PROFILE", text, StringComparison.Ordinal);
            Assert.Equal(parsed.Chart.Notes.Count, reparsed.Chart.Notes.Count);
            Assert.Equal(Keys(before), Keys(after));
            Assert.Equal(
                before.Diagnostics.Select(d => d.Kind).OrderBy(k => k),
                after.Diagnostics.Select(d => d.Kind).OrderBy(k => k));
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // 정리 실패는 테스트 결과와 무관하다.
            }
        }
    }

    [Theory]
    [MemberData(nameof(Charts))]
    public void 짝지어진_홀드는_격자에_한_줄로_그려진다(string profileId, string game, string path)
    {
        if (Skipped(profileId))
            return;

        var parsed = BmsParser.Parse(path);
        var result = HoldPairingEngine.Pair(parsed.Chart.Notes, HoldTestSupport.WavTexts(parsed), Profile(profileId));

        var failures = new List<string>();
        var checkedLinks = 0;
        var skipped = 0;
        var imagePath = Path.Combine(
            HoldTestSupport.ArtifactDirectory("real-holds"),
            HoldTestSupport.SafeFileName($"{profileId}__{SongName(path)}.png"));

        HoldTestSupport.RunOnUiThread(() =>
        {
            var grid = new NoteGridControl
            {
                Lanes = parsed.Chart.Lanes,
                Notes = parsed.Chart.Notes.ToArray(),
                HoldLinks = result.Links,
                MeasureCount = parsed.Chart.MeasureCount,
                RowHeight = 16,
                VerticalZoom = 1,
                HorizontalZoom = 1,
                LaneWidth = LaneWidth,
                BeatSplit = 16,
                GridMeasure = 4,
                Bpm = parsed.Chart.Header.Bpm,
            };

            var snapshot = GridSnapshot.Capture(grid);
            snapshot.Save(imagePath);

            var lanes = parsed.Chart.Lanes;
            var timelineLength = parsed.Chart.MeasureCount * PixelsPerMeasure;
            double Y(BmsNote note) => timelineLength - ((note.Measure + note.Position) * PixelsPerMeasure);

            var noteYsByLane = parsed.Chart.Notes
                .GroupBy(n => n.LaneId)
                .ToDictionary(g => g.Key, g => g.Select(Y).ToArray());

            foreach (var link in result.Links)
            {
                var headLane = IndexOf(lanes, link.Head.LaneId);
                var tailLane = IndexOf(lanes, link.Tail.LaneId);
                var headY = Y(link.Head);
                var tailY = Y(link.Tail);

                // 채널을 건너는 선과 너무 짧은 몸통은 노트에 가려 픽셀로 가를 수 없다. 합성 테스트가 맡는다.
                if (headLane != tailLane || Math.Abs(headY - tailY) < 24)
                {
                    skipped++;
                    continue;
                }

                // 몸통 위에 다른 노트나 경고 테두리가 겹치지 않는 자리를 고른다.
                double? sampleY = null;
                foreach (var ratio in new[] { 0.5, 0.35, 0.65, 0.25, 0.75 })
                {
                    var y = headY + ((tailY - headY) * ratio);
                    if (noteYsByLane[link.Head.LaneId].Any(noteY => Math.Abs(noteY - y) < 12))
                        continue;

                    sampleY = y;
                    break;
                }

                if (sampleY is not { } sample)
                {
                    skipped++;
                    continue;
                }

                // 같은 자리를 지나는 다른 몸통(샌드백과 홀드가 겹치는 경우 등)이 위에 그려졌을 수 있다.
                var coveringKinds = result.Links
                    .Where(other => other.Head.LaneId == link.Head.LaneId && other.Tail.LaneId == link.Head.LaneId)
                    .Where(other => Math.Min(Y(other.Head), Y(other.Tail)) <= sample && sample <= Math.Max(Y(other.Head), Y(other.Tail)))
                    .Select(other => other.Kind)
                    .Distinct()
                    .ToArray();

                var x = (headLane * LaneWidth) + (LaneWidth / 2);
                var pixel = snapshot.At(x, sample);
                checkedLinks++;

                if (!HoldTestSupport.LooksLikeAnyHoldBody(pixel, coveringKinds))
                {
                    failures.Add(
                        $"{link.Kind} {link.Head.LaneId} {link.Head.Measure:000}+{link.Head.Position:0.###} → " +
                        $"{link.Tail.Measure:000}+{link.Tail.Position:0.###}: ({x:0},{sample:0}) 픽셀 {pixel}");
                }
            }
        });

        _output.WriteLine($"{game} / {SongName(path)}: 짝 {result.Links.Count} · 픽셀 확인 {checkedLinks} · 짧거나 가려져 건너뜀 {skipped} · 그림 {imagePath}");

        Assert.Empty(failures);
        if (result.Links.Count > 0 && skipped < result.Links.Count)
            Assert.True(checkedLinks > 0, "짝은 있는데 픽셀로 확인할 수 있는 홀드가 하나도 없습니다.");
    }

    private bool Skipped(string profileId)
    {
        if (profileId != NoCharts)
            return false;

        _output.WriteLine("이 컴퓨터에서 게임 차트를 찾지 못해 건너뜁니다. BMS_EDITER_GAME_CHARTS 로 게임 폴더를 줄 수 있습니다.");
        return true;
    }

    private static GameProfile Profile(string id) =>
        GameProfileCatalog.Default.Find(id) ?? throw new InvalidOperationException($"프로파일 {id} 이 없습니다.");

    private static GameOracles.PairKey[] Keys(HoldPairingResult result) =>
        result.Links
            .Select(link => GameOracles.Key(
                link.Head.LaneId, link.Head.Measure + link.Head.Position,
                link.Tail.LaneId, link.Tail.Measure + link.Tail.Position,
                link.Kind))
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToArray();

    private static string KindCounts(HoldPairingResult result) =>
        result.Links.Count == 0
            ? "-"
            : string.Join(", ", result.Links.GroupBy(l => l.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));

    private static string SongName(string path)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return string.Equals(parent, "hwa", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(path)
            : parent;
    }

    private static int IndexOf(IReadOnlyList<LaneDefinition> lanes, string laneId)
    {
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].Id == laneId)
                return i;
        }

        return -1;
    }

    private static void WriteSummary(string profileId, string path, string summary, HoldPairingResult result)
    {
        var directory = HoldTestSupport.ArtifactDirectory(Path.Combine("real-holds", "summary"));
        var lines = new List<string> { summary };
        lines.AddRange(result.Diagnostics.Take(30).Select(d => "  " + d.DisplayText));
        if (result.Diagnostics.Count > 30)
            lines.Add($"  … 외 {result.Diagnostics.Count - 30}건");

        File.WriteAllLines(
            Path.Combine(directory, HoldTestSupport.SafeFileName($"{profileId}__{SongName(path)}.txt")),
            lines,
            Encoding.UTF8);
    }
}
