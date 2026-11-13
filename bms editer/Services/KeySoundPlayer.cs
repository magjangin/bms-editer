using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace bms_editer.Services;

// 키음(WAV/OGG) 다중 믹싱 재생기.
//
// 1. PCM 사전 디코딩 및 메모리 캐싱으로 디스크 I/O 없이 즉시 발음.
// 2. 여러 키음(화음 또는 빠른 연속타)이 동시에 들어와도 앞 소리가 잘리지 않고
//    합산되어 함께 울리는 폴리포닉(다중 채널) 믹싱 지원.
public sealed unsafe partial class KeySoundPlayer : IDisposable
{
    private sealed class Voice
    {
        public short[] Samples { get; }
        public int Position { get; set; }

        // 장치에 써 넣은 프레임 기준으로 소리가 시작될 자리. 음수면 다음 버퍼에 바로 넣는다.
        public long StartFrame { get; }

        public Voice(short[] samples, long startFrame = -1)
        {
            Samples = samples;
            Position = 0;
            StartFrame = startFrame;
        }
    }

    // Lazy 로 감싸 같은 파일을 두 번 풀지 않게 한다. IsValueCreated 로 "다 풀렸는지"를 기다리지 않고 알 수 있다.
    private readonly ConcurrentDictionary<string, Lazy<PcmAudioData?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Voice> _activeVoices = new();
    private readonly object _lock = new();

    private const int BufferFrames = 1764; // 40ms at 44100Hz
    private const int BufferCount = 3;
    private const int SampleRate = 44100;
    private const short Channels = 2;
    private const short BitsPerSample = 16;
    private const int BlockAlign = Channels * (BitsPerSample / 8);

    // 재생 중이 아닐 때는 마지막 소리가 실제로 다 나간 뒤에 장치를 닫는다.
    //
    // 빈 버퍼 3개를 큐에 넣자마자 닫으면 마지막 소리가 아직 재생 중이라 꼬리가 최대 40ms 잘린다.
    // 네 번째 빈 버퍼를 쓸 수 있다는 것은 마지막 소리가 든 버퍼가 끝났다는 뜻이다.
    // 오래 열어 두지 않는 이유는, 무음이 큐에 차 있으면 미리듣기가 그 뒤에 붙어 120ms 늦게 나기 때문이다.
    private const int IdleBuffersBeforeClose = BufferCount + 2;

    private readonly object _deviceLock = new();

    private volatile float _volume = 1.0f;

    public double Volume
    {
        get => _volume;
        set => _volume = (float)Math.Clamp(value, 0.0, 1.0);
    }

    // 장치를 연 뒤 믹서가 써 넣은 프레임 수. 믹서 스레드만 고친다.
    private long _framesWritten;

    private IntPtr _waveOut = IntPtr.Zero;
    private readonly IntPtr[] _buffers = new IntPtr[BufferCount];
    private readonly IntPtr[] _headerPtrs = new IntPtr[BufferCount];
    private Thread? _playbackThread;
    private readonly AutoResetEvent _wakeEvent = new(false);
    private volatile bool _isDisposed;
    private volatile bool _holdDeviceOpen;

    // 캐시를 비울 때마다 늘어난다. 앞 문서의 미리 읽기를 멈추는 표시다.
    private int _cacheGeneration;

    // StopAll 때마다 늘어난다. 디코딩을 기다리던 소리가 정지 뒤에 울리지 않게 한다.
    private int _stopGeneration;

    private static readonly ParallelOptions PreloadOptions = new()
    {
        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
    };

    public KeySoundPlayer()
    {
        for (var i = 0; i < BufferCount; i++)
        {
            _buffers[i] = Marshal.AllocHGlobal(BufferFrames * BlockAlign);
            _headerPtrs[i] = Marshal.AllocHGlobal(sizeof(WaveHeader));
            var hdr = (WaveHeader*)_headerPtrs[i];
            hdr->Data = _buffers[i];
            hdr->BufferLength = BufferFrames * BlockAlign;
            hdr->BytesRecorded = 0;
            hdr->User = IntPtr.Zero;
            hdr->Flags = WHDR_DONE;
            hdr->Loops = 0;
            hdr->Next = IntPtr.Zero;
            hdr->Reserved = IntPtr.Zero;
        }

        StartAudioWorker();
    }

