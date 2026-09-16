using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace bms_editer.Tests;

// 이 컴퓨터에 설치된 게임 폴더에서 실제 차트를 찾는다.
//
// 드라이브 전체를 뒤지면 수 분이 걸린다(실측 470초). 그래서 게임이 차트를 읽는 자리만 본다.
// 각 게임 모드는 <게임 폴더>\hwa\<곡>\*.bms 를 읽는다.
//   * Steam 게임: libraryfolders.vdf 에 적힌 라이브러리들의 steamapps\common\<게임>
//   * Steam 밖 게임: 각 고정 드라이브 루트의 <게임 폴더>
//   * BMS_EDITER_GAME_CHARTS="프로파일id=게임폴더;..." 로 더 줄 수 있다.
internal static class GameChartCorpus
{
    public sealed record GameChart(string ProfileId, string Game, string Path);

    private static readonly (string Folder, string ProfileId)[] SteamGames =
    {
        ("DEFLATE", "deflate"),
        ("Sixtar Gate STARGAZER", "stargazer"),
        ("Sixtar Gate STARTRAIL", "startrail"),
        ("GUNVOLT RECORDS Cychronicle", "gunvolt"),
        ("Muse Dash", "muse_dash"),
        ("UNBEATABLE", "unbeatable"),

        // 모드가 없는 게임인데, UNBEATABLE 차트를 그대로 복사한 파일이 있다(md5 동일).
        // 경로 추정 대상은 아니지만 실제 파일이므로 UNBEATABLE 규칙으로 함께 돌린다.
        ("A Dance of Fire and Ice", "unbeatable"),
    };

    private static readonly (string Folder, string ProfileId)[] DriveRootGames =
    {
        ("Sixtar Gate STARTRAIL custom mode", "startrail"),
        ("muse dash hwa", "muse_dash"),
    };

    private static readonly string[] ChartExtensions = { ".bms", ".bme", ".bml" };

    private static readonly Regex VdfPath = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

    private static readonly Lazy<IReadOnlyList<GameChart>> Cached = new(Discover);

    public static IReadOnlyList<GameChart> Charts => Cached.Value;

    private static IReadOnlyList<GameChart> Discover()
    {
        var roots = new List<(string Root, string ProfileId, string Game)>();

        foreach (var common in SteamCommonDirectories())
        {
            foreach (var (folder, id) in SteamGames)
                roots.Add((Path.Combine(common, folder), id, folder));
        }

        foreach (var drive in FixedDrives())
        {
            foreach (var (folder, id) in DriveRootGames)
                roots.Add((Path.Combine(drive, folder), id, folder));
        }

        roots.AddRange(EnvironmentRoots());

        var charts = new List<GameChart>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, id, game) in roots)
        {
            var hwa = Path.Combine(root, "hwa");
            if (!Directory.Exists(hwa))
                continue;

            foreach (var file in EnumerateCharts(hwa))
            {
                if (seen.Add(Path.GetFullPath(file)))
                    charts.Add(new GameChart(id, game, file));
            }
        }

        return charts
            .OrderBy(c => c.ProfileId, StringComparer.Ordinal)
            .ThenBy(c => c.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateCharts(string hwa)
    {
        var directories = new List<string> { hwa };
        directories.AddRange(Safe(() => Directory.EnumerateDirectories(hwa)));

        foreach (var directory in directories)
        {
            foreach (var file in Safe(() => Directory.EnumerateFiles(directory)))
            {
                if (ChartExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return file;
            }
        }
    }

    private static IEnumerable<string> SteamCommonDirectories()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifests = new List<string>();

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (programFiles.Length > 0)
        {
            libraries.Add(Path.Combine(programFiles, "Steam"));
            manifests.Add(Path.Combine(programFiles, "Steam", "steamapps", "libraryfolders.vdf"));
        }

        foreach (var drive in FixedDrives())
        {
            foreach (var name in new[] { "steam", "SteamLibrary", "Steam" })
            {
                libraries.Add(Path.Combine(drive, name));
                manifests.Add(Path.Combine(drive, name, "steamapps", "libraryfolders.vdf"));
            }
        }

        foreach (var manifest in manifests)
        {
            string text;
            try
            {
                if (!File.Exists(manifest))
                    continue;
                text = File.ReadAllText(manifest);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (Match match in VdfPath.Matches(text))
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }

        return libraries
            .Select(library => Path.Combine(library, "steamapps", "common"))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> FixedDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<(string Root, string ProfileId, string Game)> EnvironmentRoots()
    {
        var value = Environment.GetEnvironmentVariable("BMS_EDITER_GAME_CHARTS");
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
                continue;

            var root = entry[(separator + 1)..];
            yield return (root, entry[..separator], Path.GetFileName(root.TrimEnd('\\', '/')));
        }
    }

    private static IEnumerable<string> Safe(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate().ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
