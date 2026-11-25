using System;
using System.Runtime.InteropServices;
using System.Threading;
using static bms_editer.Services.WinMmNative;

namespace bms_editer.Services;

public sealed class OggAudioPlayer : IDisposable
{
    private readonly byte[] _pcmBytes;
    private readonly int _sampleRate;
    private readonly short _channels;
    private readonly short _bitsPerSample = 16;
    private IntPtr _waveOut;
    private GCHandle _dataHandle;
    private GCHandle _headerHandle;
    private WaveHeader _header;
    private bool _prepared;
    private GCHandle _leadInDataHandle;
    private GCHandle _leadInHeaderHandle;
    private WaveHeader _leadInHeader;
    private bool _leadInPrepared;
    private double _leadInSeconds;

    // 디코딩은 OggDecoder 가 한 번만 한다. 파형 계산도 같은 결과를 나눠 쓴다.
    public OggAudioPlayer(OggAudioData data)
    {
        _sampleRate = data.SampleRate;
        _channels = (short)data.Channels;
        DurationSeconds = data.DurationSeconds;
        _pcmBytes = data.Pcm16;
    }

    public double DurationSeconds { get; }

    private double _volume = 1.0;

    public double Volume
    {
        get => _volume;
        set => SetVolume(value);
    }

    // 다음 Play 부터 이 크기로 낸다. 재생 중에 바꾼 값을 바로 들리게 하려면 다시 Play 해야 한다.
    //
    // 예전에는 waveOutSetVolume 으로 장치 음량을 바꿨다. Vista 이후 그 값은 핸들 하나가 아니라
    // **이 프로세스의 오디오 세션 전체**의 음량이라(실측: 핸들 A 에 25% → 핸들 B 도 25%),
    // 배경음 음량을 내리면 키음까지 같은 비율로 작아졌고 0 이면 키음도 아예 안 들렸다.
    // 이제 표본 자체를 줄여서 키음 장치와 완전히 따로 논다.
    public void SetVolume(double volume)
    {
        var safe = double.IsFinite(volume) ? Math.Clamp(volume, 0.0, 1.0) : 1.0;
        if (safe == _volume)
            return;

        _volume = safe;
        _scaledPcm = null;
    }

    // 지금 음량으로 줄인 PCM. 음량이 1 이면 원본을 그대로 쓰고 만들지 않는다.
    // 재생 위치를 옮길 때마다(스크럽) 곡 전체를 새로 줄이지 않도록 음량이 바뀔 때까지 들고 있는다.
    private byte[]? _scaledPcm;

    private byte[] GetPlaybackPcm()
    {
        if (_volume >= 1.0)
            return _pcmBytes;

        return _scaledPcm ??= ScalePcm16(_pcmBytes, _volume);
    }

    // PCM16 표본에 음량을 곱한 새 배열. 1/32768 단위 고정소수로 곱해 곡 전체도 수십 ms 안에 끝난다.
    internal static byte[] ScalePcm16(byte[] source, double volume)
    {
        var result = new byte[source.Length];
        var gain = (int)Math.Round(Math.Clamp(volume, 0.0, 1.0) * 32768.0);
        if (gain == 0)
            return result;

        var sampleBytes = source.Length - (source.Length % sizeof(short));
        var input = MemoryMarshal.Cast<byte, short>(source.AsSpan(0, sampleBytes));
        var output = MemoryMarshal.Cast<byte, short>(result.AsSpan(0, sampleBytes));
        for (var i = 0; i < input.Length; i++)
            output[i] = (short)((input[i] * gain) >> 15);

        return result;
    }

    // 예전 버전은 세션 음량을 배경음 음량으로 낮춘 채 끝났을 수 있다. 그 값은 Windows 가 앱별로 기억해서,
    // 이번 버전이 손대지 않으면 키음·배경음이 이유 없이 작게 남는다. 실행마다 처음 한 번만 되돌린다.
    // (예전 버전도 재생할 때마다 이 값을 덮어썼으므로 사용자의 볼륨 믹서 설정을 새로 해치지 않는다)
    private static int _sessionVolumeReset;

    private static void ResetLegacySessionVolumeOnce(IntPtr waveOut)
    {
        if (Interlocked.Exchange(ref _sessionVolumeReset, 1) == 0)
            WaveOutSetVolume(waveOut, 0xFFFFFFFF);
    }

    private int BlockAlign => _channels * _bitsPerSample / 8;

    // 장치가 실제로 재생한 시간(초). 재생 중이 아니거나 장치가 위치 조회를
    // 지원하지 않으면 null.
    //
    // 벽시계로 커서를 움직이면 두 가지로 어긋난다. 첫째로 waveOutWrite 가
    // 돌아온 뒤에도 실제 출력까지 드라이버 버퍼링 지연이 있어서 커서가 그만큼
    // 앞서 나가고, 둘째로 사운드 장치는 시스템 시계와 다른 크리스털로 돌아가
    // 시간이 갈수록 미세하게 벌어진다. 장치에 직접 물어보면 둘 다 사라진다.
    //
    // 앞에 넣은 무음(LeadInSeconds)을 재생하는 동안은 음수다. 0 이 곡의 시작 지점이다.
    public double? GetPlayedSeconds()
    {
        if (_waveOut == IntPtr.Zero || _sampleRate <= 0)
            return null;

        var mmTime = new MmTime { Type = TIME_SAMPLES };
        if (WaveOutGetPosition(_waveOut, ref mmTime, Marshal.SizeOf<MmTime>()) != 0)
            return null;

        // 요청한 단위를 장치가 못 맞추면 실제로 채워준 단위가 Type 에 담겨 온다.
        double? played = mmTime.Type switch
        {
            TIME_SAMPLES => mmTime.Value / (double)_sampleRate,
            TIME_BYTES => mmTime.Value / (double)(_sampleRate * Math.Max(1, BlockAlign)),
            _ => null,
        };

        return played - _leadInSeconds;
    }

