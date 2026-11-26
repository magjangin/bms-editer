using System;
using System.Collections.Generic;
using System.IO;

namespace bms_editer.Services;

public static partial class BmsParser
{
    // 하위 폴더 색인의 한계. 곡 폴더의 키음은 많아야 두세 단계 아래 있고 수천 개를 넘지 않는다.
    //
    // 한계가 없을 때는 바탕 화면·다운로드·드라이브 루트에 둔 차트를 열면(그리고 키음 하나라도
    // 적힌 자리에 없으면) 그 아래 디스크 전체를 훑었다. 열기는 UI 스레드에서 돌아서 그동안 창이 멈췄다.
    internal const int MediaIndexMaxDepth = 4;
    internal const int MediaIndexMaxFiles = 20000;

    private static Dictionary<string, string> BuildFileNameIndex(string directory)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return index;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = MediaIndexMaxDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            var visited = 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*", options))
            {
                if (++visited > MediaIndexMaxFiles)
                    break;

                try
                {
                    var fileName = Path.GetFileName(path);
                    if (!string.IsNullOrEmpty(fileName) && !index.ContainsKey(fileName))
                        index[fileName] = path;
                }
                catch
                {
                    // 개별 파일명 해석 실패 시 다음 파일로 계속 진행
                }
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

        try
        {
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
            if (!string.IsNullOrEmpty(fileName))
            {
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
            }

            return directPath;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return mediaPath;
        }
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
