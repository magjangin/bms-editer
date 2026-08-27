using System;
using NVorbis;

namespace bms_editer.Services;

public sealed record OggWaveform(float[] Peaks, float[] Onsets, double DurationSeconds);

// OGG Vorbis 파일을 디코딩해서 파형 표시용 피크와 박자 확인용 어택 값을 다운샘플링한다.
public static class OggPeakLoader
{
    // peaksPerSecond: 초당 샘플 개수. 곡 길이에 비례해서 정하므로
    // 짧은 곡/긴 곡 모두 화면에서 비슷한 밀도로 보인다(고정 총 개수 대비 개선).
    public static OggWaveform Load(string filePath, double peaksPerSecond = 80.0)
    {
        using var reader = new VorbisReader(filePath);
        var durationSeconds = reader.TotalTime.TotalSeconds;
        var peakCount = Math.Clamp((int)(durationSeconds * peaksPerSecond), 32, 20000);

        var channels = reader.Channels;
        var totalFrames = reader.TotalSamples;

        var peaks = new float[peakCount];
        var energies = new float[peakCount];

        if (totalFrames <= 0 || channels <= 0)
            return new OggWaveform(peaks, BuildOnsets(energies), durationSeconds);

        var buffer = new float[4096 * channels];

        // 버킷 크기를 정수 프레임 수로 고정하면(예: 44100/80 = 551.25 → 551) 피크 배열이
        // 곡 전체를 덮지 못한 채 타임라인 전체 폭에 늘어나 그려져서, 재생 시간에 비례하는
        // 어긋남이 쌓인다(44.1kHz 기준 약 0.045%, 2분 지점에서 50ms 이상).
        // 그래서 프레임 절대 위치로 버킷을 직접 계산해, peaks[i]가 항상
        // i * DurationSeconds / peakCount 시점에 정확히 대응하도록 한다.
        long frameIndex = 0;
        var bucketIndex = 0;
        var frameInBucket = 0;
        double sumSquares = 0;
        var maxInBucket = 0f;

        void FlushBucket()
        {
            if (frameInBucket == 0)
                return;

            var rms = ToRms(sumSquares, frameInBucket);
            energies[bucketIndex] = rms;
            peaks[bucketIndex] = ToDisplayAmplitude(rms, maxInBucket);
            sumSquares = 0;
            frameInBucket = 0;
            maxInBucket = 0f;
        }

        int samplesRead;
        while ((samplesRead = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            var frames = samplesRead / channels;
            for (var i = 0; i < frames; i++)
            {
                // 디코더가 TotalSamples보다 조금 더 내보내도 마지막 버킷에 흡수시킨다.
                var bucket = (int)Math.Min(peakCount - 1, frameIndex * peakCount / totalFrames);
                if (bucket != bucketIndex)
                {
                    FlushBucket();
                    bucketIndex = bucket;
                }

                var value = 0f;
                for (var c = 0; c < channels; c++)
                    value = Math.Max(value, Math.Abs(buffer[i * channels + c]));

                sumSquares += (double)value * value;
                maxInBucket = Math.Max(maxInBucket, value);
                frameInBucket++;
                frameIndex++;
            }
        }

        FlushBucket();

        return new OggWaveform(peaks, BuildOnsets(energies), durationSeconds);
    }

    private static float ToRms(double sumSquares, int frameCount) =>
        (float)Math.Sqrt(sumSquares / Math.Max(1, frameCount));

    // 파형 칸을 꽉 채우지 않도록 표시 게인을 낮게 유지한다.
    private static float ToDisplayAmplitude(float rms, float peak)
    {
        var amplitude = (rms * 0.75f) + (peak * 0.30f);
        return MathF.Min(1f, amplitude);
    }

    private static float[] BuildOnsets(float[] energies)
    {
        var onsets = new float[energies.Length];
        var maxOnset = 0f;

        for (var i = 1; i < energies.Length; i++)
        {
            var previousFloor = MathF.Max(energies[i - 1] * 0.92f, GetLocalAverage(energies, i - 8, i));
            var onset = MathF.Max(0f, energies[i] - previousFloor);
            onsets[i] = onset;
            maxOnset = MathF.Max(maxOnset, onset);
        }

        if (maxOnset <= 0)
            return onsets;

        for (var i = 0; i < onsets.Length; i++)
            onsets[i] = MathF.Min(1f, onsets[i] / maxOnset);

        return onsets;
    }

    private static float GetLocalAverage(float[] values, int start, int end)
    {
        start = Math.Clamp(start, 0, values.Length);
        end = Math.Clamp(end, start, values.Length);
        if (start == end)
            return 0f;

        var sum = 0f;
        for (var i = start; i < end; i++)
            sum += values[i];

        return sum / (end - start);
    }
}
