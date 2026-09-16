using System;
using System.Collections.Generic;
using System.Linq;
using bms_editer.Models;
using bms_editer.Services.Holds;
using Xunit;

namespace bms_editer.Tests;

// 게임마다 다른 홀드 짝 규칙을 못 박는 테스트.
//
// 각 경우는 게임 모드 파서의 실제 동작에서 가져왔다. 특히 같은 배치를 두고 게임끼리 결과가
// 갈리는 자리를 골랐다. 이게 한 가지 규칙으로 뭉개지면 화면의 홀드 몸통이 게임과 다른 곳을 잇는다.
//   * 시작 두 개 뒤에 끝 하나: DEFLATE 는 앞 시작이, 스타트레일은 뒤 시작이 끝을 가져가고,
//     건볼트는 두 시작이 끝 하나를 같이 쓴다.
public sealed class HoldPairingTests
{
    private static GameProfile Profile(string id) =>
        GameProfileCatalog.Default.Find(id) ?? throw new InvalidOperationException($"프로파일 {id} 이 없습니다.");

    private static BmsNote Note(string lane, double at, string key, int branch = 0)
    {
        var measure = (int)Math.Floor(at);
        return new BmsNote { LaneId = lane, Measure = measure, Position = at - measure, WavKey = key, BranchId = branch };
    }

    private static Dictionary<string, string> Wavs(params (string Key, string Text)[] definitions) =>
        definitions.ToDictionary(d => d.Key, d => d.Text, StringComparer.OrdinalIgnoreCase);

    private static HoldPairingResult Pair(string profileId, IReadOnlyDictionary<string, string> wavs, params BmsNote[] notes) =>
        HoldPairingEngine.Pair(notes, wavs, Profile(profileId));

    private static (BmsNote Head, BmsNote Tail)[] Pairs(HoldPairingResult result, string? kind = null) =>
        result.Links.Where(l => kind is null || l.Kind == kind).Select(l => (l.Head, l.Tail)).ToArray();

    private static HoldDiagnostic[] Diagnostics(HoldPairingResult result, HoldDiagnosticKind kind) =>
        result.Diagnostics.Where(d => d.Kind == kind).ToArray();

    private static readonly Dictionary<string, string> NoWavs = new(StringComparer.OrdinalIgnoreCase);

