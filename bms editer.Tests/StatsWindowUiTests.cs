using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using bms_editer.Models;
using bms_editer.ViewModels;
using bms_editer.Views;
using Xunit;

namespace bms_editer.Tests;

// 통계 창을 화면 없이 실제로 띄워서 "눌러지는가"를 보는 테스트.
//
// 왜 필요한가:
// 뷰모델 테스트는 SelectByWavKeyCommand 를 직접 부르므로 언제나 통과했다.
// 정작 깨져 있던 건 **화면에서 그 명령까지 닿는 길**이었다.
// 명령을 조상 바인딩으로 끌어왔는데 컴파일된 바인딩에서 조용히 실패해,
// Command 가 null 인 버튼이 되어 눌러도 아무 일이 없었다.
// 빌드도 통과하고 예외도 안 나서 사람이 눌러보기 전에는 알 방법이 없었다.
public sealed class StatsWindowUiTests
{
    // Avalonia 는 자기 스레드에서만 창을 만들 수 있다. 세션 하나를 어셈블리 단위로 공유한다.
    private static void RunOnUiThread(Action body) =>
        HeadlessUnitTestSession
            .GetOrStartForAssembly(typeof(StatsWindowUiTests).Assembly)
            .Dispatch(body, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static MainWindowViewModel LoadedOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "chart.bms");
        File.WriteAllText(
            path,
            "#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n#WAV02 b.wav\r\n" +
            "#00111:01020100\r\n#00212:02020000\r\n",
            new UTF8Encoding(false));

        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(path), owner.LastErrorMessage);
        return owner;
    }

    private static (Window Window, NoteStatsViewModel ViewModel) ShowStatsWindow()
    {
        var viewModel = new NoteStatsViewModel(LoadedOwner());
        var window = new NoteStatsWindow(viewModel);
        window.Show();
        return (window, viewModel);
    }

    // 키음 줄 버튼들. 목록은 ItemsControl 안에서 만들어지므로 시각 트리에서 찾는다.
    private static Button[] WavRowButtons(Window window) =>
        window.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("statRow"))
            .ToArray();

    private static Button RowFor(Window window, string key) =>
        WavRowButtons(window).Single(button => button.DataContext is WavNoteStat stat && stat.Key == key);

    private static WavNoteStat StatFor(Window window, string key) => (WavNoteStat)RowFor(window, key).DataContext!;

    // XAML 의 Click="..." 은 Click 라우팅 이벤트를 구독하므로, 이걸 올리면 실제 클릭과 같은 길을 탄다.
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void 키음_줄이_실제로_누를_수_있는_버튼으로_만들어진다() => RunOnUiThread(() =>
    {
        var (window, _) = ShowStatsWindow();

        var rows = WavRowButtons(window);

        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.True(row.IsEffectivelyEnabled));
    });

    [Fact]
    public void 줄을_누르면_그_키음의_노트가_선택된다() => RunOnUiThread(() =>
    {
        var (window, viewModel) = ShowStatsWindow();
        var owner = viewModel.Owner;

        Assert.Empty(owner.SelectedNotes);

        Click(RowFor(window, "02"));

        Assert.Equal(3, owner.SelectedNotes.Count);
        Assert.All(owner.SelectedNotes, note => Assert.Equal("02", note.WavKey));

        // 격자가 빨간 펜을 고르는 근거.
        Assert.Equal(NoteSelectionSource.Stats, owner.SelectionSource);
    });

    [Fact]
    public void 누른_줄에만_선택_표시가_남는다() => RunOnUiThread(() =>
    {
        var (window, _) = ShowStatsWindow();

        Click(RowFor(window, "01"));

        Assert.True(StatFor(window, "01").IsSelected);
        Assert.False(StatFor(window, "02").IsSelected);
    });

    [Fact]
    public void 다른_줄을_누르면_앞의_표시는_풀린다() => RunOnUiThread(() =>
    {
        var (window, _) = ShowStatsWindow();

        Click(RowFor(window, "01"));
        Click(RowFor(window, "02"));

        Assert.False(StatFor(window, "01").IsSelected);
        Assert.True(StatFor(window, "02").IsSelected);
    });

    [Fact]
    public void 누르면_안내문이_결과로_바뀐다() => RunOnUiThread(() =>
    {
        var (window, viewModel) = ShowStatsWindow();

        var before = viewModel.StatusMessage;
        Click(RowFor(window, "01"));

        Assert.NotEqual(before, viewModel.StatusMessage);
        Assert.Contains("선택했습니다", viewModel.StatusMessage);
    });
}
