using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using bms_editer.Services;

namespace bms_editer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 자동 저장·비정상 종료 복구는 실제 앱에서만 켠다. (MainWindow.Safety 참고)
            var window = new MainWindow();
            window.EnableSafety(DocumentSnapshotStore.CreateDefault());
            desktop.MainWindow = window;

            Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // UI 스레드에서 아무도 받지 않은 예외. (알려진 문제 S-1)
    //
    // 클릭 핸들러가 async void 라 그 안에서 샌 예외는 여기까지 올라온다.
    // 받아 주지 않으면 앱이 그대로 꺼진다. 기록을 남기고 메인 창에 넘겨 비상 저장과 안내를 맡긴다.
    private void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = ErrorLog.Write(e.Exception, "UI 스레드");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window }
            && window.HandleUnexpectedException(e.Exception, logPath))
        {
            e.Handled = true;
        }
    }
}
