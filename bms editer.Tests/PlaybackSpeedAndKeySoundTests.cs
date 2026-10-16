using System;
using System.IO;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

public sealed class PlaybackSpeedAndKeySoundTests
{
    [Fact]
    public void 기본_재생배속은_1이며_키음은_활성화되어_있다()
    {
        using var vm = new MainWindowViewModel();

        Assert.Equal(1.0, vm.PlaybackSpeed);
        Assert.True(vm.IsKeySoundEnabled);
        Assert.Contains("켜짐", vm.KeySoundToggleText);
    }

    [Fact]
    public void 키음_토글_명령이_상태와_텍스트를_정상적으로_바꾼다()
    {
        using var vm = new MainWindowViewModel();

        vm.ToggleKeySoundCommand.Execute(null);
        Assert.False(vm.IsKeySoundEnabled);
        Assert.Contains("끄기", vm.KeySoundToggleText);

        vm.ToggleKeySoundCommand.Execute(null);
        Assert.True(vm.IsKeySoundEnabled);
        Assert.Contains("켜짐", vm.KeySoundToggleText);
    }

    [Fact]
    public void 재생배속이_범위_내에서_변경된다()
    {
        using var vm = new MainWindowViewModel();

        vm.PlaybackSpeed = 0.5;
        Assert.Equal(0.5, vm.PlaybackSpeed);

        vm.PlaybackSpeed = 0.1;
        Assert.Equal(0.1, vm.PlaybackSpeed);
    }

    private static MainWindowViewModel CreatePlayerVm()
    {
        const int sampleRate = 8000;
        const double seconds = 10.0;
        var frames = (int)(seconds * sampleRate);
        var silence = new bms_editer.Services.OggAudioData(new byte[frames * 4], sampleRate, 2, seconds);

        var vm = new MainWindowViewModel
        {
            DecodeOgg = _ => silence
        };
        vm.LoadOggAsync("dummy.ogg").GetAwaiter().GetResult();
        return vm;
    }

    [Fact]
    public void 재생_중_노트_배치_요청_시_재생만_멈추고_노트는_배치되지_않는다()
    {
        using var vm = CreatePlayerVm();
        vm.Chart.WavTable["01"] = "test.wav";
        vm.WavList.Add(new bms_editer.Models.BmsWavItem { Key = "01", FilePath = "test.wav", SourceText = "test.wav" });
        vm.SelectedWavItem = vm.WavList[0];

        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsPlaying);

        // 재생 중 클릭이 들어온 상황
        vm.PlaceNoteCommand.Execute(new bms_editer.Models.NotePlacementArgs("11", 1, 0.0));

        // 재생은 멈추었으나 노트는 배치되지 않아야 함 (P-1)
        Assert.False(vm.IsPlaying);
        Assert.Empty(vm.Notes);
    }

    [Fact]
    public void 재생_중_노트_삭제_요청_시_재생만_멈추고_노트는_삭제되지_않는다()
    {
        using var vm = CreatePlayerVm();
        vm.Chart.Notes.Add(new bms_editer.Models.BmsNote
        {
            LaneId = "11",
            Measure = 1,
            Position = 0.0,
            WavKey = "01"
        });

        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsPlaying);

        // 재생 중 우클릭이 들어온 상황
        vm.RemoveNoteCommand.Execute(new bms_editer.Models.NotePlacementArgs("11", 1, 0.0));

        // 재생은 멈추었으나 기존 노트는 삭제되지 않아야 함 (P-1)
        Assert.False(vm.IsPlaying);
        Assert.Single(vm.Notes);
    }

    [Fact]
    public void 곡_끝에서_재생하면_처음부터_다시_재생된다()
    {
        using var vm = CreatePlayerVm();

        vm.PlaybackPositionSeconds = vm.OggDurationSeconds;
        vm.IsPlaybackCursorVisible = true;

        vm.TogglePlaybackCommand.Execute(null);

        // P-8: 끝 지점에서 재생을 누르면 첫 틱에 멈추지 않고 0초부터 재생해야 한다
        Assert.True(vm.IsPlaying);
        Assert.Equal(0, vm.PlaybackPositionSeconds, 3);
    }

    [Fact]
    public void 새_차트_로드_시_재생이_정지된다()
    {
        using var vm = CreatePlayerVm();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_s8_{Guid.NewGuid():N}.bms");
        try
        {
            File.WriteAllText(tempFile, "#TITLE Test\n#BPM 120\n");

            vm.TogglePlaybackCommand.Execute(null);
            Assert.True(vm.IsPlaying);

            // 재생 중 다른 BMS 파일을 로드
            vm.LoadBms(tempFile);

            // S-8: 파일 로드 시 이전 재생이 멈추어야 함
            Assert.False(vm.IsPlaying);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void 재생_정지_시_키음_보이스가_모두_정지된다()
    {
        using var vm = CreatePlayerVm();
        var tempWav = Path.Combine(Path.GetTempPath(), $"test_s9_{Guid.NewGuid():N}.wav");
        try
        {
            // 44100Hz 16비트 스테레오 1초짜리 합성 WAV
            using (var stream = File.Create(tempWav))
            using (var writer = new BinaryWriter(stream))
            {
                var dataSize = 44100 * 4;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)2); // stereo
                writer.Write(44100);
                writer.Write(44100 * 4);
                writer.Write((short)4);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);
                writer.Write(new byte[dataSize]);
            }

            vm.Chart.WavTable["01"] = tempWav;
            vm.WavList.Add(new bms_editer.Models.BmsWavItem { Key = "01", FilePath = tempWav, SourceText = tempWav });

            // 키음 발음
            vm.PlayWavSound("01");

            // 정지 전에는 보이스가 활성화되어 있을 수 있음
            // StopPlayback 호출
            vm.StopCommand.Execute(null);

            // S-9: 정지 후에는 모든 키음 보이스가 0이어야 함
            Assert.Equal(0, vm.KeySoundPlayer.ActiveVoiceCount);
        }
        finally
        {
            if (File.Exists(tempWav))
                File.Delete(tempWav);
        }
    }
}
