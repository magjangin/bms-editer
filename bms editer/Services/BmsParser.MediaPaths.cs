using System;
using System.Collections.Generic;
using System.IO;

namespace bms_editer.Services;

public static partial class BmsParser
{
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

    // guessed: 적힌 자리에 파일이 없어서 같은 이름을 하위 폴더에서 찾아 붙였는지.
    // 부르는 쪽이 이 결과를 저장 파일에 박지 않도록 구분해서 알려준다.
    private static string ResolveMediaPath(
        string baseDirectory, string mediaPath, Dictionary<string, string> fileNameIndex, out bool guessed)
    {
        guessed = false;

        if (Path.IsPathRooted(mediaPath))
            return mediaPath;

        var directPath = Path.GetFullPath(Path.Combine(baseDirectory, mediaPath));
        if (File.Exists(directPath))
            return directPath;

        var fileName = Path.GetFileName(mediaPath);
        if (fileNameIndex.TryGetValue(fileName, out var indexedPath))
        {
            guessed = true;
            return indexedPath;
        }

        return directPath;
    }
}
