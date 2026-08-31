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

    // #<필드명> 형태의 비표준/확장 헤더 필드를 그대로 보관
    public Dictionary<string, string> ExtendedFields { get; } = new();

    public void CopyFrom(BmsHeader source)
    {
        Title = source.Title;
        Artist = source.Artist;
        Genre = source.Genre;
        Bpm = source.Bpm;
        Player = source.Player;
        Rank = source.Rank;
        Level = source.Level;

        ExtendedFields.Clear();
        foreach (var (key, value) in source.ExtendedFields)
            ExtendedFields[key] = value;
    }

    public void Reset() => CopyFrom(new BmsHeader());
}