    // 이번 재생에서 곡 앞에 넣은 무음의 길이(곡 시각, 초).
    public double LeadInSeconds => _leadInSeconds;

    // 장치가 넘겨준 PCM 을 끝까지 다 재생했는지.
    //
    // 재생 위치만 보면 부동소수 끝자리 차이나 장치가 마지막 조각을 보고하지 않는 일로
    // 곡 길이에 영영 못 닿을 수 있다. 그러면 곡이 끝났는데도 재생 중 상태로 남는다.
    public bool HasReachedEnd
    {
        get
        {
            if (_waveOut == IntPtr.Zero || !_headerHandle.IsAllocated)
                return false;

            var flags = Marshal.ReadInt32(_headerHandle.AddrOfPinnedObject(), HeaderFlagsOffset);
            return (flags & WHDR_DONE) != 0;
        }
    }

    // leadInSeconds: 곡 앞에 넣을 무음의 길이(곡 시각, 초).
    //
    // 시작 지점 바로 뒤의 키음은 미리 예약할 시간이 없다. 무음이 흐르는 동안 장치 시계가 먼저 돌아
    // 첫 음까지 정확한 자리에 예약할 수 있다. 같은 스트림에 이어 붙이므로 무음과 곡 사이에 틈이 없다.
    public void Play(double startSeconds, double speed = 1.0, double leadInSeconds = 0)
    {
        Stop();

        if (_pcmBytes.Length == 0 || _sampleRate <= 0 || _channels <= 0)
            return;

        var speedClamped = Math.Clamp(speed, 0.05, 4.0);
        var targetSampleRate = Math.Max(1000, (int)Math.Round(_sampleRate * speedClamped));

        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = _channels,
            SamplesPerSec = targetSampleRate,
            BitsPerSample = _bitsPerSample,
        };
        format.BlockAlign = (short)(format.Channels * format.BitsPerSample / 8);
        format.AvgBytesPerSec = format.SamplesPerSec * format.BlockAlign;

        var startByte = SecondsToByteOffset(startSeconds, format.BlockAlign);
        var byteCount = _pcmBytes.Length - startByte;
        if (byteCount <= 0)
            return;

        var pcm = GetPlaybackPcm();

        ThrowIfFailed(WaveOutOpen(out _waveOut, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0));
        try
        {
            ResetLegacySessionVolumeOnce(_waveOut);

            var leadInFrames = (int)Math.Round(Math.Max(0, leadInSeconds) * _sampleRate);
            if (leadInFrames > 0)
            {
                var silence = new byte[leadInFrames * format.BlockAlign];
                _leadInDataHandle = GCHandle.Alloc(silence, GCHandleType.Pinned);
                _leadInHeader = new WaveHeader
                {
                    Data = _leadInDataHandle.AddrOfPinnedObject(),
                    BufferLength = silence.Length,
                };
                _leadInHeaderHandle = GCHandle.Alloc(_leadInHeader, GCHandleType.Pinned);

                ThrowIfFailed(WaveOutPrepareHeader(_waveOut, _leadInHeaderHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>()));
                _leadInPrepared = true;
                ThrowIfFailed(WaveOutWrite(_waveOut, _leadInHeaderHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>()));
                _leadInSeconds = leadInFrames / (double)_sampleRate;
            }

            _dataHandle = GCHandle.Alloc(pcm, GCHandleType.Pinned);
            _header = new WaveHeader
            {
                Data = _dataHandle.AddrOfPinnedObject() + startByte,
                BufferLength = byteCount,
            };
            _headerHandle = GCHandle.Alloc(_header, GCHandleType.Pinned);

            ThrowIfFailed(WaveOutPrepareHeader(_waveOut, _headerHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>()));
            _prepared = true;
            ThrowIfFailed(WaveOutWrite(_waveOut, _headerHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>()));
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        if (_waveOut == IntPtr.Zero)
            return;

        WaveOutReset(_waveOut);

        if (_prepared && _headerHandle.IsAllocated)
            WaveOutUnprepareHeader(_waveOut, _headerHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>());

        if (_leadInPrepared && _leadInHeaderHandle.IsAllocated)
            WaveOutUnprepareHeader(_waveOut, _leadInHeaderHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>());

        WaveOutClose(_waveOut);
        _waveOut = IntPtr.Zero;
        _prepared = false;
        _leadInPrepared = false;
        _leadInSeconds = 0;

        if (_headerHandle.IsAllocated)
            _headerHandle.Free();

        if (_dataHandle.IsAllocated)
            _dataHandle.Free();

        if (_leadInHeaderHandle.IsAllocated)
            _leadInHeaderHandle.Free();

        if (_leadInDataHandle.IsAllocated)
            _leadInDataHandle.Free();
    }

    public void Dispose() => Stop();

    private int SecondsToByteOffset(double seconds, int blockAlign)
    {
        if (blockAlign <= 0 || !double.IsFinite(seconds))
            return 0;

        var clampedSeconds = Math.Clamp(seconds, 0, Math.Max(0, DurationSeconds));
        var byteOffset = (long)(clampedSeconds * _sampleRate * blockAlign);
        var aligned = byteOffset - (byteOffset % blockAlign);
        return (int)Math.Clamp(aligned, 0, _pcmBytes.Length);
    }

    private static void ThrowIfFailed(int result)
    {
        if (result != 0)
            throw new InvalidOperationException($"waveOut error: {result}");
    }

    private static readonly int HeaderFlagsOffset = WaveHeaderFlagsOffset;
}
