using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using bms_editer.Models;

namespace bms_editer.Services;

public static class BmsParser
{
    private static readonly Regex WavRegex = new(@"^#WAV([0-9a-zA-Z]{2,3})\s+(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TitleRegex = new(@"^#TITLE\s+(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ArtistRegex = new(@"^#ARTIST\s+(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BpmRegex = new(@"^#BPM\s+([0-9\.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DataRegex = new(@"^#([0-9]{3})([0-9a-zA-Z]{2}):(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SupportedChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "11", "12", "13", "16", "14", "15", "18"
    };

    public static BmsChart Parse(string filePath, out double parsedBpm, out int measureCount, out List<BmsWavItem> wavItems)
    {
        var chart = new BmsChart();
        parsedBpm = 120.0;
        measureCount = 32;
        wavItems = new List<BmsWavItem>();

        if (!File.Exists(filePath))
            return chart;

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
        var maxMeasure = 0;
        var has3DigitWav = false;

        // 1단계: WAV 정의 및 기본 메타데이터 수집
        foreach (var rawLine in rawLines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || !line.StartsWith("#")) continue;

            var titleMatch = TitleRegex.Match(line);
            if (titleMatch.Success)
            {
                chart.Header.Title = titleMatch.Groups[1].Value.Trim();
                continue;
            }

            var artistMatch = ArtistRegex.Match(line);
            if (artistMatch.Success)
            {
                chart.Header.Artist = artistMatch.Groups[1].Value.Trim();
                continue;
            }

            var bpmMatch = BpmRegex.Match(line);
            if (bpmMatch.Success)
            {
                if (double.TryParse(bpmMatch.Groups[1].Value, out var tempBpm))
                {
                    parsedBpm = tempBpm;
                }
                continue;
            }

            var wavMatch = WavRegex.Match(line);
            if (wavMatch.Success)
            {
                var key = wavMatch.Groups[1].Value.ToUpper();
                var wavFile = wavMatch.Groups[2].Value.Trim();

                if (key.Length == 3)
                {
                    has3DigitWav = true;
                }

                // 상대 경로를 절대 경로로 보정
                var absoluteWavPath = Path.Combine(directory, wavFile);
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

            var dataMatch = DataRegex.Match(line);
            if (!dataMatch.Success) continue;

            var measureNum = int.Parse(dataMatch.Groups[1].Value);
            var channel = dataMatch.Groups[2].Value; // 예: "11", "12" ...
            var dataStr = dataMatch.Groups[3].Value.Trim();

            // 1P 건반 채널 필터링 (11-12-13-16-14-15-18)
            if (!SupportedChannels.Contains(channel))
            {
                continue; // 지원하지 않는 다른 채널(예: BGA, BPM 변화, 2P 채널 등)은 우선 무시
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
        return chart;
    }

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
