using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace bms_editer.Services;

public static partial class BmsParser
{
    // ── 인코딩 감지 ────────────────────────────────────────────────────────────
    //
    // 예전에는 "UTF-8 로 읽어보고 앞 100줄에 U+FFFD 가 있으면 CP949 로 다시 읽기" 였다.
    // 구멍이 셋이었다.
    //   * Shift_JIS(CP932) 갈래가 없어서 야생 차트 대부분이 엉뚱한 한글로 읽혔다.
    //     제목뿐 아니라 #WAV 파일명까지 깨져서 키음이 하나도 안 붙었다.
    //   * CP949 는 Shift_JIS 바이트를 오류 없이 삼켜서 U+FFFD 검사에 걸리지도 않았다.
    //   * 앞 100줄만 봐서, 비ASCII 글자가 그 뒤에 처음 나오면 재시도가 아예 안 돌았다.
    //     (#WAV 가 수백 줄이고 헤더는 영문인 차트가 여기에 딱 걸린다)
    //
    // 이제 바이트로 한 번만 읽고 BOM -> UTF-8(엄격) -> CP932/CP949 순으로 가른다.
    // 줄 수 제한이 사라졌고, 고른 인코딩을 그대로 들고 나가 저장에 쓴다.

    private static readonly Encoding DefaultEncoding = new UTF8Encoding(false);

    // 한 바이트라도 어긋나면 예외를 던지는 UTF-8. 조용히 U+FFFD 로 때우면 감지가 안 된다.
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static (string[] Lines, Encoding Encoding) ReadAllLines(
        string filePath, string directory, Dictionary<string, string> fileNameIndex)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var bytes = File.ReadAllBytes(filePath);

        if (TryReadByBom(bytes, out var bomText, out var bomEncoding))
            return (SplitLines(bomText), bomEncoding);

        // UTF-8 로 한 글자도 어긋나지 않고 읽히면 UTF-8 이다.
        // CP932/CP949 로 쓴 한글·일본어가 우연히 올바른 UTF-8 이 되는 일은 사실상 없다.
        if (TryDecodeStrict(bytes, StrictUtf8, out var utf8Text))
            return (SplitLines(utf8Text), DefaultEncoding);

        return DecodeLegacy(bytes, directory, fileNameIndex);
    }

    private static bool TryReadByBom(byte[] bytes, out string text, out Encoding encoding)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            encoding = new UTF8Encoding(true);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            encoding = Encoding.Unicode;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            encoding = Encoding.BigEndianUnicode;
            return true;
        }

        text = string.Empty;
        encoding = DefaultEncoding;
        return false;
    }

    private static bool TryDecodeStrict(byte[] bytes, Encoding encoding, out string text)
    {
        try
        {
            text = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    // CP932(일본어)와 CP949(한국어)는 서로의 바이트열을 대부분 오류 없이 삼킨다.
    // 그래서 "디코딩에 실패했는가"로는 못 가른다. 대신 증거 두 가지를 쓴다.
    //
    //   1순위: #WAV/#BMP 파일명이 실제로 폴더에 있는가.
    //          인코딩이 틀리면 파일명이 깨져서 하나도 안 맞는다. 이게 가장 확실한 증거다.
    //   2순위: 읽어낸 글자가 말이 되는가. 한국어 바이트를 CP932 로 읽으면 반각 가타카나가
    //          잔뜩 나오는데, 실제 일본어 제목·파일명에는 거의 안 쓰인다.
    //
    // 둘 다 못 가르면 CP932 로 둔다. 야생의 BMS 차트는 Shift_JIS 가 가장 많다.
    private static (string[] Lines, Encoding Encoding) DecodeLegacy(
        byte[] bytes, string directory, Dictionary<string, string> fileNameIndex)
    {
        Encoding? bestEncoding = null;
        string? bestText = null;
        var bestResolved = -1;
        var bestScore = int.MinValue;

        foreach (var codePage in new[] { 932, 949 })
        {
            Encoding candidate;
            try
            {
                candidate = Encoding.GetEncoding(codePage);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            var text = candidate.GetString(bytes);
            var resolved = CountResolvableMedia(text, directory, fileNameIndex);
            var score = ScoreLegacyText(text);

            if (resolved > bestResolved || (resolved == bestResolved && score > bestScore))
            {
                bestEncoding = candidate;
                bestText = text;
                bestResolved = resolved;
                bestScore = score;
            }
        }

        if (bestEncoding is null || bestText is null)
        {
            // CodePages 공급자가 없는 환경. 손실을 감수하고라도 읽기는 해야 한다.
            return (SplitLines(Encoding.UTF8.GetString(bytes)), DefaultEncoding);
        }

        return (SplitLines(bestText), bestEncoding);
    }

    private static int CountResolvableMedia(string text, string directory, Dictionary<string, string> fileNameIndex)
    {
        var found = 0;

        foreach (var line in SplitLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != '#')
                continue;

            var match = MediaLineRegex().Match(trimmed);
            if (!match.Success)
                continue;

            var mediaPath = match.Groups[1].Value.Trim();
            if (mediaPath.Length == 0)
                continue;

            try
            {
                if (fileNameIndex.ContainsKey(Path.GetFileName(mediaPath)))
                {
                    found++;
                    continue;
                }

                if (directory.Length > 0 && File.Exists(Path.Combine(directory, mediaPath)))
                    found++;
            }
            catch (ArgumentException)
            {
                // 깨진 파일명에 경로로 못 쓰는 글자가 섞인 경우. 못 찾은 것으로 친다.
            }
        }

        return found;
    }

    private static int ScoreLegacyText(string text)
    {
        var score = 0;

        foreach (var c in text)
        {
            // 디코딩 실패 자리.
            if (c == '�')
                score -= 6;
            // 반각 가타카나. 한국어 바이트를 CP932 로 읽으면 여기가 잔뜩 나온다.
            else if (c is >= '｡' and <= 'ﾟ')
                score -= 3;
            // 텍스트 한가운데의 제어 문자는 어느 쪽이든 잘못 읽은 신호다.
            else if (char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
                score -= 4;
        }

        return score;
    }

    private static string[] SplitLines(string text) =>
        text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
}
