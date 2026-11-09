using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using bms_editer.Views.Controls;
using Xunit;

namespace bms_editer.Tests;

// 프레임 드랍을 잡으면서 바꾼 자리들. 빨라진 대신 그림이나 소리가 달라지면 안 되므로
// "예전과 같은 결과"와 "다시 그리지 않음"을 함께 못박아 둔다.
public sealed class RenderingPerformanceRegressionTests
{
    private sealed class GridLineProbe : TimelineControlBase
    {
        public IReadOnlyList<(double Position, string Kind, int Measure, double Seconds)> Lines(double length, double min = double.NegativeInfinity, double max = double.PositiveInfinity) =>
            EnumerateGridLines(length, min, max)
                .Select(line => (line.Position, line.Kind.ToString(), line.Measure, line.Seconds))
                .ToList();

        public int RenderCount { get; private set; }

        public override void Render(DrawingContext context)
        {
            RenderCount++;
            base.Render(context);
        }

        protected override Size MeasureOverride(Size availableSize) => new(200, GetTimelineHeight());
    }

    private static ChartTimeline VariableTimeline() =>
        new(
            150,
            new Dictionary<int, double> { [2] = 0.75, [5] = 1.5 },
            new[] { new BpmChange(1, 0.5, 180), new BpmChange(4, 0.0, 90), new BpmChange(7, 0.25, 240) });

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void 화면_구간만_돌아도_전체를_걸러낸_결과와_같다(bool horizontal, bool withAudio) => HoldTestSupport.RunOnUiThread(() =>
    {
        var probe = new GridLineProbe
        {
            IsHorizontalView = horizontal,
            DurationSeconds = withAudio ? 40 : 0,
            Bpm = 150,
            Timeline = VariableTimeline(),
            MeasureCount = 24,
            BeatSplit = 16,
            GridMeasure = 4,
            RowHeight = 10,
            VerticalZoom = 2,
        };

        const double length = 6000;
        var all = probe.Lines(length);

        foreach (var (min, max) in new[] { (0.0, 300.0), (1234.5, 2000.0), (5800.0, 6000.0), (3000.0, 3000.0) })
        {
            var expected = all.Where(line => line.Position >= min - 0.5 && line.Position <= max + 0.5).ToList();
            Assert.Equal(expected, probe.Lines(length, min, max));
        }
    });

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    [InlineData(1.5)]
    [InlineData(2.9)]
    [InlineData(7.26)]
    [InlineData(123.456)]
    public void 시각에서_마디로_되돌리면_원래_자리다(double measurePosition)
    {
        var timeline = VariableTimeline();
        Assert.Equal(measurePosition, timeline.MeasurePositionAt(timeline.SecondsAt(measurePosition)), 6);
    }

    [Fact]
    public void 재생_커서가_움직여도_격자는_다시_그리지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var probe = new GridLineProbe { DurationSeconds = 60, IsPlaybackCursorVisible = true, Bpm = 120 };
        var window = new Window { Width = 400, Height = 300, Content = probe };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        var rendersBefore = probe.RenderCount;
        Assert.True(rendersBefore > 0);

        for (var i = 0; i < 30; i++)
        {
            probe.PlaybackPositionSeconds = i * 0.5;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }

        Assert.Equal(rendersBefore, probe.RenderCount);
        window.Close();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 재생_커서는_타임라인의_같은_자리에_그려진다(bool horizontal) => HoldTestSupport.RunOnUiThread(() =>
    {
        var waveform = new OggWaveformControl
        {
            IsHorizontalView = horizontal,
            DurationSeconds = 100,
            BeatSplit = 4,
            GridMeasure = 4,
            RowHeight = 16,
            VerticalZoom = 1,
            Bpm = 120,
            Width = horizontal ? double.NaN : 220,
            Height = horizontal ? 220 : double.NaN,
            PlaybackPositionSeconds = 25,
            IsPlaybackCursorVisible = true,
        };

        var snapshot = GridSnapshot.Capture(waveform);
        var timelineLength = horizontal ? snapshot.Width : snapshot.Height;
        var cursor = horizontal ? timelineLength * 0.25 : timelineLength * 0.75;

        var color = horizontal ? snapshot.At(cursor, 110) : snapshot.At(110, cursor);
        Assert.True(color.G > 200 && color.R < 120, $"커서 자리의 색이 예상과 다릅니다: {color}");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 스크롤해서_버퍼_밖으로_나가면_그_자리의_노트가_그려진다(bool withAudio) => HoldTestSupport.RunOnUiThread(() =>
    {
        const int targetMeasure = 150;
        var grid = new NoteGridControl
        {
            Lanes = LaneDefinition.CreateDefault(),
            Notes = new[] { new BmsNote { LaneId = "13", Measure = targetMeasure, Position = 0.5, WavKey = "01" } },
            MeasureCount = 200,
            BeatSplit = 16,
            GridMeasure = 4,
            RowHeight = 16,
            VerticalZoom = 1,
            Bpm = 120,
            DurationSeconds = withAudio ? 400 : 0,
        };

        var scroll = new ScrollViewer { Content = grid };
        var window = new Window { Width = 400, Height = 400, Content = scroll };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        var length = grid.Bounds.Height;
        var noteY = withAudio
            ? (1.0 - ((targetMeasure + 0.5) * 2.0 / 400.0)) * length
            : length - ((targetMeasure + 0.5) * 16.0 * 4.0);

        var offsetY = Math.Max(0, noteY - 200);
        scroll.Offset = new Vector(0, offsetY);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        using var locked = new Avalonia.Media.Imaging.WriteableBitmap(frame!.PixelSize, frame.Dpi, Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using var buffer = locked.Lock();
        frame.CopyPixels(buffer);

        var laneCenterX = (int)(LaneIndexOf("13") * 40.0 + 20);
        var row = (int)(noteY - scroll.Offset.Y);
        var pixel = System.Runtime.InteropServices.Marshal.ReadInt32(buffer.Address + (row * buffer.RowBytes) + (laneCenterX * 4));

        // 흰 노트(240,240,240)가 그 자리에 있어야 한다.
        Assert.True(((pixel >> 8) & 0xFF) > 200, $"노트 자리의 픽셀: {pixel:X8}");
        window.Close();
    });

    private static int LaneIndexOf(string id)
    {
        var lanes = LaneDefinition.CreateDefault();
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].Id == id)
                return i;
        }

        throw new InvalidOperationException(id);
    }

