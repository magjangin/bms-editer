using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using bms_editer.Models;
using bms_editer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bms_editer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public BmsChart Chart { get; } = new();
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _gridSyncFlashTimer;
    private OggAudioPlayer? _audioPlayer;
    private DateTimeOffset _playbackStartedAt;
    private double _playbackStartSeconds;
    private BmsNote[] _playbackNotes = Array.Empty<BmsNote>();

    // OGG 실험용 로딩 상태
    [ObservableProperty] private string? _oggFileName;
    [ObservableProperty] private IReadOnlyList<float>? _oggPeaks;
    [ObservableProperty] private IReadOnlyList<float>? _oggOnsets;
    [ObservableProperty] private double _oggDurationSeconds;
    [ObservableProperty] private double _playbackPositionSeconds;
    [ObservableProperty] private bool _isPlaybackCursorVisible;
    [ObservableProperty] private bool _isGridSyncFlashVisible;

    // HEADER 패널
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private string _genre = string.Empty;
    [ObservableProperty] private double _bpm = 120.0;
    [ObservableProperty] private int _player = 1;
    [ObservableProperty] private int _rank = 2;
    [ObservableProperty] private string _level = string.Empty;

    // 격자 패널
    [ObservableProperty] private int _measureCount = 32;
    [ObservableProperty] private int _rowHeight = 16;
    [ObservableProperty] private int _beatSplit = 16;
    [ObservableProperty] private int _gridMeasure = 4;
    [ObservableProperty] private double _verticalZoom = 8.0;
    [ObservableProperty] private double _horizontalZoom = 1.50;
    [ObservableProperty] private double _waveformHorizontalZoom = 1.00;
    [ObservableProperty] private bool _followPlaybackCursor = true;
    [ObservableProperty] private bool _snapToGrid = true;
    [ObservableProperty] private bool _lockVerticalPosition;
    [ObservableProperty] private bool _isHorizontalView;
    [ObservableProperty] private bool _isEditMode;

    public MainWindowViewModel()
    {
        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playbackTimer.Tick += (_, _) => UpdatePlaybackPosition();

        _gridSyncFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _gridSyncFlashTimer.Tick += (_, _) =>
        {
            _gridSyncFlashTimer.Stop();
            IsGridSyncFlashVisible = false;
        };
    }

    public string GridDisplay => $"{BeatSplit}/{Math.Max(1, GridMeasure)}";

    partial void OnBeatSplitChanged(int value)
    {
        OnPropertyChanged(nameof(GridDisplay));
    }

    partial void OnGridMeasureChanged(int value)
    {
        OnPropertyChanged(nameof(GridDisplay));
    }

    public void LoadOgg(string filePath)
    {
        try
        {
            var waveform = OggPeakLoader.Load(filePath);
            var audioPlayer = new OggAudioPlayer(filePath);

            StopPlayback(resetCursor: true);
            _audioPlayer?.Dispose();
            _audioPlayer = audioPlayer;
            OggDurationSeconds = waveform.DurationSeconds;
            OggPeaks = waveform.Peaks;
            OggOnsets = waveform.Onsets;
            OggFileName = System.IO.Path.GetFileName(filePath);
            UpdateMeasureCountFromAudio();
        }
        catch (Exception ex)
        {
            _audioPlayer?.Dispose();
            _audioPlayer = null;
            OggDurationSeconds = 0;
            OggPeaks = null;
            OggOnsets = null;
            IsPlaybackCursorVisible = false;
            OggFileName = $"로드 실패: {ex.Message}";
        }
    }

    partial void OnBpmChanged(double value)
    {
        UpdateMeasureCountFromAudio();
        FlashGridSync();
    }

    private void FlashGridSync()
    {
        if (OggDurationSeconds <= 0)
            return;

        IsGridSyncFlashVisible = true;
        _gridSyncFlashTimer.Stop();
        _gridSyncFlashTimer.Start();
    }

    private void UpdateMeasureCountFromAudio()
    {
        if (OggDurationSeconds <= 0)
            return;

        // 곡 길이(BPM 기준 4/4 마디 수)에 맞춰 그리드를 늘려서
        // BPM 변경이 즉시 파형/그리드 세로 길이에 반영되게 한다.
        var totalBeats = OggDurationSeconds * (Bpm / 60.0);
        MeasureCount = Math.Max(1, (int)Math.Ceiling(totalBeats / 4.0));
        Chart.MeasureCount = MeasureCount;
    }

    public void ScrubToRatio(double ratio)
    {
        if (_audioPlayer is null || OggDurationSeconds <= 0)
            return;

        PlayFrom(Math.Clamp(ratio, 0, 1) * OggDurationSeconds);
    }

    public void StopPlaybackAtCurrentPosition() => StopPlayback(resetCursor: false);

    private double _lastPlaybackPositionSeconds;

    private void PlayFrom(double seconds)
    {
        if (_audioPlayer is null)
            return;

        var startSeconds = Math.Clamp(seconds, 0, OggDurationSeconds);
        _playbackNotes = Chart.Notes
            .OrderBy(n => n.Measure + n.Position)
            .ToArray();

        _audioPlayer.Play(startSeconds);
        _playbackStartSeconds = startSeconds;
        _playbackStartedAt = DateTimeOffset.UtcNow;
        PlaybackPositionSeconds = startSeconds;
        _lastPlaybackPositionSeconds = startSeconds;
        IsPlaybackCursorVisible = true;
        _playbackTimer.Start();
    }

    private void UpdatePlaybackPosition()
    {
        if (_audioPlayer is null)
            return;

        var currentSec = _playbackStartSeconds + (DateTimeOffset.UtcNow - _playbackStartedAt).TotalSeconds;
        PlaybackPositionSeconds = currentSec;

        PlayNotesInTimeRange(_lastPlaybackPositionSeconds, currentSec);
        _lastPlaybackPositionSeconds = currentSec;

        if (PlaybackPositionSeconds >= OggDurationSeconds)
            StopPlayback(resetCursor: false);
    }

    private void PlayNotesInTimeRange(double start, double end)
    {
        if (Bpm <= 0 || _playbackNotes.Length == 0) return;

        var secondsPerMeasure = 240.0 / Bpm;

        // Binary search to find the first note that is >= start
        var low = 0;
        var high = _playbackNotes.Length - 1;
        var startIndex = _playbackNotes.Length;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var noteSec = (_playbackNotes[mid].Measure + _playbackNotes[mid].Position) * secondsPerMeasure;

            if (noteSec >= start)
            {
                startIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        for (var i = startIndex; i < _playbackNotes.Length; i++)
        {
            var note = _playbackNotes[i];
            var noteSec = (note.Measure + note.Position) * secondsPerMeasure;

            if (noteSec >= end)
                break;

            PlayWavSound(note.WavKey);
        }
    }

    private void StopPlayback(bool resetCursor)
    {
        _playbackTimer.Stop();
        _audioPlayer?.Stop();
        _playbackNotes = Array.Empty<BmsNote>();

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

    [RelayCommand]
    private void NewFile()
    {
        // TODO: Chart 상태 초기화
    }

    public void LoadBms(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return;
        try
        {
            // 기존 상태 완전 초기화
            Chart.Notes.Clear();
            Chart.WavTable.Clear();
            WavList.Clear();
            SelectedWavItem = null;

            // BMS 파싱 실행
            var parsedChart = BmsParser.Parse(filePath, out var parsedBpm, out var calculatedMeasureCount, out var parsedWavItems);

            // 데이터 동기화
            Title = parsedChart.Header.Title;
            Artist = parsedChart.Header.Artist;
            Bpm = parsedBpm;
            MeasureCount = calculatedMeasureCount;

            foreach (var wavItem in parsedWavItems)
            {
                Chart.WavTable[wavItem.Key] = wavItem.FilePath;
                WavList.Add(wavItem);
            }

            foreach (var note in parsedChart.Notes)
            {
                Chart.Notes.Add(note);
            }

            if (WavList.Count > 0)
            {
                SelectedWavItem = WavList[0];
            }

            // UI 렌더링 강제 업데이트 유도
            OnPropertyChanged(nameof(Notes));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BMS 로드 실패: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveFile()
    {
        // TODO: Chart -> .bms 텍스트 포맷 직렬화
    }

    [RelayCommand]
    private void Play()
    {
        PlayFrom(IsPlaybackCursorVisible ? PlaybackPositionSeconds : 0);
    }

    [RelayCommand]
    private void Stop()
    {
        StopPlayback(resetCursor: false);
    }

    // WAV 키음 관리 및 재생 믹서 (Win32 PInvoke)
    [System.Runtime.InteropServices.DllImport("winmm.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_FILENAME = 0x00020000;

    public ObservableCollection<BmsWavItem> WavList { get; } = new();
    [ObservableProperty] private BmsWavItem? _selectedWavItem;

    public IReadOnlyList<BmsNote> Notes => Chart.Notes.ToArray();

    public void PlayWavSound(string key)
    {
        if (Chart.WavTable.TryGetValue(key, out var path) && System.IO.File.Exists(path))
        {
            try
            {
                PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME);
            }
            catch
            {
                // 음원 재생 실패 시 무시
            }
        }
    }

    private string GetNextWavKey()
    {
        var chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        for (var i = 0; i < chars.Length; i++)
        {
            for (var j = 0; j < chars.Length; j++)
            {
                if (i == 0 && j == 0) continue;
                var key = $"{chars[i]}{chars[j]}";
                if (!Chart.WavTable.ContainsKey(key))
                    return key;
            }
        }
        throw new InvalidOperationException("WAV 키 한도를 초과했습니다.");
    }

    public void AddWav(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return;
        try
        {
            var key = GetNextWavKey();
            Chart.WavTable[key] = filePath;
            var item = new BmsWavItem { Key = key, FilePath = filePath };
            WavList.Add(item);
            SelectedWavItem = item;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WAV 추가 실패: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RemoveWav()
    {
        if (SelectedWavItem is null) return;
        var key = SelectedWavItem.Key;
        Chart.WavTable.Remove(key);
        
        WavList.Remove(SelectedWavItem);
        SelectedWavItem = null;
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
    private void PlaceNote(NotePlacementArgs args)
    {
        if (SelectedWavItem is null) return;

        var wavKey = SelectedWavItem.Key;

        var existing = Chart.Notes.Find(n => 
            n.Measure == args.Measure && 
            n.LaneId == args.LaneId && 
            Math.Abs(n.Position - args.Position) < 0.0001);

        if (existing is not null)
        {
            existing.WavKey = wavKey;
        }
        else
        {
            var note = new BmsNote
            {
                Measure = args.Measure,
                LaneId = args.LaneId,
                Position = args.Position,
                WavKey = wavKey,
                Type = NoteType.Normal
            };
            Chart.Notes.Add(note);
        }

        PlayWavSound(wavKey);
        OnPropertyChanged(nameof(Notes));
    }

    [RelayCommand]
    private void RemoveNote(NotePlacementArgs args)
    {
        var existing = Chart.Notes.Find(n => 
            n.Measure == args.Measure && 
            n.LaneId == args.LaneId && 
            Math.Abs(n.Position - args.Position) < 0.005);

        if (existing is not null)
        {
            Chart.Notes.Remove(existing);
            OnPropertyChanged(nameof(Notes));
        }
    }
}
