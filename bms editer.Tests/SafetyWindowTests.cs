using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using bms_editer.Views;
using Xunit;

namespace bms_editer.Tests;

// 안전장치가 창에서 실제로 이어지는지 본다. (알려진 문제 S-1·S-3)
//
// 뷰모델 테스트만으로는 "예외가 정말 전역 처리기까지 올라오는가",
// "복구 질문이 창을 열 때 정말 뜨는가"를 알 수 없다.
public sealed class SafetyWindowTests : IDisposable
{
    private readonly string _directory;
    private DateTime _clock = new(2026, 9, 24, 3, 0, 0, DateTimeKind.Utc);

    public SafetyWindowTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private static void RunOnUiThread(Action body) =>
        HeadlessUnitTestSession
            .GetOrStartForAssembly(typeof(SafetyWindowTests).Assembly)
            .Dispatch(body, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private DocumentSnapshotStore Store(string sessionId) =>
        new(Path.Combine(_directory, "recovery"), sessionId, _ => false, () => _clock = _clock.AddSeconds(1));

    private string WriteChart()
    {
        var folder = Path.Combine(_directory, "song");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "hwa2.bms");
        File.WriteAllText(path, "#TITLE 원본\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:01000000\r\n", new UTF8Encoding(false));
        return path;
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static void CloseDialogs(Window owner)
    {
        foreach (var dialog in owner.OwnedWindows.ToList())
            dialog.Close();
        Pump();
    }

    [Fact]
    public void 디스패처에서_샌_예외는_전역_처리기로_올라온다() => RunOnUiThread(() =>
    {
        // App 의 처리기가 기대는 경로다. 클릭 핸들러가 async void 라서, await 뒤에서 던진 예외는
        // 동기화 컨텍스트를 거쳐 디스패처 작업으로 다시 던져진다.
        Exception? caught = null;
        void Handler(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            caught = e.Exception;
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += Handler;
        try
        {
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("post"));
            Pump();
            Assert.Equal("post", caught?.Message);

            caught = null;
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("async void");
            });
            Pump();
            Assert.Equal("async void", caught?.Message);
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= Handler;
        }
    });

    [Fact]
    public void 예상하지_못한_예외가_나면_비상_저장하고_알린다() => RunOnUiThread(() =>
    {
        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(WriteChart()));
        var store = Store("ui");

        var window = new MainWindow { DataContext = owner };
        window.EnableSafety(store);
        window.Show();
        Pump();

        owner.PlaceNoteCommand.Execute(new NotePlacementArgs("12", 1, 0.0));
        Assert.True(owner.IsDirty);

        Assert.True(window.HandleUnexpectedException(new InvalidOperationException("boom"), logPath: null));

        // 다음 자동 저장(2분)을 기다리지 않고 그 자리에서 남긴다.
        var snapshot = Assert.Single(store.ListSnapshots(owner.CurrentFilePath, SnapshotKind.Auto));
        Assert.Equal(2, BmsParser.Parse(snapshot.SnapshotPath, owner.CurrentFilePath!).Chart.Notes.Count);

        // 죽으면 다음 실행이 이걸 되살릴 수 있게 세션에도 적혀 있다.
        Assert.Single(Store("probe").FindAbandonedSessions());

        Pump();
        var dialog = Assert.Single(window.OwnedWindows.OfType<ConfirmWindow>());
        Assert.Equal("오류", dialog.Title);

        // 안내 창이 떠 있는 동안 또 나도 창을 겹쳐 띄우지 않는다.
        Assert.True(window.HandleUnexpectedException(new InvalidOperationException("again"), logPath: null));
        Pump();
        Assert.Single(window.OwnedWindows.OfType<ConfirmWindow>());

        CloseDialogs(window);
        Assert.True(window.IsVisible); // [계속 작업]과 같다. 앱은 살아 있다
    });

    [Fact]
    public void 지난번_실행이_남긴_작업을_열_때_되살린다() => RunOnUiThread(() =>
    {
        var path = WriteChart();

        var crashed = new MainWindowViewModel { Snapshots = Store("crashed") };
        Assert.True(crashed.LoadBms(path));
        crashed.PlaceNoteCommand.Execute(new NotePlacementArgs("12", 1, 0.0));
        Assert.NotNull(crashed.AutoSave());

        // 다음 실행.
        var owner = new MainWindowViewModel();
        var window = new MainWindow { DataContext = owner };
        window.EnableSafety(Store("next"));
        window.Show();
        Pump();

        var dialog = Assert.Single(window.OwnedWindows.OfType<ConfirmWindow>());
        Assert.Equal("작업 복구", dialog.Title);

        var recover = dialog.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "복구");
        recover.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();

        Assert.Equal(path, owner.CurrentFilePath);
        Assert.True(owner.IsDirty);
        Assert.Equal(2, owner.Chart.Notes.Count);

        // 되살렸으니 다음에 켤 때 또 묻지 않는다.
        Assert.Empty(Store("probe").FindAbandonedSessions().Where(s => s.SessionId == "crashed"));
    });
}
