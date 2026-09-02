using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using bms_editer.Models;
using bms_editer.ViewModels;
using bms_editer.Views;
using Xunit;

namespace bms_editer.Tests;

// 통계 창을 화면 없이 실제로 띄워서 "고르면 되는가"를 보는 테스트.
//
// 왜 필요한가:
// 뷰모델 테스트는 SelectByWavKeyCommand 를 직접 부르므로 언제나 통과했다.
// 정작 깨져 있던 건 **화면에서 그 명령까지 닿는 길**이었다. 두 번 연달아 그랬다.
//   * 명령을 조상 바인딩으로 끌어왔더니 컴파일된 바인딩에서 조용히 실패했다
//   * 그 전에는 버튼이 버튼처럼 보이지도 않아 누를 수 있는 줄도 몰랐다
// 지금은 ListBox 라 중간 배선이 없지만, 그 "없음"도 확인해 둔다.
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

    private static (Window Window, NoteStatsViewModel ViewModel, ListBox List) ShowStatsWindow()
    {
        var viewModel = new NoteStatsViewModel(LoadedOwner());
        var window = new NoteStatsWindow(viewModel);
        window.Show();

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        return (window, viewModel, list);
    }

    // 목록에서 줄을 고르는 것 = 사용자가 클릭하는 것. ListBox 는 클릭을 SelectedItem 으로 바꾼다.
    private static void SelectRow(ListBox list, string key) =>
        list.SelectedItem = list.ItemsSource!.Cast<WavNoteStat>().Single(stat => stat.Key == key);

    [Fact]
    public void 키음_목록이_고를_수_있는_ListBox_로_만들어진다() => RunOnUiThread(() =>
    {
        var (_, _, list) = ShowStatsWindow();

        Assert.Equal(2, list.ItemsSource!.Cast<WavNoteStat>().Count());
        Assert.True(list.IsEffectivelyEnabled);
        Assert.True(list.IsEffectivelyVisible);
    });

    [Fact]
    public void 줄을_고르면_그_키음의_노트가_선택된다() => RunOnUiThread(() =>
    {
        var (_, viewModel, list) = ShowStatsWindow();
        var owner = viewModel.Owner;

        Assert.Empty(owner.SelectedNotes);

        SelectRow(list, "02");

        Assert.Equal(3, owner.SelectedNotes.Count);
        Assert.All(owner.SelectedNotes, note => Assert.Equal("02", note.WavKey));

        // 격자가 빨간 펜을 고르는 근거.
        Assert.Equal(NoteSelectionSource.Stats, owner.SelectionSource);
    });

    [Fact]
    public void 고른_줄이_목록에_그대로_남는다() => RunOnUiThread(() =>
    {
        // 고른 줄 표시는 ListBox 가 맡는다. 뷰모델에도 같은 값이 들어와 있어야 한다.
        var (_, viewModel, list) = ShowStatsWindow();

        SelectRow(list, "01");

        Assert.Equal("01", viewModel.SelectedWavStat?.Key);
        Assert.Same(viewModel.SelectedWavStat, list.SelectedItem);
    });

    [Fact]
    public void 다른_줄을_고르면_그쪽_노트로_바뀐다() => RunOnUiThread(() =>
    {
        var (_, viewModel, list) = ShowStatsWindow();
        var owner = viewModel.Owner;

        SelectRow(list, "01");
        Assert.Equal(2, owner.SelectedNotes.Count);

        SelectRow(list, "02");
        Assert.Equal(3, owner.SelectedNotes.Count);
        Assert.All(owner.SelectedNotes, note => Assert.Equal("02", note.WavKey));
    });

    [Fact]
    public void 고르면_안내문이_결과로_바뀐다() => RunOnUiThread(() =>
    {
        var (_, viewModel, list) = ShowStatsWindow();

        var before = viewModel.StatusMessage;
        SelectRow(list, "01");

        Assert.NotEqual(before, viewModel.StatusMessage);
        Assert.Contains("선택했습니다", viewModel.StatusMessage);
    });

    [Fact]
    public void 화면_밖_선택이면_격자를_그리로_옮긴다() => RunOnUiThread(() =>
    {
        var (_, viewModel, list) = ShowStatsWindow();

        var requested = 0;
        viewModel.Owner.ScrollToRatioRequested += _ => requested++;

        SelectRow(list, "02");

        Assert.Equal(1, requested);
    });
}
