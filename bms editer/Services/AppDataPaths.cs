using System;
using System.IO;

namespace bms_editer.Services;

// 앱이 스스로 만드는 파일(오류 기록·자동 저장·WebView2 캐시)을 두는 곳.
//
// exe 옆에 두면 Program Files 처럼 쓸 수 없는 곳에 설치됐을 때 쓰기부터 실패한다.
// 사용자마다 늘 쓸 수 있는 %LocalAppData% 아래로 모은다.
public static class AppDataPaths
{
    private static readonly string LegacyRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bms editer");

    // 한 번만 정한다. 예전에는 Root 를 읽을 때마다(오류 기록·WebView2·폴더 열기) 디스크를 두 번 확인하고,
    // 옮기기가 실패하면 매번 다시 시도했다.
    private static readonly Lazy<string> ResolvedRoot = new(ResolveRoot, isThreadSafe: true);

    public static string Root => ResolvedRoot.Value;

    private static string ResolveRoot()
    {
        var newRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rhythm bms editer");

        // 기존 bms editer 폴더가 있고 새 rhythm bms editer 폴더가 없으면 옮긴다.
        // 옮기지 못하면(WebView2 가 붙잡고 있는 등) 이번 실행은 옛 폴더를 그대로 쓴다.
        if (!Directory.Exists(newRoot) && Directory.Exists(LegacyRoot))
        {
            try
            {
                Directory.Move(LegacyRoot, newRoot);
            }
            catch
            {
                return LegacyRoot;
            }
        }

        return newRoot;
    }

    public static string Logs => Path.Combine(Root, "logs");

    public static string Recovery => Path.Combine(Root, "recovery");
}
