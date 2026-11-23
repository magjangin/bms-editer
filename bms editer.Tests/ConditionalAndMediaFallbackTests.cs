using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 2026-11-23 감사에서 고친 것들.
// 1) 여러 마디에 걸친 #IF 블록 사이로 블록 밖 줄이 끼어들어 조건부가 되던 저장 순서.
// 2) #WAV 에 .wav 로 적혀 있지만 실제로는 .ogg 만 있는 키음(BMS 플레이어 관례).
public sealed class ConditionalAndMediaFallbackTests : IDisposable
{
    private readonly string _directory;

    public ConditionalAndMediaFallbackTests()
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
        }
    }

    private string WriteChart(string content)
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static string[] Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();

    [Fact]
    public void 여러_마디에_걸친_IF_블록_안으로_블록_밖_줄이_끼어들지_않는다()
    {
        var path = WriteChart(
            "#TITLE t\n#BPM 120\n#WAV01 a.wav\n" +
            "#00101:01\n#00201:01\n" +
            "#RANDOM 2\n#IF 1\n#00211:01\n#00311:01\n#ENDIF\n#ENDRANDOM\n" +
            "#00301:01\n");

        var parsed = BmsParser.Parse(path);
        var text = BmsWriter.Write(parsed.Chart, "t", "", "", 120, 0, 2, "", parsed.WavItems, path);
        var lines = Lines(text);

        var ifIndex = Array.IndexOf(lines, "#IF 1");
        var endIfIndex = Array.IndexOf(lines, "#ENDIF");
        Assert.True(ifIndex >= 0 && endIfIndex > ifIndex, text);

        // 블록 밖 BGM 줄은 블록 안에 있으면 안 된다.
        var bgmIndex = Array.IndexOf(lines, "#00301:01");
        Assert.True(bgmIndex >= 0, text);
        Assert.False(bgmIndex > ifIndex && bgmIndex < endIfIndex, text);

        // 갈래 노트는 블록 안에 남는다.
        var branchNote = Array.FindIndex(lines, l => l.StartsWith("#00311:", StringComparison.Ordinal));
        Assert.True(branchNote > ifIndex && branchNote < endIfIndex, text);
        Assert.True(Array.IndexOf(lines, "#ENDRANDOM") > endIfIndex, text);
    }

    [Fact]
    public void 갈래_노트를_다른_마디로_옮겨도_블록_안에_남는다()
    {
        var path = WriteChart(
            "#TITLE t\n#BPM 120\n#WAV01 a.wav\n" +
            "#RANDOM 2\n#IF 1\n#00111:01\n#ENDIF\n#ENDRANDOM\n");

        var parsed = BmsParser.Parse(path);
        var note = Assert.Single(parsed.Chart.Notes);
        note.Measure = 7;
        note.SourceLineOrder = 0;

        var text = BmsWriter.Write(parsed.Chart, "t", "", "", 120, 0, 2, "", parsed.WavItems, path);
        var lines = Lines(text);

        var ifIndex = Array.IndexOf(lines, "#IF 1");
        var endIfIndex = Array.IndexOf(lines, "#ENDIF");
        var moved = Array.FindIndex(lines, l => l.StartsWith("#00711:", StringComparison.Ordinal));
        Assert.True(moved > ifIndex && moved < endIfIndex, text);
    }

    [Fact]
    public void wav로_적힌_키음이_ogg로만_있으면_ogg를_쓰되_저장은_원문을_지킨다()
    {
        File.WriteAllText(Path.Combine(_directory, "kick.ogg"), "not really audio");
        var path = WriteChart("#TITLE t\n#BPM 120\n#WAV01 kick.wav\n#00111:01\n");

        var parsed = BmsParser.Parse(path);
        var wav = Assert.Single(parsed.WavItems);

        Assert.Equal(Path.Combine(_directory, "kick.ogg"), wav.FilePath);
        Assert.True(wav.IsPathGuessed);

        var text = BmsWriter.Write(parsed.Chart, "t", "", "", 120, 0, 2, "", parsed.WavItems, path);
        Assert.Contains("#WAV01 kick.wav", text);
    }

    [Fact]
    public void 적힌_파일이_있으면_확장자를_바꿔_찾지_않는다()
    {
        File.WriteAllText(Path.Combine(_directory, "kick.wav"), "a");
        File.WriteAllText(Path.Combine(_directory, "kick.ogg"), "b");
        var path = WriteChart("#TITLE t\n#BPM 120\n#WAV01 kick.wav\n");

        var wav = Assert.Single(BmsParser.Parse(path).WavItems);

        Assert.Equal(Path.Combine(_directory, "kick.wav"), wav.FilePath);
        Assert.False(wav.IsPathGuessed);
    }

    [Fact]
    public void 오디오가_아닌_확장자는_다른_이름으로_바꿔_찾지_않는다()
    {
        File.WriteAllText(Path.Combine(_directory, "intro.ogg"), "a");
        var path = WriteChart("#TITLE t\n#BPM 120\n#WAV01 intro.mp4\n");

        var wav = Assert.Single(BmsParser.Parse(path).WavItems);

        Assert.EndsWith("intro.mp4", wav.FilePath);
    }
}
