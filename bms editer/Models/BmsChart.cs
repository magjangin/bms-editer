using System.Collections.Generic;

namespace bms_editer.Models;

public sealed class BmsChart
{
    public BmsHeader Header { get; } = new();

    public IReadOnlyList<LaneDefinition> Lanes { get; set; } = LaneDefinition.CreateDefault();

    // 마디 인덱스 -> 박자 배율 (#xxx02, 기본값 1.0 = 4/4)
    public Dictionary<int, double> MeasureLengths { get; } = new();

    public List<BmsNote> Notes { get; } = new();

    public Dictionary<string, string> WavTable { get; } = new();
    public Dictionary<string, string> BmpTable { get; } = new();

    public int MeasureCount { get; set; } = 32;

    public double GetMeasureLength(int measure) =>
        MeasureLengths.TryGetValue(measure, out var length) ? length : 1.0;
}
