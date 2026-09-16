using System.Collections.Generic;

namespace bms_editer.Models;

public sealed class BmsHeader
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public double Bpm { get; set; } = 120.0;
    public int Player { get; set; } = 1;
    public int Rank { get; set; } = 2;
    public string Level { get; set; } = string.Empty;
    public double AudioOffsetMs { get; set; } = 0.0;

    // 이 차트의 홀드를 어느 게임 규칙으로 짝짓는지(#BMSEDITER_PROFILE).
    //
    // 사용자가 직접 고른 경우에만 채운다. 경로로 추정한 프로파일은 여기 넣지 않는다.
    // 추측을 파일에 박으면 차트를 다른 폴더로 옮긴 뒤에도 틀린 추측이 따라다닌다.
    public string ProfileId { get; set; } = string.Empty;

    // 확장 헤더(#TOTAL, #STAGEFILE, #BPMxx 등)는 여기 담지 않는다.
    // BmsChart.PreservedLines 가 원문 그대로 들고 있다가 저장할 때 되돌려 놓는다.
    // 예전에는 ExtendedFields 사전이 있었는데, 채우는 곳도 읽는 곳도 없는 빈 껍데기였다.

    public void CopyFrom(BmsHeader source)
    {
        Title = source.Title;
        Artist = source.Artist;
        Genre = source.Genre;
        Bpm = source.Bpm;
        Player = source.Player;
        Rank = source.Rank;
        Level = source.Level;
        AudioOffsetMs = source.AudioOffsetMs;
        ProfileId = source.ProfileId;
    }
}
