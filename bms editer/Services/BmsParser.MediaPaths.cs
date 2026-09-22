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

        // 들어갈 수 없는 하위 폴더가 하나라도 있으면 SearchOption.AllDirectories 는 예외를 던져서
        // 차트 열기 자체가 실패했다. 키음을 찾는 보조 색인일 뿐이니 못 들어가는 곳은 건너뛴다.
        // 숨김·시스템 파일은 예전처럼 색인에 넣는다(기본값은 빼 버린다).
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };

        foreach (var path in Directory.EnumerateFiles(directory, "*", options))
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
