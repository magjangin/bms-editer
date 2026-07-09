using System.Collections.Generic;
using System.IO;
using System.Linq;
using bms_editer.Models;

namespace bms_editer.ViewModels;

public sealed record LaneNoteStat(string LaneId, int Count);

public sealed record WavNoteStat(string Key, string FileName, int Count);

public sealed class NoteStatsViewModel
{
    public IReadOnlyList<LaneNoteStat> Stats { get; }

    public IReadOnlyList<WavNoteStat> WavStats { get; }

    public int TotalCount { get; }

    public NoteStatsViewModel(BmsChart chart, IReadOnlyList<BmsWavItem> wavItems)
    {
        Stats = chart.Lanes
            .Select(lane => new LaneNoteStat(lane.Header, chart.Notes.Count(n => n.LaneId == lane.Id)))
            .ToList();
        WavStats = wavItems
            .Select(item => new WavNoteStat(item.Key, Path.GetFileName(item.FilePath), chart.Notes.Count(n => n.WavKey == item.Key)))
            .ToList();
        TotalCount = chart.Notes.Count;
    }
}
