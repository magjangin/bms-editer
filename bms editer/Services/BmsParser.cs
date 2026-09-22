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
//
// Encoding 은 이 파일을 어떤 인코딩으로 읽어냈는지다. 저장할 때 같은 인코딩으로
// 되돌려 써야 원본이 갈아치워지지 않는다.
public sealed record BmsParseResult(BmsChart Chart, IReadOnlyList<BmsWavItem> WavItems, Encoding Encoding)
{
    public double Bpm => Chart.Header.Bpm;
    public int MeasureCount => Chart.MeasureCount;
}

public static partial class BmsParser
{
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
            return new BmsParseResult(chart, wavItems, DefaultEncoding);
        }

        var directory = Path.GetDirectoryName(filePath) ?? "";
        var mediaPathIndex = BuildFileNameIndex(directory);

        // 파일을 바이트로 한 번만 읽고 인코딩을 가려낸다. 어느 인코딩으로 읽었는지는
        // 결과에 실어 보내, 저장할 때 그대로 되돌려 쓴다.
        var (rawLines, encoding) = ReadAllLines(filePath, directory, mediaPathIndex);

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

            var extendedBpmMatch = ExtendedBpmRegex().Match(line);
            if (extendedBpmMatch.Success)
            {
                if (double.TryParse(extendedBpmMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tableBpm)
                    && tableBpm > 0)
                {
                    chart.BpmTable[extendedBpmMatch.Groups[1].Value.ToUpper()] = tableBpm;
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

            var audioOffsetMatch = AudioOffsetRegex().Match(line);
            if (audioOffsetMatch.Success)
            {
                if (double.TryParse(audioOffsetMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempOffset))
                    chart.Header.AudioOffsetMs = tempOffset;
                continue;
            }

            var profileMatch = ProfileRegex().Match(line);
            if (profileMatch.Success)
            {
                chart.Header.ProfileId = profileMatch.Groups[1].Value.Trim();
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

                var absoluteWavPath = ResolveMediaPath(directory, wavFile, mediaPathIndex, out var pathGuessed);
                chart.WavTable[key] = absoluteWavPath;

                wavItems.Add(new BmsWavItem
                {
                    Key = key,
                    FilePath = absoluteWavPath,
                    SourceText = wavFile,
                    IsPathGuessed = pathGuessed,
                });
            }
        }

        // 2단계: 채보 데이터(노트) 및 제어문 파싱
        var currentMeasure = 0;
        var currentBranchId = 0;
        var nextBranchId = 1;
        var branchStack = new Stack<int>();

        for (var lineIndex = 0; lineIndex < rawLines.Length; lineIndex++)
        {
            var rawLine = rawLines[lineIndex];
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            // '#' 로 시작하지 않는 줄은 주석이다(`*----` 같은 구분선이 흔하다).
            // 예전에는 원문 보존 대상에서 빠져 저장할 때 통째로 사라졌다.
            if (!line.StartsWith("#"))
            {
                chart.PreservedLines.Add(new BmsRawLine
                {
                    Text = line,
                    Measure = -1,
                    Order = lineIndex,
                    BranchId = currentBranchId,
                });
                continue;
            }

            var dataMatch = DataRegex().Match(line);
            if (!dataMatch.Success)
            {
                // 갈래를 나누는 제어 줄
                if (ControlFlowRegex().IsMatch(line))
                {
                    chart.HasConditionalBlocks = true;

                    if (Regex.IsMatch(line, @"^#(?:IF|CASE|DEF)\b", RegexOptions.IgnoreCase))
                    {
                        currentBranchId = nextBranchId++;
                        branchStack.Push(currentBranchId);
                    }
                    else if (Regex.IsMatch(line, @"^#(?:ELSEIF|ELSE)\b", RegexOptions.IgnoreCase))
                    {
                        if (branchStack.Count > 0) branchStack.Pop();
                        currentBranchId = nextBranchId++;
                        branchStack.Push(currentBranchId);
                    }
                    else if (Regex.IsMatch(line, @"^#(?:ENDIF|ENDSW)\b", RegexOptions.IgnoreCase))
                    {
                        if (branchStack.Count > 0) branchStack.Pop();
                        currentBranchId = branchStack.Count > 0 ? branchStack.Peek() : 0;
                    }

                    chart.PreservedLines.Add(new BmsRawLine
                    {
                        Text = line,
                        Measure = currentMeasure,
                        Order = lineIndex,
                        BranchId = currentBranchId,
                        IsControlFlow = true,
                    });
                    continue;
                }

                // 데이터 줄도 아니고 1단계에서 읽어간 헤더도 아니면 에디터가 모르는 줄이다.
                // 저장할 때 그대로 되돌려 놓으려고 원문을 보관한다.
                if (!IsConsumedHeader(line))
                {
                    chart.PreservedLines.Add(new BmsRawLine
                    {
                        Text = line,
                        Measure = -1,
                        Order = lineIndex,
                        BranchId = 0,
                    });
                }
                continue;
            }

            var measureNum = int.Parse(dataMatch.Groups[1].Value);
            var channel = dataMatch.Groups[2].Value; // 예: "11", "12" ...
            var dataStr = dataMatch.Groups[3].Value.Trim();
            currentMeasure = measureNum;

            // 편집 대상은 1P 건반 채널(16-11-12-13-14-15-18)뿐이다.
            // 나머지(BGM·마디 길이·BPM 변화·STOP·BGA·롱노트·2P 등)는 원문 그대로 보관한다.
            if (!SupportedChannels.Contains(channel))
            {
                chart.PreservedLines.Add(new BmsRawLine
                {
                    Text = line,
                    Measure = measureNum,
                    Order = lineIndex,
                    BranchId = currentBranchId,
                });

                // 건반이 없는 뒷마디까지 그리드가 이어지도록 마디 수에도 반영한다.
                maxMeasure = Math.Max(maxMeasure, measureNum);

                // 편집 대상은 아니지만 격자·재생 시각을 맞추려면 읽어는 둬야 하는 채널들.
                // 원문 보존은 위에서 이미 했으므로 저장에는 영향이 없다.
                ReadTimingChannel(chart, measureNum, channel, dataStr);
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
                    Type = NoteType.Normal,
                    BranchId = currentBranchId,
                    SourceLineOrder = lineIndex,
                });
            }
        }

        measureCount = Math.Max(32, maxMeasure + 1);
        chart.Header.Bpm = parsedBpm;
        chart.MeasureCount = measureCount;
        return new BmsParseResult(chart, wavItems, encoding);
    }
}
