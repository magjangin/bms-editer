using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace bms_editer.Services;

// 검출한 어택 한 개. Seconds 는 곡 시작부터의 초, Strength 는 0~1 세기.
//
// 예전에는 버킷마다 세기 하나를 담은 밀집 배열이었다. 2만 개를 매 프레임 훑으면서
// 실제로 그린 선은 몇백 개뿐이었고, 무엇보다 "어택이 언제였나"가 버킷 칸에 갇혀
// 12.5ms 단위로만 표현됐다. 이제 검출된 것만 초 단위로 담는다.
public readonly record struct OnsetMarker(double Seconds, float Strength);

// Peaks/Rms 는 같은 길이의 버킷 배열이다. 둘 다 곡 최대치를 1.0 으로 맞춰 놓아서
// 마스터 볼륨이 작은 음원도 화면을 같은 높이로 채운다.
public sealed record OggWaveform(float[] Peaks, float[] Rms, OnsetMarker[] Onsets, double DurationSeconds);

// OGG Vorbis 파일을 디코딩해서 파형 표시용 피크와 박자 확인용 어택 값을 다운샘플링한다.
public static class OggPeakLoader
{
    // 초당 버킷 수. 한 칸이 2.5ms 다.
    //
    // 예전 값은 80(12.5ms)이었다. 세로 줌을 끝까지 올리면 화면이 초당 256px 라
    // 한 버킷이 3px 짜리 계단이 됐고, 마디선을 드럼 어택에 맞출 때 눈으로 고를 수 있는
    // 정밀도가 ±12.5ms 였다. BPM 141 에서 16분음표가 106ms 이므로 12%다.
    // 그 정밀도로 첫 어택을 잡으면 106마디 끝에서는 마디 하나가 통째로 밀린다.
    //
    // 화면이 낼 수 있는 최대 밀도(초당 256칸)보다 더 촘촘해야 그리는 쪽에서
    // "구간의 최댓값"을 집계할 수 있다. 그래야 줌을 줄여도 어택이 사라지지 않는다.
    public const double DefaultPeaksPerSecond = 400.0;

    private const int MinBucketCount = 32;

    // 10분짜리도 24만 칸(배열 두 개 합쳐 2MB)이라 잘릴 일이 없다.
    // 예전 상한 20,000 은 4분 10초를 넘는 곡의 해상도를 소리 없이 떨어뜨렸다.
    private const int MaxBucketCount = 4_000_000;

    // 실제 타격의 1/8 도 안 되는 세기는 마커로 세우지 않는다.
    private const float MinOnsetStrength = 0.12f;

    // 버킷 index 가 대응하는 시각의 비율(0~1).
    //
    // Load() 가 버킷 경계를 `i * totalFrames / bucketCount` 로 잡으므로,
    // peaks[i] · rms[i] 는 정확히 `i * DurationSeconds / count` 시점에서 시작한다.
    // 그리는 쪽은 반드시 이 규칙을 써야 한다.
    //
    // 이 한 줄을 각자 다시 쓰다가 네 번 어긋났다.
    //   f86a58c — 버킷 크기를 정수로 절삭해서 2분 지점 53ms
    //   e19c25b — 재생 커서를 벽시계로 잡아서 약 12ms
    //   (27번)  — 온셋 마커만 i/(count-1) 이라 곡 끝에서 12~15ms
    //   (이번)  — 파형 블록만 i/(count-1) 이라 곡 가운데에서 반 버킷
    // 그래서 규칙을 담은 곳 옆에 두고 한 군데서만 정의한다.
    public static double GetBucketRatio(int index, int count) => GetBucketRatio((double)index, count);

    // 어택 위치는 버킷 사이를 보간해서 잡으므로 소수 index 도 받는다.
    public static double GetBucketRatio(double index, int count) =>
        count <= 0 ? 0.0 : index / count;

    // 화면 블록 하나가 덮는 소스 버킷 범위 [Start, End).
    //
    // 그리는 쪽이 블록마다 버킷 하나를 "찍어서" 읽으면 두 가지가 같이 망가진다.
    //   - 줌을 줄이면 블록보다 버킷이 많아져서 대부분의 버킷이 그냥 버려진다.
    //     드럼 한 방이 통째로 화면에서 사라지고, 줌을 바꿀 때마다 파형 모양이 달라진다.
    //   - 비율을 i/(count-1) 로 잡으면 GetBucketRatio 규칙과 어긋나 반 버킷씩 민다.
    // 범위를 돌려주고 그 안의 최댓값을 쓰면 둘 다 없어진다.
    // 이웃한 블록의 End 와 Start 가 맞물리므로 버킷은 정확히 한 번씩 읽힌다.
    public static (int Start, int End) GetBlockSourceRange(int blockIndex, int blockCount, int sourceCount)
    {
        if (sourceCount <= 0 || blockCount <= 0)
            return (0, 0);

        blockIndex = Math.Clamp(blockIndex, 0, blockCount - 1);

        var start = (int)((long)blockIndex * sourceCount / blockCount);
        var end = (int)((long)(blockIndex + 1) * sourceCount / blockCount);

        start = Math.Clamp(start, 0, sourceCount - 1);

        // 버킷보다 블록이 많으면(줌을 키운 경우) start == end 가 된다.
        // 그때는 그 시점을 덮는 버킷 하나를 읽는다.
        end = Math.Clamp(end, start + 1, sourceCount);
        return (start, end);
    }

