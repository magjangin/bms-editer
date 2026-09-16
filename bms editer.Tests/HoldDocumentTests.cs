using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 문서 단위에서 홀드 짝이 어떻게 붙고, 무엇이 파일에 남는지.
//
// 가장 조심할 것은 "추정이 파일에 박히는 것"이다. 경로로 게임을 알아보는 건 화면에만 쓰고,
// 열었다 바로 저장한 파일은 원본과 같아야 한다. 사용자가 직접 고른 것만 #BMSEDITER_PROFILE 로 남긴다.
public sealed class HoldDocumentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));

    // DEFLATE 차트의 모양 그대로(3자리 키, 홀드 쌍은 시작이 뒤 글자).
    private const string DeflateChart =
        "#PLAYER 2\r\n#TITLE test\r\n#BPM 134\r\n" +
        "#WAV001 hihat_1.wav\r\n#WAV00A hihat_1 홀드 끝.wav\r\n#WAV00B hihat_1 홀드 시작.wav\r\n" +
        "#00116:00B000000000\r\n" +
        "#00216:00A000001000\r\n";

    public HoldDocumentTests()
    {
        Directory.CreateDirectory(_root);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private string WriteChart(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string GamePath() => WriteChart(Path.Combine("DEFLATE", "hwa", "곡", "hwa2.bms"), DeflateChart);

    private string PlainPath(string content = DeflateChart) => WriteChart(Path.Combine("plain", "chart.bms"), content);

    private static MainWindowViewModel Loaded(string path)
    {
        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);
        return vm;
    }

    [Fact]
    public void 헤더의_프로파일을_읽고_한_번만_다시_쓴다()
    {
        var path = PlainPath("#BMSEDITER_PROFILE deflate\r\n" + DeflateChart);

        var parsed = BmsParser.Parse(path);
        var header = parsed.Chart.Header;
        var text = BmsWriter.Write(
            parsed.Chart, header.Title, header.Artist, header.Genre, header.Bpm,
            header.Player - 1, header.Rank, header.Level, parsed.WavItems, path);

        Assert.Equal("deflate", header.ProfileId);

        // 원문 보존 줄로도 들어가면 저장할 때 두 줄이 된다.
        Assert.DoesNotContain(parsed.Chart.PreservedLines, l => l.Text.Contains("BMSEDITER_PROFILE", StringComparison.OrdinalIgnoreCase));
        Assert.Single(text.Split('\n'), l => l.StartsWith("#BMSEDITER_PROFILE deflate", StringComparison.Ordinal));
    }

    [Fact]
    public void 경로로_추정한_프로파일은_홀드를_그리지만_파일에_적지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var path = GamePath();
        using var vm = Loaded(path);

        Assert.Equal("deflate", vm.SelectedProfile.Id);
        Assert.Equal(HoldProfileOrigin.Path, vm.ProfileOrigin);
        Assert.Single(vm.HoldLinks);
        Assert.False(vm.IsDirty);

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);
        Assert.DoesNotContain("BMSEDITER_PROFILE", File.ReadAllText(path), StringComparison.Ordinal);
    });

    [Fact]
    public void 직접_고른_프로파일은_저장하면_파일에_적히고_다시_열면_헤더에서_읽는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var path = PlainPath();
        using var vm = Loaded(path);

        Assert.Equal(HoldProfileOrigin.None, vm.ProfileOrigin);
        Assert.Empty(vm.HoldLinks);

        vm.SelectedProfile = vm.Profiles.Single(p => p.Id == "deflate");

        Assert.True(vm.IsDirty);
        Assert.Equal(HoldProfileOrigin.User, vm.ProfileOrigin);
        Assert.Single(vm.HoldLinks);

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);
        Assert.Contains("#BMSEDITER_PROFILE deflate", File.ReadAllText(path), StringComparison.Ordinal);

        using var reopened = Loaded(path);
        Assert.Equal(HoldProfileOrigin.Header, reopened.ProfileOrigin);
        Assert.Single(reopened.HoldLinks);
    });

    [Fact]
    public void 모르는_프로파일_헤더는_경로_추정보다_우선하고_저장해도_지워지지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var path = WriteChart(Path.Combine("DEFLATE", "hwa", "곡2", "hwa2.bms"), "#BMSEDITER_PROFILE someday_game\r\n" + DeflateChart);
        using var vm = Loaded(path);

        Assert.Equal(HoldProfileOrigin.UnknownHeader, vm.ProfileOrigin);
        Assert.True(vm.SelectedProfile.IsNone);
        Assert.Empty(vm.HoldLinks);

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);
        Assert.Contains("#BMSEDITER_PROFILE someday_game", File.ReadAllText(path), StringComparison.Ordinal);
    });

    [Fact]
    public void 편집하면_홀드_짝을_다시_읽는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        using var vm = Loaded(GamePath());
        var link = Assert.Single(vm.HoldLinks);

        // 끝 노트를 지우면 시작이 고아가 되고, 격자에 경고 테두리가 붙는다.
        vm.DeleteNotes(new[] { link.Tail });

        Assert.Empty(vm.HoldLinks);
        Assert.Equal(HoldDiagnosticKind.OrphanHead, Assert.Single(vm.HoldDiagnostics).Kind);
        Assert.Contains(link.Head, vm.HoldProblemNotes);

        // 시작 노트의 키음을 일반 키음으로 바꾸면 더는 홀드가 아니다.
        vm.ReplaceWavKey(new[] { link.Head }, "001");

        Assert.Empty(vm.HoldDiagnostics);
        Assert.Empty(vm.HoldProblemNotes);
    });

    [Fact]
    public void 검사기_항목을_고르면_그_노트가_선택된다() => HoldTestSupport.RunOnUiThread(() =>
    {
        using var vm = Loaded(GamePath());
        var link = Assert.Single(vm.HoldLinks);
        vm.DeleteNotes(new[] { link.Tail });

        vm.SelectedHoldDiagnostic = vm.HoldDiagnostics[0];

        Assert.Equal(new[] { link.Head }, vm.SelectedNotes);
    });

    [Fact]
    public void 새로_만들기는_프로파일과_짝을_비운다() => HoldTestSupport.RunOnUiThread(() =>
    {
        using var vm = Loaded(GamePath());
        Assert.Single(vm.HoldLinks);

        vm.NewFileCommand.Execute(null);

        Assert.True(vm.SelectedProfile.IsNone);
        Assert.Equal(HoldProfileOrigin.None, vm.ProfileOrigin);
        Assert.Empty(vm.HoldLinks);
        Assert.Equal("", vm.HoldSummaryText);
    });

    [Fact]
    public void 검색_창의_롱_필터는_홀드_짝으로_가른다() => HoldTestSupport.RunOnUiThread(() =>
    {
        using var vm = Loaded(GamePath());
        var search = new NoteSearchViewModel(vm);

        Assert.True(search.AreNoteTypeFiltersUsable);

        search.IncludeNormal = false;
        Assert.Equal(2, search.FindMatches().Count);

        search.IncludeNormal = true;
        search.IncludeLong = false;
        Assert.Equal("001", Assert.Single(search.FindMatches()).WavKey);
    });
}
