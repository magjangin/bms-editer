using System;
using System.Collections.Generic;
using System.IO;

namespace bms_editer.Services;

public static partial class BmsParser
{
    private const int MaxMediaSearchDepth = 6;

    private static Dictionary<string, string> BuildFileNameIndex(string directory)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return index;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,

            // 곡 폴더는 얕다. 바탕 화면·다운로드 폴더에 둔 차트에서 키음 하나가 없을 때
            // 그 아래 전체(수십만 개)를 훑느라 열기가 수십 초 멈추지 않게 깊이를 묶는다.
            MaxRecursionDepth = MaxMediaSearchDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", options))
            {
                var fileName = Path.GetFileName(path);
                if (!index.ContainsKey(fileName))
                    index[fileName] = path;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"색인 생성 중 오류: {ex.Message}");
        }

        return index;
    }

    // guessed: 적힌 자리에 파일이 없어서 같은 이름을 하위 폴더에서 찾아 붙였는지.
    // 부르는 쪽이 이 결과를 저장 파일에 박지 않도록 구분해서 알려준다.
    private static string ResolveMediaPath(
        string baseDirectory, string mediaPath, Lazy<Dictionary<string, string>> fileNameIndex, out bool guessed)
    {
        guessed = false;

        if (Path.IsPathRooted(mediaPath))
            return mediaPath;

        var directPath = Path.GetFullPath(Path.Combine(baseDirectory, mediaPath));
        if (File.Exists(directPath))
            return directPath;

        // BMS 플레이어(LR2·beatoraja 등)는 #WAV 에 .wav 라고 적혀 있어도 같은 이름의 .ogg 를 쓴다.
        // 배포용으로 키음을 OGG 로 바꾸면서 차트는 그대로 두는 일이 흔해서, 이걸 안 하면 키음이 전부 빠진다.
        foreach (var alternate in AlternateAudioNames(mediaPath))
        {
            var alternatePath = Path.GetFullPath(Path.Combine(baseDirectory, alternate));
            if (File.Exists(alternatePath))
            {
                guessed = true;
                return alternatePath;
            }
        }

        var fileName = Path.GetFileName(mediaPath);
        if (fileNameIndex.Value.TryGetValue(fileName, out var indexedPath))
        {
            guessed = true;
            return indexedPath;
        }

        foreach (var alternate in AlternateAudioNames(fileName))
        {
            if (fileNameIndex.Value.TryGetValue(alternate, out var alternateIndexed))
            {
                guessed = true;
                return alternateIndexed;
            }
        }

        return directPath;
    }

    // 키음 디코더가 읽을 수 있는 다른 확장자로 바꾼 이름. 오디오 확장자가 아니면 아무것도 내지 않는다.
    private static readonly string[] InterchangeableAudioExtensions = [".wav", ".ogg"];

    internal static IEnumerable<string> AlternateAudioNames(string mediaPath)
    {
        var extension = Path.GetExtension(mediaPath);
        if (Array.FindIndex(InterchangeableAudioExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)) < 0)
            yield break;

        foreach (var alternate in InterchangeableAudioExtensions)
        {
            if (!alternate.Equals(extension, StringComparison.OrdinalIgnoreCase))
                yield return Path.ChangeExtension(mediaPath, alternate);
        }
    }
}
