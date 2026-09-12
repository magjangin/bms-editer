using System;
using System.Collections.Generic;
using System.Globalization;
using bms_editer.Models;

namespace bms_editer.Services;

public static partial class BmsParser
{
    // 시간축을 바꾸는 채널을 읽어 차트에 담는다.
    //
    // 이 줄들은 예전에도 원문 그대로 보존돼서 저장하면 되돌아왔다. 문제는 **읽는 쪽**이었다.
    // 아무도 해석하지 않아서 격자·노트 위치·키음 타이밍이 전부 단일 BPM 과 4/4 를 가정했고,
    // 박자나 BPM 이 바뀌는 차트는 그 지점 이후로 화면과 소리가 어긋났다.
    private static void ReadTimingChannel(BmsChart chart, int measure, string channel, string data)
    {
        // 02: 마디 길이 배율. 슬롯이 아니라 실수 하나가 통째로 온다. (#00002:0.75)
        if (string.Equals(channel, "02", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) && length > 0)
                chart.MeasureLengths[measure] = length;
            return;
        }

        // 03: 16진수 두 자리를 그대로 BPM 으로 쓴다. 정수만 되고 255가 한계다.
        if (string.Equals(channel, "03", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (position, code) in EnumerateSlots(data))
            {
                if (int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bpm) && bpm > 0)
                    chart.BpmChanges.Add(new BpmChange(measure, position, bpm));
            }
            return;
        }

        // 08: base-36 번호로 #BPMxx 표를 가리킨다. 소수점도 되고 255도 넘는다.
        if (string.Equals(channel, "08", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (position, code) in EnumerateSlots(data))
            {
                if (chart.BpmTable.TryGetValue(code, out var bpm) && bpm > 0)
                    chart.BpmChanges.Add(new BpmChange(measure, position, bpm));
            }
        }
    }

    // 데이터 줄을 두 자리씩 끊어 "마디 안 위치 + 코드"로 내놓는다. 빈 칸("00")은 건너뛴다.
    private static IEnumerable<(double Position, string Code)> EnumerateSlots(string data)
    {
        var count = data.Length / 2;
        if (count == 0)
            yield break;

        for (var i = 0; i < count; i++)
        {
            var code = data.Substring(i * 2, 2).ToUpperInvariant();
            if (code == "00")
                continue;

            yield return ((double)i / count, code);
        }
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
}
