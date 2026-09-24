using System;
using System.IO;

namespace bms_editer.Services;

// 앱이 스스로 만드는 파일(오류 기록·자동 저장·WebView2 캐시)을 두는 곳.
//
// exe 옆에 두면 Program Files 처럼 쓸 수 없는 곳에 설치됐을 때 쓰기부터 실패한다.
// 사용자마다 늘 쓸 수 있는 %LocalAppData% 아래로 모은다.
public static class AppDataPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bms editer");

    public static string Logs => Path.Combine(Root, "logs");

    public static string Recovery => Path.Combine(Root, "recovery");
}
