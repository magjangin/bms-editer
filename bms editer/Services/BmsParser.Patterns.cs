using System.Text.RegularExpressions;

namespace bms_editer.Services;

public static partial class BmsParser
{
    [GeneratedRegex(@"^#WAV([0-9a-zA-Z]{2,3})\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex WavRegex();

    [GeneratedRegex(@"^#TITLE\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^#ARTIST\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistRegex();

    [GeneratedRegex(@"^#BPM\s+([0-9\.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BpmRegex();

    // #BPMxx 확장 BPM 표. "#BPM " 과 달리 번호가 붙으므로 위 정규식과 겹치지 않는다.
    // 이 줄은 원문 보존 대상으로도 남는다(IsConsumedHeader 에 넣지 않는다).
    // 읽어두는 이유는 저장이 아니라 채널 08 의 BPM 변화를 풀어내기 위해서다.
    [GeneratedRegex(@"^#BPM([0-9a-zA-Z]{2})\s+([0-9\.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExtendedBpmRegex();

    [GeneratedRegex(@"^#GENRE\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex GenreRegex();

    // #PLAYLEVEL 과 겹치지 않는다. "#PLAYER" 뒤에는 반드시 공백이 와야 한다.
    [GeneratedRegex(@"^#PLAYER\s+([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerRegex();

    [GeneratedRegex(@"^#RANK\s+([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RankRegex();

    [GeneratedRegex(@"^#PLAYLEVEL\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayLevelRegex();

    [GeneratedRegex(@"^#BMSEDITER_OFFSET\s+([-+0-9\.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AudioOffsetRegex();

    // 이 차트의 홀드를 어느 게임 규칙으로 짝짓는지. 게임 쪽 파서는 모르는 헤더라 무시한다.
    [GeneratedRegex(@"^#BMSEDITER_PROFILE\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileRegex();

    // 마디는 보통 세 자리지만, 규격을 넘겨 네 자리를 쓰는 차트가 실제로 있다.
    // 세 자리로만 받으면 해석에 실패해서 "에디터가 모르는 줄"이 되고,
    // 저장할 때 데이터 줄이 아니라 파일 맨 위 헤더 블록으로 끌려 올라갔다.
    [GeneratedRegex(@"^#([0-9]{3,})([0-9a-zA-Z]{2}):(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex DataRegex();

    // 갈래를 나누는 제어 줄. 어느 갈래인지는 ConditionalBlocks 가 가린다.
    [GeneratedRegex(
        @"^#(?:RANDOM|SETRANDOM|ENDRANDOM|RONDAM|IF|ELSEIF|ELSE|ENDIF|SWITCH|SETSWITCH|CASE|SKIP|DEF|ENDSW)(?:\s|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ControlFlowRegex();

    // 인코딩을 가릴 때만 쓰는, #WAV/#BMP 줄의 파일명 추출용.
    [GeneratedRegex(@"^#(?:WAV|BMP)[0-9a-zA-Z]{2,3}\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MediaLineRegex();

    // 1단계에서 이미 읽어간(= 저장할 때 에디터가 새로 써주는) 헤더인지 판별한다.
    // 여기서 false 인 줄만 원문 보관 대상이 된다.
    private static bool IsConsumedHeader(string line) =>
        TitleRegex().IsMatch(line)
        || ArtistRegex().IsMatch(line)
        || BpmRegex().IsMatch(line)
        || GenreRegex().IsMatch(line)
        || PlayerRegex().IsMatch(line)
        || RankRegex().IsMatch(line)
        || PlayLevelRegex().IsMatch(line)
        || AudioOffsetRegex().IsMatch(line)
        || ProfileRegex().IsMatch(line)
        || WavRegex().IsMatch(line);
}
