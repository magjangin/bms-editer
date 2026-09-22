using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;

namespace bms_editer.Services;

public static class BmsWriter
{
    private const int MaxResolutionDenominator = 1920;

    public static string Write(
        BmsChart chart,
        string title,
        string artist,
        string genre,
        double bpm,
        int player,
        int rank,
        string level,
        IReadOnlyList<BmsWavItem> wavItems,
        string outputFilePath)
    {
        var sb = new StringBuilder();

        // 값이 빈 헤더는 아예 쓰지 않는다.
        // 예전에는 무조건 다 써서, 세 줄짜리 차트를 열었다 저장하면 원본에 없던
        // `#TITLE `·`#GENRE `·`#PLAYLEVEL ` 같은 빈 줄이 붙어 열한 줄이 됐다.
        AppendIfPresent(sb, "#TITLE", title);
        AppendIfPresent(sb, "#ARTIST", artist);
        AppendIfPresent(sb, "#GENRE", genre);

        // BPM·PLAYER·RANK 는 재생에 반드시 필요한 값이라 비어 있을 수 없다. 늘 쓴다.
        sb.Append("#BPM ").AppendLine(bpm.ToString("0.######", CultureInfo.InvariantCulture));
        sb.Append("#PLAYER ").AppendLine((player + 1).ToString(CultureInfo.InvariantCulture));
        sb.Append("#RANK ").AppendLine(rank.ToString(CultureInfo.InvariantCulture));

        AppendIfPresent(sb, "#PLAYLEVEL", level);

        if (Math.Abs(chart.Header.AudioOffsetMs) > 1e-4)
            sb.Append("#BMSEDITER_OFFSET ").AppendLine(chart.Header.AudioOffsetMs.ToString("0.####", CultureInfo.InvariantCulture));

        // 사용자가 고른 게임 프로파일만 적는다. 경로로 추정한 프로파일은 Header 에 들어오지 않는다.
        if (!string.IsNullOrWhiteSpace(chart.Header.ProfileId))
            sb.Append("#BMSEDITER_PROFILE ").AppendLine(chart.Header.ProfileId);

        // 조건 블록(#RANDOM·#IF·#SWITCH)이 차지하던 원문 줄 범위. 이 안의 줄은 아래에서
        // 원래 순서 그대로 내보낸다. 헤더 블록으로 끌어올리면 갈래 밖으로 빠져나간다.
        var conditionalRegions = ConditionalBlocks.FindRegions(chart.PreservedLines);

        // 에디터가 다루지 않는 헤더(#TOTAL, #STAGEFILE, #BPMxx, #STOPxx, #BMPxx 등)를
        // 읽어들인 원문 그대로 되돌려 놓는다. 없으면 저장할 때마다 사라진다.
        foreach (var raw in chart.PreservedLines)
        {
            if (!raw.IsData && !ConditionalBlocks.Contains(conditionalRegions, raw.Order))
                sb.AppendLine(raw.Text);
        }

        sb.AppendLine();

        var keyWidth = ComputeKeyWidth(chart, wavItems);
        var emptySlot = new string('0', keyWidth);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath)) ?? "";

        // 같은 번호가 두 번 정의돼 있으면 마지막 것만 쓴다.
        //
        // WavTable(재생에 쓰는 표)은 원래부터 마지막 것만 남기는데 WavItems 에는 둘 다
        // 들어 있어서, 저장하면 #WAV01 이 두 줄로 늘어났다. 저장할 때마다 늘어나고,
        // 재생과 파일이 서로 다른 파일을 가리키게 된다.
        var uniqueWavItems = wavItems
            .GroupBy(w => w.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var wav in uniqueWavItems)
        {
            var key = wav.Key.PadLeft(keyWidth, '0');
            sb.Append("#WAV").Append(key).Append(' ').AppendLine(ResolveOutputPath(wav, outputDirectory));
        }
        sb.AppendLine();

        var laneOrder = BuildLaneOrder(chart.Lanes);

        // 데이터 줄은 두 무리로 나눠 내보낸다.
        //
        //   * 조건 블록 밖의 줄: 편집한 건반 줄과 보존한 원문 줄(BGM·마디 길이·BPM 변화 등)을
        //     마디 순으로 합친다. BMS 에서 데이터 줄의 위치는 뜻이 없으므로 다시 정렬해도 된다.
        //   * 조건 블록 안의 줄: 제어 줄·원문 줄·갈래 노트를 **원래 줄 순서 그대로** 뒤에 붙인다.
        //     조건 블록의 뜻은 줄의 위치에 달려 있다. 예전에는 이것까지 마디 순으로 섞어서,
        //     블록 뒤의 무조건 줄이 #IF 안으로 끌려 들어가거나 #IF 가 닫히지 않은 파일이 나왔다.
        //     블록끼리의 순서도 지킨다(#RANDOM 은 나올 때마다 새 난수를 뽑는다).
        var dataLines = new List<(int Measure, int Order, string Text)>();
        var conditionalLines = new List<(int Order, int Measure, int Lane, string Text)>();

        foreach (var raw in chart.PreservedLines)
        {
            if (ConditionalBlocks.Contains(conditionalRegions, raw.Order))
                conditionalLines.Add((raw.Order, 0, 0, raw.Text));
            else if (raw.IsData)
                dataLines.Add((raw.Measure, 0, raw.Text));
        }

        var groups = chart.Notes
            .GroupBy(n => (n.Measure, n.BranchId, n.LaneId))
            .OrderBy(g => g.Key.Measure)
            .ThenBy(g => g.Key.BranchId)
            .ThenBy(g => laneOrder.TryGetValue(g.Key.LaneId, out var order) ? order : int.MaxValue);

        foreach (var group in groups)
        {
            var notes = group.OrderBy(n => n.Position).ToList();
            var resolution = ComputeResolution(notes);
            var slots = new string[resolution];
            for (var i = 0; i < resolution; i++)
                slots[i] = emptySlot;

            foreach (var note in notes)
            {
                var index = Math.Clamp((int)Math.Round(note.Position * resolution), 0, resolution - 1);
                var code = note.WavKey;
                slots[index] = code.Length >= keyWidth ? code.Substring(0, keyWidth) : code.PadLeft(keyWidth, '0');
            }

            var measureTag = group.Key.Measure.ToString("000", CultureInfo.InvariantCulture);
            var lIndex = laneOrder.TryGetValue(group.Key.LaneId, out var o) ? o + 1 : 100;
            var text = $"#{measureTag}{group.Key.LaneId}:{string.Concat(slots)}";

            if (group.Key.BranchId > 0)
            {
                // 갈래 노트는 읽어 온 줄 자리로 돌아간다. 옮긴 노트도 원래 줄 번호를 들고 있어서 같은 갈래에 남는다.
                var sourceOrder = notes.Min(n => n.SourceLineOrder);
                conditionalLines.Add((sourceOrder, group.Key.Measure, lIndex, text));
            }
            else
            {
                dataLines.Add((group.Key.Measure, lIndex, text));
            }
        }

        // OrderBy 는 안정 정렬이라 순서 값이 같은 원문 줄끼리는 담은 순서가 유지된다.
        foreach (var line in dataLines.OrderBy(d => d.Measure).ThenBy(d => d.Order))
            sb.AppendLine(line.Text);

        foreach (var line in conditionalLines.OrderBy(d => d.Order).ThenBy(d => d.Measure).ThenBy(d => d.Lane))
            sb.AppendLine(line.Text);

        return sb.ToString();
    }

    private static void AppendIfPresent(StringBuilder sb, string tag, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        sb.Append(tag).Append(' ').AppendLine(value);
    }

    // 키 자릿수는 #WAV 테이블과 노트 키 **양쪽**의 최대 길이로 잡는다.
    //
    // 테이블만 보면, 테이블에 정의되지 않은 3자리 키를 가리키는 노트가 있을 때 keyWidth 가 2 로
    // 잡히고, 아래 슬롯 채우기의 Substring(0, keyWidth) 이 "0ZZ" 를 "0Z" 로 잘라 버린다.
    // 노트가 전혀 다른 소리를 가리키게 되는데 아무 경고도 없다.
    private static int ComputeKeyWidth(BmsChart chart, IReadOnlyList<BmsWavItem> wavItems)
    {
        var width = 2;

        foreach (var wav in wavItems)
            width = Math.Max(width, wav.Key.Length);

        foreach (var note in chart.Notes)
            width = Math.Max(width, note.WavKey.Length);

        // BMS 규격상 키는 2자리 아니면 3자리다.
        return Math.Clamp(width, 2, 3);
    }

    // 적힌 자리에 파일이 없어 하위 폴더에서 같은 이름을 찾아 붙인 경우에는
    // 그 **추측 결과를 파일에 박지 않는다.** 재생에는 쓰되 저장은 원문을 지킨다.
    // 오래된 백업 폴더가 남아 있으면 차트가 조용히 그쪽을 가리키게 되기 때문이다.
    private static string ResolveOutputPath(BmsWavItem wav, string outputDirectory) =>
        wav.IsPathGuessed && !string.IsNullOrEmpty(wav.SourceText)
            ? wav.SourceText
            : MakeRelativePath(outputDirectory, wav.FilePath);

    private static Dictionary<string, int> BuildLaneOrder(IReadOnlyList<LaneDefinition> lanes)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lanes.Count; i++)
            map[lanes[i].Id] = i;
        return map;
    }

    // 마디 내 노트 위치(0.0~1.0)를 정확히 표현할 수 있는 최소 분할 수를 구함 (분모의 최소공배수)
    private static int ComputeResolution(IReadOnlyList<BmsNote> notes)
    {
        long resolution = 1;
        foreach (var note in notes)
        {
            var denominator = ToDenominator(note.Position, MaxResolutionDenominator);
            resolution = Lcm(resolution, denominator);
            if (resolution >= MaxResolutionDenominator)
            {
                resolution = MaxResolutionDenominator;
                break;
            }
        }
        return (int)resolution;
    }

    private static int ToDenominator(double value, int maxDenominator)
    {
        value -= Math.Floor(value);
        if (value <= 1e-9)
            return 1;

        for (var denominator = 1; denominator <= maxDenominator; denominator++)
        {
            var numerator = value * denominator;
            if (Math.Abs(numerator - Math.Round(numerator)) < 1e-6)
                return denominator;
        }

        return maxDenominator;
    }

    private static long Lcm(long a, long b) => a / Gcd(a, b) * b;

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a == 0 ? 1 : a;
    }

    private static string MakeRelativePath(string baseDirectory, string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath))
            return targetPath;

        try
        {
            return Path.GetRelativePath(baseDirectory, targetPath);
        }
        catch
        {
            return targetPath;
        }
    }
}