    // 재생하는 동안 장치를 계속 열어 둔다. 키음 지연이 구간마다 달라지지 않게 한다. (P-9)
    public bool HoldDeviceOpen
    {
        get => _holdDeviceOpen;
        set
        {
            _holdDeviceOpen = value;
            if (value)
                WakeMixer();
        }
    }

    public void ClearCache()
    {
        Interlocked.Increment(ref _cacheGeneration);
        _cache.Clear();
    }

    private Lazy<PcmAudioData?> GetCacheEntry(string filePath) =>
        _cache.GetOrAdd(
            filePath,
            static path => new Lazy<PcmAudioData?>(() => WavDecoder.Decode(path), LazyThreadSafetyMode.ExecutionAndPublication));

    // 지금 백그라운드에서 풀고 있는 파일. 같은 파일을 두 번 맡기지 않는다.
    //
    // 예전에는 안 풀린 노트를 만날 때마다 Task.Run 을 새로 냈다. 곡을 열자마자 재생하면
    // 같은 키음을 가리키는 노트가 틱마다 작업을 하나씩 더 냈고, 그 작업들은 Lazy 의 잠금에서
    // 첫 디코딩이 끝나기만 기다리며 스레드 풀을 붙잡았다. 풀이 마르면 미리 읽기까지 밀렸다.
    private readonly ConcurrentDictionary<string, byte> _decodesInFlight = new(StringComparer.OrdinalIgnoreCase);

