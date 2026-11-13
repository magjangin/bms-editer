using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Services.Holds;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 2026-11 전체 점검에서 고친 것들을 못 박는 테스트.
public sealed class AuditRegressionTests : IDisposable
{
    private readonly string _directory;

    public AuditRegressionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private string WriteChart(string content)
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // ── 시간축 ─────────────────────────────────────────────────────────────

    // 예전에는 SecondsAt 에 상한이 없어서 큰 값 하나에 경계 표를 끝없이 늘렸다.
    [Theory]
    [InlineData(double.MaxValue)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e12)]
    public void 터무니없이_큰_마디_위치도_곧바로_유한한_시각을_돌려준다(double measurePosition)
    {
        var timeline = ChartTimeline.Uniform(120);

        var stopwatch = Stopwatch.StartNew();
        var seconds = timeline.SecondsAt(measurePosition);
        stopwatch.Stop();

        Assert.True(double.IsFinite(seconds));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"{stopwatch.Elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public void NaN_마디_위치는_0초다()
    {
        Assert.Equal(0.0, ChartTimeline.Uniform(120).SecondsAt(double.NaN));
    }

    // ── 문서 안내 ──────────────────────────────────────────────────────────

    // 예전에는 없는 키음 안내를 LastWarningMessage 에 넣었는데 열기 경로에서는 아무도 띄우지 않았다.
    [Fact]
    public void 없는_키음이_있으면_문서_안내에_남는다()
    {
        var path = WriteChart("#BPM 120\r\n#WAV01 없는파일.wav\r\n#00111:01\r\n");
        var vm = new MainWindowViewModel();

        Assert.True(vm.LoadBms(path));

        Assert.Contains("키음 1개", vm.DocumentNotice);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void 키음이_다_있으면_문서_안내가_없다()
    {
        File.WriteAllBytes(Path.Combine(_directory, "a.wav"), Array.Empty<byte>());
        var path = WriteChart("#BPM 120\r\n#WAV01 a.wav\r\n#00111:01\r\n");
        var vm = new MainWindowViewModel();

        Assert.True(vm.LoadBms(path));

        Assert.Null(vm.DocumentNotice);
    }

    [Fact]
    public async Task 새로_만들면_앞_문서의_안내가_지워진다()
    {
        var path = WriteChart("#BPM 120\r\n#WAV01 없는파일.wav\r\n#00111:01\r\n");
        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.NotNull(vm.DocumentNotice);

        await vm.NewFileCommand.ExecuteAsync(null);

        Assert.Null(vm.DocumentNotice);
    }

    [Fact]
    public void 다른_문서를_열면_앞_문서의_안내가_남지_않는다()
    {
        var missing = WriteChart("#BPM 120\r\n#WAV01 없는파일.wav\r\n#00111:01\r\n");
        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(missing));

        var cleanPath = Path.Combine(_directory, "clean.bms");
        File.WriteAllText(cleanPath, "#BPM 120\r\n#00111:00\r\n", new UTF8Encoding(false));
        Assert.True(vm.LoadBms(cleanPath));

        Assert.Null(vm.DocumentNotice);
    }

    // ── 홀드 짝 ────────────────────────────────────────────────────────────

    // 짝짓기를 미뤘다가 끝에 한 번 하더라도, 열고 난 결과는 그 자리에서 다시 짝지은 것과 같아야 한다.
    [Fact]
    public void 문서를_열고_나면_홀드_짝이_바로_채워져_있다()
    {
        var gameDirectory = Path.Combine(_directory, "DEFLATE", "hwa", "song");
        Directory.CreateDirectory(gameDirectory);
        var path = Path.Combine(gameDirectory, "hwa2.bms");
        File.WriteAllText(
            path,
            "#BPM 120\r\n#WAV0B hihat_1 홀드 시작.wav\r\n#WAV0A hihat_1 홀드 끝.wav\r\n#00111:0B000A00\r\n",
            new UTF8Encoding(false));

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));

        var expected = HoldPairingEngine.Pair(vm.Chart.Notes, WavTexts(vm), vm.SelectedProfile);
        Assert.False(vm.SelectedProfile.IsNone);
        Assert.Single(vm.HoldLinks);
        Assert.Equal(expected.Links.Count, vm.HoldLinks.Count);
    }

    private static Dictionary<string, string> WavTexts(MainWindowViewModel vm) =>
        vm.WavList.ToDictionary(
            item => item.Key,
            item => string.IsNullOrEmpty(item.SourceText) ? item.FileName : item.SourceText,
            StringComparer.OrdinalIgnoreCase);

    // 끝을 앞에서부터 다시 훑지 않게 바꿨다. 결과는 예전의 단순한 방식과 똑같아야 한다.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DEFLATE_가까운_안_쓴_끝_짝짓기는_단순한_전수_탐색과_같다(int seed)
    {
        var wavs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["00A"] = "hihat_1 홀드 끝.wav",
            ["00B"] = "hihat_1 홀드 시작.wav",
        };
        var notes = RandomHoldNotes(seed, headKey: "00B", tailKey: "00A");

        var result = HoldPairingEngine.Pair(notes, wavs, Profile("deflate"));

        Assert.Equal(NaiveNearestUnconsumed(notes, "00B", "00A"), result.Links.Select(l => (l.Head, l.Tail)).ToList());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 건볼트_끝을_같이_쓰는_짝짓기는_단순한_전수_탐색과_같다(int seed)
    {
        var notes = RandomHoldNotes(seed, headKey: "02", tailKey: "19", lane: "16");

        var result = HoldPairingEngine.Pair(notes, new Dictionary<string, string>(), Profile("gunvolt"));

        Assert.Equal(
            NaiveNearestShared(notes, "02", "19"),
            result.Links.Where(l => l.Kind == "Hold").Select(l => (l.Head, l.Tail)).ToList());
    }

    [Fact]
    public void 홀드가_수만_개여도_짝짓기가_금방_끝난다()
    {
        var wavs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["00A"] = "hihat_1 홀드 끝.wav",
            ["00B"] = "hihat_1 홀드 시작.wav",
        };

        // 시작이 먼저 몰려 있고 끝이 뒤에 몰려 있으면 예전 방식은 시작마다 끝 전체를 훑었다.
        var notes = new List<BmsNote>();
        for (var i = 0; i < 20000; i++)
            notes.Add(Note("11", i / 16.0, "00B"));
        for (var i = 0; i < 20000; i++)
            notes.Add(Note("11", 2000 + (i / 16.0), "00A"));

        var stopwatch = Stopwatch.StartNew();
        var result = HoldPairingEngine.Pair(notes, wavs, Profile("deflate"));
        stopwatch.Stop();

        Assert.Equal(20000, result.Links.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"{stopwatch.Elapsed.TotalMilliseconds}ms");
    }

    private static GameProfile Profile(string id) =>
        GameProfileCatalog.Default.Find(id) ?? throw new InvalidOperationException($"프로파일 {id} 이 없습니다.");

    private static BmsNote Note(string lane, double at, string key)
    {
        var measure = (int)Math.Floor(at);
        return new BmsNote { LaneId = lane, Measure = measure, Position = at - measure, WavKey = key };
    }

    // 한 레인에 시작과 끝을 마구 섞는다. 같은 자리 겹침도 일부러 만든다.
    private static List<BmsNote> RandomHoldNotes(int seed, string headKey, string tailKey, string lane = "11")
    {
        var random = new Random(seed);
        var notes = new List<BmsNote>();
        for (var i = 0; i < 400; i++)
        {
            var at = random.Next(0, 640) / 16.0;
            notes.Add(Note(lane, at, random.Next(2) == 0 ? headKey : tailKey));
        }

        return notes;
    }

    private static IEnumerable<(BmsNote Note, int Index)> InTimeOrder(IReadOnlyList<BmsNote> notes) =>
        notes.Select((note, index) => (note, index))
            .OrderBy(x => x.note.Measure + x.note.Position)
            .ThenBy(x => x.index);

    // 예전 구현 그대로: 시작마다 끝 목록을 처음부터 훑는다.
    private static List<(BmsNote, BmsNote)> NaiveNearestUnconsumed(IReadOnlyList<BmsNote> notes, string headKey, string tailKey)
    {
        var ordered = InTimeOrder(notes).Select(x => x.Note).ToList();
        var tails = ordered.Where(n => n.WavKey == tailKey).ToList();
        var consumed = new HashSet<BmsNote>();
        var links = new List<(BmsNote, BmsNote)>();

        foreach (var head in ordered.Where(n => n.WavKey == headKey))
        {
            var headTime = head.Measure + head.Position;
            var tail = tails.FirstOrDefault(t => !consumed.Contains(t) && t.Measure + t.Position > headTime + 1e-9);
            if (tail is null)
                continue;

            consumed.Add(tail);
            links.Add((head, tail));
        }

        return links;
    }

    private static List<(BmsNote, BmsNote)> NaiveNearestShared(IReadOnlyList<BmsNote> notes, string headKey, string tailKey)
    {
        var ordered = InTimeOrder(notes).Select(x => x.Note).ToList();
        var tails = ordered.Where(n => n.WavKey == tailKey).ToList();
        var links = new List<(BmsNote, BmsNote)>();

        foreach (var head in ordered.Where(n => n.WavKey == headKey))
        {
            var headTime = head.Measure + head.Position;
            var tail = tails.FirstOrDefault(t => t.Measure + t.Position > headTime + 1e-9);
            if (tail is not null)
                links.Add((head, tail));
        }

        return links;
    }
}
