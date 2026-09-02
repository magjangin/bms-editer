using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 통계 창에서 키음 번호를 눌렀을 때 실제로 무슨 일이 일어나는지 못 박아 두는 테스트.
//
// 선택 자체는 원래도 됐지만, 고른 노트가 보이는 구간 밖이면 화면에서는 아무 일도
// 안 일어난 것처럼 보였다. 기능이 없는 줄 알게 되는 종류의 문제다.
public sealed class StatsSelectionTests : IDisposable
{
    private readonly string _directory;

    public StatsSelectionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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

    private MainWindowViewModel LoadChart()
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(
            path,
            "#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n#WAV02 b.wav\r\n" +
            "#00111:01020100\r\n#08012:02020000\r\n",
            new UTF8Encoding(false));

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);
        return vm;
    }

    [Fact]
    public void 키음_번호를_누르면_그_노트가_선택된다()
    {
        var vm = LoadChart();
        var stats = new NoteStatsViewModel(vm);

        stats.SelectByWavKeyCommand.Execute("02");

        Assert.Equal(3, vm.SelectedNotes.Count);
        Assert.All(vm.SelectedNotes, n => Assert.Equal("02", n.WavKey));
    }

    [Fact]
    public void 출처가_Stats_라서_격자가_빨강으로_그린다()
    {
        // NoteGridControl 이 SelectionSource == Stats 일 때만 빨간 펜을 고른다.
        var vm = LoadChart();
        var stats = new NoteStatsViewModel(vm);

        stats.SelectByWavKeyCommand.Execute("01");

        Assert.Equal(NoteSelectionSource.Stats, vm.SelectionSource);
    }

    [Fact]
    public void 화면_밖_선택이면_그리로_스크롤해_달라고_알린다()
    {
        var vm = LoadChart();
        var stats = new NoteStatsViewModel(vm);

        var requested = new List<double>();
        vm.ScrollToRatioRequested += requested.Add;

        // 80마디에 있는 노트를 고른다. 처음 보이는 구간에서는 한참 아래다.
        stats.SelectByWavKeyCommand.Execute("02");

        var ratio = Assert.Single(requested);
        Assert.InRange(ratio, 0.0, 1.0);

        // 가장 앞선 노트(1마디)가 기준이다. 전체 마디 수 대비 그 비율이어야 한다.
        Assert.Equal(1.25 / vm.MeasureCount, ratio, 6);
    }

    [Fact]
    public void 격자에서_직접_고른_선택은_스크롤을_요청하지_않는다()
    {
        // 손으로 끌어서 고른 것은 이미 보고 있는 자리다. 화면이 움직이면 오히려 방해된다.
        var vm = LoadChart();

        var requested = 0;
        vm.ScrollToRatioRequested += _ => requested++;

        vm.SetNoteSelection(vm.Notes, NoteSelectionSource.Grid);

        Assert.Equal(0, requested);
    }

    [Fact]
    public void 누른_결과를_글로도_알려준다()
    {
        var vm = LoadChart();
        var stats = new NoteStatsViewModel(vm);

        stats.SelectByWavKeyCommand.Execute("02");

        Assert.Contains("3개", stats.StatusMessage);
        Assert.Contains("1~80마디", stats.StatusMessage);
    }

    [Fact]
    public void 쓰이지_않는_번호를_누르면_그렇다고_말한다()
    {
        var vm = LoadChart();
        var stats = new NoteStatsViewModel(vm);

        stats.SelectByWavKeyCommand.Execute("ZZ");

        Assert.Contains("없습니다", stats.StatusMessage);
        Assert.Empty(vm.SelectedNotes);
    }
}
