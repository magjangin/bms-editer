using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using bms_editer.Services;
using bms_editer.ViewModels;
using bms_editer.Views;
using Xunit;

namespace bms_editer.Tests;

// 5분짜리 곡을 창에 띄워 한 프레임 그리는 데 걸리는 시간을 잰다.
//
// 시간이 오래 걸리고 결과가 기계마다 달라서 기본 실행에서는 건너뛴다.
// BMS_EDITER_BENCH=1 을 주고 돌리면 결과를 %TEMP%\bms-editer-bench.txt 에 적는다.
// 이 테스트는 시간을 단정하지 않는다. 수치를 보고 사람이 판단한다.
public sealed class RenderBenchmarkTests
{
    private const string EnvVar = "BMS_EDITER_BENCH";
    private const int FrameCount = 20;

    private static void RunOnUiThread(Action body) =>
        HeadlessUnitTestSession
            .GetOrStartForAssembly(typeof(RenderBenchmarkTests).Assembly)
            .Dispatch(body, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    [Fact]
    public void 오분_곡_한_프레임_그리기_시간을_잰다() => RunOnUiThread(() =>
    {
        if (Environment.GetEnvironmentVariable(EnvVar) != "1")
            return;

        var directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var chartPath = Path.Combine(directory, "five_minutes.bms");
        File.WriteAllText(chartPath, BuildFiveMinuteChart(), new UTF8Encoding(false));

        const double seconds = 300.0;
        const int sampleRate = 8000;
        var frames = (int)(seconds * sampleRate);
        var pcm = new byte[frames * 4];
        for (var i = 0; i < frames; i++)
        {
            // 소리가 있어야 파형 블록이 그려진다. 낮은 주파수 사인파면 충분하다.
            var sample = (short)(Math.Sin(i * 2 * Math.PI * 220.0 / sampleRate) * 12000);
            pcm[i * 4] = (byte)(sample & 0xFF);
            pcm[i * 4 + 1] = (byte)((sample >> 8) & 0xFF);
            pcm[i * 4 + 2] = pcm[i * 4];
            pcm[i * 4 + 3] = pcm[i * 4 + 1];
        }
        var audio = new OggAudioData(pcm, sampleRate, 2, seconds);

        var vm = new MainWindowViewModel { DecodeOgg = _ => audio };
        Assert.True(vm.LoadBms(chartPath), vm.LastErrorMessage);
        vm.LoadOggAsync("five_minutes.ogg").GetAwaiter().GetResult();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "EditorScrollViewer");
        var report = new StringBuilder();
        report.AppendLine($"notes={vm.Notes.Count} measures={vm.MeasureCount} duration={vm.OggDurationSeconds}s");

        foreach (var (label, fraction) in new[] { ("top", 0.0), ("middle", 0.5), ("bottom", 1.0) })
        {
            var maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Avalonia.Vector(scroll.Offset.X, maxOffset * fraction);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // 한 번은 버려서 JIT·첫 렌더 비용을 뺀다.
            window.CaptureRenderedFrame();

            var times = new double[FrameCount];
            for (var i = 0; i < FrameCount; i++)
            {
                var sw = Stopwatch.StartNew();
                window.CaptureRenderedFrame();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            Array.Sort(times);
            report.AppendLine($"{label}: median={times[FrameCount / 2]:F1}ms p90={times[FrameCount * 9 / 10]:F1}ms");
        }

        window.Close();

        var output = Path.Combine(Path.GetTempPath(), "bms-editer-bench.txt");
        File.WriteAllText(output, report.ToString());
    });

    private static string BuildFiveMinuteChart()
    {
        // BPM 150 이면 4/4 마디 하나가 1.6초다. 300초는 약 188마디.
        const int measureCount = 188;
        var sb = new StringBuilder();
        sb.AppendLine("#TITLE five minutes");
        sb.AppendLine("#BPM 150");
        sb.AppendLine("#WAV01 a.wav");
        sb.AppendLine("#WAV02 b.wav");

        for (var m = 1; m <= measureCount; m++)
        {
            // 레인 11 은 8분음표, 레인 14 는 16분음표 일부를 찍어 노트 수를 현실적으로 맞춘다.
            var lane11 = string.Concat(Enumerable.Range(0, 16).Select(k => k % 2 == 0 ? "01" : "00"));
            var lane14 = string.Concat(Enumerable.Range(0, 16).Select(k => k % 4 == 1 ? "02" : "00"));
            sb.AppendLine($"#{m:D3}11:{lane11}");
            sb.AppendLine($"#{m:D3}14:{lane14}");
        }

        return sb.ToString();
    }
}
