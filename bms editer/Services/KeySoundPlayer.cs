using System;
using System.IO;
using System.Runtime.InteropServices;

namespace bms_editer.Services;

// 키음(WAV) 한 발을 재생한다.
//
// ⚠️ 지금 구현은 Win32 PlaySound 라서 **한 번에 하나만 소리가 난다.**
// SND_NOSTOP 없이 부르면 앞서 나던 소리를 끊고 새로 시작하기 때문에,
//   * 같은 자리에 있는 화음은 마지막 한 음만 들리고
//   * 긴 키음은 다음 노트가 나올 때 잘린다.
// 재생 중에 들리는 건 곡이 아니라 "마지막 노트들"이다.
//
// 제대로 하려면 키음을 PCM 으로 미리 풀어두고, OggAudioPlayer 가 이미 쥐고 있는
// waveOut 스트림에 합산해서 내보내야 한다. 그 작업을 하려면 재생 지점이 한 곳에
// 모여 있어야 해서, 먼저 뷰모델에 박혀 있던 P/Invoke 를 여기로 옮겼다.
public sealed partial class KeySoundPlayer
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_FILENAME = 0x00020000;

    public void Play(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        try
        {
            PlaySound(filePath, IntPtr.Zero, SND_ASYNC | SND_FILENAME);
        }
        catch
        {
            // 음원 재생 실패는 편집을 막을 이유가 못 된다.
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);
}
