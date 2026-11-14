using System;
using System.Runtime.InteropServices;

namespace bms_editer.Services;

internal static partial class WinMmNative
{
    public const int WHDR_DONE = 0x00000001;
    public const int WHDR_PREPARED = 0x00000002;

    public const int SND_ASYNC = 0x0001;
    public const int SND_NODEFAULT = 0x0002;
    public const int SND_FILENAME = 0x00020000;

    public const uint TIME_SAMPLES = 0x0002;
    public const uint TIME_BYTES = 0x0004;

    public static readonly int WaveHeaderFlagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));

    // MMTIME. wType 뒤에 오는 union 은 smpte 구조체가 가장 커서 8바이트라
    // 전체 12바이트다. 우리는 union 의 첫 4바이트(sample/cb)만 읽는다.
    [StructLayout(LayoutKind.Sequential)]
    public struct MmTime
    {
        public uint Type;
        public uint Value;
        public uint UnionTail;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveFormat
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
    public struct WaveHeader
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

    [LibraryImport("winmm.dll", EntryPoint = "waveOutSetVolume")]
    public static partial int WaveOutSetVolume(IntPtr hWaveOut, uint dwVolume);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutOpen")]
    public static partial int WaveOutOpen(out IntPtr hWaveOut, int deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    public static partial int WaveOutPrepareHeader(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutWrite")]
    public static partial int WaveOutWrite(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutReset")]
    public static partial int WaveOutReset(IntPtr hWaveOut);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    public static partial int WaveOutUnprepareHeader(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutClose")]
    public static partial int WaveOutClose(IntPtr hWaveOut);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutGetPosition")]
    public static partial int WaveOutGetPosition(IntPtr hWaveOut, ref MmTime mmTime, int size);

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int PlaySound(string pszSound, IntPtr hmod, int fdwSound);
}
