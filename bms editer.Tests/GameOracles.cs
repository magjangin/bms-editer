using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace bms_editer.Tests;

// 실제 차트를 에디터 파서와 **따로** 읽는다.
//
// 오라클이 에디터의 BmsParser 를 쓰면, 파서가 틀렸을 때 둘이 같이 틀려서 테스트가 통과한다.
// 게임 모드들이 하는 것처럼 줄을 직접 끊어 읽는다.
internal sealed class RawChart
{
    private static readonly Regex WavLine = new(@"^#WAV([0-9A-Za-z]{2,3})\s+(.*)$", RegexOptions.IgnoreCase);
    private static readonly Regex DataLine = new(@"^#([0-9]{3})([0-9A-Za-z]{2}):(.*)$");

    private RawChart(IReadOnlyDictionary<string, string> wavs, IReadOnlyList<RawLine> lines, int width)
    {
        Wavs = wavs;
        Lines = lines;
        Width = width;
    }

    // 키(대문자) -> #WAV 에 적힌 글자 그대로.
    public IReadOnlyDictionary<string, string> Wavs { get; }

    public IReadOnlyList<RawLine> Lines { get; }

    // 게임 모드들은 #WAV 키가 하나라도 3자리면 슬롯을 3자리로 끊는다.
    public int Width { get; }

    public static RawChart Read(string path)
    {
        var wavs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<RawLine>();
        var width = 2;
        var order = 0;

        foreach (var rawLine in File.ReadAllText(path).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] != '#')
                continue;

            var wav = WavLine.Match(line);
            if (wav.Success)
            {
                var key = wav.Groups[1].Value.ToUpperInvariant();
                wavs[key] = wav.Groups[2].Value.Trim();
                if (key.Length == 3)
                    width = 3;
                continue;
            }

            var data = DataLine.Match(line);
            if (data.Success)
            {
                lines.Add(new RawLine(
                    int.Parse(data.Groups[1].Value, CultureInfo.InvariantCulture),
                    data.Groups[2].Value.ToUpperInvariant(),
                    data.Groups[3].Value.Trim(),
                    order++));
            }
        }

        return new RawChart(wavs, lines, width);
    }

    public static IEnumerable<RawSlot> Slots(RawLine line, int width)
    {
        var count = line.Data.Length / width;
        for (var i = 0; i < count; i++)
        {
            var value = line.Data.Substring(i * width, width).ToUpperInvariant();
            if (value.All(c => c == '0'))
                continue;

            yield return new RawSlot(line, i, count, value);
        }
    }
}

internal readonly record struct RawLine(int Measure, string Channel, string Data, int Order);

internal readonly record struct RawSlot(RawLine Line, int Index, int Count, string Value)
{
    public string Channel => Line.Channel;
    public double Time => Line.Measure + ((double)Index / Count);
}

// 게임 모드들의 홀드 짝 맞추기를 거의 그대로 옮긴 "오라클".
//
// 에디터는 JSON 프로파일로 규칙을 받는 범용 엔진이다. 오라클은 반대로 게임마다 코드를 따로
// 옮겨 적었다(변수 이름·분기 순서까지 되도록 원본을 따른다). 둘이 모든 실제 차트에서 똑같은
// 짝을 내면, 프로파일 값과 엔진이 게임을 제대로 흉내 낸다는 뜻이다.
// 원본 위치는 각 함수 위에 적었다. 원본이 바뀌면 여기와 프로파일을 함께 고친다.
internal static class GameOracles
{
    internal readonly record struct PairKey(string HeadChannel, double HeadTime, string TailChannel, double TailTime, string Kind)
    {
        public override string ToString() =>
            $"{Kind} {HeadChannel}@{HeadTime.ToString("0.######", CultureInfo.InvariantCulture)} → " +
            $"{TailChannel}@{TailTime.ToString("0.######", CultureInfo.InvariantCulture)}";
    }

