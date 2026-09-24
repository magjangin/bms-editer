using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace bms_editer.Services;

// 예상하지 못한 예외를 파일로 남기는 곳. (알려진 문제 S-2)
//
// 예전에는 Program.Main 의 catch 가 exe 폴더에 crash.log 를 썼다. 쓸 수 없는 폴더면
// 그 쓰기가 다시 예외를 던져 원래 예외를 가렸고, 로그도 안 남았다.
// 이제 %LocalAppData%\bms editer\logs 에 한 건씩 따로 쓰고, 여기서는 절대 던지지 않는다.
public static class ErrorLog
{
    // 오래된 기록은 지운다. 매 프레임 터지는 예외가 있어도 폴더가 끝없이 커지지 않게.
    public const int KeepCount = 30;

    // 같은 예외가 연달아 터지면(타이머·렌더마다) 이 시간 안에서는 한 번만 쓴다.
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(5);

    private static readonly object Gate = new();
    private static string? _lastSignature;
    private static DateTime _lastWrittenAt;

    public static string? Write(Exception exception, string source) =>
        Write(AppDataPaths.Logs, exception, source);

    // 쓴 파일 경로를 돌려준다. 같은 예외를 방금 썼거나 쓰기에 실패하면 null.
    public static string? Write(string directory, Exception exception, string source)
    {
        try
        {
            var now = DateTime.Now;
            var signature = $"{directory}|{source}|{exception.GetType().FullName}|{exception.Message}|{exception.StackTrace}";

            lock (Gate)
            {
                if (signature == _lastSignature && now - _lastWrittenAt < RepeatWindow)
                    return null;

                _lastSignature = signature;
                _lastWrittenAt = now;
            }

            Directory.CreateDirectory(directory);

            // 예외가 줄줄이 터지면 같은 밀리초에 여러 건이 온다. 덮어쓰지 않게 번호를 붙인다.
            var stem = $"error-{now:yyyyMMdd-HHmmss-fff}";
            var path = Path.Combine(directory, stem + ".log");
            for (var i = 2; File.Exists(path); i++)
                path = Path.Combine(directory, $"{stem}-{i}.log");

            File.WriteAllText(path, Format(exception, source, now), new UTF8Encoding(false));

            Prune(directory);
            return path;
        }
        catch
        {
            // 기록하다 실패했다고 원래 문제를 덮으면 안 된다.
            return null;
        }
    }

    private static string Format(Exception exception, string source, DateTime now)
    {
        var version = typeof(ErrorLog).Assembly.GetName().Version?.ToString() ?? "?";

        var sb = new StringBuilder();
        sb.AppendLine($"시각: {now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"어디서: {source}");
        sb.AppendLine($"앱 버전: {version}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine();
        sb.AppendLine(exception.ToString());
        return sb.ToString();
    }

    private static void Prune(string directory)
    {
        IEnumerable<FileInfo> stale = new DirectoryInfo(directory)
            .GetFiles("error-*.log")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(KeepCount);

        foreach (var file in stale)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // 다음 기록 때 다시 지운다.
            }
        }
    }
}
