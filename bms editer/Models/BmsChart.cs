using System.Collections.Generic;

namespace bms_editer.Models;

public sealed class BmsChart
{
    public BmsHeader Header { get; } = new();

    public IReadOnlyList<LaneDefinition> Lanes { get; set; } = LaneDefinition.CreateDefault();

    // 마디 인덱스 -> 박자 배율 (#xxx02, 기본값 1.0 = 4/4)
    public Dictionary<int, double> MeasureLengths { get; } = new();

    public List<BmsNote> Notes { get; } = new();

    // 편집 대상이 아닌 원본 줄(BGM·BPM 변화·BGA·2P 채널·확장 헤더 등).
    // 저장할 때 그대로 다시 내보내야 하므로 파일에 있던 순서대로 담는다.
    public List<BmsRawLine> PreservedLines { get; } = new();

    public Dictionary<string, string> WavTable { get; } = new();
    public Dictionary<string, string> BmpTable { get; } = new();

    public int MeasureCount { get; set; } = 32;

    public double GetMeasureLength(int measure) =>
        MeasureLengths.TryGetValue(measure, out var length) ? length : 1.0;
}
