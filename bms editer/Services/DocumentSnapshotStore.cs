using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace bms_editer.Services;

public enum SnapshotKind
{
    // 편집 중 몇 분마다 찍는 자동 저장본. 비정상 종료 복구에 쓴다.
    Auto,

    // [저장]이 원본을 덮어쓰기 직전의 파일을 그대로 옮겨 둔 것.
    BeforeSave,
}

// 스냅숏 한 벌. 되살리는 데 필요한 것(원래 자리, 인코딩)을 같이 들고 있다.
public sealed record SnapshotInfo(
    string SnapshotPath,
    string? OriginalPath,
    SnapshotKind Kind,
    DateTime SavedAtUtc,
    int CodePage,
    bool HasPreamble)
{
    // 저장 전 사본은 원본 바이트를 그대로 옮기므로 인코딩을 모른다(CodePage 0).
    public Encoding? GetEncoding()
    {
        if (CodePage <= 0)
            return null;

        if (CodePage == Encoding.UTF8.CodePage)
            return new UTF8Encoding(HasPreamble);

        try
        {
            return Encoding.GetEncoding(CodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

// 지난번 실행이 비정상 종료되면서 남긴, 아직 저장되지 않은 작업.
public sealed record AbandonedSession(string SessionId, SnapshotInfo Snapshot);

// 자동 저장본과 저장 이력을 %LocalAppData% 아래에 쌓고, 비정상 종료를 알아챈다. (알려진 문제 S-3·S-4)
//
// 왜 필요한가:
//   * 앱이 꺼지면 저장 안 한 작업이 통째로 사라졌다. 한 곡에 네 시간이 걸리는 도구다.
//   * 원본 옆의 .bak 은 한 벌뿐이라, 저장을 두 번 하면 그 전 내용이 사라졌다.
//
// 스냅숏은 **원래 파일 자리를 기준으로** 적는다(#WAV 상대 경로 포함). 그래서 곡 폴더에
// 복사해 넣으면 그대로 열린다. 문서마다 폴더를 하나씩 두고, 종류마다 최근 10벌만 남긴다.
//
// 비정상 종료는 실행마다 세션 파일을 하나 두는 것으로 알아챈다. 정상 종료하면 지우고,
// 다음 실행 때 주인 프로세스가 없는 세션 파일이 남아 있으면 그 실행은 죽은 것이다.
// 세션 파일은 "지금 저장 안 된 작업이 있는가, 있다면 가장 최근 자동 저장본은 무엇인가"만 적는다.
public sealed class DocumentSnapshotStore
{
    public const int KeepPerKind = 10;

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private const string MetadataExtension = ".json";

    private readonly Func<string, bool> _isSessionAlive;
    private readonly Func<DateTime> _utcNow;

    public DocumentSnapshotStore(
        string rootDirectory,
        string sessionId,
        Func<string, bool>? isSessionAlive = null,
        Func<DateTime>? utcNow = null,
        string? logDirectory = null)
    {
        RootDirectory = rootDirectory;
        SessionId = sessionId;
        LogDirectory = logDirectory ?? Path.Combine(rootDirectory, "logs");
        _isSessionAlive = isSessionAlive ?? IsProcessSessionAlive;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // 실제 앱이 쓰는 저장소. 세션 이름은 "프로세스 번호-시작 시각"이라 번호가 재사용돼도 헷갈리지 않는다.
    public static DocumentSnapshotStore CreateDefault()
    {
        using var process = Process.GetCurrentProcess();
        var sessionId = $"{process.Id}-{process.StartTime.ToUniversalTime().Ticks}";
        return new DocumentSnapshotStore(AppDataPaths.Recovery, sessionId, logDirectory: AppDataPaths.Logs);
    }

    public string RootDirectory { get; }

    // 자동 저장·저장 이력이 실패했을 때 기록을 남길 곳. 앱에서는 ErrorLog 와 같은 폴더다.
    // 테스트가 만든 저장소는 제 폴더 안에 두어 사용자의 기록 폴더를 건드리지 않는다.
    public string LogDirectory { get; }

    public string SessionId { get; }

    public string SnapshotsDirectory => Path.Combine(RootDirectory, "snapshots");

    private string SessionsDirectory => Path.Combine(RootDirectory, "sessions");

    private string SessionFilePath(string sessionId) => Path.Combine(SessionsDirectory, sessionId + ".json");

    // 문서 하나의 스냅숏이 모이는 폴더.
    //
    // 이 PC 의 게임 차트는 전부 파일명이 hwa2.bms 라서 이름만으로는 곡을 못 가린다.
    // 곡 폴더명을 앞에 붙여 사람이 알아보게 하고, 전체 경로의 해시로 겹치지 않게 한다.
    // 경로가 없는(제목 없는) 문서는 이번 실행의 몫으로 모은다.
    public string GetDocumentDirectory(string? originalPath) =>
        Path.Combine(SnapshotsDirectory, DocumentKey(originalPath));

    private string DocumentKey(string? originalPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
            return $"untitled_{SessionId}";

        var fullPath = Path.GetFullPath(originalPath);
        var folder = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())))[..8];

        return $"{Sanitize($"{folder}_{name}")}_{hash}";
    }

    private static string Sanitize(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return safe.Length > 60 ? safe[..60] : safe;
    }

    // ── 스냅숏 ───────────────────────────────────────────────────────────────

    public SnapshotInfo WriteSnapshot(string? originalPath, string content, Encoding encoding, SnapshotKind kind = SnapshotKind.Auto)
    {
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(content);

        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);

        return WriteSnapshotBytes(originalPath, bytes, kind, encoding.CodePage, preamble.Length > 0);
    }

    // 덮어쓰기 직전의 파일을 그대로 옮겨 둔다. 바로 앞 사본과 똑같으면 또 만들지 않는다.
    // (바꾼 것 없이 [저장]을 여러 번 눌러도 이력이 같은 것으로 채워지지 않게)
    public SnapshotInfo? CopyBeforeOverwrite(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var bytes = File.ReadAllBytes(filePath);
        var latest = ListSnapshots(filePath, SnapshotKind.BeforeSave).FirstOrDefault();
        if (latest is not null && File.ReadAllBytes(latest.SnapshotPath).AsSpan().SequenceEqual(bytes))
            return null;

        return WriteSnapshotBytes(filePath, bytes, SnapshotKind.BeforeSave, codePage: 0, hasPreamble: false);
    }

    // 이 문서의 스냅숏을 최근 것부터.
    public IReadOnlyList<SnapshotInfo> ListSnapshots(string? originalPath, SnapshotKind kind)
    {
        var directory = GetDocumentDirectory(originalPath);
        if (!Directory.Exists(directory))
            return Array.Empty<SnapshotInfo>();

        return Directory.EnumerateFiles(directory, "*" + MetadataExtension)
            .Select(metadataPath => ReadSnapshotInfo(metadataPath[..^MetadataExtension.Length]))
            .OfType<SnapshotInfo>()
            .Where(info => info.Kind == kind)
            .OrderByDescending(info => info.SavedAtUtc)
            .ThenByDescending(info => info.SnapshotPath, StringComparer.Ordinal)
            .ToList();
    }

    private SnapshotInfo WriteSnapshotBytes(string? originalPath, byte[] bytes, SnapshotKind kind, int codePage, bool hasPreamble)
    {
        var directory = GetDocumentDirectory(originalPath);
        Directory.CreateDirectory(directory);

        var savedAtUtc = _utcNow();
        var suffix = kind == SnapshotKind.Auto ? "auto" : "before-save";
        var extension = string.IsNullOrEmpty(Path.GetExtension(originalPath)) ? ".bms" : Path.GetExtension(originalPath);
        var stem = $"{savedAtUtc.ToLocalTime():yyyyMMdd-HHmmss-fff}_{suffix}";

        var path = Path.Combine(directory, stem + extension);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(directory, $"{stem}-{i}{extension}");

        var fullOriginalPath = string.IsNullOrWhiteSpace(originalPath) ? null : Path.GetFullPath(originalPath);
        var info = new SnapshotInfo(path, fullOriginalPath, kind, savedAtUtc, codePage, hasPreamble);

        WriteAtomically(path, bytes);
        WriteAtomically(path + MetadataExtension, JsonSerializer.SerializeToUtf8Bytes(SnapshotMetadata.From(info)));

        Prune(directory, kind);
        return info;
    }

    private static SnapshotInfo? ReadSnapshotInfo(string snapshotPath)
    {
        try
        {
            if (!File.Exists(snapshotPath))
                return null;

            var metadata = JsonSerializer.Deserialize<SnapshotMetadata>(File.ReadAllBytes(snapshotPath + MetadataExtension));
            return metadata?.ToInfo(snapshotPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // 종류마다 최근 KeepPerKind 벌만 남긴다. 복구를 기다리는 세션이 가리키는 것은 남긴다.
    private void Prune(string directory, SnapshotKind kind)
    {
        var referenced = ReferencedSnapshotPaths();

        var stale = Directory.EnumerateFiles(directory, "*" + MetadataExtension)
            .Select(metadataPath => ReadSnapshotInfo(metadataPath[..^MetadataExtension.Length]))
            .OfType<SnapshotInfo>()
            .Where(info => info.Kind == kind)
            .OrderByDescending(info => info.SavedAtUtc)
            .ThenByDescending(info => info.SnapshotPath, StringComparer.Ordinal)
            .Skip(KeepPerKind)
            .Where(info => !referenced.Contains(info.SnapshotPath));

        foreach (var info in stale)
        {
            TryDelete(info.SnapshotPath);
            TryDelete(info.SnapshotPath + MetadataExtension);
        }
    }

    // 한 달 넘게 손대지 않은 문서의 스냅숏 폴더를 지운다. 실행할 때 한 번 부른다.
    public void PruneOldDocuments()
    {
        if (!Directory.Exists(SnapshotsDirectory))
            return;

        var referenced = ReferencedSnapshotPaths();
        var cutoff = _utcNow() - MaxAge;

        foreach (var directory in Directory.EnumerateDirectories(SnapshotsDirectory))
        {
            try
            {
                var files = Directory.GetFiles(directory);
                if (files.Any(referenced.Contains))
                    continue;

                if (files.Length == 0 || files.Max(File.GetLastWriteTimeUtc) < cutoff)
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 다음 실행 때 다시 본다.
            }
        }
    }

    // ── 세션 ─────────────────────────────────────────────────────────────────

    // 실행을 시작했다고 적는다. 아직 저장 안 된 작업은 없다.
    public void BeginSession() => UpdateSession(null);

    // 저장 안 된 작업이 있으면 그 최근 자동 저장본을, 없으면 null 을 적는다.
    public void UpdateSession(SnapshotInfo? unsavedWork)
    {
        Directory.CreateDirectory(SessionsDirectory);
        var record = new SessionRecord { SnapshotPath = unsavedWork?.SnapshotPath };
        WriteAtomically(SessionFilePath(SessionId), JsonSerializer.SerializeToUtf8Bytes(record));
    }

    // 정상 종료. 세션 파일을 지운다. 스냅숏은 이력으로 남는다.
    public void EndSession() => TryDelete(SessionFilePath(SessionId));

    // 주인 없이 남은 세션 가운데 되살릴 작업이 있는 것. 최근 것부터.
    // 되살릴 것이 없는 세션 파일은 여기서 치운다.
    public IReadOnlyList<AbandonedSession> FindAbandonedSessions()
    {
        if (!Directory.Exists(SessionsDirectory))
            return Array.Empty<AbandonedSession>();

        var found = new List<AbandonedSession>();

        foreach (var sessionFile in Directory.EnumerateFiles(SessionsDirectory, "*.json").ToList())
        {
            var sessionId = Path.GetFileNameWithoutExtension(sessionFile);
            if (sessionId == SessionId || _isSessionAlive(sessionId))
                continue;

            var snapshot = ReadSession(sessionFile)?.SnapshotPath is { } snapshotPath
                ? ReadSnapshotInfo(snapshotPath)
                : null;

            if (snapshot is null)
            {
                TryDelete(sessionFile);
                continue;
            }

            found.Add(new AbandonedSession(sessionId, snapshot));
        }

        return found.OrderByDescending(session => session.Snapshot.SavedAtUtc).ToList();
    }

    // 복구했거나 버리기로 했다. 다시 묻지 않는다. 스냅숏은 이력으로 남는다.
    public void DismissSession(AbandonedSession session) => TryDelete(SessionFilePath(session.SessionId));

    private static SessionRecord? ReadSession(string sessionFile)
    {
        try
        {
            return JsonSerializer.Deserialize<SessionRecord>(File.ReadAllBytes(sessionFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private HashSet<string> ReferencedSnapshotPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(SessionsDirectory))
            return paths;

        foreach (var sessionFile in Directory.EnumerateFiles(SessionsDirectory, "*.json"))
        {
            if (ReadSession(sessionFile)?.SnapshotPath is { } snapshotPath)
            {
                paths.Add(snapshotPath);
                paths.Add(snapshotPath + MetadataExtension);
            }
        }

        return paths;
    }

    private static bool IsProcessSessionAlive(string sessionId)
    {
        var parts = sessionId.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var pid) || !long.TryParse(parts[1], out var startTicks))
            return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime().Ticks == startTicks;
        }
        catch
        {
            // 없는 번호(ArgumentException)이거나 들여다볼 수 없는 남의 프로세스다. 어느 쪽이든 그 실행은 끝났다.
            return false;
        }
    }

    // ── 파일 ─────────────────────────────────────────────────────────────────

    // 쓰다 만 파일이 남지 않게 임시 파일에 다 쓴 뒤 바꿔치기한다.
    // SafeFileWriter 는 덮어쓸 때 .bak 을 남겨서, 몇 분마다 고쳐 쓰는 세션 파일에는 맞지 않는다.
    private static void WriteAtomically(string path, byte[] bytes)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 지우지 못한 것은 다음에 다시 본다.
        }
    }

    private sealed class SessionRecord
    {
        public string? SnapshotPath { get; set; }
    }

    private sealed class SnapshotMetadata
    {
        public string? OriginalPath { get; set; }
        public SnapshotKind Kind { get; set; }
        public DateTime SavedAtUtc { get; set; }
        public int CodePage { get; set; }
        public bool HasPreamble { get; set; }

        public static SnapshotMetadata From(SnapshotInfo info) => new()
        {
            OriginalPath = info.OriginalPath,
            Kind = info.Kind,
            SavedAtUtc = info.SavedAtUtc,
            CodePage = info.CodePage,
            HasPreamble = info.HasPreamble,
        };

        public SnapshotInfo ToInfo(string snapshotPath) =>
            new(snapshotPath, OriginalPath, Kind, DateTime.SpecifyKind(SavedAtUtc, DateTimeKind.Utc), CodePage, HasPreamble);
    }
}
