using System;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 온셋 마커가 실제 타격 순간에 서는지 재는 테스트.
//
// 마커는 "BPM 이 맞았나"를 눈으로 확인하는 유일한 기준선이다. 마커가 5ms 밀리면
// 그만큼 틀린 BPM 을 맞다고 믿게 되고, 그 오차는 마디가 쌓일수록 그대로 커진다.
// 그래서 합성 신호로 정답 시각을 알고 있는 상태에서 잰다.
public sealed class WaveformOnsetTests
{
    private const int SampleRate = 44100;
    private const int Channels = 2;

    // BPM 에 맞춰 박마다 짧고 날카로운 타격이 들어간 PCM 을 만든다.
    // 0번 박은 비워 둬서 "곡 시작"과 "첫 타격"이 구분되게 한다.
    private static OggAudioData BuildClickTrack(double bpm, int beats)
    {
        var secondsPerBeat = 60.0 / bpm;
        var totalFrames = (int)(secondsPerBeat * (beats + 1) * SampleRate);
        var pcm = new byte[totalFrames * Channels * sizeof(short)];
        var random = new Random(20260903);

        for (var frame = 0; frame < totalFrames; frame++)
        {
            var t = (double)frame / SampleRate;

            // 조용한 배경. 완전한 무음이면 검출이 너무 쉬워져서 시험이 헐거워진다.
            var value = (Math.Sin(2 * Math.PI * 55.0 * t) * 0.02) + ((random.NextDouble() - 0.5) * 0.01);

            var beatIndex = (int)(t / secondsPerBeat);
            var sinceBeat = t - (beatIndex * secondsPerBeat);

            if (beatIndex >= 1 && beatIndex <= beats && sinceBeat < 0.06)
                value += Math.Sin(2 * Math.PI * 180.0 * sinceBeat) * 0.9 * Math.Exp(-sinceBeat * 60.0);

            var sample = (short)(Math.Clamp(value, -1.0, 1.0) * short.MaxValue);

            for (var c = 0; c < Channels; c++)
            {
                var offset = ((frame * Channels) + c) * sizeof(short);
                pcm[offset] = (byte)(sample & 0xff);
                pcm[offset + 1] = (byte)((sample >> 8) & 0xff);
            }
        }

        return new OggAudioData(pcm, SampleRate, Channels, (double)totalFrames / SampleRate);
    }

    private static double DistanceToNearestOnset(OnsetMarker[] onsets, double seconds)
    {
        var nearest = double.MaxValue;
        foreach (var onset in onsets)
            nearest = Math.Min(nearest, Math.Abs(onset.Seconds - seconds));

        return nearest;
    }

    [Fact]
    public void 온셋_마커가_실제_타격_시각에_선다()
    {
        const double bpm = 141.0;
        const int beats = 16;

        var waveform = OggPeakLoader.Load(BuildClickTrack(bpm, beats));
        var secondsPerBeat = 60.0 / bpm;

        Assert.NotEmpty(waveform.Onsets);

        for (var beat = 1; beat <= beats; beat++)
        {
            var expected = beat * secondsPerBeat;
            var errorMs = DistanceToNearestOnset(waveform.Onsets, expected) * 1000;

            // 실측은 2ms 안쪽이다. 예전 버킷 해상도(12.5ms)로는 낼 수 없던 값이다.
            Assert.True(errorMs < 4.0, $"{beat}번째 타격의 마커가 {errorMs:F2}ms 어긋났다");
        }
    }

    [Fact]
    public void 타격_하나에_마커_하나만_선다()
    {
        const int beats = 16;

        var waveform = OggPeakLoader.Load(BuildClickTrack(141.0, beats));

        // 예전 방식은 어택 하나가 이웃 버킷 서너 개에 번져서 마커가 뭉쳐 섰다.
        // 봉우리를 골라내면 타격 수를 크게 넘지 않는다.
        Assert.InRange(waveform.Onsets.Length, beats, beats + 2);
    }

    [Fact]
    public void 세기가_0인_봉우리는_마커로_남지_않는다()
    {
        var waveform = OggPeakLoader.Load(BuildClickTrack(141.0, 16));

        // 세기 0 짜리까지 목록에 담으면 그리는 쪽이 매 프레임 헛돌기만 한다.
        Assert.All(waveform.Onsets, onset => Assert.True(
            onset.Strength > 0f,
            $"{onset.Seconds:F3}s 의 세기가 {onset.Strength:F3} 인데도 마커로 남았다"));
    }

    [Fact]
    public void 마커는_시간_순서대로_나온다()
    {
        var waveform = OggPeakLoader.Load(BuildClickTrack(141.0, 16));

        for (var i = 1; i < waveform.Onsets.Length; i++)
            Assert.True(waveform.Onsets[i].Seconds > waveform.Onsets[i - 1].Seconds);
    }

    [Fact]
    public void 버킷_해상도가_초당_400칸이다()
    {
        var data = BuildClickTrack(141.0, 8);
        var waveform = OggPeakLoader.Load(data);

        var bucketMs = data.DurationSeconds / waveform.Peaks.Length * 1000;

        // 화면이 낼 수 있는 최대 밀도(세로 줌 8 에서 초당 256px)보다 촘촘해야
        // 그리는 쪽이 구간 최댓값을 집계할 수 있다.
        Assert.InRange(bucketMs, 2.0, 3.0);
        Assert.Equal(waveform.Peaks.Length, waveform.Rms.Length);
    }

    [Fact]
    public void 피크가_곡_최대치를_1로_맞춘다()
    {
        var waveform = OggPeakLoader.Load(BuildClickTrack(141.0, 8));

        var maxPeak = 0f;
        var maxRms = 0f;
        foreach (var peak in waveform.Peaks)
            maxPeak = MathF.Max(maxPeak, peak);
        foreach (var rms in waveform.Rms)
            maxRms = MathF.Max(maxRms, rms);

        Assert.Equal(1.0, (double)maxPeak, 3);

        // RMS 는 피크와 같은 게인으로 올라간 값이라 항상 피크보다 작다.
        Assert.True(maxRms > 0f && maxRms < maxPeak);
    }

    [Fact]
    public void 무음이면_마커가_없다()
    {
        var frames = SampleRate * 2;
        var data = new OggAudioData(new byte[frames * Channels * sizeof(short)], SampleRate, Channels, 2.0);

        var waveform = OggPeakLoader.Load(data);

        Assert.Empty(waveform.Onsets);
    }
}