    [Fact]
    public void 임계값_아래의_표본은_한_값도_바뀌지_않는다()
    {
        foreach (var sample in new[] { 0, 1, -1, 12345, -12345, 27852, -27852 })
            Assert.Equal(sample, KeySoundPlayer.SoftLimit(sample));
    }

    [Fact]
    public void 큰_합산_값은_범위_안에서_단조롭게_눌린다()
    {
        var previous = 27852;
        foreach (var sample in new[] { 28000, 30000, 33000, 40000, 65534, 200000, 1000000 })
        {
            var limited = KeySoundPlayer.SoftLimit(sample);
            Assert.InRange(limited, previous, short.MaxValue);
            Assert.Equal(-limited, KeySoundPlayer.SoftLimit(-sample));
            previous = limited;
        }
    }

    [Fact]
    public void 재생_키음은_디코딩이_안_끝났으면_건너뛰고_끝났으면_바로_낸다()
    {
        var path = WriteLongWav();
        try
        {
            using var player = new KeySoundPlayer();

            player.ScheduleIfDecoded(path, 0);
            Assert.Equal(0, player.ActiveVoiceCount);

            player.Preload(path);
            player.ScheduleIfDecoded(path, 0);
            Assert.Equal(1, player.ActiveVoiceCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 곡_앞_무음을_재생하는_동안_위치는_시작_지점보다_앞선다()
    {
        var audio = new OggAudioData(new byte[44100 * 4], 44100, 2, 1.0);
        using var player = new OggAudioPlayer(audio);

        player.Play(0.5, 1.0, 0.2);

        Assert.Equal(0.2, player.LeadInSeconds, 3);
        Assert.True(player.GetPlayedSeconds() < 0);

        player.Stop();
        Assert.Equal(0, player.LeadInSeconds);
        Assert.Null(player.GetPlayedSeconds());
    }

    [Fact]
    public void 재생_위치를_옮기면_아직_시작하지_않은_예약만_지운다()
    {
        var path = WriteLongWav();
        try
        {
            using var player = new KeySoundPlayer();
            player.Preload(path);
            player.HoldDeviceOpen = true;

            var deadline = DateTime.UtcNow.AddSeconds(5);
            do
            {
                player.StopAll();
                player.ScheduleIfDecoded(path, 60);
                player.CancelScheduled();
                Thread.Sleep(10);
            }
            while (player.ActiveVoiceCount != 0 && DateTime.UtcNow < deadline);

            Assert.Equal(0, player.ActiveVoiceCount);

            player.Play(path);
            player.CancelScheduled();
            Assert.Equal(1, player.ActiveVoiceCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task 미리듣기는_디코딩이_끝나는_대로_소리를_낸다()
    {
        var path = WriteLongWav();
        try
        {
            using var player = new KeySoundPlayer();
            player.Play(path);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (player.ActiveVoiceCount == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.Equal(1, player.ActiveVoiceCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // 믹서가 다 재생해 버리지 않을 만큼 긴(5초) 무음 WAV.
    private static string WriteLongWav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_long_{Guid.NewGuid():N}.wav");
        using var writer = new BinaryWriter(File.Create(path));

        var dataSize = 44100 * 4 * 5;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(44100);
        writer.Write(44100 * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        return path;
    }

    [Fact]
    public void 밀린_틱은_최근_구간의_키음만_낸다()
    {
        Assert.Equal(10.0, MainWindowViewModel.GetKeySoundWindowStart(10.0, 10.016), 6);

        var start = MainWindowViewModel.GetKeySoundWindowStart(10.0, 13.0);
        Assert.Equal(13.0 - MainWindowViewModel.MaxKeySoundCatchUpSeconds, start, 6);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(44100)]
    [InlineData(44101)]
    public void 피크_버킷은_프레임_절대_위치로_잡힌다(int frames)
    {
        var pcm = new byte[frames * 4];
        var impulseFrame = frames * 3 / 5;
        pcm[impulseFrame * 4] = 0xFF;
        pcm[(impulseFrame * 4) + 1] = 0x7F;

        var data = new OggAudioData(pcm, 44100, 2, frames / 44100.0);
        var waveform = OggPeakLoader.Load(data);

        var expected = (int)Math.Min(waveform.Peaks.Length - 1, (long)impulseFrame * waveform.Peaks.Length / frames);
        Assert.True(waveform.Peaks[expected] > 0);

        for (var i = 0; i < waveform.Peaks.Length; i++)
        {
            if (i != expected)
                Assert.Equal(0f, waveform.Peaks[i]);
        }
    }

    [Fact]
    public void 일괄_삭제는_지정한_노트만_지우고_선택에서도_뺀다()
    {
        using var vm = new MainWindowViewModel();
        var notes = Enumerable.Range(0, 2000)
            .Select(i => new BmsNote { LaneId = "11", Measure = i % 32, Position = i / 2000.0, WavKey = "01" })
            .ToList();
        vm.Chart.Notes.AddRange(notes);

        var doomed = notes.Where((_, i) => i % 2 == 0).ToList();
        var outsider = new BmsNote { LaneId = "11", WavKey = "01" };
        vm.SetNoteSelection(notes.Take(4));

        var removed = vm.DeleteNotes(doomed.Append(outsider).ToList());

        Assert.Equal(doomed.Count, removed);
        Assert.Equal(notes.Where((_, i) => i % 2 != 0), vm.Chart.Notes);
        Assert.Equal(new[] { notes[1], notes[3] }, vm.SelectedNotes.OrderBy(notes.IndexOf));
    }

    // 실제 창 + 실제 오디오 장치. 커서가 장치 시계를 따라가고 곡 끝에서 스스로 멈춰야 한다.
    //
    // DispatcherTimer 는 진짜 메인 루프에서만 돈다. 그래서 RunJobs 로 돌리지 않고 await 로 양보한다.
    [Fact]
    public Task 실제_창에서_재생하면_커서가_나아가고_곡_끝에서_멈춘다() => HeadlessUnitTestSession
        .GetOrStartForAssembly(typeof(HoldTestSupport).Assembly)
        .Dispatch(PlayThroughRealWindowAsync, CancellationToken.None);

    private static async Task<bool> PlayThroughRealWindowAsync()
    {
        const double seconds = 1.0;
        const int sampleRate = 8000;
        var audio = new OggAudioData(new byte[(int)(seconds * sampleRate) * 4], sampleRate, 2, seconds);

        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.DecodeOgg = _ => audio;
        window.Show();

        Assert.Equal(OggLoadResult.Loaded, await vm.LoadOggAsync("dummy.ogg"));

        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsPlaying, vm.LastErrorMessage);

        await WaitUntilAsync(() => vm.PlaybackPositionSeconds > 0.2, TimeSpan.FromSeconds(3));
        Assert.True(vm.PlaybackPositionSeconds > 0.2, $"커서가 나아가지 않았습니다: {vm.PlaybackPositionSeconds}");
        Assert.True(vm.IsPlaying);

        await WaitUntilAsync(() => !vm.IsPlaying, TimeSpan.FromSeconds(5));
        Assert.False(vm.IsPlaying);
        Assert.Equal(seconds, vm.PlaybackPositionSeconds, 3);

        window.Close();
        return true;
    }

    private static async Task WaitUntilAsync(Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public async Task 백그라운드_자동_저장은_스냅숏과_세션을_남긴다()
    {
        var directory = NewTempDirectory();
        var store = new DocumentSnapshotStore(directory, "session-a", _ => false);

        using var vm = new MainWindowViewModel { Snapshots = store };
        store.BeginSession();

        vm.Title = "자동 저장";
        var snapshot = await vm.AutoSaveInBackgroundAsync();

        Assert.NotNull(snapshot);
        Assert.Contains("자동 저장", File.ReadAllText(snapshot!.SnapshotPath));

        var other = new DocumentSnapshotStore(directory, "session-b", _ => false);
        var abandoned = Assert.Single(other.FindAbandonedSessions());
        Assert.Equal(snapshot.SnapshotPath, abandoned.Snapshot.SnapshotPath);

        // 그 뒤로 바뀐 게 없으면 또 쓰지 않는다.
        var again = await vm.AutoSaveInBackgroundAsync();
        Assert.Equal(snapshot.SnapshotPath, again!.SnapshotPath);
        Assert.Single(store.ListSnapshots(null, SnapshotKind.Auto));
    }

    [Fact]
    public async Task 고친_것이_없으면_백그라운드_자동_저장은_아무것도_쓰지_않는다()
    {
        var directory = NewTempDirectory();
        using var vm = new MainWindowViewModel { Snapshots = new DocumentSnapshotStore(directory, "s", _ => false) };

        Assert.Null(await vm.AutoSaveInBackgroundAsync());
        Assert.False(Directory.Exists(Path.Combine(directory, "snapshots")));
    }
}
