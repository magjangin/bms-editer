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
        var framesPerBucket = Math.Max(1, (int)(totalFrames / Math.Max(1, peakCount)));

        var peaks = new float[peakCount];
        var energies = new float[peakCount];
        var buffer = new float[4096 * channels];

        var bucketIndex = 0;
        var frameInBucket = 0;
        double sumSquares = 0;
        var maxInBucket = 0f;

        int samplesRead;
        while ((samplesRead = reader.ReadSamples(buffer, 0, buffer.Length)) > 0 && bucketIndex < peakCount)
        {
            var frames = samplesRead / channels;
            for (var i = 0; i < frames; i++)
            {
                var value = 0f;
                for (var c = 0; c < channels; c++)
                    value = Math.Max(value, Math.Abs(buffer[i * channels + c]));

                sumSquares += (double)value * value;
                maxInBucket = Math.Max(maxInBucket, value);
                frameInBucket++;

                if (frameInBucket >= framesPerBucket)
                {
                    var rms = ToRms(sumSquares, frameInBucket);
                    energies[bucketIndex] = rms;
                    peaks[bucketIndex++] = ToDisplayAmplitude(rms, maxInBucket);
                    sumSquares = 0;
                    frameInBucket = 0;
                    maxInBucket = 0f;

                    if (bucketIndex >= peakCount)
                        break;
                }
            }
        }

        if (bucketIndex < peakCount && frameInBucket > 0)
        {
            var rms = ToRms(sumSquares, frameInBucket);
            energies[bucketIndex] = rms;
            peaks[bucketIndex] = ToDisplayAmplitude(rms, maxInBucket);
        }

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
