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

        sb.Append("#TITLE ").AppendLine(title);
        sb.Append("#ARTIST ").AppendLine(artist);
        sb.Append("#GENRE ").AppendLine(genre);
        sb.Append("#BPM ").AppendLine(bpm.ToString("0.######", CultureInfo.InvariantCulture));
        sb.Append("#PLAYER ").AppendLine((player + 1).ToString(CultureInfo.InvariantCulture));
        sb.Append("#RANK ").AppendLine(rank.ToString(CultureInfo.InvariantCulture));
        sb.Append("#PLAYLEVEL ").AppendLine(level);
        sb.AppendLine();

        var keyWidth = wavItems.Any(w => w.Key.Length > 2) ? 3 : 2;
        var emptySlot = new string('0', keyWidth);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath)) ?? "";

        foreach (var wav in wavItems.OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = wav.Key.PadLeft(keyWidth, '0');
            var relativePath = MakeRelativePath(outputDirectory, wav.FilePath);
            sb.Append("#WAV").Append(key).Append(' ').AppendLine(relativePath);
        }
        sb.AppendLine();

        var laneOrder = BuildLaneOrder(chart.Lanes);

        var groups = chart.Notes
            .GroupBy(n => (n.Measure, n.LaneId))
            .OrderBy(g => g.Key.Measure)
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
            sb.Append('#').Append(measureTag).Append(group.Key.LaneId).Append(':').AppendLine(string.Concat(slots));
        }

        return sb.ToString();
    }

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