    // 이미 풀어놓은 PCM 에서 피크를 뽑는다.
    //
    // 예전에는 여기서도 VorbisReader 를 따로 열어 곡 전체를 다시 디코딩했다.
    // OggAudioPlayer 가 하는 일과 똑같은 일을 한 번 더 한 셈이라 로딩이 두 배 걸렸다.
    public static OggWaveform Load(OggAudioData data, double peaksPerSecond = DefaultPeaksPerSecond)
    {
        var durationSeconds = data.DurationSeconds;
        var bucketCount = Math.Clamp(
            (int)Math.Round(durationSeconds * peaksPerSecond),
            MinBucketCount,
            MaxBucketCount);

        var channels = data.Channels;
        var totalFrames = data.FrameCount;

        var peaks = new float[bucketCount];
        var rms = new float[bucketCount];

        if (totalFrames <= 0 || channels <= 0)
            return new OggWaveform(peaks, rms, Array.Empty<OnsetMarker>(), durationSeconds);

        // OggDecoder 가 리틀엔디언 PCM16 으로 채워 넣는다. 바이트를 한 개씩 조립하지 않고
        // 그대로 short 로 본다. (지원 대상 플랫폼은 전부 리틀엔디언이다.)
        var samples = MemoryMarshal.Cast<byte, short>(data.Pcm16);

        // 버킷 경계를 프레임 절대 위치로 계산한다. 버킷 크기를 정수 프레임 수로 고정하면
        // (예: 44100/400 = 110.25 → 110) 피크 배열이 곡 전체를 덮지 못한 채 타임라인
        // 전체 폭에 늘어나 그려져서, 재생 시간에 비례하는 어긋남이 쌓인다.
        //
        // 예전에는 프레임마다 `frameIndex * bucketCount / totalFrames` 를 계산했다.
        // 결과는 같지만 수백만 프레임마다 64비트 나눗셈을 한 번씩 돌렸다.
        // 경계는 버킷당 한 번만 구하면 된다.
        long frameIndex = 0;

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var bucketEnd = Math.Min(totalFrames, (bucket + 1) * totalFrames / bucketCount);

            double sumSquares = 0;
            var maxInBucket = 0f;
            var frameInBucket = 0;

            for (; frameIndex < bucketEnd; frameIndex++)
            {
                var baseIndex = (int)(frameIndex * channels);
                var value = 0f;

                for (var c = 0; c < channels; c++)
                {
                    // short.MinValue 의 절댓값은 short 범위를 넘으므로 float 로 올려 나눈다.
                    var sample = samples[baseIndex + c] / 32768f;
                    value = MathF.Max(value, MathF.Abs(sample));
                }

                sumSquares += (double)value * value;
                maxInBucket = MathF.Max(maxInBucket, value);
                frameInBucket++;
            }

            if (frameInBucket == 0)
                continue;

            peaks[bucket] = maxInBucket;
            rms[bucket] = ToRms(sumSquares, frameInBucket);
        }

        // 표시 게인을 여기서 한 번만 맞춘다. 예전에는 `rms*0.75 + peak*0.30` 처럼
        // 둘을 섞어 하나로 눌러 담았는데, 그러면 어택(피크)이 지속음(RMS)에 묻혀서
        // "소리가 언제 튀었나"가 흐려졌다. 이제 두 값을 따로 넘겨 그리는 쪽에서 겹쳐 그린다.
        Normalize(peaks, rms);

