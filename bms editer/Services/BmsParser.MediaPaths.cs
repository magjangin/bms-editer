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

        try
        {
            if (Path.IsPathRooted(mediaPath))
                return mediaPath;

            var directPath = Path.GetFullPath(Path.Combine(baseDirectory, mediaPath));
            if (File.Exists(directPath))
                return directPath;

            var fileName = Path.GetFileName(mediaPath);
            if (!string.IsNullOrEmpty(fileName) && fileNameIndex.Value.TryGetValue(fileName, out var indexedPath))
            {
                guessed = true;
                return indexedPath;
            }

            return directPath;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return mediaPath;
        }
    }
}
