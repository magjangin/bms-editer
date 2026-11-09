using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using bms_editer.Models;
using bms_editer.Services;

namespace bms_editer.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void Play()
    {
        // 커서가 곡 끝에 남아 있으면 첫 틱에 바로 멈추므로 처음부터 다시 튼다. (P-8)
        var atEnd = PlaybackPositionSeconds >= OggDurationSeconds - 0.05;
        PlayFrom(IsPlaybackCursorVisible && !atEnd ? PlaybackPositionSeconds : 0);
    }

    [RelayCommand]
    private void Stop()
    {
        StopPlayback(resetCursor: false);
    }

    // 스페이스바 한 키로 재생/정지를 오간다. 편집하면서 가장 자주 하는 동작이다.
    [RelayCommand]
    private void TogglePlayback()
    {
        if (IsPlaying)
            StopPlayback(resetCursor: false);
        else
            Play();
    }

    // 드래그하는 동안에는 커서만 옮긴다.
    //
    // 예전에는 마우스가 움직일 때마다 PlayFrom 을 불렀다. 초당 100번 넘게 들어오는
    // 이벤트마다 오디오 장치를 닫았다 다시 열고(딸깍거림) 노트 전체를 다시 정렬했다.
    public void ScrubPreview(double ratio)
    {
        if (OggDurationSeconds <= 0)
            return;

        StopPlayback(resetCursor: false);
        PlaybackPositionSeconds = Math.Clamp(ratio, 0, 1) * OggDurationSeconds;
        IsPlaybackCursorVisible = true;
    }

    // 버튼을 뗄 때 한 번만 실제로 재생을 옮긴다.
    public void ScrubCommit(double ratio)
    {
        if (_audioPlayer is null || OggDurationSeconds <= 0)
            return;

        PlayFrom(Math.Clamp(ratio, 0, 1) * OggDurationSeconds);
    }

    public void StopPlaybackAtCurrentPosition() => StopPlayback(resetCursor: false);

    public KeySoundPlayer KeySoundPlayer => _keySoundPlayer;

    // 재생을 시작하지 못했을 때. 창이 사유를 띄운다. (P-7)
    public event Action<string>? PlaybackFailed;

    // 재생을 시작한다. 오디오 장치를 열 수 없으면(장치 없음·다른 앱이 독점) false.
    //
    // 예전에는 OggAudioPlayer 가 던진 예외를 아무도 받지 않아서, 재생 버튼 한 번에
    // 앱이 그대로 죽고 편집 중이던 내용이 전부 사라졌다.
    private bool PlayFrom(double seconds)
    {
        if (_audioPlayer is null)
            return false;

        var startSeconds = Math.Clamp(seconds, 0, OggDurationSeconds);
        _playbackNotes = GetSortedNotes();

        // 위치나 배속이 바뀌었으면 앞서 예약해 둔 키음은 이제 틀린 자리다.
        _keySoundPlayer.CancelScheduled();

        // 첫 노트를 예약할 때 재생 위치를 물어볼 수 있도록 배경음보다 먼저 장치를 깨운다.
        _keySoundPlayer.HoldDeviceOpen = true;

        // 곡 시각 기준의 무음 길이. 재생 속도가 느리면 무음도 그만큼 늘어나므로 배속을 곱한다.
        var leadIn = HasNoteRightAfter(startSeconds) ? KeySoundLeadInSeconds * PlaybackSpeed : 0;

        try
        {
            _audioPlayer.Play(startSeconds, PlaybackSpeed, leadIn);
        }
        catch (Exception ex)
        {
            StopPlayback(resetCursor: false);
            LastErrorMessage = $"재생을 시작하지 못했습니다.\n\n{ex.Message}";
            System.Diagnostics.Debug.WriteLine($"재생 실패: {ex.Message}");
            PlaybackFailed?.Invoke(LastErrorMessage);
            return false;
        }

        _playbackStartSeconds = startSeconds;
        _playbackStartedAt = DateTimeOffset.UtcNow;
        PlaybackPositionSeconds = startSeconds;
        _keySoundsScheduledUntil = startSeconds;
        IsPlaybackCursorVisible = true;
        IsPlaying = true;
        _playbackTimer.Start();
        LastErrorMessage = null;
        return true;
    }

    // 화면 프레임마다 창이 부른다. 타이머는 창이 가려져 프레임이 안 올 때도 키음이 이어지게 남겨 둔다.
    public void RefreshPlaybackPosition()
    {
        if (IsPlaying)
            UpdatePlaybackPosition();
    }

    private void UpdatePlaybackPosition()
    {
        if (_audioPlayer is null)
            return;

        // 장치가 실제로 재생한 위치를 기준으로 삼는다. 벽시계를 쓰면 출력 지연만큼
        // 커서가 처음부터 앞서 나가고, 장치 클럭과도 시간이 갈수록 벌어져서
        // 화면은 맞아 보이는데 소리와는 안 맞는 상태가 된다.
        // 장치가 위치 조회를 지원하지 않을 때만 예전처럼 벽시계로 되돌아간다.
        var playedSeconds = _audioPlayer.GetPlayedSeconds();

        // 곡 앞의 무음을 재생하는 동안에는 시작 지점보다 앞선 값이 나온다. 키음 예약은 이 값을 그대로 쓴다.
        var clockSeconds = playedSeconds is { } played
            ? _playbackStartSeconds + played
            : _playbackStartSeconds + (DateTimeOffset.UtcNow - _playbackStartedAt).TotalSeconds * PlaybackSpeed;

        var reachedEnd = _audioPlayer.HasReachedEnd;
        var currentSec = Math.Max(clockSeconds, _playbackStartSeconds);
        if (reachedEnd)
            currentSec = Math.Max(currentSec, OggDurationSeconds);

        PlaybackPositionSeconds = currentSec;

        ScheduleDueKeySounds(clockSeconds, playedSeconds);

        // 곡이 끝나서 멈출 때는 마지막 키음의 여운을 자르지 않는다.
        if (reachedEnd || PlaybackPositionSeconds >= OggDurationSeconds)
            StopPlayback(resetCursor: false, stopKeySounds: false);
    }

    private void ScheduleDueKeySounds(double clockSeconds, double? playedSeconds)
    {
        // 두 장치의 시계가 다 돌기 전에는 "지금"을 믿을 수 없다. 재생 시작 직후의 위치는 아직 0 에 머물러 있어서,
        // 그 값으로 예약하면 첫 음이 어긋난다. 그동안은 앞서 예약하지 않고, 시계가 없는 환경은 제때 바로 낸다.
        var leadIn = _audioPlayer?.LeadInSeconds ?? 0;
        var clocksRunning = playedSeconds is { } played && played > 1e-6 - leadIn && _keySoundPlayer.IsClockRunning;
        var lookahead = clocksRunning ? KeySoundLookaheadSeconds * PlaybackSpeed : 0;

        // 곡 끝을 넘겨 예약하면 끝난 뒤의 노트까지 울린다. 재생은 곡 끝에서 멈춘다.
        var scheduleUntil = Math.Min(OggDurationSeconds, Math.Max(_keySoundsScheduledUntil, clockSeconds + lookahead));
        var windowStart = GetKeySoundWindowStart(_keySoundsScheduledUntil, clockSeconds);
        if (scheduleUntil <= windowStart)
            return;

        ScheduleNotesInTimeRange(windowStart + AudioOffsetSeconds, scheduleUntil + AudioOffsetSeconds, clockSeconds + AudioOffsetSeconds);
        _keySoundsScheduledUntil = scheduleUntil;
    }

    // 시작 지점 바로 뒤에 울릴 노트가 있는지. 없으면 무음을 넣지 않고 바로 재생한다.
    private bool HasNoteRightAfter(double startSeconds)
    {
        if (!IsKeySoundEnabled || Bpm <= 0 || _playbackNotes.Length == 0)
            return false;

        var from = startSeconds + AudioOffsetSeconds;
        var index = FindFirstNoteAtOrAfter(from);
        if (index >= _playbackNotes.Length)
            return false;

        var note = _playbackNotes[index];
        return Timeline.SecondsAt(note.Measure + note.Position) < from + (KeySoundLookaheadSeconds * PlaybackSpeed);
    }

    // 믹서는 40ms 버퍼 3개를 앞서 채운다. 그 대기열에 틱 간격과 잠깐의 UI 지연을 더한 여유다.
    public const double KeySoundLookaheadSeconds = 0.25;

    // 시작 지점 바로 뒤에 노트가 있을 때 곡 앞에 넣는 무음(실제 시간, 초).
    // 키음 장치를 열고 대기열(120ms)보다 앞서 첫 음을 예약할 수 있는 최소한의 길이다.
    public const double KeySoundLeadInSeconds = 0.2;

    // 화면이 잠깐 멈춰 틱이 밀렸을 때 뒤늦게 울려도 되는 구간의 길이(곡 시각, 초).
    public const double MaxKeySoundCatchUpSeconds = 0.25;

    // 이번 틱에 키음을 낼 구간의 시작.
    //
    // 틱이 오래 밀리면 그 사이 노트를 한꺼번에 내게 되는데, 뭉쳐 나는 소리는
    // 차트가 틀린 것처럼 들린다. 최근 구간만 내고 더 지난 것은 버린다. (P-10)
    public static double GetKeySoundWindowStart(double lastSeconds, double currentSeconds) =>
        Math.Max(lastSeconds, currentSeconds - MaxKeySoundCatchUpSeconds);

    // 해당 시각 이상인 첫 노트를 이진 탐색으로 찾는다. 없으면 노트 개수.
    private int FindFirstNoteAtOrAfter(double seconds)
    {
        var timeline = Timeline;
        var low = 0;
        var high = _playbackNotes.Length - 1;
        var found = _playbackNotes.Length;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (timeline.SecondsAt(_playbackNotes[mid].Measure + _playbackNotes[mid].Position) >= seconds)
            {
                found = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return found;
    }

    private void ScheduleNotesInTimeRange(double start, double end, double now)
    {
        if (!IsKeySoundEnabled || Bpm <= 0 || _playbackNotes.Length == 0) return;

        // 시각 계산은 Timeline 이 맡는다. 예전에는 여기서 240/BPM 을 직접 써서,
        // BPM 이 바뀌거나 4/4가 아닌 마디가 있는 차트는 키음이 엉뚱한 때에 울렸다.
        var timeline = Timeline;

        for (var i = FindFirstNoteAtOrAfter(start); i < _playbackNotes.Length; i++)
        {
            var note = _playbackNotes[i];
            var noteSeconds = timeline.SecondsAt(note.Measure + note.Position);
            if (noteSeconds >= end)
                break;

            if (Chart.WavTable.TryGetValue(note.WavKey, out var path))
                _keySoundPlayer.ScheduleIfDecoded(path, (noteSeconds - now) / Math.Max(0.05, PlaybackSpeed));
        }
    }

    private void StopPlayback(bool resetCursor, bool stopKeySounds = true)
    {
        _playbackTimer.Stop();
        _audioPlayer?.Stop();

        if (stopKeySounds)
            _keySoundPlayer.StopAll();

        _keySoundPlayer.HoldDeviceOpen = false;
        _playbackNotes = Array.Empty<BmsNote>();
        IsPlaying = false;

        if (resetCursor)
        {
            PlaybackPositionSeconds = 0;
            IsPlaybackCursorVisible = false;
        }
        else
        {
            PlaybackPositionSeconds = Math.Clamp(PlaybackPositionSeconds, 0, OggDurationSeconds);
        }
    }

    public void PlayWavSound(string key)
    {
        if (Chart.WavTable.TryGetValue(key, out var path))
            _keySoundPlayer.Play(path);
    }

    [RelayCommand]
    private void TestPlayWav()
    {
        if (SelectedWavItem is not null)
        {
            PlayWavSound(SelectedWavItem.Key);
        }
    }

    [RelayCommand]
    private void ToggleKeySound()
    {
        IsKeySoundEnabled = !IsKeySoundEnabled;
    }
}
