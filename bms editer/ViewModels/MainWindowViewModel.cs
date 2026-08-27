using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using bms_editer.Models;
using bms_editer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bms_editer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public BmsChart Chart { get; } = new();
    public string? CurrentFilePath { get; private set; }
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
    [ObservableProperty] private bool _isPlaying;

    // 오른쪽 패널 비디오 프리뷰
    [ObservableProperty] private string? _videoFilePath;
    [ObservableProperty] private string? _videoFileName;

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
    [ObservableProperty] private bool _isCircleNoteShape;

    // 키음 팔레트 창의 보기 모드(0 목록, 1 보통, 2 큰, 3 아주 큰 아이콘).
    // 창을 닫았다 다시 열어도 고른 보기가 남도록 메인 뷰모델이 들고 있는다.
    [ObservableProperty] private int _wavPaletteViewModeIndex = 2;

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

    public void LoadVideo(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
            return;

        VideoFilePath = filePath;
        VideoFileName = System.IO.Path.GetFileName(filePath);
    }

    public void ClearVideo()
    {
        VideoFilePath = null;
        VideoFileName = null;
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
        IsPlaying = true;
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

    // 새로 만들기처럼 작업 내용을 버리는 동작 전에 사용자 확인을 받는 콜백. (View가 주입)
    public Func<string, Task<bool>>? ConfirmDiscardAsync { get; set; }

    // 되돌릴 수 없게 사라질 편집 내용이 남아 있는지 여부.
    private bool HasDocumentContent =>
        Chart.Notes.Count > 0
        || WavList.Count > 0
        || CurrentFilePath is not null
        || !string.IsNullOrWhiteSpace(Title)
        || !string.IsNullOrWhiteSpace(Artist);

    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (HasDocumentContent && ConfirmDiscardAsync is { } confirm)
        {
            var proceed = await confirm("현재 작업 중인 내용이 모두 사라집니다.\n새로 만들까요?");
            if (!proceed)
                return;
        }

        // 재생 중이던 배경 음원을 먼저 정리한다.
        StopPlayback(resetCursor: true);
        _audioPlayer?.Dispose();
        _audioPlayer = null;
        OggFileName = null;
        OggPeaks = null;
        OggOnsets = null;
        OggDurationSeconds = 0;
        ClearVideo();

        // 곡 길이가 0이 된 뒤에 초기화해야 UpdateMeasureCountFromAudio가 덮어쓰지 않는다.
        ResetDocumentState();

        // 새 차트는 명세서 기준값인 16분할 그리드로 시작한다.
        BeatSplit = 16;
        GridMeasure = 4;
    }

    // 차트/키음/헤더 등 문서 상태를 초기 상태로 되돌린다. (배경 음원·비디오는 대상 아님)
    private void ResetDocumentState()
    {
        Chart.Notes.Clear();
        Chart.WavTable.Clear();
        Chart.BmpTable.Clear();
        Chart.MeasureLengths.Clear();
        WavList.Clear();
        SelectedWavItem = null;
        _selectedNotes.Clear();

        Title = string.Empty;
        Artist = string.Empty;
        Genre = string.Empty;
        Level = string.Empty;
        Bpm = 120.0;
        Player = 1;
        Rank = 2;

        MeasureCount = 32;
        Chart.MeasureCount = 32;
        CurrentFilePath = null;

        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(SelectedNotes));
    }

    public void LoadBms(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return;
        try
        {
            // 기존 상태 완전 초기화
            ResetDocumentState();

            // BMS 파싱 실행
            var parsedChart = BmsParser.Parse(filePath, out var parsedBpm, out var calculatedMeasureCount, out var parsedWavItems);

            // 데이터 동기화
            Title = parsedChart.Header.Title;
            Artist = parsedChart.Header.Artist;
            Bpm = parsedBpm;
            MeasureCount = calculatedMeasureCount;
            Chart.MeasureCount = calculatedMeasureCount;

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

            CurrentFilePath = filePath;

            // UI 렌더링 강제 업데이트 유도
            OnPropertyChanged(nameof(Notes));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BMS 로드 실패: {ex.Message}");
        }
    }

    public void SaveBms(string filePath)
    {
        try
        {
            var content = BmsWriter.Write(Chart, Title, Artist, Genre, Bpm, Player, Rank, Level, WavList, filePath);
            System.IO.File.WriteAllText(filePath, content, new System.Text.UTF8Encoding(false));
            CurrentFilePath = filePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BMS 저장 실패: {ex.Message}");
        }
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
    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_FILENAME = 0x00020000;

    public ObservableCollection<BmsWavItem> WavList { get; } = new();
    [ObservableProperty] private BmsWavItem? _selectedWavItem;

    public IReadOnlyList<BmsNote> Notes => Chart.Notes.ToArray();

    private readonly HashSet<BmsNote> _selectedNotes = new();
    public IReadOnlyList<BmsNote> SelectedNotes => _selectedNotes.ToArray();

    [RelayCommand]
    private void SelectNotes(NoteSelectionArgs args) => SetNoteSelection(args.Notes);

    // 격자 밖(검색/삭제/교체 창 등)에서 선택 집합을 통째로 교체한다.
    public void SetNoteSelection(IEnumerable<BmsNote> notes)
    {
        _selectedNotes.Clear();
        foreach (var note in notes)
        {
            _selectedNotes.Add(note);
        }
        OnPropertyChanged(nameof(SelectedNotes));
    }

    public void ClearNoteSelection() => SetNoteSelection(Array.Empty<BmsNote>());

    // 선택 여부와 관계없이 지정한 노트들을 지우고, 실제로 지워진 개수를 돌려준다.
    public int DeleteNotes(IReadOnlyList<BmsNote> notes)
    {
        if (notes.Count == 0) return 0;

        var removed = 0;
        foreach (var note in notes)
        {
            if (!Chart.Notes.Remove(note)) continue;

            _selectedNotes.Remove(note);
            removed++;
        }

        if (removed > 0)
        {
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(SelectedNotes));
        }

        return removed;
    }

    // 지정한 노트들의 키음 번호를 한꺼번에 바꾸고, 실제로 바뀐 개수를 돌려준다.
    public int ReplaceWavKey(IReadOnlyList<BmsNote> notes, string wavKey)
    {
        if (notes.Count == 0 || string.IsNullOrWhiteSpace(wavKey)) return 0;

        var changed = 0;
        foreach (var note in notes)
        {
            if (note.WavKey == wavKey) continue;

            note.WavKey = wavKey;
            changed++;
        }

        if (changed > 0)
            OnPropertyChanged(nameof(Notes));

        return changed;
    }

    [RelayCommand]
    private void DeleteSelectedNotes()
    {
        if (_selectedNotes.Count == 0) return;

        foreach (var note in _selectedNotes)
        {
            Chart.Notes.Remove(note);
        }
        _selectedNotes.Clear();
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(SelectedNotes));
    }

    [RelayCommand]
    private void MoveSelectedNotes(NoteMoveDirection direction)
    {
        if (_selectedNotes.Count == 0) return;

        var split = Math.Max(1, BeatSplit);
        var lanes = Chart.Lanes;

        foreach (var note in _selectedNotes)
        {
            switch (direction)
            {
                case NoteMoveDirection.TimeForward:
                    MoveNoteInTime(note, 1, split);
                    break;
                case NoteMoveDirection.TimeBackward:
                    MoveNoteInTime(note, -1, split);
                    break;
                case NoteMoveDirection.LanePrevious:
                    MoveNoteInLane(note, -1, lanes);
                    break;
                case NoteMoveDirection.LaneNext:
                    MoveNoteInLane(note, 1, lanes);
                    break;
            }
        }

        OnPropertyChanged(nameof(Notes));
    }

    private void MoveNoteInTime(BmsNote note, int steps, int split)
    {
        var totalStepIndex = (int)Math.Round((note.Measure + note.Position) * split) + steps;
        if (totalStepIndex < 0) return;

        var newMeasure = totalStepIndex / split;
        var newPosition = (double)(totalStepIndex % split) / split;
        if (newMeasure < 0 || newMeasure >= MeasureCount) return;
        if (IsSlotOccupied(note.LaneId, newMeasure, newPosition, note)) return;

        note.Measure = newMeasure;
        note.Position = newPosition;
    }

    private void MoveNoteInLane(BmsNote note, int steps, IReadOnlyList<LaneDefinition> lanes)
    {
        var currentIndex = -1;
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].Id == note.LaneId)
            {
                currentIndex = i;
                break;
            }
        }
        if (currentIndex == -1) return;

        var newIndex = currentIndex + steps;
        if (newIndex < 0 || newIndex >= lanes.Count) return;

        var newLaneId = lanes[newIndex].Id;
        if (IsSlotOccupied(newLaneId, note.Measure, note.Position, note)) return;

        note.LaneId = newLaneId;
    }

    private bool IsSlotOccupied(string laneId, int measure, double position, BmsNote excluding)
    {
        return Chart.Notes.Any(n =>
            n != excluding &&
            !_selectedNotes.Contains(n) &&
            n.LaneId == laneId &&
            n.Measure == measure &&
            Math.Abs(n.Position - position) < 0.0001);
    }

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
