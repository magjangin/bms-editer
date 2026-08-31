using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using bms_editer.Models;

namespace bms_editer.Services;

// 파일 하나를 읽어낸 결과. BPM 과 마디 수는 Chart 안에 들어 있다.
// 키음 목록만 따로 나오는데, 정의된 순서를 그대로 지켜야 해서
// 순서가 없는 Chart.WavTable 로는 대신할 수 없기 때문이다.
public sealed record BmsParseResult(BmsChart Chart, IReadOnlyList<BmsWavItem> WavItems)
{
    public double Bpm => Chart.Header.Bpm;
    public int MeasureCount => Chart.MeasureCount;
}

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

    [GeneratedRegex(@"^#GENRE\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex GenreRegex();

    // #PLAYLEVEL 과 겹치지 않는다. "#PLAYER" 뒤에는 반드시 공백이 와야 한다.
    [GeneratedRegex(@"^#PLAYER\s+([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerRegex();

    [GeneratedRegex(@"^#RANK\s+([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RankRegex();

    [GeneratedRegex(@"^#PLAYLEVEL\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayLevelRegex();

    [GeneratedRegex(@"^#([0-9]{3})([0-9a-zA-Z]{2}):(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex DataRegex();

    private static readonly HashSet<string> SupportedChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "16", "11", "12", "13", "14", "15", "18"
    };

    // 예전에는 반환값 하나에 out 3개였다. 그중 BPM 과 마디 수는 차트 안에도 같은 값이
    // 들어 있어서, 호출한 쪽이 어느 쪽을 믿어야 하는지 매번 헷갈렸다.
    // 이제 차트를 다 채워서 하나로 돌려준다.
    public static BmsParseResult Parse(string filePath)
    {
        var chart = new BmsChart();
        var parsedBpm = 120.0;
        var measureCount = 32;
        var wavItems = new List<BmsWavItem>();

        if (!File.Exists(filePath))
        {
            chart.Header.Bpm = parsedBpm;
            chart.MeasureCount = measureCount;
            return new BmsParseResult(chart, wavItems);
        }

        // 다양한 인코딩 대응을 위해 C# Default (ANSI/UTF-8)을 우선하되 한국어 완성형(CP949) 디코딩 대비
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding("utf-8");
        
        // 텍스트 파일 읽은 후 인코딩 추정 혹은 기본 UTF-8 시도
        var rawLines = File.ReadAllLines(filePath, encoding);
        
        // 첫 훑기로 UTF-8 깨짐 감지되면 CP949로 재시도
        if (IsMalformedUtf8(rawLines))
        {
            try
            {
                var cp949 = Encoding.GetEncoding(949);
                rawLines = File.ReadAllLines(filePath, cp949);
            }
            catch
            {
                // 실패 시 UTF-8 유지
            }
        }

        var directory = Path.GetDirectoryName(filePath) ?? "";
        var mediaPathIndex = BuildFileNameIndex(directory);
        var maxMeasure = 0;
        var has3DigitWav = false;

        // 1단계: WAV 정의 및 기본 메타데이터 수집
        foreach (var rawLine in rawLines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || !line.StartsWith("#")) continue;

            var titleMatch = TitleRegex().Match(line);
            if (titleMatch.Success)
            {
                chart.Header.Title = titleMatch.Groups[1].Value.Trim();
                continue;
            }

            var artistMatch = ArtistRegex().Match(line);
            if (artistMatch.Success)
            {
                chart.Header.Artist = artistMatch.Groups[1].Value.Trim();
                continue;
            }

            var bpmMatch = BpmRegex().Match(line);
            if (bpmMatch.Success)
            {
                if (double.TryParse(bpmMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempBpm))
                {
                    parsedBpm = tempBpm;
                }
                continue;
            }

            var genreMatch = GenreRegex().Match(line);
            if (genreMatch.Success)
            {
                chart.Header.Genre = genreMatch.Groups[1].Value.Trim();
                continue;
            }

            // #PLAYER 는 1(Single)/2(Couple)/3(Double). 파일 값을 그대로 담아둔다.
            var playerMatch = PlayerRegex().Match(line);
            if (playerMatch.Success)
            {
                if (int.TryParse(playerMatch.Groups[1].Value, out var tempPlayer))
                    chart.Header.Player = tempPlayer;
                continue;
            }

            var rankMatch = RankRegex().Match(line);
            if (rankMatch.Success)
            {
                if (int.TryParse(rankMatch.Groups[1].Value, out var tempRank))
                    chart.Header.Rank = tempRank;
                continue;
            }

            var playLevelMatch = PlayLevelRegex().Match(line);
            if (playLevelMatch.Success)
            {
                chart.Header.Level = playLevelMatch.Groups[1].Value.Trim();
                continue;
            }

            var wavMatch = WavRegex().Match(line);
            if (wavMatch.Success)
            {
                var key = wavMatch.Groups[1].Value.ToUpper();
                var wavFile = wavMatch.Groups[2].Value.Trim();

                if (key.Length == 3)
                {
                    has3DigitWav = true;
                }

                var absoluteWavPath = ResolveMediaPath(directory, wavFile, mediaPathIndex);
                chart.WavTable[key] = absoluteWavPath;

                wavItems.Add(new BmsWavItem
                {
                    Key = key,
                    FilePath = absoluteWavPath
                });
            }
        }

        // 2단계: 채보 데이터(노트) 파싱
        foreach (var rawLine in rawLines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || !line.StartsWith("#")) continue;

            var dataMatch = DataRegex().Match(line);
            if (!dataMatch.Success)
            {
                // 데이터 줄도 아니고 1단계에서 읽어간 헤더도 아니면 에디터가 모르는 줄이다.
                // 저장할 때 그대로 되돌려 놓으려고 원문을 보관한다.
                if (!IsConsumedHeader(line))
                    chart.PreservedLines.Add(new BmsRawLine { Text = line });
                continue;
            }

            var measureNum = int.Parse(dataMatch.Groups[1].Value);
            var channel = dataMatch.Groups[2].Value; // 예: "11", "12" ...
            var dataStr = dataMatch.Groups[3].Value.Trim();

            // 편집 대상은 1P 건반 채널(16-11-12-13-14-15-18)뿐이다.
            // 나머지(BGM·마디 길이·BPM 변화·STOP·BGA·롱노트·2P 등)는 원문 그대로 보관한다.
            if (!SupportedChannels.Contains(channel))
            {
                chart.PreservedLines.Add(new BmsRawLine
                {
                    Text = line,
                    Measure = measureNum,
                    Channel = channel,
                });

                // 건반이 없는 뒷마디까지 그리드가 이어지도록 마디 수에도 반영한다.
                maxMeasure = Math.Max(maxMeasure, measureNum);
                continue;
            }

            maxMeasure = Math.Max(maxMeasure, measureNum);

            // 데이터 자르기 단위 크기 결정 (2자리 또는 3자리)
            var chunkSize = DetermineChunkSize(dataStr, has3DigitWav, chart.WavTable);

            var chunks = new List<string>();
            for (var i = 0; i < dataStr.Length; i += chunkSize)
            {
                if (i + chunkSize <= dataStr.Length)
                {
                    chunks.Add(dataStr.Substring(i, chunkSize).ToUpper());
                }
            }

            var totalChunks = chunks.Count;
            for (var index = 0; index < totalChunks; index++)
            {
                var code = chunks[index];
                
                // 빈 데이터("00", "000") 건너뛰기
                if (code == "00" || code == "000") continue;

                var position = (double)index / totalChunks;
                chart.Notes.Add(new BmsNote
                {
                    Measure = measureNum,
                    LaneId = channel,
                    Position = position,
                    WavKey = code,
                    Type = NoteType.Normal
                });
            }
        }

        measureCount = Math.Max(32, maxMeasure + 1);
        chart.Header.Bpm = parsedBpm;
        chart.MeasureCount = measureCount;
        return new BmsParseResult(chart, wavItems);
    }

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
        || WavRegex().IsMatch(line);

    private static int DetermineChunkSize(string dataStr, bool has3DigitWav, Dictionary<string, string> wavTable)
    {
        if (!has3DigitWav)
            return 2;

        if (dataStr.Length % 3 == 0 && dataStr.Length % 2 != 0)
            return 3;

        if (dataStr.Length % 6 == 0)
        {
            var matchCount2 = 0;
            var matchCount3 = 0;

            for (var i = 0; i < dataStr.Length; i += 2)
            {
                var k = dataStr.Substring(i, 2).ToUpper();
                if (k != "00" && wavTable.ContainsKey(k))
                    matchCount2++;
            }

            for (var i = 0; i < dataStr.Length; i += 3)
            {
                var k = dataStr.Substring(i, 3).ToUpper();
                if (k != "000" && wavTable.ContainsKey(k))
                    matchCount3++;
            }

            if (matchCount3 > matchCount2)
            {
                return 3;
            }
        }

        return 2;
    }

    private static Dictionary<string, string> BuildFileNameIndex(string directory)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return index;

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(path);
            if (!index.ContainsKey(fileName))
                index[fileName] = path;
        }

        return index;
    }

    private static string ResolveMediaPath(string baseDirectory, string mediaPath, Dictionary<string, string> fileNameIndex)
    {
        if (Path.IsPathRooted(mediaPath))
            return mediaPath;

        var directPath = Path.GetFullPath(Path.Combine(baseDirectory, mediaPath));
        if (File.Exists(directPath))
            return directPath;

        var fileName = Path.GetFileName(mediaPath);
        if (fileNameIndex.TryGetValue(fileName, out var indexedPath))
            return indexedPath;

        return directPath;
    }

    private static bool IsMalformedUtf8(string[] lines)
    {
        var limit = Math.Min(lines.Length, 100);
        for (var i = 0; i < limit; i++)
        {
            // UTF-8 디코딩 실패 시 나오는 대체 문자(\uFFFD) 검출
            if (lines[i].Contains("\uFFFD"))
            {
                return true;
            }
        }
        return false;
    }
}