    // ── DEFLATE ────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> DeflateWavs = Wavs(
        ("001", "hihat_1.wav"),
        ("00A", "hihat_1 홀드 끝.wav"),
        ("00B", "hihat_1 홀드 시작.wav"),
        ("00C", "hihat_2 홀드 끝.wav"),
        ("00D", "hihat_2 홀드 시작.wav"));

    [Fact]
    public void DEFLATE_시작마다_뒤에서_가장_가까운_안_쓴_끝을_가져간다()
    {
        var first = Note("11", 1.0, "00B");
        var second = Note("11", 1.25, "00B");
        var tail = Note("11", 1.5, "00A");

        var result = Pair("deflate", DeflateWavs, first, second, tail);

        Assert.Equal(new[] { (first, tail) }, Pairs(result));
        Assert.Same(second, Assert.Single(Diagnostics(result, HoldDiagnosticKind.OrphanHead)).Note);
    }

    [Fact]
    public void DEFLATE_파일명이_시작과_끝을_다_담으면_끝으로_본다()
    {
        var wavs = Wavs(("002", "hold start hold end.wav"), ("003", "hihat 홀드 시작.wav"));
        var head = Note("12", 1.0, "003");
        var both = Note("12", 2.0, "002");

        var result = Pair("deflate", wavs, head, both);

        Assert.Equal(new[] { (head, both) }, Pairs(result));
    }

    [Fact]
    public void DEFLATE_파일_이름이_달라도_같은_채널이면_짝이다()
    {
        // hihat_1 시작 ↔ hihat_2 끝. 게임은 파일 이름의 앞부분을 맞춰 보지 않는다.
        var head = Note("12", 2.0, "00B");
        var tail = Note("12", 2.5, "00C");

        Assert.Single(Pair("deflate", DeflateWavs, head, tail).Links);
    }

    [Fact]
    public void DEFLATE_같은_자리의_끝은_짝이_아니고_마디를_넘는_끝은_짝이다()
    {
        var head = Note("13", 3.0, "00B");
        var sameSpot = Note("13", 3.0, "00A");
        var later = Note("13", 5.75, "00A");

        var result = Pair("deflate", DeflateWavs, head, sameSpot, later);

        Assert.Equal(new[] { (head, later) }, Pairs(result));
        Assert.Same(sameSpot, Assert.Single(Diagnostics(result, HoldDiagnosticKind.OrphanTail)).Note);
    }

    [Fact]
    public void DEFLATE_채널이_다르면_짝짓지_않는다()
    {
        var result = Pair("deflate", DeflateWavs, Note("11", 1.0, "00B"), Note("12", 2.0, "00A"));

        Assert.Empty(result.Links);
        Assert.Single(Diagnostics(result, HoldDiagnosticKind.OrphanHead));
        Assert.Single(Diagnostics(result, HoldDiagnosticKind.OrphanTail));
    }

    [Fact]
    public void DEFLATE_노트_채널이_아닌_15와_18은_보지_않는다()
    {
        var result = Pair("deflate", DeflateWavs, Note("15", 1.0, "00B"), Note("15", 2.0, "00A"));

        Assert.Empty(result.Links);
        Assert.Empty(result.Diagnostics);
    }

    // ── 스타게이저 ─────────────────────────────────────────────────────────

    [Fact]
    public void 스타게이저_hold_가_없으면_시작_끝_단어가_있어도_일반_노트다()
    {
        var wavs = Wavs(("01", "synth start.wav"), ("02", "synth end.wav"));

        var result = Pair("stargazer", wavs, Note("16", 1.0, "01"), Note("16", 2.0, "02"));

        Assert.Empty(result.Links);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void 스타게이저는_DEFLATE와_달리_시작을_먼저_본다()
    {
        var wavs = Wavs(("02", "hold start end.wav"), ("03", "hold 끝.wav"));
        var head = Note("12", 1.0, "02");
        var tail = Note("12", 2.0, "03");

        Assert.Equal(new[] { (head, tail) }, Pairs(Pair("stargazer", wavs, head, tail)));
    }

    // ── 스타트레일 ─────────────────────────────────────────────────────────

    [Fact]
    public void 스타트레일은_대기칸이_하나라_새_시작이_앞_시작을_버린다()
    {
        var first = Note("16", 1.0, "02");
        var second = Note("16", 1.5, "02");
        var tail = Note("16", 2.0, "03");

        var result = Pair("startrail", NoWavs, first, second, tail);

        // 같은 배치에서 DEFLATE 는 first 가 끝을 가져간다. 스타트레일은 second 다.
        Assert.Equal(new[] { (second, tail) }, Pairs(result));
        Assert.Same(first, Assert.Single(Diagnostics(result, HoldDiagnosticKind.ReplacedHead)).Note);
    }

    [Fact]
    public void 스타트레일_3자리_키도_같은_값으로_본다()
    {
        Assert.Single(Pair("startrail", NoWavs, Note("11", 1.0, "002"), Note("11", 2.0, "003")).Links);
    }

    [Fact]
    public void 스타트레일_게이트는_채널을_가리지_않고_짝짓는다()
    {
        var open = Note("16", 1.0, "04");
        var close = Note("18", 2.0, "05");

        var link = Assert.Single(Pair("startrail", NoWavs, open, close).Links);

        Assert.Equal("Gate", link.Kind);
        Assert.Same(open, link.Head);
        Assert.Same(close, link.Tail);
    }

    [Fact]
    public void 스타트레일_짝_없는_끝은_고아다()
    {
        var tail = Note("13", 2.0, "03");

        Assert.Same(tail, Assert.Single(Diagnostics(Pair("startrail", NoWavs, tail), HoldDiagnosticKind.OrphanTail)).Note);
    }

    // ── 건볼트 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 건볼트_홀드는_끝을_소비하지_않아_시작_둘이_끝_하나를_같이_쓴다()
    {
        var first = Note("16", 1.0, "02");
        var second = Note("16", 1.5, "02");
        var tail = Note("16", 2.0, "19");

        var result = Pair("gunvolt", NoWavs, first, second, tail);

        Assert.Equal(new[] { (first, tail), (second, tail) }, Pairs(result, "Hold"));
        Assert.Same(tail, Assert.Single(Diagnostics(result, HoldDiagnosticKind.SharedTail)).Note);
    }

    [Fact]
    public void 건볼트_13채널은_노트_채널이_아니다()
    {
        var result = Pair("gunvolt", NoWavs, Note("13", 1.0, "02"), Note("13", 2.0, "19"));

        Assert.Empty(result.Links);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void 건볼트_페어리는_줄을_세워_가장_오래_기다린_시작과_짝짓는다()
    {
        var first = Note("14", 1.0, "11");
        var second = Note("14", 1.25, "13");
        var firstEnd = Note("14", 1.5, "1A");
        var secondEnd = Note("14", 2.0, "1B");

        var result = Pair("gunvolt", NoWavs, first, second, firstEnd, secondEnd);

        Assert.Equal(new[] { (first, firstEnd), (second, secondEnd) }, Pairs(result, "Fairy"));
    }

    [Fact]
    public void 건볼트_페어리_시작과_끝이_같은_자리면_둘_다_버린다()
    {
        var result = Pair("gunvolt", NoWavs, Note("14", 1.0, "1A"), Note("14", 1.0, "11"));

        Assert.Empty(result.Links);
        Assert.Single(Diagnostics(result, HoldDiagnosticKind.OrphanHead));
        Assert.Single(Diagnostics(result, HoldDiagnosticKind.OrphanTail));
    }

    [Fact]
    public void 건볼트_키를_자릿수와_무관하게_본다()
    {
        Assert.Single(Pair("gunvolt", NoWavs, Note("15", 1.0, "011"), Note("15", 2.0, "01A")).Links);
    }

    // ── 뮤즈 대시 ──────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> MuseDashWavs = Wavs(
        ("00B", @"1번 씬 wav폴더\010201_홀드 지상 끝 노트_dt1.47.wav"),
        ("00C", @"1번 씬 wav폴더\010201_홀드 지상 시작 노트_dt1.47.wav"),
        ("00F", @"1번 씬 wav폴더\010401_샌드백 지상_dt1.48.wav"),
        ("0HG", @"씬 전환 wav 폴더\000401_1번 씬 전환_dt0.wav"),
        ("00T", @"1번 씬 wav폴더\011001_일반 노트1 지상 노멀_dt1.48.wav"),
        ("0X1", "010107_hold 액션_dt0.wav"),
        ("0X2", "hold.wav"),
        ("0X3", "hold note.wav"));

    [Fact]
    public void 뮤즈대시는_파일명_표식이_아니라_순서로_짝짓는다()
    {
        // '끝 노트'를 먼저 찍었다. 게임은 이걸 시작으로 쓴다.
        var labeledEnd = Note("13", 1.0, "00B");
        var labeledStart = Note("13", 1.5, "00C");

        var result = Pair("muse_dash", MuseDashWavs, labeledEnd, labeledStart);

        Assert.Equal(new[] { (labeledEnd, labeledStart) }, Pairs(result));
        Assert.Same(labeledEnd, Assert.Single(Diagnostics(result, HoldDiagnosticKind.ParityBreak)).Note);
    }

    [Fact]
    public void 뮤즈대시_홀드와_샌드백은_수열을_따로_센다()
    {
        var holdStart = Note("13", 1.0, "00C");
        var sandbagStart = Note("13", 1.25, "00F");
        var holdEnd = Note("13", 1.5, "00B");
        var sandbagEnd = Note("13", 1.75, "00F");

        var result = Pair("muse_dash", MuseDashWavs, holdStart, sandbagStart, holdEnd, sandbagEnd);

        Assert.Equal(new[] { (holdStart, holdEnd) }, Pairs(result, "Hold"));
        Assert.Equal(new[] { (sandbagStart, sandbagEnd) }, Pairs(result, "Sandbag"));
    }

    [Fact]
    public void 뮤즈대시_씬_전환은_타입_자리가_04여도_샌드백_수열에_끼지_않는다()
    {
        var start = Note("14", 1.0, "00F");
        var scene = Note("14", 1.25, "0HG");
        var end = Note("14", 1.5, "00F");

        Assert.Equal(new[] { (start, end) }, Pairs(Pair("muse_dash", MuseDashWavs, start, scene, end), "Sandbag"));
    }

    [Fact]
    public void 뮤즈대시_끼워_넣은_홀드는_그_자리를_지목한다()
    {
        var s1 = Note("13", 1.0, "00C");
        var e1 = Note("13", 2.0, "00B");
        var s2 = Note("13", 3.0, "00C");
        var inserted = Note("13", 3.5, "00C");
        var e2 = Note("13", 4.0, "00B");
        var s3 = Note("13", 5.0, "00C");
        var e3 = Note("13", 6.0, "00B");

        var result = Pair("muse_dash", MuseDashWavs, s1, e1, s2, inserted, e2, s3, e3);

        // 47마디를 고치고 60마디부터 무너지는 증상. 뒤쪽을 잔뜩 늘어놓지 않고 원인 한 곳만 짚는다.
        Assert.Same(inserted, Assert.Single(Diagnostics(result, HoldDiagnosticKind.ParityBreak)).Note);
        Assert.Same(e3, Assert.Single(Diagnostics(result, HoldDiagnosticKind.OrphanHead)).Note);
    }

    [Fact]
    public void 뮤즈대시_UID로_해석된_보스_전환_코드는_이름에_hold가_있어도_홀드가_아니다()
    {
        var result = Pair("muse_dash", MuseDashWavs, Note("13", 1.0, "0X1"), Note("13", 2.0, "0X1"));

        Assert.Empty(result.Links);
    }

    [Fact]
    public void 뮤즈대시_UID가_없으면_이름으로_추정하되_note가_hold보다_먼저다()
    {
        Assert.Single(Pair("muse_dash", MuseDashWavs, Note("13", 1.0, "0X2"), Note("13", 2.0, "0X2")).Links);
        Assert.Empty(Pair("muse_dash", MuseDashWavs, Note("13", 1.0, "0X3"), Note("13", 2.0, "0X3")).Links);
    }

    // ── UNBEATABLE ─────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> UnbeatableWavs = Wavs(
        ("001", "Default.wav"),
        ("004", "Hold.wav"),
        ("005", "Hold end.wav"),
        ("007", "Spam.wav"),
        ("008", "Spam end.wav"));

    [Fact]
    public void 언비터블은_end를_떼고_같은_종류끼리_순서로_짝짓는다()
    {
        var hold = Note("12", 1.0, "004");
        var spam = Note("12", 1.25, "007");
        var holdEnd = Note("12", 1.5, "005");
        var spamEnd = Note("12", 1.75, "008");

        var result = Pair("unbeatable", UnbeatableWavs, hold, spam, holdEnd, spamEnd);

        Assert.Equal(new[] { (hold, holdEnd) }, Pairs(result, "Hold"));
        Assert.Equal(new[] { (spam, spamEnd) }, Pairs(result, "Spam"));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void 언비터블_Hold를_두_번_써도_순서대로_짝이다()
    {
        var result = Pair("unbeatable", UnbeatableWavs, Note("13", 1.0, "004"), Note("13", 2.0, "004"));

        Assert.Single(result.Links);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void 언비터블_end가_시작_자리에_오면_지목한다()
    {
        var misplaced = Note("13", 1.0, "005");

        var result = Pair("unbeatable", UnbeatableWavs, misplaced, Note("13", 2.0, "004"));

        Assert.Single(result.Links);
        Assert.Same(misplaced, Assert.Single(Diagnostics(result, HoldDiagnosticKind.ParityBreak)).Note);
    }

    [Fact]
    public void 언비터블_18채널은_노트가_아니다()
    {
        Assert.Empty(Pair("unbeatable", UnbeatableWavs, Note("18", 1.0, "004"), Note("18", 2.0, "005")).Links);
    }

    // ── 공통 ───────────────────────────────────────────────────────────────

    [Fact]
    public void 프로파일이_없으면_아무것도_짝짓지_않는다()
    {
        var notes = new[] { Note("11", 1.0, "00B"), Note("11", 2.0, "00A") };

        Assert.Same(HoldPairingResult.Empty, HoldPairingEngine.Pair(notes, DeflateWavs, null));
        Assert.Same(HoldPairingResult.Empty, HoldPairingEngine.Pair(notes, DeflateWavs, GameProfile.None));
    }

    [Fact]
    public void 조건_블록_갈래끼리는_짝짓지_않는다()
    {
        var result = Pair("deflate", DeflateWavs, Note("11", 1.0, "00B", branch: 1), Note("11", 2.0, "00A", branch: 2));

        Assert.Empty(result.Links);
    }

    [Fact]
    public void 짝_맞추기는_노트를_바꾸지_않는다()
    {
        // 불변식 I-3. 홀드는 노트 위에 얹는 파생 정보다. 노트를 고치면 저장 결과가 달라진다.
        var head = Note("11", 1.0, "00B");
        var tail = Note("11", 2.0, "00A");
        var notes = new List<BmsNote> { head, tail };

        Pair("deflate", DeflateWavs, notes.ToArray());

        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.Equal(NoteType.Normal, n.Type));
        Assert.Equal(("11", 1, 0.0, "00B"), (head.LaneId, head.Measure, head.Position, head.WavKey));
        Assert.Equal(("11", 2, 0.0, "00A"), (tail.LaneId, tail.Measure, tail.Position, tail.WavKey));
    }
}
