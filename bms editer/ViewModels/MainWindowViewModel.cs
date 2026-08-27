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

    // 마디 000의 첫 박이 오디오의 몇 초 지점인지.
    // 이 값이 없으면 앞 무음을 BPM 으로 흡수할 수밖에 없어서 곡 뒤로 갈수록 격자가 벌어진다.
    [ObservableProperty] private double _startOffsetSeconds;

    // 싱크 보정용 기준점 두 개. 재생 위치를 담아두고 BPM/오프셋을 역산한다.
    [ObservableProperty] private int _syncMeasureA;
    [ObservableProperty] private double _syncSecondsA;
    [ObservableProperty] private int _syncMeasureB = 32;
    [ObservableProperty] private double _syncSecondsB;
    [ObservableProperty] private string _syncStatus = "재생 위치를 기준점에 담고 보정하세요.";

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

    partial void OnStartOffsetSecondsChanged(double value)
    {
        UpdateMeasureCountFromAudio();
        FlashGridSync();
    }

    // 현재 재생 위치를 기준점에 담는다. 파형에서 가운데 버튼으로 문질러 위치를 잡은 뒤 누른다.
    [RelayCommand]
    private void CaptureSyncA() => SyncSecondsA = PlaybackPositionSeconds;

    [RelayCommand]
    private void CaptureSyncB() => SyncSecondsB = PlaybackPositionSeconds;

    // BPM 은 그대로 두고 오프셋만 맞춘다. BPM 을 알고 있는(고정해서 쓰는) 경우의 기본 동작.
    [RelayCommand]
    private void AlignOffsetToSyncA()
    {
        if (Bpm <= 0)
        {
            SyncStatus = "BPM 이 올바르지 않습니다.";
            return;
        }

        StartOffsetSeconds = SyncSecondsA - (SyncMeasureA * 240.0 / Bpm);
        SyncStatus = $"BPM {Bpm:0.0000} 유지, 오프셋 {StartOffsetSeconds:0.0000}s 적용 "
            + $"(마디 {SyncMeasureA:D3} = {SyncSecondsA:0.0000}s)";
    }

    // 두 기준점으로 BPM 과 오프셋을 동시에 역산한다.
    //   BPM    = 240 × (마디B - 마디A) / (초B - 초A)
    //   오프셋 = 초A - 마디A × 240 / BPM
    // 두 점을 멀리 잡을수록 정확해진다. 결과 정확도는 찍은 정밀도와 거의 같다.
    [RelayCommand]
    private void ApplySyncCalibration()
    {
        var measureSpan = SyncMeasureB - SyncMeasureA;
        var secondSpan = SyncSecondsB - SyncSecondsA;

        if (measureSpan <= 0 || secondSpan <= 0)
        {
            SyncStatus = "기준점 B 가 A 보다 뒤(마디·시간 모두)여야 합니다.";
            return;
        }

        var bpm = 240.0 * measureSpan / secondSpan;
        if (bpm is < 1 or > 999 or double.NaN)
        {
            SyncStatus = $"계산된 BPM {bpm:0.0000} 이 범위를 벗어납니다. 기준점을 확인하세요.";
            return;
        }

        Bpm = bpm;
        StartOffsetSeconds = SyncSecondsA - (SyncMeasureA * 240.0 / bpm);
        SyncStatus = $"BPM {bpm:0.0000}, 오프셋 {StartOffsetSeconds:0.0000}s 적용 "
            + $"({measureSpan}마디 / {secondSpan:0.0000}s 기준)";
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
        // 마디 000이 오프셋 지점에서 시작하므로 남은 길이만큼만 마디를 채운다.
        var totalBeats = (OggDurationSeconds - StartOffsetSeconds) * (Bpm / 60.0);
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

        // 장치가 실제로 재생한 위치를 기준으로 삼는다. 벽시계를 쓰면 출력 지연만큼
        // 커서가 처음부터 앞서 나가고, 장치 클럭과도 시간이 갈수록 벌어져서
        // 화면은 맞아 보이는데 소리와는 안 맞는 상태가 된다.
        // 장치가 위치 조회를 지원하지 않을 때만 예전처럼 벽시계로 되돌아간다.
        var playedSeconds = _audioPlayer.GetPlayedSeconds();
        var currentSec = playedSeconds is { } played
            ? _playbackStartSeconds + played
            : _playbackStartSeconds + (DateTimeOffset.UtcNow - _playbackStartedAt).TotalSeconds;

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
            var noteSec = StartOffsetSeconds + ((_playbackNotes[mid].Measure + _playbackNotes[mid].Position) * secondsPerMeasure);

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
            var noteSec = StartOffsetSeconds + ((note.Measure + note.Position) * secondsPerMeasure);

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

    // 작업 내용을 버리는 동작(새로 만들기·열기) 앞에서 확인을 받는다.
    // 버릴 내용이 없거나 콜백이 없으면 그냥 진행한다.
    public async Task<bool> ConfirmDiscardIfNeededAsync(string message)
    {
        if (!HasDocumentContent || ConfirmDiscardAsync is not { } confirm)
            return true;

        return await confirm(message);
    }

    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (!await ConfirmDiscardIfNeededAsync("현재 작업 중인 내용이 모두 사라집니다.\n새로 만들까요?"))
            return;

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
        Chart.PreservedLines.Clear();
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

        StartOffsetSeconds = 0;
        SyncMeasureA = 0;
        SyncSecondsA = 0;
        SyncMeasureB = 32;
        SyncSecondsB = 0;
        SyncStatus = "재생 위치를 기준점에 담고 보정하세요.";

        MeasureCount = 32;
        Chart.MeasureCount = 32;
        CurrentFilePath = null;

        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(SelectedNotes));
    }

    // 마지막 열기/저장이 실패한 이유. 성공하면 null.
    public string? LastErrorMessage { get; private set; }

    // 실패하면 false. 예전에는 조용히 무시해서 사용자가 성공한 줄 알았다.
    public bool LoadBms(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            LastErrorMessage = "파일을 찾을 수 없습니다.";
            return false;
        }

        BmsChart parsedChart;
        double parsedBpm;
        int calculatedMeasureCount;
        List<BmsWavItem> parsedWavItems;

        try
        {
            // 먼저 다 읽고 나서 지운다. 파싱이 중간에 실패했을 때
            // 작업 중이던 내용까지 같이 날아가지 않도록 순서를 지킨다.
            parsedChart = BmsParser.Parse(filePath, out parsedBpm, out calculatedMeasureCount, out parsedWavItems);
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"BMS 로드 실패: {ex.Message}");
            return false;
        }

        ResetDocumentState();

        Title = parsedChart.Header.Title;
        Artist = parsedChart.Header.Artist;
        Genre = parsedChart.Header.Genre;
        Level = parsedChart.Header.Level;

        // #PLAYER 는 파일에서 1/2/3, 콤보박스는 0부터 시작하는 인덱스다.
        Player = Math.Clamp(parsedChart.Header.Player - 1, 0, 2);
        Rank = Math.Clamp(parsedChart.Header.Rank, 0, 3);

        Bpm = parsedBpm;

        // MeasureCount 를 덮어쓰기 전에 오프셋을 넣는다.
        // (오프셋이 바뀌면 UpdateMeasureCountFromAudio 가 다시 계산한다)
        StartOffsetSeconds = parsedChart.Header.StartOffsetSeconds;

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

        // 편집 대상이 아닌 채널·헤더는 원문 그대로 들고 있다가 저장할 때 되돌려 놓는다.
        Chart.PreservedLines.AddRange(parsedChart.PreservedLines);

        if (WavList.Count > 0)
        {
            SelectedWavItem = WavList[0];
        }

        CurrentFilePath = filePath;
        LastErrorMessage = null;

        // UI 렌더링 강제 업데이트 유도
        OnPropertyChanged(nameof(Notes));
        return true;
    }

    // 실패하면 false. 호출한 쪽에서 LastErrorMessage 를 사용자에게 보여준다.
    public bool SaveBms(string filePath)
    {
        try
        {
            // 오프셋은 인자로 넘기지 않고 헤더에 실어 보낸다(라이터 인자가 이미 많다).
            Chart.Header.StartOffsetSeconds = StartOffsetSeconds;

            var content = BmsWriter.Write(Chart, Title, Artist, Genre, Bpm, Player, Rank, Level, WavList, filePath);
            System.IO.File.WriteAllText(filePath, content, new System.Text.UTF8Encoding(false));
            CurrentFilePath = filePath;
            LastErrorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"BMS 저장 실패: {ex.Message}");
            return false;
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
