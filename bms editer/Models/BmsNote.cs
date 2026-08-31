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

// 지금 선택이 어디서 만들어졌는지. 격자가 강조 색을 이걸로 고른다.
// 통계 창에서 고른 것은 "이 키음이 어디에 찍혀 있나"를 훑어보는 용도라,
// 손으로 고른 선택(노랑)과 한눈에 구별되도록 다른 색으로 그린다.
public enum NoteSelectionSource
{
    Grid,
    Search,
    Stats,
}

public enum NoteMoveDirection
{
    TimeForward,
    TimeBackward,
    LanePrevious,
    LaneNext,
}
