using System;
using System.Runtime.InteropServices;
using NVorbis;

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

    public OggAudioPlayer(string filePath)
    {
        using var reader = new VorbisReader(filePath);
        _sampleRate = reader.SampleRate;
        _channels = (short)reader.Channels;
        DurationSeconds = reader.TotalTime.TotalSeconds;
        _pcmBytes = DecodePcm16(reader);
    }

    public double DurationSeconds { get; }

    public void Play(double startSeconds)
    {
        Stop();

        if (_pcmBytes.Length == 0)
            return;

        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = _channels,
            SamplesPerSec = _sampleRate,
            BitsPerSample = _bitsPerSample,
        };
        format.BlockAlign = (short)(format.Channels * format.BitsPerSample / 8);
        format.AvgBytesPerSec = format.SamplesPerSec * format.BlockAlign;

        ThrowIfFailed(WaveOutOpen(out _waveOut, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0));

        var startByte = SecondsToByteOffset(startSeconds, format.BlockAlign);
        var byteCount = _pcmBytes.Length - startByte;
        if (byteCount <= 0)
            return;

        _dataHandle = GCHandle.Alloc(_pcmBytes, GCHandleType.Pinned);
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

    public void Stop()
    {
        if (_waveOut == IntPtr.Zero)
            return;

        WaveOutReset(_waveOut);

        if (_prepared && _headerHandle.IsAllocated)
            WaveOutUnprepareHeader(_waveOut, _headerHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>());

        WaveOutClose(_waveOut);
        _waveOut = IntPtr.Zero;
        _prepared = false;

        if (_headerHandle.IsAllocated)
            _headerHandle.Free();

        if (_dataHandle.IsAllocated)
            _dataHandle.Free();
    }

    public void Dispose() => Stop();

    private int SecondsToByteOffset(double seconds, int blockAlign)
    {
        var clampedSeconds = Math.Clamp(seconds, 0, DurationSeconds);
        var byteOffset = (int)(clampedSeconds * _sampleRate * blockAlign);
        return Math.Clamp(byteOffset - (byteOffset % blockAlign), 0, _pcmBytes.Length);
    }

    private static byte[] DecodePcm16(VorbisReader reader)
    {
        var totalSamples = checked((int)(reader.TotalSamples * reader.Channels));
        var bytes = new byte[totalSamples * sizeof(short)];
        var floatBuffer = new float[4096 * reader.Channels];
        var byteOffset = 0;

        int samplesRead;
        while ((samplesRead = reader.ReadSamples(floatBuffer, 0, floatBuffer.Length)) > 0)
        {
            for (var i = 0; i < samplesRead; i++)
            {
                var sample = (short)(Math.Clamp(floatBuffer[i], -1f, 1f) * short.MaxValue);
                bytes[byteOffset++] = (byte)(sample & 0xff);
                bytes[byteOffset++] = (byte)((sample >> 8) & 0xff);
            }
        }

        return bytes;
    }

    private static void ThrowIfFailed(int result)
    {
        if (result != 0)
            throw new InvalidOperationException($"waveOut error: {result}");
    }

    [DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static extern int WaveOutOpen(out IntPtr hWaveOut, int deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static extern int WaveOutPrepareHeader(IntPtr hWaveOut, IntPtr header, int headerSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static extern int WaveOutWrite(IntPtr hWaveOut, IntPtr header, int headerSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static extern int WaveOutReset(IntPtr hWaveOut);

    [DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static extern int WaveOutUnprepareHeader(IntPtr hWaveOut, IntPtr header, int headerSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static extern int WaveOutClose(IntPtr hWaveOut);

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
}