    // HiddenFromEditor: 게임은 읽지만 에디터가 편집 채널로 읽지 않는 자리의 홀드 노트 수.
    // 0이 아니면 에디터 화면에 안 보이는 홀드가 있다는 뜻이라 테스트가 따로 알린다.
    internal sealed record Result(IReadOnlyList<PairKey> Pairs, int OrphanHeads, int OrphanTails, int HiddenFromEditor);

    internal static readonly HashSet<string> EditorChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "16", "11", "12", "13", "14", "15", "18",
    };

    public static PairKey Key(string headChannel, double headTime, string tailChannel, double tailTime, string kind) =>
        new(headChannel.ToUpperInvariant(), Math.Round(headTime, 9), tailChannel.ToUpperInvariant(), Math.Round(tailTime, 9), kind.ToUpperInvariant());

    public static Result Run(string profileId, RawChart chart) => profileId switch
    {
        "deflate" => Deflate(chart),
        "stargazer" => Stargazer(chart),
        "startrail" => Startrail(chart),
        "gunvolt" => Gunvolt(chart),
        "muse_dash" => MuseDash(chart),
        "unbeatable" => Unbeatable(chart),
        _ => throw new ArgumentException($"오라클이 없는 프로파일입니다: {profileId}"),
    };

    // ── DEFLATE ────────────────────────────────────────────────────────────
    // DEFLATE custom chart / Core/Bms/BmsParser.cs PairHoldNotesByKeysound

    private static readonly string[] HoldStartKeywords = { "홀드 시작", "홀드시작", "hold start", "holdstart", "hold_start", "ln start", "lnstart" };
    private static readonly string[] HoldEndKeywords = { "홀드 끝", "홀드끝", "hold end", "holdend", "hold_end", "ln end", "lnend" };
    private static readonly HashSet<string> DeflateNoteChannels = new() { "16", "11", "12", "13", "14" };

    private sealed class KeysoundNote
    {
        public KeysoundNote(RawSlot slot) => Slot = slot;
        public RawSlot Slot { get; }
        public bool IsHoldStart { get; set; }
        public bool IsHoldEnd { get; set; }
    }

    private static bool ContainsAny(string text, string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static Result Deflate(RawChart chart)
    {
        var notes = new List<KeysoundNote>();
        foreach (var raw in chart.Lines)
        {
            if (!DeflateNoteChannels.Contains(raw.Channel)) continue;
            if (string.IsNullOrEmpty(raw.Data) || raw.Data.Length % chart.Width != 0) continue;
            foreach (var slot in RawChart.Slots(raw, chart.Width))
                notes.Add(new KeysoundNote(slot));
        }

        // 1. 키음 파일명으로 Head / Tail 분류
        foreach (var note in notes)
        {
            if (!chart.Wavs.TryGetValue(note.Slot.Value, out var wavFile) || string.IsNullOrEmpty(wavFile)) continue;

            if (ContainsAny(wavFile, HoldEndKeywords)) note.IsHoldEnd = true;
            else if (ContainsAny(wavFile, HoldStartKeywords)) note.IsHoldStart = true;
        }

        return PairNearestUnconsumedTail(notes, "Hold", hidden: 0);
    }

    // DEFLATE 의 "Head ➔ 가장 가까운 Tail" 매칭 본문.
    private static Result PairNearestUnconsumedTail(List<KeysoundNote> notes, string kind, int hidden)
    {
        var pairs = new List<PairKey>();
        var consumedTails = new HashSet<KeysoundNote>();
        int orphanHeads = 0, orphanTails = 0;

        foreach (var group in notes.GroupBy(n => n.Slot.Channel))
        {
            var ordered = group.OrderBy(n => n.Slot.Time).ToList();

            foreach (var head in ordered)
            {
                if (!head.IsHoldStart) continue;

                KeysoundNote? tail = null;
                foreach (var candidate in ordered)
                {
                    if (!candidate.IsHoldEnd || consumedTails.Contains(candidate)) continue;
                    if (candidate.Slot.Time <= head.Slot.Time) continue;
                    tail = candidate;
                    break;
                }

                if (tail == null)
                {
                    orphanHeads++;
                    continue;
                }

                consumedTails.Add(tail);
                pairs.Add(Key(head.Slot.Channel, head.Slot.Time, tail.Slot.Channel, tail.Slot.Time, kind));
            }

            foreach (var note in ordered)
            {
                if (note.IsHoldEnd && !consumedTails.Contains(note))
                    orphanTails++;
            }
        }

        return new Result(pairs, orphanHeads, orphanTails, hidden);
    }

    // ── 스타게이저 ─────────────────────────────────────────────────────────
    // STARGAZER custom chart / src/Bms/BmsChart.cs ClassifyNoteKind · NormalizeSoundId · TryParse
    // 짝은 모드가 아니라 게임 엔진이 맺어서 옮길 코드가 없다. 같은 채널의 뒤에 있는 가장 가까운
    // 끝으로 **가정**한다(프로파일도 Assumed 로 표시돼 있다). 이 비교는 분류가 맞는지를 확인한다.

    private static readonly HashSet<string> StargazerLanes = new() { "16", "12", "13", "11" };

    private static int ClassifyStargazer(string fileName)
    {
        if (fileName.IndexOf("hold", StringComparison.OrdinalIgnoreCase) < 0) return 0;
        if (fileName.IndexOf("시작", StringComparison.Ordinal) >= 0
            || fileName.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
        if (fileName.IndexOf("끝", StringComparison.Ordinal) >= 0
            || fileName.IndexOf("end", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
        return 0;
    }

    private static string NormalizeSoundId(string id, bool extended) =>
        extended && id.Length == 3 && id[0] == '0' ? id.Substring(1) : id;

    private static Result Stargazer(RawChart chart)
    {
        var extended = chart.Wavs.Keys.Any(k => k.Length == 3);
        var kindBySoundId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawId, fileName) in chart.Wavs)
            kindBySoundId[NormalizeSoundId(rawId, extended)] = ClassifyStargazer(fileName);

        var notes = new List<KeysoundNote>();
        var hidden = 0;
        foreach (var raw in chart.Lines)
        {
            if (raw.Data.Length == 0 || raw.Data.Length % chart.Width != 0) continue;

            foreach (var slot in RawChart.Slots(raw, chart.Width))
            {
                var kind = kindBySoundId.TryGetValue(NormalizeSoundId(slot.Value, extended), out var found) ? found : 0;
                if (kind == 0) continue;

                if (!StargazerLanes.Contains(raw.Channel))
                {
                    if (!EditorChannels.Contains(raw.Channel)) hidden++;
                    continue;
                }

                notes.Add(new KeysoundNote(slot) { IsHoldStart = kind == 1, IsHoldEnd = kind == 2 });
            }
        }

        return PairNearestUnconsumedTail(notes, "Hold", hidden);
    }

    // ── 스타트레일 ─────────────────────────────────────────────────────────
    // sxtg2 / sxtg2-mod/Loaders/BmsParser.cs ParseNoteData · TryResolveLane · PairHoldNotes

    private enum SxtgNoteType { Normal, Long, HoldEnd, Open, Close }

    private static readonly Dictionary<string, int> LaneMapping = new()
    {
        { "16", 0 }, { "11", 1 }, { "12", 2 }, { "13", 3 }, { "14", 4 }, { "15", 5 }, { "18", 6 },
    };

    private static readonly Dictionary<string, SxtgNoteType> NoteTypeMapping = new()
    {
        { "01", SxtgNoteType.Normal }, { "02", SxtgNoteType.Long }, { "03", SxtgNoteType.HoldEnd },
        { "04", SxtgNoteType.Open }, { "05", SxtgNoteType.Close },
    };

    private static Result Startrail(RawChart chart)
    {
        var notes = new List<(RawSlot Slot, int Lane, SxtgNoteType NoteType)>();
        var hidden = 0;

        foreach (var raw in chart.Lines)
        {
            var channelNumber = raw.Channel;
            if (!LaneMapping.ContainsKey(channelNumber) && channelNumber != "04" && channelNumber != "05")
                continue;

            foreach (var slot in RawChart.Slots(raw, chart.Width))
            {
                var typeKey = chart.Width == 3 && slot.Value[0] == '0' ? slot.Value.Substring(1) : slot.Value;
                if (!NoteTypeMapping.TryGetValue(typeKey, out var noteType))
                    continue;

                int lane;
                if (noteType == SxtgNoteType.Open || noteType == SxtgNoteType.Close) lane = 9;
                else if (!LaneMapping.TryGetValue(channelNumber, out lane)) continue;

                if (!EditorChannels.Contains(channelNumber) && noteType != SxtgNoteType.Normal)
                    hidden++;

                notes.Add((slot, lane, noteType));
            }
        }

        var pairs = new List<PairKey>();
        int missingEnd = 0, orphanEnd = 0;

        foreach (var laneGroup in notes.GroupBy(note => note.Lane))
        {
            var startType = laneGroup.Key == 9 ? SxtgNoteType.Open : SxtgNoteType.Long;
            var endType = laneGroup.Key == 9 ? SxtgNoteType.Close : SxtgNoteType.HoldEnd;
            (RawSlot Slot, int Lane, SxtgNoteType NoteType)? pending = null;

            foreach (var note in laneGroup.OrderBy(note => note.Slot.Time))
            {
                if (note.NoteType == startType)
                {
                    if (pending != null)
                        missingEnd++;
                    pending = note;
                }
                else if (note.NoteType == endType)
                {
                    if (pending == null)
                    {
                        orphanEnd++;
                    }
                    else
                    {
                        var start = pending.Value.Slot;
                        pairs.Add(Key(start.Channel, start.Time, note.Slot.Channel, note.Slot.Time, laneGroup.Key == 9 ? "Gate" : "Hold"));
                        pending = null;
                    }
                }
            }

            if (pending != null)
                missingEnd++;
        }

        return new Result(pairs, missingEnd, orphanEnd, hidden);
    }

    // ── 건볼트 ─────────────────────────────────────────────────────────────
    // GRC2 / Parsers/BmsNoteDataParser.cs ParseNoteData · ParseHexData · GetNoteType
    //        Processors/HoldNoteProcessor.cs MatchHoldNotes
    //        Processors/FairyNoteProcessor.cs RunPrimaryFairyMatch

    private enum GrcNoteType { Touch, Hold, HoldEnd, Flick, Fairy, FairyEnd }

    private static readonly Dictionary<string, (int Lane, bool IsLeft)> ChannelToLaneMap = new()
    {
        { "16", (0, true) }, { "11", (1, true) }, { "12", (2, true) },
        { "14", (0, false) }, { "15", (1, false) }, { "18", (2, false) },
    };

    private sealed class GrcNote
    {
        public GrcNote(string channel, double time, float tick, int lane, bool isLeft, GrcNoteType type)
        {
            Channel = channel;
            Time = time;
            Tick = tick;
            Lane = lane;
            IsLeft = isLeft;
            Type = type;
        }

        public string Channel { get; }
        public double Time { get; }
        public float Tick { get; }
        public int Lane { get; }
        public bool IsLeft { get; }
        public GrcNoteType Type { get; }
    }

    private static GrcNoteType GetNoteType(int value)
    {
        if (value == 0x01) return GrcNoteType.Touch;
        if (value == 0x02) return GrcNoteType.Hold;
        if (value == 0x19) return GrcNoteType.HoldEnd;
        if (value == 0x1A) return GrcNoteType.FairyEnd;
        if (value == 0x1B) return GrcNoteType.FairyEnd;
        if (value >= 0x03 && value <= 0x0A) return GrcNoteType.Flick;
        if (value >= 0x11 && value <= 0x18) return GrcNoteType.Fairy;
        return GrcNoteType.Touch;
    }

    private static Result Gunvolt(RawChart chart)
    {
        var notes = new List<GrcNote>();

        foreach (var raw in chart.Lines)
        {
            if (!ChannelToLaneMap.TryGetValue(raw.Channel, out var laneInfo))
                continue;

            // ParseHexData: 16진수로 안 읽히는 칸은 목록에서 빠진다. 그래서 같은 줄의 다른 노트 위치가 당겨진다.
            var hexValues = new List<int>();
            var trimmed = raw.Data.Trim();
            for (var i = 0; i + chart.Width <= trimmed.Length; i += chart.Width)
            {
                if (int.TryParse(trimmed.Substring(i, chart.Width), NumberStyles.HexNumber, null, out var value))
                    hexValues.Add(value);
            }

            var measureLength = hexValues.Count;
            for (var i = 0; i < hexValues.Count; i++)
            {
                if (hexValues[i] == 0) continue;

                var positionInMeasure = (float)i / measureLength;
                var tick = raw.Measure + positionInMeasure;
                notes.Add(new GrcNote(
                    raw.Channel,
                    raw.Measure + ((double)i / measureLength),
                    tick,
                    laneInfo.Lane,
                    laneInfo.IsLeft,
                    GetNoteType(hexValues[i])));
            }
        }

        var pairs = new List<PairKey>();
        int orphanHeads = 0, orphanTails = 0;

        // MatchHoldNotes: 끝을 소비하지 않는다.
        var holdStarts = notes.Where(n => n.Type == GrcNoteType.Hold).ToList();
        var holdEnds = notes.Where(n => n.Type == GrcNoteType.HoldEnd).ToList();
        var referencedEnds = new HashSet<GrcNote>();

        foreach (var start in holdStarts)
        {
            var end = holdEnds
                .Where(e => e.Lane == start.Lane && e.IsLeft == start.IsLeft && e.Tick > start.Tick)
                .OrderBy(e => e.Tick)
                .FirstOrDefault();

            if (end != null)
            {
                referencedEnds.Add(end);
                pairs.Add(Key(start.Channel, start.Time, end.Channel, end.Time, "Hold"));
            }
            else
            {
                orphanHeads++;
            }
        }

        orphanTails += holdEnds.Count(e => !referencedEnds.Contains(e));

        // RunPrimaryFairyMatch
        var linked = new HashSet<GrcNote>();
        var items = notes
            .Where(n => n != null && (n.Type == GrcNoteType.Fairy || n.Type == GrcNoteType.FairyEnd))
            .OrderBy(n => n.Tick)
            .ThenByDescending(n => n.Type == GrcNoteType.Fairy)
            .ToList();

        foreach (var group in items.GroupBy(n => (n.Lane, n.IsLeft)))
        {
            var openStarts = new Queue<GrcNote>();

            foreach (var note in group)
            {
                if (note.Type == GrcNoteType.Fairy)
                {
                    openStarts.Enqueue(note);
                    continue;
                }

                if (openStarts.Count == 0) continue;

                var start = openStarts.Dequeue();
                if (note.Tick <= start.Tick) continue;

                linked.Add(start);
                linked.Add(note);
                pairs.Add(Key(start.Channel, start.Time, note.Channel, note.Time, "Fairy"));
            }
        }

        orphanHeads += items.Count(n => n.Type == GrcNoteType.Fairy && !linked.Contains(n));
        orphanTails += items.Count(n => n.Type == GrcNoteType.FairyEnd && !linked.Contains(n));

        return new Result(pairs, orphanHeads, orphanTails, 0);
    }

    // ── 뮤즈 대시 ──────────────────────────────────────────────────────────
    // muse dash test / Bms/BmsParser.cs ChannelToLaneMap · 정렬
    //                  Bms/BmsWavParser.cs ParseWavInfo · ApplyFallbackNoteType (NoteType 부분)
    //                  Bms/BmsNoteMatcher.cs MatchSpecialNotes

    private static readonly HashSet<string> MuseDashChannels = new() { "13", "14", "15", "18" };
    private static readonly Regex UidRegex = new(@"^([0-9]{6})", RegexOptions.Compiled);

    private static readonly Dictionary<string, int> UidPrefixNoteType = new(StringComparer.OrdinalIgnoreCase)
    {
        { "0002", 6 }, { "0003", 7 }, { "0004", 9 },
    };

    private static readonly Dictionary<string, int> XxNoteType = new(StringComparer.OrdinalIgnoreCase)
    {
        { "02", 3 }, { "03", 2 }, { "04", 8 }, { "09", 2 }, { "17", 4 },
    };

    private static readonly HashSet<string> XxyyTransitionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "0101", "0102", "0107", "0108", "0109", "0110", "0113", "0114",
    };

    internal static int MuseDashNoteType(string wavName)
    {
        // 게임은 Windows 에서 돌아서 폴더 구분자 '\' 를 떼어낸다. 테스트도 같은 결과가 나오게 맞춘다.
        var nameWithoutExt = Path.GetFileNameWithoutExtension(wavName.Replace('\\', '/'));
        var noteType = 1;
        string? uid = null;

        var uidMatch = UidRegex.Match(nameWithoutExt);
        if (uidMatch.Success)
            uid = uidMatch.Groups[1].Value;

        var lowerName = nameWithoutExt.ToLowerInvariant();
        var typeResolvedFromUid = false;

        if (uid != null && uid.Length == 6)
        {
            var xx = uid.Substring(2, 2);
            var xxyy = uid.Substring(2, 4);
            var prefix4 = uid.Substring(0, 4);

            if (UidPrefixNoteType.TryGetValue(prefix4, out var prefixType))
            {
                noteType = prefixType;
                typeResolvedFromUid = true;
            }
            else if (XxNoteType.TryGetValue(xx, out var xxType))
            {
                noteType = xxType;
                typeResolvedFromUid = true;
            }

            if (XxyyTransitionKeys.Contains(xxyy))
                typeResolvedFromUid = true;

            if (xx == "06" || xx == "07" || xx == "08" || xx == "09")
                typeResolvedFromUid = true;
        }

        // ApplyFallbackNoteType
        if (uid != null && uid.StartsWith("0004", StringComparison.Ordinal))
            return 9;

        if (lowerName.Contains("boss_swap") || lowerName.Contains("boss_out") || lowerName.Contains("boss_in"))
            return 0;

        if (typeResolvedFromUid)
            return noteType;

        if (lowerName.Contains("heart") || lowerName.Contains("hp") || (uid != null && uid.StartsWith("0002", StringComparison.Ordinal)))
            return 6;
        if (lowerName.Contains("score") || lowerName.Contains("note") || (uid != null && uid.StartsWith("0003", StringComparison.Ordinal)))
            return 7;
        if (lowerName.Contains("sandbag") || (uid != null && uid.Substring(2, 2) == "04"))
            return 8;
        if (lowerName.Contains("hold") || lowerName.Contains("long") || (uid != null && uid.Substring(2, 2) == "02"))
            return 3;
        if (uid != null && uid.Substring(2, 2) == "17")
            return 4;

        return noteType;
    }

    private static Result MuseDash(RawChart chart)
    {
        var notes = new List<RawSlot>();
        foreach (var raw in chart.Lines)
        {
            if (!MuseDashChannels.Contains(raw.Channel)) continue;
            notes.AddRange(RawChart.Slots(raw, chart.Width));
        }

        // notes.Sort: Tick 다음 Channel
        notes = notes
            .OrderBy(n => n.Time)
            .ThenBy(n => int.Parse(n.Channel, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToList();

        var pairs = new List<PairKey>();
        var orphanHeads = 0;

        foreach (var sortedNotesInLane in notes.GroupBy(n => n.Channel))
        {
            RawSlot? activeHoldStart = null;
            RawSlot? activeSandbagStart = null;

            foreach (var currentNote in sortedNotesInLane)
            {
                if (!chart.Wavs.TryGetValue(currentNote.Value, out var wavName) || string.IsNullOrWhiteSpace(wavName))
                    continue;

                var noteType = MuseDashNoteType(wavName);
                var isHold = noteType == 3;
                var isSandbag = noteType == 8;

                if (isHold)
                {
                    if (activeHoldStart == null)
                    {
                        activeHoldStart = currentNote;
                    }
                    else
                    {
                        var start = activeHoldStart.Value;
                        pairs.Add(Key(start.Channel, start.Time, currentNote.Channel, currentNote.Time, "Hold"));
                        activeHoldStart = null;
                    }
                }

                if (isSandbag)
                {
                    if (activeSandbagStart == null)
                    {
                        activeSandbagStart = currentNote;
                    }
                    else
                    {
                        var start = activeSandbagStart.Value;
                        pairs.Add(Key(start.Channel, start.Time, currentNote.Channel, currentNote.Time, "Sandbag"));
                        activeSandbagStart = null;
                    }
                }
            }

            if (activeHoldStart != null) orphanHeads++;
            if (activeSandbagStart != null) orphanHeads++;
        }

        return new Result(pairs, orphanHeads, 0, 0);
    }

    // ── UNBEATABLE ─────────────────────────────────────────────────────────
    // UNBEATABLE custom mode / HwaSongLoader.cs ChannelMap · TypeEncoding · BuildNotes

    private static readonly Dictionary<string, (int Side, int Height)> ChannelMap = new()
    {
        { "16", (0, 0) }, { "11", (0, 2) }, { "12", (0, 1) },
        { "13", (1, 0) }, { "14", (1, 2) }, { "15", (1, 1) },
    };

    private static readonly Dictionary<string, bool> TypeEncodingIsHold = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Default", false }, { "Dodge", false }, { "Setpiece", false },
        { "Hold", true }, { "Double", true }, { "Spam", true },
        { "Freestyle", false }, { "Nothing", false }, { "Brawl", false },
    };

    private static Result Unbeatable(RawChart chart)
    {
        var raw = new List<(RawSlot Slot, int Side, int Height, string BaseType)>();

        foreach (var line in chart.Lines)
        {
            if (!ChannelMap.TryGetValue(line.Channel, out var entry)) continue;

            foreach (var slot in RawChart.Slots(line, chart.Width))
            {
                if (!chart.Wavs.TryGetValue(slot.Value, out var filename)) continue;

                var name = Path.GetFileNameWithoutExtension(filename).Trim();
                if (name.EndsWith(" end", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - " end".Length).Trim();

                raw.Add((slot, entry.Side, entry.Height, name));
            }
        }

        raw = raw.OrderBy(r => r.Slot.Time).ToList();

        var pendingStart = new Dictionary<(int Side, int Height, string BaseType), RawSlot>();
        var pairs = new List<PairKey>();

        foreach (var hit in raw)
        {
            if (!TypeEncodingIsHold.TryGetValue(hit.BaseType, out var isHold)) continue;
            if (!isHold) continue;

            var key = (hit.Side, hit.Height, hit.BaseType);
            if (pendingStart.TryGetValue(key, out var start))
            {
                var canonical = TypeEncodingIsHold.Keys.First(k => string.Equals(k, hit.BaseType, StringComparison.OrdinalIgnoreCase));
                pairs.Add(Key(start.Channel, start.Time, hit.Slot.Channel, hit.Slot.Time, canonical));
                pendingStart.Remove(key);
            }
            else
            {
                pendingStart[key] = hit.Slot;
            }
        }

        return new Result(pairs, pendingStart.Count, 0, 0);
    }
}
