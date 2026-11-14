using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Services.Holds;
using bms_editer.ViewModels;
using bms_editer.Views.Controls;
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

    // ── Undo / Redo ────────────────────────────────────────────────────────

    [Fact]
    public void Undo_Redo_배치_삭제_이동_교체_복제를_모두_되돌리고_다시_실행한다()
    {
        using var vm = new MainWindowViewModel();
        var wav1Path = Path.Combine(_directory, "a.wav");
        var wav2Path = Path.Combine(_directory, "b.wav");
        File.WriteAllBytes(wav1Path, Array.Empty<byte>());
        File.WriteAllBytes(wav2Path, Array.Empty<byte>());
        Assert.True(vm.AddWav(wav1Path));
        Assert.True(vm.AddWav(wav2Path));
        vm.SelectedWavItem = vm.WavList[0];

        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);

        // 1. PlaceNote (추가)
        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("11", 1, 0.0));
        Assert.Single(vm.Chart.Notes);
        Assert.Equal("01", vm.Chart.Notes[0].WavKey);
        Assert.True(vm.CanUndo);

        // 2. PlaceNote (기존 노트 키음 교체)
        vm.SelectedWavItem = vm.WavList[1];
        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("11", 1, 0.0));
        Assert.Equal("02", vm.Chart.Notes[0].WavKey);

        // 3. MoveSelectedNotes (선택 보존 포함)
        vm.SetNoteSelection(new[] { vm.Chart.Notes[0] });
        vm.MoveSelectedNotesCommand.Execute(NoteMoveDirection.TimeForward);
        Assert.Equal(1.0 / vm.BeatSplit, vm.Chart.Notes[0].Position, 6);

        // 4. CopyNotesByMeasureOffset
        var copyResult = vm.CopyNotesByMeasureOffset(vm.SelectedNotes, 1);
        Assert.Equal(1, copyResult.Copied);
        Assert.Equal(2, vm.Chart.Notes.Count);

        // 5. ReplaceWavKey
        Assert.Equal(2, vm.ReplaceWavKey(vm.Chart.Notes, "01"));
        Assert.All(vm.Chart.Notes, n => Assert.Equal("01", n.WavKey));

        // 6. DeleteNotes
        Assert.Equal(1, vm.DeleteNotes(new[] { vm.Chart.Notes[1] }));
        Assert.Single(vm.Chart.Notes);

        // 7. RemoveNote (우클릭 삭제)
        vm.RemoveNoteCommand.Execute(new NotePlacementArgs("11", 1, 1.0 / vm.BeatSplit));
        Assert.Empty(vm.Chart.Notes);

        // 재생 중에는 Undo 가 막혀야 한다.
        vm.IsPlaying = true;
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
        vm.IsPlaying = false;

        // 역순 Undo 확인
        vm.UndoCommand.Execute(null); // undo RemoveNote -> 1 note
        Assert.Single(vm.Chart.Notes);

        vm.UndoCommand.Execute(null); // undo DeleteNotes -> 2 notes
        Assert.Equal(2, vm.Chart.Notes.Count);

        vm.UndoCommand.Execute(null); // undo ReplaceWavKey -> both "02"
        Assert.All(vm.Chart.Notes, n => Assert.Equal("02", n.WavKey));

        vm.UndoCommand.Execute(null); // undo Copy -> 1 note
        Assert.Single(vm.Chart.Notes);
        Assert.Equal(1.0 / vm.BeatSplit, vm.Chart.Notes[0].Position, 6);

        vm.UndoCommand.Execute(null); // undo Move -> back at 0.0 and still selected
        Assert.Equal(0.0, vm.Chart.Notes[0].Position, 6);
        Assert.Single(vm.SelectedNotes);
        Assert.Same(vm.Chart.Notes[0], vm.SelectedNotes[0]);

        vm.UndoCommand.Execute(null); // undo PlaceNote replace -> back to "01"
        Assert.Equal("01", vm.Chart.Notes[0].WavKey);

        vm.UndoCommand.Execute(null); // undo PlaceNote add -> empty
        Assert.Empty(vm.Chart.Notes);
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);

        // Redo 로 복원 검증
        while (vm.CanRedo)
            vm.RedoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
        Assert.False(vm.CanRedo);

        // 새 edit 가 들어오면 Redo 스택은 비워진다
        vm.UndoCommand.Execute(null);
        Assert.True(vm.CanRedo);
        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("12", 2, 0.0));
        Assert.False(vm.CanRedo);

        // 다른 차트를 열면 히스토리가 초기화된다
        var path = WriteChart("#BPM 120\r\n#WAV01 a.wav\r\n#00111:01\r\n");
        Assert.True(vm.LoadBms(path));
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
    }

    // ── 타임라인 스크럽 수학 ────────────────────────────────────────────────

    [Fact]
    public void PositionToAudioRatio는_세로와_가로_뷰에서_오프셋을_일관되게_반영한다()
    {
        // 세로 뷰(아래->위): position=250 / 1000 -> timelineRatio=0.75 -> 75s - 5s = 70s -> 0.70
        var verticalRatio = TimelineControlBase.PositionToAudioRatio(
            position: 250.0,
            timelineLength: 1000.0,
            isHorizontalView: false,
            durationSeconds: 100.0,
            audioOffsetSeconds: 5.0);
        Assert.Equal(0.70, verticalRatio, 9);

        // 가로 뷰(왼->오): position=750 / 1000 -> timelineRatio=0.75 -> 75s - (-10s) = 85s -> 0.85
        var horizontalRatio = TimelineControlBase.PositionToAudioRatio(
            position: 750.0,
            timelineLength: 1000.0,
            isHorizontalView: true,
            durationSeconds: 100.0,
            audioOffsetSeconds: -10.0);
        Assert.Equal(0.85, horizontalRatio, 9);

        // 유효하지 않은 길이/시각 -> 0
        Assert.Equal(0.0, TimelineControlBase.PositionToAudioRatio(100, 0, true, 100, 0));
        Assert.Equal(0.0, TimelineControlBase.PositionToAudioRatio(100, 1000, false, 0, 0));
    }

    // ── BmsParser 최적화 회귀 ───────────────────────────────────────────────

    [Fact]
    public void BmsParser_WAV_빠른경로와_빈슬롯_무할당_건너뛰기가_정확하게_파싱한다()
    {
        var content2Digit =
            "#TITLE FastPath\r\n" +
            "#ARTIST Composer\r\n" +
            "#BPM 150\r\n" +
            "#BPM01 180.5\r\n" +
            "#BMP01 bg.bmp\r\n" +
            "#WAV01 a.wav\r\n" +
            "#WAV02 b.wav\r\n" +
            "#00103:0096\r\n" +
            "#00108:0100\r\n" +
            "#00111:00010002\r\n" +
            "#00211:00000000\r\n";
        var parsed2 = BmsParser.Parse(WriteChart(content2Digit));

        Assert.Equal("FastPath", parsed2.Chart.Header.Title);
        Assert.Equal("Composer", parsed2.Chart.Header.Artist);
        Assert.Equal(150.0, parsed2.Chart.Header.Bpm);
        Assert.Equal(2, parsed2.WavItems.Count);
        Assert.Contains(parsed2.Chart.PreservedLines, l => l.Text == "#BMP01 bg.bmp");
        Assert.DoesNotContain(parsed2.Chart.PreservedLines, l => l.Text.StartsWith("#WAV", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, parsed2.Chart.BpmChanges.Count);
        Assert.Equal(2, parsed2.Chart.Notes.Count);
        Assert.Equal("01", parsed2.Chart.Notes[0].WavKey);
        Assert.Equal("02", parsed2.Chart.Notes[1].WavKey);

        var content3Digit =
            "#TITLE FastPath3\r\n" +
            "#BPM 140\r\n" +
            "#WAV001 a.wav\r\n" +
            "#WAV00A b.wav\r\n" +
            "#00111:00000100A000\r\n" +
            "#00211:000000\r\n";
        var parsed3 = BmsParser.Parse(WriteChart(content3Digit));

        Assert.Equal(2, parsed3.WavItems.Count);
        Assert.Equal(2, parsed3.Chart.Notes.Count);
        Assert.Equal("001", parsed3.Chart.Notes[0].WavKey);
        Assert.Equal("00A", parsed3.Chart.Notes[1].WavKey);
    }

    // ── HoldProblemRenderIndex 렌더링 회귀 ──────────────────────────────────

    [Fact]
    public void NoteGridControl_HoldProblemNotes_렌더링과_갱신이_정상_동작한다()
    {
        HeadlessUnitTestSession
            .GetOrStartForAssembly(typeof(AuditRegressionTests).Assembly)
            .Dispatch(() =>
            {
                var lanes = new[]
                {
                    new LaneDefinition("11", "1"),
                    new LaneDefinition("12", "2"),
                };
                var problem1 = new BmsNote { LaneId = "11", Measure = 1, Position = 0.0, WavKey = "01" };
                var problem2 = new BmsNote { LaneId = "12", Measure = 2, Position = 0.5, WavKey = "01" };

                var grid = new NoteGridControl
                {
                    Lanes = lanes,
                    MeasureCount = 8,
                    Bpm = 120,
                    Notes = new[] { problem1, problem2 },
                    HoldProblemNotes = new[] { problem1 },
                };

                var window = new Window { Width = 300, Height = 400, Content = grid };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                grid.HoldProblemNotes = new[] { problem1, problem2 };
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Close();
            }, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }
}