        return new OggWaveform(peaks, rms, BuildOnsets(rms, durationSeconds), durationSeconds);
    }

    private static float ToRms(double sumSquares, int frameCount) =>
        (float)Math.Sqrt(sumSquares / Math.Max(1, frameCount));

    // 곡 최대 진폭이 1.0 이 되도록 두 배열을 같은 비율로 올린다.
    // 조용하게 마스터링된 음원도 화면을 같은 높이로 채워서, 인트로의 작은 어택이 보인다.
    private static void Normalize(float[] peaks, float[] rms)
    {
        var maxPeak = 0f;
        for (var i = 0; i < peaks.Length; i++)
            maxPeak = MathF.Max(maxPeak, peaks[i]);

        if (maxPeak <= 0f || maxPeak >= 1f)
            return;

        var gain = 1f / maxPeak;
        for (var i = 0; i < peaks.Length; i++)
        {
            peaks[i] = MathF.Min(1f, peaks[i] * gain);
            rms[i] = MathF.Min(1f, rms[i] * gain);
        }
    }

    // 에너지가 튀는 지점을 골라 어택 목록을 만든다.
    //
    // 예전에는 버킷마다 `energies[i] - 직전값*0.92` 를 그대로 세기로 썼다.
    // 어택 하나가 이웃 버킷 서너 개에 번져서 마커가 뭉치고, 전체 최댓값으로 나눠
    // 정규화하는 바람에 심벌 한 방이 들어오면 나머지 구간이 전부 흐려졌다.
    private static OnsetMarker[] BuildOnsets(float[] energies, double durationSeconds)
    {
        var count = energies.Length;
        if (count < 16 || durationSeconds <= 0)
            return Array.Empty<OnsetMarker>();

        var bucketsPerSecond = count / durationSeconds;
        var lookback = BucketsFor(bucketsPerSecond, 0.012);  // 어택이 솟는 데 걸리는 시간
        var window = BucketsFor(bucketsPerSecond, 0.120);    // 적응 임계를 재는 창
        var minGap = BucketsFor(bucketsPerSecond, 0.030);    // 같은 타격을 두 번 세지 않을 간격

        var envelope = BuildAttackEnvelope(energies, bucketsPerSecond);

        // 로그(라우드니스) 영역에서 재면 조용한 구간의 어택도 큰 구간과 같은 잣대로 잡힌다.
        // 선형 에너지 차이만 쓰면 후렴에서만 마커가 나오고 인트로는 텅 빈다.
        var loudness = new float[count];
        for (var i = 0; i < count; i++)
            loudness[i] = MathF.Log(1f + (120f * envelope[i]));

        var flux = new float[count];
        for (var i = lookback; i < count; i++)
            flux[i] = MathF.Max(0f, loudness[i] - loudness[i - lookback]);

        var threshold = BuildAdaptiveThreshold(flux, window);
        var picked = PickPeaks(flux, threshold, lookback, minGap);

        if (picked.Count == 0)
            return Array.Empty<OnsetMarker>();

        var strengths = new float[picked.Count];
        for (var i = 0; i < picked.Count; i++)
            strengths[i] = flux[picked[i]];

        var reference = GetUpperPercentile(strengths, 0.90);
        if (reference <= 0f)
            return Array.Empty<OnsetMarker>();

        var markers = new List<OnsetMarker>(picked.Count);
        for (var i = 0; i < picked.Count; i++)
        {
            var strength = MathF.Min(1f, strengths[i] / reference);

            // 배경 베이스음이나 잡음이 만드는 잔물결도 봉우리이긴 하다.
            // 세기가 실제 타격의 1/8 도 안 되는 것까지 세우면 격자와 대조할 때
            // 어느 선이 드럼인지 알아볼 수 없다.
            if (strength < MinOnsetStrength)
                continue;

            var bucket = RefineAttackBucket(envelope, picked[i], lookback);
            markers.Add(new OnsetMarker(GetBucketRatio(bucket, count) * durationSeconds, strength));
        }

        return markers.ToArray();
    }

    private static int BucketsFor(double bucketsPerSecond, double seconds) =>
        Math.Max(1, (int)Math.Round(bucketsPerSecond * seconds));

    // 어택은 그대로 받고 감쇄만 늦추는 포락선.
    //
    // 버킷 한 칸이 2.5ms 라 낮은 음에서는 RMS 가 파형 자체를 따라 출렁인다.
    // (180Hz 면 5.5ms 주기, 55Hz 베이스면 18ms 주기) 그 값을 그대로 검출에 쓰면
    // 타격 한 번에 봉우리가 네댓 개 서고, 배경 베이스음만으로도 마커가 줄줄이 선다.
    // 최댓값은 즉시 따라가고 내려갈 때만 시정수를 두면, 출렁임은 사라지는데
    // 어택의 앞날은 무뎌지지 않는다. 마커 위치는 그 앞날에서 나온다.
    private static float[] BuildAttackEnvelope(float[] energies, double bucketsPerSecond)
    {
        var release = (float)Math.Exp(-1.0 / Math.Max(1.0, bucketsPerSecond * 0.050));
        var envelope = new float[energies.Length];
        var current = 0f;

        for (var i = 0; i < energies.Length; i++)
        {
            current = MathF.Max(energies[i], current * release);
            envelope[i] = current;
        }

        return envelope;
    }

    // 주변 ±window 의 평균을 기준선으로 삼는다. 고정 임계를 쓰면 조용한 구간은 아무것도
    // 못 잡고 시끄러운 구간은 전부 잡는다.
    private static float[] BuildAdaptiveThreshold(float[] flux, int window)
    {
        var count = flux.Length;
        var prefix = new double[count + 1];
        for (var i = 0; i < count; i++)
            prefix[i + 1] = prefix[i] + flux[i];

        // 전곡 평균은 완전한 무음에서 잡음을 줍지 않게 하는 바닥값으로만 쓴다.
        var globalMean = (float)(prefix[count] / count);

        var threshold = new float[count];
        for (var i = 0; i < count; i++)
        {
            var start = Math.Max(0, i - window);
            var end = Math.Min(count, i + window + 1);
            var localMean = (float)((prefix[end] - prefix[start]) / (end - start));
            threshold[i] = (localMean * 2.0f) + (globalMean * 0.6f);
        }

        return threshold;
    }

    private static List<int> PickPeaks(float[] flux, float[] threshold, int radius, int minGap)
    {
        var picked = new List<int>();

        for (var i = radius; i < flux.Length; i++)
        {
            var value = flux[i];
            if (value <= 0f || value < threshold[i] || !IsLocalMaximum(flux, i, radius))
                continue;

            // 같은 타격의 두 번째 봉우리면 더 센 쪽만 남긴다.
            if (picked.Count > 0 && i - picked[^1] < minGap)
            {
                if (value > flux[picked[^1]])
                    picked[^1] = i;
                continue;
            }

            picked.Add(i);
        }

        return picked;
    }

    // 왼쪽은 같은 값도 탈락시켜서 평평한 구간에서 마커가 여러 개 서지 않게 한다.
    private static bool IsLocalMaximum(float[] values, int index, int radius)
    {
        var start = Math.Max(0, index - radius);
        var end = Math.Min(values.Length, index + radius + 1);

        for (var i = start; i < index; i++)
        {
            if (values[i] >= values[index])
                return false;
        }

        for (var i = index + 1; i < end; i++)
        {
            if (values[i] > values[index])
                return false;
        }

        return true;
    }

    // 최댓값 하나(심벌 한 방)로 나누면 나머지가 전부 흐려진다. 상위 백분위를 기준으로 삼는다.
    private static float GetUpperPercentile(float[] values, double percentile)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);

        var index = Math.Clamp((int)(sorted.Length * percentile), 0, sorted.Length - 1);
        return sorted[index] > 0f ? sorted[index] : sorted[^1];
    }

    // flux 의 꼭대기는 소리가 이미 다 커진 뒤다. 그 앞에서 에너지가 오르기 시작한
    // 지점까지 되짚어야 마커가 실제 타격 순간에 선다. 격자와 대조하는 기준선이라
    // 여기서 한 버킷만 밀려도 BPM 을 그만큼 잘못 맞추게 된다.
    private static double RefineAttackBucket(float[] energies, int peakIndex, int lookback)
    {
        var start = Math.Max(0, peakIndex - (lookback * 2));

        var floorEnergy = float.MaxValue;
        for (var i = start; i <= peakIndex; i++)
            floorEnergy = MathF.Min(floorEnergy, energies[i]);

        var peakEnergy = 0f;
        var peakEnd = Math.Min(energies.Length, peakIndex + lookback + 1);
        for (var i = peakIndex; i < peakEnd; i++)
            peakEnergy = MathF.Max(peakEnergy, energies[i]);

        if (peakEnergy <= floorEnergy)
            return peakIndex;

        var trigger = floorEnergy + ((peakEnergy - floorEnergy) * 0.2f);

        for (var i = start; i <= peakIndex; i++)
        {
            if (energies[i] < trigger)
                continue;

            if (i == start)
                return i + BucketCenterOffset;

            // 버킷 두 칸 사이를 선형 보간해서 버킷보다 잘게 잡는다.
            var previous = energies[i - 1];
            var span = energies[i] - previous;
            var fraction = span > 0f ? (trigger - previous) / span : 0f;
            return (i - 1) + Math.Clamp((double)fraction, 0.0, 1.0) + BucketCenterOffset;
        }

        return peakIndex + BucketCenterOffset;
    }

    // 버킷 값은 그 칸 전체를 요약한 값이다. 그리는 쪽은 칸의 시작 위치에 그리는 게 맞지만,
    // 두 칸 값 사이를 보간해서 시각을 얻을 때는 값이 칸 한가운데를 가리킨다고 봐야 한다.
    // 이 반 칸을 빼먹으면 마커가 항상 2~3ms 씩 이르게 선다.
    private const double BucketCenterOffset = 0.5;
}
