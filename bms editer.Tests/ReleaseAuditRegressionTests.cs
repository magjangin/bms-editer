using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 2026-11-25 v0.1.8 점검에서 고친 것들을 못 박는 테스트.
public sealed class ReleaseAuditRegressionTests : IDisposable
{
    private readonly string _directory;

    public ReleaseAuditRegressionTests()
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

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // ── 배경음 음량 ─────────────────────────────────────────────────────────

    [Fact]
    public void 배경음_음량은_표본에_곱해진다()
    {
        var source = MemoryMarshal.AsBytes(new short[] { 32767, -32768, 1000, -1000 }.AsSpan()).ToArray();

        var half = MemoryMarshal.Cast<byte, short>(OggAudioPlayer.ScalePcm16(source, 0.5).AsSpan()).ToArray();
        Assert.Equal(new short[] { 16383, -16384, 500, -500 }, half);

        var silent = OggAudioPlayer.ScalePcm16(source, 0.0);
        Assert.All(silent, b => Assert.Equal(0, b));

        // 원본은 건드리지 않는다. 음량을 다시 올리면 원본에서 새로 줄인다.
        Assert.Equal(32767, MemoryMarshal.Cast<byte, short>(source.AsSpan())[0]);
    }

    // 예전에는 waveOutSetVolume 이 프로세스 세션 전체 음량을 바꿔서, 배경음을 줄이면 키음도 같이 줄었다.
    [Fact]
    public void 배경음_음량을_바꿔도_다른_오디오_장치의_음량은_그대로다()
    {
        var format = new TestWaveFormat { Tag = 1, Channels = 2, SamplesPerSec = 44100, AvgBytesPerSec = 176400, BlockAlign = 4, BitsPerSample = 16 };
        if (waveOutOpen(out var other, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            return; // 오디오 장치가 없는 환경

        try
        {
            using var player = new OggAudioPlayer(new OggAudioData(new byte[44100 * 4], 44100, 2, 1.0));

            // 실행마다 한 번 하는 세션 음량 되돌리기를 먼저 지나가게 한다.
            player.Play(0);
            player.Stop();

            Assert.Equal(0, waveOutSetVolume(other, 0x80008000));

            player.SetVolume(0.25);
            player.Play(0);
            waveOutGetVolume(other, out var afterPlay);
            player.Stop();

            Assert.Equal(0x80008000u, afterPlay);
        }
        finally
        {
            waveOutSetVolume(other, 0xFFFFFFFF);
            waveOutClose(other);
        }
    }

    [Fact]
    public void 재생_중_배경음_음량을_바꿔도_예외없이_값이_반영된다()
    {
        var vm = new MainWindowViewModel();
        vm.BgmVolume = 0.3;
        Assert.Equal(0.3, vm.BgmVolume);

        vm.BgmVolume = double.NaN;
        Assert.Equal(1.0, vm.BgmVolume);
    }

    // ── 저장할 때 노트가 겹쳐 사라지지 않는다 ──────────────────────────────────

    [Fact]
    public void 한_칸보다_가깝게_붙은_두_노트도_저장_뒤에_둘_다_남는다()
    {
        var source = WriteFile("close.bms", "#BPM 120\r\n#WAV01 a.wav\r\n#WAV02 b.wav\r\n");
        var parsed = BmsParser.Parse(source);
        var chart = parsed.Chart;

        // 격자에 붙이지 않고 찍으면 1/1920 마디보다 가까울 수 있다. 배치 허용오차(0.0001)보다는 멀다.
        chart.Notes.Add(new BmsNote { Measure = 1, LaneId = "11", Position = 0.5, WavKey = "01" });
        chart.Notes.Add(new BmsNote { Measure = 1, LaneId = "11", Position = 0.5003, WavKey = "02" });

        var output = Path.Combine(_directory, "close-out.bms");
        File.WriteAllText(output, BmsWriter.Write(chart, "", "", "", 120, 0, 2, "", parsed.WavItems, output), new UTF8Encoding(false));

        var reread = BmsParser.Parse(output).Chart.Notes.Where(n => n.Measure == 1 && n.LaneId == "11").OrderBy(n => n.Position).ToList();
        Assert.Equal(2, reread.Count);
        Assert.Equal(new[] { "01", "02" }, reread.Select(n => n.WavKey));
        Assert.All(reread.Zip(new[] { 0.5, 0.5003 }), pair => Assert.True(Math.Abs(pair.First.Position - pair.Second) < 0.0011));
    }

    [Fact]
    public void 겹치지_않는_노트의_분할은_예전처럼_가장_작게_쓴다()
    {
        var source = WriteFile("plain.bms", "#BPM 120\r\n#WAV01 a.wav\r\n#00111:01010101\r\n");
        var parsed = BmsParser.Parse(source);

        var text = BmsWriter.Write(parsed.Chart, "", "", "", 120, 0, 2, "", parsed.WavItems, source);

        Assert.Contains("#00111:01010101", text);
    }

    // ── 키음 경로 색인 ───────────────────────────────────────────────────────

    [Fact]
    public void 적힌_자리에_없는_키음은_가까운_하위_폴더에서만_찾는다()
    {
        WriteFile(Path.Combine("song", "sub", "near.wav"), "x");
        WriteFile(Path.Combine("song", "a", "b", "c", "d", "e", "f", "far.wav"), "x");
        var chart = WriteFile(Path.Combine("song", "chart.bms"), "#BPM 120\r\n#WAV01 near.wav\r\n#WAV02 far.wav\r\n");

        var items = BmsParser.Parse(chart).WavItems;

        var near = items.Single(i => i.Key == "01");
        Assert.True(near.IsPathGuessed);
        Assert.EndsWith(Path.Combine("sub", "near.wav"), near.FilePath);

        // 너무 깊이 있는 것은 찾지 않는다. 드라이브 전체를 훑느라 창이 멈추는 것보다 "없음" 표시가 낫다.
        var far = items.Single(i => i.Key == "02");
        Assert.False(far.IsPathGuessed);
        Assert.True(far.IsMissing);
    }

    // ── 같은 음원은 다시 풀지 않는다 ─────────────────────────────────────────

    [Fact]
    public async Task 같은_음원_파일이면_다시_읽지_않고_바뀌었으면_다시_읽는다()
    {
        var ogg = WriteFile("song.ogg", "first");
        var vm = new MainWindowViewModel { DecodeOgg = _ => new OggAudioData(new byte[8000 * 4], 8000, 2, 1.0) };

        Assert.False(vm.IsSameOggLoaded(ogg));
        Assert.Equal(OggLoadResult.Loaded, await vm.LoadOggAsync(ogg));
        Assert.True(vm.IsSameOggLoaded(ogg));

        // DAW 에서 다시 뽑았다.
        File.WriteAllText(ogg, "second, longer");
        File.SetLastWriteTimeUtc(ogg, DateTime.UtcNow.AddMinutes(1));
        Assert.False(vm.IsSameOggLoaded(ogg));

        vm.ClearOgg();
        Assert.False(vm.IsSameOggLoaded(ogg));
    }

    // ── 폴더를 끌어다 놓아도 저장 안 한 작업을 묻는다 ───────────────────────

    [Fact]
    public void 폴더를_열_때_저장_안_한_작업이_있으면_먼저_묻고_거절하면_그대로_둔다() =>
        HeadlessUnitTestSession
            .GetOrStartForAssembly(typeof(ReleaseAuditRegressionTests).Assembly)
            .Dispatch(async () =>
            {
                var original = WriteFile(Path.Combine("current", "work.bms"), "#TITLE 작업 중\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:01\r\n");
                var other = Path.Combine(_directory, "other");
                WriteFile(Path.Combine("other", "other.bms"), "#TITLE 다른 곡\r\n#BPM 140\r\n");

                var vm = new MainWindowViewModel();
                Assert.True(vm.LoadBms(original));
                vm.Title = "고친 제목";
                Assert.True(vm.IsDirty);

                var asked = 0;
                vm.ConfirmAsync = _ =>
                {
                    asked++;
                    return Task.FromResult(false);
                };

                var window = new MainWindow { DataContext = vm };
                await window.LoadFolderMediaAsync(vm, other);

                Assert.Equal(1, asked);
                Assert.Equal(original, vm.CurrentFilePath);
                Assert.Equal("고친 제목", vm.Title);
                Assert.True(vm.IsDirty);

                // 고친 채 닫으면 "저장하고 닫을까요?" 창이 떠서 테스트가 끝나지 않는다.
                vm.MarkClean();
                window.Close();
            }, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    [StructLayout(LayoutKind.Sequential)]
    private struct TestWaveFormat
    {
        public short Tag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr handle, int deviceId, ref TestWaveFormat format, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutSetVolume(IntPtr handle, uint volume);

    [DllImport("winmm.dll")]
    private static extern int waveOutGetVolume(IntPtr handle, out uint volume);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr handle);
}
