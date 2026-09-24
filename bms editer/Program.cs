using System;
using System.Threading.Tasks;
using Avalonia;
using bms_editer.Services;

namespace bms_editer;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // UI 스레드 밖(백그라운드 스레드·버려진 Task)에서 난 예외도 기록은 남긴다.
        // UI 스레드 쪽은 App 이 받아서 앱을 살려 둔다. 이쪽은 살릴 수 없다.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ErrorLog.Write(ex, "백그라운드 스레드");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write(e.Exception, "처리되지 않은 Task");
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // 예전에는 exe 폴더에 crash.log 를 썼다. 쓸 수 없는 폴더면 그 쓰기가 다시 던져
            // 원래 예외를 가렸다. ErrorLog 는 %LocalAppData% 에 쓰고, 실패해도 던지지 않는다.
            ErrorLog.Write(ex, "Main");
            Console.Error.WriteLine(ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
