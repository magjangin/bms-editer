using System.Collections.Generic;

namespace bms_editer.Models;

public enum NoteType
{
    Normal,
    LongStart,
    LongEnd,
    Invisible,
    Mine,
}

public sealed class BmsNote
{
    public int Measure { get; set; }
    public string LaneId { get; set; } = string.Empty;

    // 마디 내 상대 위치 (0.0 ~ 1.0)
    public double Position { get; set; }

    // WAV 테이블을 가리키는 base-36 키 (예: "01", "A3")
    public string WavKey { get; set; } = string.Empty;

    public NoteType Type { get; set; } = NoteType.Normal;
}

public record NotePlacementArgs(string LaneId, int Measure, double Position);

// 마디 단위 복제 결과. 건너뛴 이유를 나눠서 알려줘야 사용자가 왜 덜 복사됐는지 안다.
public readonly record struct NoteCopyResult(int Copied, int Blocked, int OutOfRange);

public record NoteSelectionArgs(IReadOnlyList<BmsNote> Notes);

public enum NoteMoveDirection
{
    TimeForward,
    TimeBackward,
    LanePrevious,
    LaneNext,
}