    private void DecodeInBackground(string filePath, Lazy<PcmAudioData?> entry)
    {
        if (!_decodesInFlight.TryAdd(filePath, 0))
            return;

        Task.Run(() =>
        {
            try
            {
                _ = entry.Value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"키음 디코딩 실패: {filePath} ({ex.Message})");
            }
            finally
            {
                _decodesInFlight.TryRemove(filePath, out _);
            }
        });
    }

    // 호출한 스레드에서 끝까지 푼다. UI 스레드에서는 PreloadAsync 를 쓴다.
    public void Preload(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        _ = GetCacheEntry(filePath).Value;
    }

    public void PreloadAsync(IEnumerable<string> filePaths)
    {
        var generation = Volatile.Read(ref _cacheGeneration);
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // 키음 수백 개를 하나씩 풀면 곡을 열고 한참 동안 디코딩이 끝나지 않는다.
        Task.Run(() => Parallel.ForEach(paths, PreloadOptions, (path, state) =>
        {
            if (_isDisposed || generation != Volatile.Read(ref _cacheGeneration))
            {
                state.Stop();
                return;
            }

            Preload(path);
        }));
    }

    // 사용자가 직접 듣는 소리(미리듣기·노트 찍기). 아직 안 풀렸으면 백그라운드에서 풀고 끝나는 대로 낸다.
    //
    // 예전에는 여기서 UI 스레드가 디코딩까지 했다. 재생 타이머가 그만큼 밀려
    // 다음 틱에 지난 노트가 한꺼번에 울렸고, 화면도 같이 멈췄다. (P-10)
    public void Play(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || _isDisposed)
            return;

        var entry = GetCacheEntry(filePath);
        if (entry.IsValueCreated)
        {
            StartVoice(filePath, entry.Value);
            return;
        }

        // 사용자가 직접 낸 소리는 드물고 하나하나가 들려야 하므로 겹쳐 맡겨도 된다.
        // (재생 중 노트처럼 틱마다 쏟아지지 않는다. 중복 제거는 ScheduleIfDecoded 쪽만 한다)
        var generation = Volatile.Read(ref _stopGeneration);
        Task.Run(() =>
        {
            var pcm = entry.Value;
            if (!_isDisposed)
                StartVoice(filePath, pcm, generation);
        });
    }

    // 재생 중인 노트의 소리를 지금부터 delaySeconds 뒤에 정확히 울리도록 예약한다.
    //
    // 믹서는 버퍼 3개(120ms)를 앞서 채워 두므로, 그냥 넣으면 대기열 뒤에 붙어 배경음보다 늦게 난다.
    // 실측으로 100~120ms 였다. 장치의 재생 위치를 기준으로 미리 자리를 잡아 대기열 지연을 없앤다.
    //
    // 아직 안 풀렸으면 이번 소리는 건너뛰고 다음을 위해 풀어 둔다.
    // 늦게 풀린 소리를 뒤늦게 내면 박자가 어긋나 차트가 틀린 것처럼 들린다.
    public void ScheduleIfDecoded(string? filePath, double delaySeconds)
    {
        if (string.IsNullOrWhiteSpace(filePath) || _isDisposed)
            return;

        var entry = GetCacheEntry(filePath);
        if (!entry.IsValueCreated)
        {
            DecodeInBackground(filePath, entry);
            return;
        }

        var pcm = entry.Value;
        if (pcm is null || pcm.Samples.Length == 0)
            return;

        var startFrame = GetPlayedFrames() is { } played
            ? played + (long)Math.Round(Math.Max(0, delaySeconds) * SampleRate)
            : -1;

        lock (_lock)
        {
            _activeVoices.Add(new Voice(pcm.Samples, startFrame));
        }

        WakeMixer();
    }

    // 예약만 되고 아직 울리기 시작하지 않은 소리를 지운다. 재생 위치를 옮길 때 쓴다.
    public void CancelScheduled()
    {
        lock (_lock)
        {
            _activeVoices.RemoveAll(voice => voice.StartFrame >= 0 && voice.Position == 0);
        }
    }

    // 장치가 열려 있고 재생 위치가 나아가기 시작했는지. 이때부터 앞서 예약할 수 있다.
    public bool IsClockRunning => GetPlayedFrames() is > 0;

    // 장치가 지금까지 실제로 재생한 프레임. 장치가 닫혀 있거나 조회가 안 되면 null.
    private long? GetPlayedFrames()
    {
        lock (_deviceLock)
        {
            if (_waveOut == IntPtr.Zero)
                return null;

            var time = new MmTime { Type = TIME_SAMPLES };
            if (WaveOutGetPosition(_waveOut, ref time, sizeof(MmTime)) != 0)
                return null;

            return time.Type switch
            {
                TIME_SAMPLES => time.Value,
                TIME_BYTES => time.Value / BlockAlign,
                _ => null,
            };
        }
    }

    // requiredGeneration 이 있으면 그 사이 StopAll 이 불리지 않았을 때만 낸다.
    // 확인과 추가를 같은 잠금 안에서 해야 정지 직후에 소리가 끼어들지 않는다.
    private void StartVoice(string filePath, PcmAudioData? pcm, int? requiredGeneration = null)
    {
        if (pcm is null || pcm.Samples.Length == 0)
        {
            if (requiredGeneration is null || requiredGeneration == Volatile.Read(ref _stopGeneration))
            {
                // 디코딩 실패 시 OS 기본 PlaySound 로 시도
                PlaySoundFallback(filePath);
            }

            return;
        }

        lock (_lock)
        {
            if (requiredGeneration is { } generation && generation != _stopGeneration)
                return;

            _activeVoices.Add(new Voice(pcm.Samples));
        }

        WakeMixer();
    }

    private void WakeMixer()
    {
        try
        {
            _wakeEvent.Set();
        }
        catch (ObjectDisposedException)
        {
            // 디코딩이 끝나기 전에 앱이 닫혔다.
        }
    }

    // 파일 확인과 PlaySound 의 파일 읽기는 디스크를 건드리므로 UI 스레드에서 하지 않는다.
    private static void PlaySoundFallback(string filePath)
    {
        ThreadPool.QueueUserWorkItem(static path =>
        {
            try
            {
                if (File.Exists(path))
                    PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_NODEFAULT | SND_FILENAME);
            }
            catch
            {
                // 대체 재생까지 실패하면 소리 없이 넘어간다.
            }
        }, filePath, preferLocal: false);
    }

    public int ActiveVoiceCount
    {
        get
        {
            lock (_lock)
                return _activeVoices.Count;
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            Volatile.Write(ref _stopGeneration, _stopGeneration + 1);
            _activeVoices.Clear();
        }
    }

    private void StartAudioWorker()
    {
        _playbackThread = new Thread(AudioLoop)
        {
            IsBackground = true,
            Name = "KeySoundPlayer_Mixer",

            // UI 가 바쁠 때 믹서까지 밀리면 버퍼가 비어 소리가 끊긴다.
            Priority = ThreadPriority.AboveNormal,
        };
        _playbackThread.Start();
    }

    private void AudioLoop()
    {
        var mixBuffer = new int[BufferFrames * Channels];
        var currentBufferIndex = 0;

        // 시작할 때는 낼 소리가 없으니 장치를 열지 않고 기다린다.
        var silentBuffersCount = IdleBuffersBeforeClose;

        while (!_isDisposed)
        {
            bool hasVoices;
            lock (_lock)
            {
                hasVoices = _activeVoices.Count > 0;
            }

            if (!hasVoices && !_holdDeviceOpen && silentBuffersCount >= IdleBuffersBeforeClose)
            {
                CloseWaveOut();
                _wakeEvent.WaitOne(500);
                continue;
            }

            if (!EnsureWaveOutOpen())
            {
                Thread.Sleep(30);
                continue;
            }

            var hdr = (WaveHeader*)_headerPtrs[currentBufferIndex];

            // 현재 버퍼가 재생 완료될 때까지 대기. 드라이버가 다른 스레드에서 고치는 값이다.
            var flags = Volatile.Read(ref hdr->Flags);
            if ((flags & WHDR_DONE) == 0 && (flags & WHDR_PREPARED) != 0)
            {
                Thread.Sleep(3);
                continue;
            }

            Array.Clear(mixBuffer);
            var mixedAny = false;

            var bufferStartFrame = _framesWritten;

            lock (_lock)
            {
                for (var v = _activeVoices.Count - 1; v >= 0; v--)
                {
                    var voice = _activeVoices[v];

                    // 예약된 자리가 이 버퍼보다 뒤면 다음 버퍼에서 다시 본다. 이미 지났으면 맨 앞에 넣는다.
                    var startOffset = Math.Max(0, voice.StartFrame - bufferStartFrame);
                    if (startOffset >= BufferFrames)
                        continue;

                    var writeOffset = (int)startOffset * Channels;
                    var source = voice.Samples;
                    var position = voice.Position;
                    var samplesToCopy = Math.Min(source.Length - position, mixBuffer.Length - writeOffset);

                    var currentVol = _volume;
                    for (var i = 0; i < samplesToCopy; i++)
                        mixBuffer[writeOffset + i] += (int)(source[position + i] * currentVol);

                    mixedAny |= samplesToCopy > 0;
                    voice.Position = position + samplesToCopy;

                    if (voice.Position >= source.Length)
                        _activeVoices.RemoveAt(v);
                }

                hasVoices = _activeVoices.Count > 0;
            }

            // 이번 버퍼에 소리를 넣었으면 그 소리가 다 나갈 때까지는 조용한 게 아니다.
            // hasVoices 만 보면 40ms 보다 짧은 소리는 넣자마자 장치가 닫혀 아예 안 들린다.
            silentBuffersCount = hasVoices || mixedAny ? 0 : silentBuffersCount + 1;

            // 네이티브 버퍼에 바로 쓴다. 합산을 다 끝낸 뒤에 한 번만 제한해야 겹치는 순서와 무관한 결과가 나온다.
            var output = new Span<short>((void*)_buffers[currentBufferIndex], mixBuffer.Length);
            if (mixedAny)
            {
                for (var i = 0; i < mixBuffer.Length; i++)
                    output[i] = SoftLimit(mixBuffer[i]);
            }
            else
            {
                output.Clear();
            }

            // waveOutWrite
            if ((hdr->Flags & WHDR_PREPARED) != 0)
            {
                WaveOutUnprepareHeader(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader));
            }

            hdr->Data = _buffers[currentBufferIndex];
            hdr->BufferLength = BufferFrames * BlockAlign;
            hdr->BytesRecorded = 0;
            hdr->Flags = 0;

            WaveOutPrepareHeader(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader));
            WaveOutWrite(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader));

            _framesWritten += BufferFrames;
            currentBufferIndex = (currentBufferIndex + 1) % BufferCount;
        }

        CloseWaveOut();
    }

    // 이 크기를 넘는 합산 값만 부드럽게 눌러 범위 안에 넣는다. (전체 범위의 약 85%)
    private const int LimiterThreshold = 27852;

    // 키음 합산 값을 16비트로 줄인다.
    //
    // 예전에는 범위를 넘는 표본을 그대로 잘라서(hard clip) 화음이 겹치면 소리가 찢어졌다. (P-11)
    // 임계값 아래는 한 표본도 건드리지 않으므로 평소 소리는 그대로다.
    public static short SoftLimit(int sample)
    {
        var magnitude = Math.Abs(sample);
        if (magnitude <= LimiterThreshold)
            return (short)sample;

        double headroom = short.MaxValue - LimiterThreshold;
        var limited = LimiterThreshold + (headroom * (1.0 - Math.Exp(-(magnitude - LimiterThreshold) / headroom)));
        var result = (short)Math.Min(short.MaxValue, Math.Round(limited));
        return sample < 0 ? (short)-result : result;
    }

    private bool EnsureWaveOutOpen()
    {
        if (_waveOut != IntPtr.Zero)
            return true;

        try
        {
            var format = new WaveFormat
            {
                FormatTag = 1,
                Channels = Channels,
                SamplesPerSec = SampleRate,
                BitsPerSample = BitsPerSample,
                BlockAlign = (short)BlockAlign,
                AvgBytesPerSec = SampleRate * BlockAlign,
                Size = 0,
            };

            lock (_deviceLock)
            {
                var res = WaveOutOpen(out _waveOut, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0);
                if (res != 0 || _waveOut == IntPtr.Zero)
                    return false;

                _framesWritten = 0;
            }

            for (var i = 0; i < BufferCount; i++)
            {
                var hdr = (WaveHeader*)_headerPtrs[i];
                hdr->Flags = WHDR_DONE;
            }

            return true;
        }
        catch
        {
            _waveOut = IntPtr.Zero;
            return false;
        }
    }

    private void CloseWaveOut()
    {
        lock (_deviceLock)
        {
            if (_waveOut == IntPtr.Zero)
                return;

            try
            {
                WaveOutReset(_waveOut);

                for (var i = 0; i < BufferCount; i++)
                {
                    var hdr = (WaveHeader*)_headerPtrs[i];
                    if ((hdr->Flags & WHDR_PREPARED) != 0)
                    {
                        WaveOutUnprepareHeader(_waveOut, _headerPtrs[i], sizeof(WaveHeader));
                    }
                }

                WaveOutClose(_waveOut);
            }
            catch
            {
                // 무시
            }
            finally
            {
                _waveOut = IntPtr.Zero;
            }
        }
    }

    // 믹서 스레드가 끝나기를 기다리는 최대 시간.
    private static readonly TimeSpan MixerShutdownTimeout = TimeSpan.FromSeconds(2);

    // 믹서 스레드가 끝난 **뒤에만** 네이티브 버퍼를 풀어 준다. (알려진 문제 S-7)
    //
    // 예전에는 300ms 만 기다리고 무조건 풀었다. 그 스레드가 드라이버 호출에 붙잡혀 있다가
    // 돌아와 이미 풀린 버퍼에 쓰면 접근 위반으로 종료 순간 앱이 죽었다.
    // 끝내 안 끝나면 풀지 않고 둔다. 종료 중이라 곧 운영체제가 거둬 가고, 새는 편이 죽는 것보다 낫다.
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        WakeMixer();

        var mixerStopped = _playbackThread is null || _playbackThread.Join(MixerShutdownTimeout);
        if (!mixerStopped)
            return;

        // 믹서는 빠져나오면서 장치를 스스로 닫는다. 여기서는 남은 게 있을 때만 닫는다.
        CloseWaveOut();

        for (var i = 0; i < BufferCount; i++)
        {
            if (_buffers[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffers[i]);
                _buffers[i] = IntPtr.Zero;
            }

            if (_headerPtrs[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_headerPtrs[i]);
                _headerPtrs[i] = IntPtr.Zero;
            }
        }

        _wakeEvent.Dispose();
        _cache.Clear();
    }

    private const int WHDR_DONE = 0x00000001;
    private const int WHDR_PREPARED = 0x00000002;

    private const int SND_ASYNC = 0x0001;
    private const int SND_NODEFAULT = 0x0002;
    private const int SND_FILENAME = 0x00020000;

    private const uint TIME_SAMPLES = 0x0002;
    private const uint TIME_BYTES = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct MmTime
    {
        public uint Type;
        public uint Value;
        public uint UnionTail;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [LibraryImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static partial int WaveOutOpen(out IntPtr hWaveOut, int deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static partial int WaveOutPrepareHeader(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static partial int WaveOutWrite(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static partial int WaveOutReset(IntPtr hWaveOut);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static partial int WaveOutUnprepareHeader(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static partial int WaveOutClose(IntPtr hWaveOut);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutGetPosition")]
    private static partial int WaveOutGetPosition(IntPtr hWaveOut, ref MmTime mmTime, int size);

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PlaySound(string pszSound, IntPtr hmod, int fdwSound);
}
