using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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

    // 콤보박스 인덱스(0=Single, 1=Couple, 2=Double). 파일의 #PLAYER 값보다 하나 작다.
    // 예전에는 기본값이 1 이라 새 차트가 Couple 로 저장됐다.
    [ObservableProperty] private int _player;
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
        Chart.Clear();
        WavList.Clear();
        SelectedWavItem = null;
        _selectedNotes.Clear();

        PullHeaderFromChart();
        MeasureCount = Chart.MeasureCount;
        CurrentFilePath = null;
        DocumentEncoding = new UTF8Encoding(false);

        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(SelectedNotes));
    }

    // Chart.Header 의 값을 화면에 묶인 프로퍼티로 옮긴다.
    //
    // #PLAYER 는 파일에서 1/2/3 인데 콤보박스는 0부터 시작하는 인덱스라 한 칸 어긋난다.
    // 그 변환을 여기 한 곳에서만 하고, 되돌리는 쪽은 PlayerHeaderValue 가 맡는다.
    private void PullHeaderFromChart()
    {
        Title = Chart.Header.Title;
        Artist = Chart.Header.Artist;
        Genre = Chart.Header.Genre;
        Level = Chart.Header.Level;
        Bpm = Chart.Header.Bpm;
        Player = Math.Clamp(Chart.Header.Player - 1, 0, 2);
        Rank = Math.Clamp(Chart.Header.Rank, 0, 3);
    }

    // 마지막 열기/저장이 실패한 이유. 성공하면 null.
    public string? LastErrorMessage { get; private set; }

    // 이 문서를 어떤 인코딩으로 읽었는지. 저장할 때 같은 인코딩으로 되돌려 쓴다.
    // 무조건 UTF-8 로 쓰면 CP949·CP932 차트가 다른 플레이어·에디터에서 깨진다.
    // 새로 만든 문서는 UTF-8(BOM 없음)로 시작한다.
    public Encoding DocumentEncoding { get; private set; } = new UTF8Encoding(false);

    // 실패하면 false. 예전에는 조용히 무시해서 사용자가 성공한 줄 알았다.
    public bool LoadBms(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            LastErrorMessage = "파일을 찾을 수 없습니다.";
            return false;
        }

        BmsParseResult parsed;

        try
        {
            // 먼저 다 읽고 나서 지운다. 파싱이 중간에 실패했을 때
            // 작업 중이던 내용까지 같이 날아가지 않도록 순서를 지킨다.
            parsed = BmsParser.Parse(filePath);
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"BMS 로드 실패: {ex.Message}");
            return false;
        }

        ResetDocumentState();

        // 노트·보존줄·마디길이·키음표 등 차트 안의 모든 컬렉션이 여기서 한꺼번에 옮겨진다.
        Chart.ReplaceContentWith(parsed.Chart);
        PullHeaderFromChart();
        MeasureCount = Chart.MeasureCount;

        // 읽어낸 인코딩을 기억해 두었다가 저장할 때 그대로 되돌려 쓴다.
        DocumentEncoding = parsed.Encoding;

        foreach (var wavItem in parsed.WavItems)
        {
            WavList.Add(wavItem);
        }

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

    // 조건 블록이 있는 차트는 저장을 거부하는 이유.
    // 사용자가 "왜 안 되는지"와 "그래서 뭘 하면 되는지"를 둘 다 알아야 한다.
    private const string ConditionalBlockSaveBlockedMessage =
        "이 차트에는 #RANDOM / #IF 같은 조건 블록이 들어 있습니다.\n\n" +
        "에디터가 아직 조건 블록을 해석하지 못합니다. 지금 저장하면 조건 줄이 파일 맨 위로 끌려 올라가\n" +
        "속이 빈 껍데기만 남고, 갈래별로 달랐던 노트가 하나로 합쳐져 항상 동시에 나오는 패턴이 됩니다.\n\n" +
        "원본을 지키려고 저장을 막았습니다. 편집이 필요하면 조건 블록이 없는 차트로 작업해 주세요.";

    // 저장은 됐지만 사용자가 알아야 할 것. 성공하면서도 채워질 수 있다.
    public string? LastWarningMessage { get; private set; }

    // 어떤 인코딩으로 쓸지 정한다.
    //
    // 기본은 읽어온 인코딩 그대로다(9번). 그런데 CP932 차트에 한글 제목을 넣는 식으로
    // 원본 인코딩이 담지 못하는 글자가 생기면, 그대로 쓸 경우 '?' 로 뭉개져 조용히 사라진다.
    // 인코딩을 지키려다 글자를 잃는 건 본말전도라, 그때만 UTF-8 로 물러나고 사실을 알린다.
    private Encoding ChooseSaveEncoding(string content)
    {
        LastWarningMessage = null;

        if (CanEncodeWithoutLoss(DocumentEncoding, content))
            return DocumentEncoding;

        LastWarningMessage =
            $"원본 인코딩({DocumentEncoding.WebName})으로 담을 수 없는 글자가 있어 UTF-8로 저장했습니다.\n" +
            "그대로 뒀다면 그 글자들이 '?' 로 바뀌어 사라졌을 것입니다.";

        return new UTF8Encoding(false);
    }

    private static bool CanEncodeWithoutLoss(Encoding encoding, string content)
    {
        // 원본 인코딩은 못 담는 글자를 조용히 '?' 로 바꾼다. 예외를 던지게 복제해서 확인한다.
        var strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;

        try
        {
            strict.GetBytes(content);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    // 실패하면 false. 호출한 쪽에서 LastErrorMessage 를 사용자에게 보여준다.
    public bool SaveBms(string filePath)
    {
        // 되돌릴 수 없는 손상이라 아예 쓰지 않는다. 저장을 막는 것만으로 피해가 0이 된다.
        if (Chart.HasConditionalBlocks)
        {
            LastErrorMessage = ConditionalBlockSaveBlockedMessage;
            return false;
        }

        try
        {
            var content = BmsWriter.Write(Chart, Title, Artist, Genre, Bpm, Player, Rank, Level, WavList, filePath);
            var encoding = ChooseSaveEncoding(content);

            // 원본을 바로 덮어쓰지 않는다. 쓰다 말면 되돌릴 방법이 없다. (SafeFileWriter 주석 참고)
            SafeFileWriter.WriteAllText(filePath, content, encoding);
            DocumentEncoding = encoding;
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

    private readonly KeySoundPlayer _keySoundPlayer = new();

    public ObservableCollection<BmsWavItem> WavList { get; } = new();
    [ObservableProperty] private BmsWavItem? _selectedWavItem;

    public IReadOnlyList<BmsNote> Notes => Chart.Notes.ToArray();

    private readonly HashSet<BmsNote> _selectedNotes = new();
    public IReadOnlyList<BmsNote> SelectedNotes => _selectedNotes.ToArray();

    // 지금 선택을 누가 만들었는지. 격자가 강조 색을 이 값으로 고른다.
    [ObservableProperty] private NoteSelectionSource _selectionSource = NoteSelectionSource.Grid;

    [RelayCommand]
    private void SelectNotes(NoteSelectionArgs args) => SetNoteSelection(args.Notes);

    // 격자 밖(검색/삭제/교체 창, 통계 창 등)에서 선택 집합을 통째로 교체한다.
    // source 는 격자가 강조 색을 고르는 데만 쓴다. 선택 자체의 의미는 같다.
    public void SetNoteSelection(IEnumerable<BmsNote> notes, NoteSelectionSource source = NoteSelectionSource.Grid)
    {
        _selectedNotes.Clear();
        foreach (var note in notes)
        {
            _selectedNotes.Add(note);
        }
        SelectionSource = source;
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

    // 지정한 노트들을 마디 단위로 옮긴 자리에 복제한다.
    //
    // 이미 노트가 있는 자리와 마디 범위 밖은 건너뛴다. 겹쳐 두면 저장할 때
    // BmsWriter 가 같은 슬롯을 덮어써서 한쪽이 조용히 사라지므로 아예 만들지 않는다.
    // 만든 노트를 선택 상태로 두어 결과를 바로 확인하고 이어서 손볼 수 있게 한다.
    public NoteCopyResult CopyNotesByMeasureOffset(IReadOnlyList<BmsNote> notes, int measureOffset)
    {
        if (notes.Count == 0 || measureOffset == 0)
            return new NoteCopyResult(0, 0, 0);

        // 원본뿐 아니라 이번에 새로 만든 것까지 함께 봐야 복제본끼리 겹치는 것도 막힌다.
        var occupied = new HashSet<(string Lane, int Measure, long Slot)>();
        foreach (var existing in Chart.Notes)
            occupied.Add(ToSlotKey(existing.LaneId, existing.Measure, existing.Position));

        var created = new List<BmsNote>();
        var blocked = 0;
        var outOfRange = 0;

        foreach (var note in notes)
        {
            var targetMeasure = note.Measure + measureOffset;
            if (targetMeasure < 0 || targetMeasure >= MeasureCount)
            {
                outOfRange++;
                continue;
            }

            if (!occupied.Add(ToSlotKey(note.LaneId, targetMeasure, note.Position)))
            {
                blocked++;
                continue;
            }

            created.Add(new BmsNote
            {
                Measure = targetMeasure,
                LaneId = note.LaneId,
                Position = note.Position,
                WavKey = note.WavKey,
                Type = note.Type,
            });
        }

        if (created.Count > 0)
        {
            Chart.Notes.AddRange(created);
            SetNoteSelection(created, NoteSelectionSource.Search);
            OnPropertyChanged(nameof(Notes));
        }

        return new NoteCopyResult(created.Count, blocked, outOfRange);
    }

    // 마디 내 위치를 0.0001 단위로 끊어 슬롯 키를 만든다.
    // BMS 위치는 최대 1/1920(≈0.00052) 간격이라 서로 다른 자리가 같은 칸에 들어가지 않고,
    // double 오차로 미세하게 다른 같은 자리는 하나로 묶인다.
    private static (string, int, long) ToSlotKey(string laneId, int measure, double position) =>
        (laneId, measure, (long)Math.Round(position * 10000));

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
        if (Chart.WavTable.TryGetValue(key, out var path))
            _keySoundPlayer.Play(path);
    }

    private const string Base36Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // 이 차트가 쓰는 키 자릿수. 키음 테이블과 노트가 가리키는 번호를 함께 본다.
    // BmsWriter.ComputeKeyWidth 와 같은 규칙이어야 한다.
    private int GetWavKeyWidth()
    {
        var width = 2;

        foreach (var key in Chart.WavTable.Keys)
            width = Math.Max(width, key.Length);

        foreach (var note in Chart.Notes)
            width = Math.Max(width, note.WavKey.Length);

        return Math.Clamp(width, 2, 3);
    }

    // 비어 있는 다음 키음 번호. **차트가 쓰는 자릿수에 맞춰서** 만든다.
    //
    // 예전에는 항상 2자리만 만들었다. 기존 키가 전부 3자리인 차트에서는 2자리 "01" 이
    // "비어 있는" 키로 보이는데, 저장할 때 BmsWriter 가 폭을 3으로 맞추면서 "001" 이 되어
    // 기존 001번 키음을 조용히 덮어썼다. 001을 쓰던 노트가 전부 다른 소리로 바뀌었다.
    private string GetNextWavKey()
    {
        var width = GetWavKeyWidth();
        var limit = 1;
        for (var i = 0; i < width; i++)
            limit *= 36;

        // 00 / 000 은 "빈 자리"를 뜻하므로 1부터 시작한다.
        for (var value = 1; value < limit; value++)
        {
            var key = ToBase36(value, width);
            if (!Chart.WavTable.ContainsKey(key))
                return key;
        }

        throw new InvalidOperationException("WAV 키 한도를 초과했습니다.");
    }

    private static string ToBase36(int value, int width)
    {
        var chars = new char[width];
        for (var i = width - 1; i >= 0; i--)
        {
            chars[i] = Base36Digits[value % 36];
            value /= 36;
        }
        return new string(chars);
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
