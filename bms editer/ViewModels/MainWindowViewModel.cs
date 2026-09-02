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

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            if (_currentFilePath == value)
                return;

            _currentFilePath = value;
            OnPropertyChanged(nameof(CurrentFilePath));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private string? _currentFilePath;
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

        // 키음을 넣거나 뺀 것도 저장해야 할 변경이다.
        WavList.CollectionChanged += (_, _) => MarkDirty();
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

    // 지금 음원을 읽는 중인지. 화면에 로딩 표시를 띄우는 데 쓴다.
    [ObservableProperty] private bool _isLoadingOgg;

    // 실패하면 false. 실패해도 이미 물려 있던 음원은 그대로 둔다.
    //
    // 예전에는 catch 가 _audioPlayer(= 기존 플레이어)를 Dispose 하고 파형·길이를 0/null 로
    // 밀어버렸다. 새로 고른 파일이 깨졌을 뿐인데 멀쩡하던 파형과 재생이 같이 사라졌다.
    public async Task<bool> LoadOggAsync(string filePath)
    {
        OggWaveform waveform;
        OggAudioPlayer audioPlayer;

        IsLoadingOgg = true;
        try
        {
            // 디코딩은 5분짜리 곡이면 수 초가 걸린다. UI 스레드에서 하면 그동안 창이 얼어붙는다.
            // 새 음원을 끝까지 다 읽고 나서야 기존 것을 건드린다.
            var decoded = await Task.Run(() => OggDecoder.Decode(filePath));

            waveform = OggPeakLoader.Load(decoded);
            audioPlayer = new OggAudioPlayer(decoded);
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"OGG 로드 실패: {ex.Message}");
            return false;
        }
        finally
        {
            IsLoadingOgg = false;
        }

        StopPlayback(resetCursor: true);
        _audioPlayer?.Dispose();
        _audioPlayer = audioPlayer;
        OggDurationSeconds = waveform.DurationSeconds;
        OggPeaks = waveform.Peaks;
        OggOnsets = waveform.Onsets;
        OggFileName = System.IO.Path.GetFileName(filePath);
        UpdateMeasureCountFromAudio();
        LastErrorMessage = null;
        return true;
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
        MarkDirty();
        InvalidateTimeline();
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

    // 마디 위치 <-> 시각 변환. 격자·노트·재생이 모두 이걸 통해 계산한다.
    //
    // BPM 변화(#xxx03/#xxx08)와 마디 길이(#xxx02)를 여기서만 다룬다.
    // 예전에는 세 곳이 각자 240/BPM 을 써서, 그런 차트는 화면과 소리가 어긋났다.
    private ChartTimeline? _timeline;

    public ChartTimeline Timeline => _timeline ??= ChartTimeline.FromChart(Chart, Bpm);

    private void InvalidateTimeline()
    {
        _timeline = null;
        OnPropertyChanged(nameof(Timeline));
    }

    // 오디오 길이가 요구하는 마디 수. 음원이 없으면 0.
    private int GetAudioMeasureCount()
    {
        if (OggDurationSeconds <= 0 || Bpm <= 0)
            return 0;

        var totalBeats = OggDurationSeconds * (Bpm / 60.0);
        return Math.Max(1, (int)Math.Ceiling(totalBeats / 4.0));
    }

    // 차트 안의 노트와 보존줄이 요구하는 마디 수.
    private int GetChartMeasureCount()
    {
        var maxMeasure = 0;

        foreach (var note in Chart.Notes)
            maxMeasure = Math.Max(maxMeasure, note.Measure);

        foreach (var raw in Chart.PreservedLines)
        {
            if (raw.IsData)
                maxMeasure = Math.Max(maxMeasure, raw.Measure);
        }

        return maxMeasure + 1;
    }

    // 이 문서가 가질 수 있는 가장 작은 마디 수. 오디오와 차트 중 큰 쪽을 따른다.
    public int MinimumMeasureCount =>
        Math.Max(MinimumMeasureFloor, Math.Max(GetAudioMeasureCount(), GetChartMeasureCount()));

    private const int MinimumMeasureFloor = 32;

    // 오디오가 요구하는 만큼은 반드시 확보하되, **줄이지는 않는다.**
    //
    // 예전에는 오디오 길이 기준으로 무조건 덮어썼다. 200마디 차트를 열고 그보다 짧은
    // OGG를 얹으면 MeasureCount 가 75로 줄어, 75마디 이후 노트는 화면에는 보이는데
    // 배치·이동·복사가 전부 거부됐다. BPM 을 만질 때마다 다시 계산돼서
    // BPM 조율 중에도 튀어나왔다.
    private void UpdateMeasureCountFromAudio()
    {
        var required = MinimumMeasureCount;
        if (MeasureCount < required)
            MeasureCount = required;
        else
            Chart.MeasureCount = MeasureCount;

        OnPropertyChanged(nameof(MinimumMeasureCount));
    }

    // 사용자가 마디 수를 직접 줄이더라도, 이미 노트가 있는 마디까지 잠기면 안 된다.
    partial void OnMeasureCountChanged(int value)
    {
        var floor = MinimumMeasureCount;
        if (value < floor)
        {
            MeasureCount = floor;
            return;
        }

        Chart.MeasureCount = value;
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

    private double _lastPlaybackPositionSeconds;

    // 시각 순으로 정렬한 노트. 재생 중 "지금 울릴 노트"를 이진 탐색으로 찾는 데 쓴다.
    // 노트가 바뀔 때만 다시 만든다. (NotifyNotesChanged 참고)
    private BmsNote[]? _sortedNotesCache;

    private BmsNote[] GetSortedNotes() =>
        _sortedNotesCache ??= Chart.Notes.OrderBy(n => n.Measure + n.Position).ToArray();

    // 노트가 바뀌었다고 알린다. 화면 갱신과 정렬 캐시 무효화가 늘 짝이어야 해서 한곳에 모은다.
    private void NotifyNotesChanged()
    {
        _sortedNotesCache = null;
        MarkDirty();
        OnPropertyChanged(nameof(Notes));
    }

    // 화면에 묶인 헤더 값이 바뀌면 문서를 고친 것이다.
    // 불러오기·새로 만들기는 이 뒤에 MarkClean 을 부르므로 깨끗한 상태로 돌아간다.
    partial void OnTitleChanged(string value) => MarkDirty();
    partial void OnArtistChanged(string value) => MarkDirty();
    partial void OnGenreChanged(string value) => MarkDirty();
    partial void OnLevelChanged(string value) => MarkDirty();
    partial void OnPlayerChanged(int value) => MarkDirty();
    partial void OnRankChanged(int value) => MarkDirty();

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

        try
        {
            _audioPlayer.Play(startSeconds);
        }
        catch (Exception ex)
        {
            StopPlayback(resetCursor: false);
            LastErrorMessage = $"재생을 시작하지 못했습니다.\n\n{ex.Message}";
            System.Diagnostics.Debug.WriteLine($"재생 실패: {ex.Message}");
            return false;
        }

        _playbackStartSeconds = startSeconds;
        _playbackStartedAt = DateTimeOffset.UtcNow;
        PlaybackPositionSeconds = startSeconds;
        _lastPlaybackPositionSeconds = startSeconds;
        IsPlaybackCursorVisible = true;
        IsPlaying = true;
        _playbackTimer.Start();
        LastErrorMessage = null;
        return true;
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

        // 시각 계산은 Timeline 이 맡는다. 예전에는 여기서 240/BPM 을 직접 써서,
        // BPM 이 바뀌거나 4/4가 아닌 마디가 있는 차트는 키음이 엉뚱한 때에 울렸다.
        var timeline = Timeline;

        // 시작 지점 이상인 첫 노트를 이진 탐색으로 찾는다.
        var low = 0;
        var high = _playbackNotes.Length - 1;
        var startIndex = _playbackNotes.Length;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var noteSec = timeline.SecondsAt(_playbackNotes[mid].Measure + _playbackNotes[mid].Position);

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
            if (timeline.SecondsAt(note.Measure + note.Position) >= end)
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
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }

    // 마지막 저장(또는 열기) 이후에 고친 것이 있는지.
    //
    // 예전에는 "내용이 있는지"(HasDocumentContent)로만 판단해서, 열어놓고 아무것도
    // 안 고쳤는데도 확인창이 떴다. 그러면 사용자가 확인창을 습관적으로 넘기게 되고,
    // 정작 진짜로 잃을 게 있을 때도 그냥 넘겨 버린다.
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
                return;

            _isDirty = value;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private bool _isDirty;

    // 제목 표시줄. 어떤 파일을 열었고 저장했는지가 여기 말고는 드러나는 곳이 없다.
    public string WindowTitle
    {
        get
        {
            var name = CurrentFilePath is { } path ? System.IO.Path.GetFileName(path) : "제목 없음";
            return $"{(IsDirty ? "*" : "")}{name} - bms editer";
        }
    }

    // 문서를 고쳤다고 표시한다. 노트·헤더·키음이 바뀌는 모든 자리에서 부른다.
    public void MarkDirty() => IsDirty = true;

    private void MarkClean()
    {
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
    }

    // 작업 내용을 버리는 동작(새로 만들기·열기) 앞에서 확인을 받는다.
    // 고친 것이 없거나 콜백이 없으면 그냥 진행한다.
    public async Task<bool> ConfirmDiscardIfNeededAsync(string message)
    {
        if (!IsDirty || ConfirmAsync is not { } confirm)
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
        NotifySelectionChanged();

        PullHeaderFromChart();
        InvalidateTimeline();
        MeasureCount = Chart.MeasureCount;
        CurrentFilePath = null;
        DocumentEncoding = new UTF8Encoding(false);

        NotifyNotesChanged();
        OnPropertyChanged(nameof(SelectedNotes));

        // 방금 비운 직후는 "고친 것 없음"이다. 위 알림들이 세운 표시를 여기서 내린다.
        MarkClean();
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

        // 노트·보존줄·마디길이·BPM 변화·키음표 등 차트 안의 모든 컬렉션이 여기서 한꺼번에 옮겨진다.
        Chart.ReplaceContentWith(parsed.Chart);
        PullHeaderFromChart();
        InvalidateTimeline();
        MeasureCount = Chart.MeasureCount;

        // 읽어낸 인코딩을 기억해 두었다가 저장할 때 그대로 되돌려 쓴다.
        DocumentEncoding = parsed.Encoding;

        // 한 번에 갈아끼운다. 하나씩 Add 하면 항목마다 알림이 나가고,
        // 통계·팔레트 창이 그때마다 전체 재집계를 돈다. (BulkObservableCollection 주석 참고)
        WavList.ReplaceAll(parsed.WavItems);
        _keySoundPlayer.PreloadAsync(Chart.WavTable.Values);

        if (WavList.Count > 0)
        {
            SelectedWavItem = WavList[0];
        }

        CurrentFilePath = filePath;
        LastErrorMessage = null;

        // UI 렌더링 강제 업데이트 유도
        NotifyNotesChanged();

        // 방금 읽어온 그대로다. 아직 고친 것이 없다.
        MarkClean();
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
        try
        {
            var content = BmsWriter.Write(Chart, Title, Artist, Genre, Bpm, Player, Rank, Level, WavList, filePath);
            var encoding = ChooseSaveEncoding(content);

            // 원본을 바로 덮어쓰지 않는다. 쓰다 말면 되돌릴 방법이 없다. (SafeFileWriter 주석 참고)
            SafeFileWriter.WriteAllText(filePath, content, encoding);
            DocumentEncoding = encoding;
            CurrentFilePath = filePath;
            LastErrorMessage = null;
            MarkClean();
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

    // 스페이스바 한 키로 재생/정지를 오간다. 편집하면서 가장 자주 하는 동작이다.
    [RelayCommand]
    private void TogglePlayback()
    {
        if (IsPlaying)
            StopPlayback(resetCursor: false);
        else
            Play();
    }

    private readonly KeySoundPlayer _keySoundPlayer = new();

    public BulkObservableCollection<BmsWavItem> WavList { get; } = new();
    [ObservableProperty] private BmsWavItem? _selectedWavItem;

    public IReadOnlyList<BmsNote> Notes => Chart.Notes;

    private readonly HashSet<BmsNote> _selectedNotes = new();
    private IReadOnlyList<BmsNote> _selectedNotesCache = Array.Empty<BmsNote>();
    public IReadOnlyList<BmsNote> SelectedNotes => _selectedNotesCache;

    private void NotifySelectionChanged()
    {
        _selectedNotesCache = _selectedNotes.ToArray();
        OnPropertyChanged(nameof(SelectedNotes));
    }

    [RelayCommand]
    private void SelectNotes(NoteSelectionArgs args)
    {
        if (!args.Additive)
        {
            SetNoteSelection(args.Notes);
            return;
        }

        // 이미 고른 것에 더한다. 이미 들어 있는 것을 다시 고르면 빼서, 잘못 잡은 것을 되돌릴 수 있다.
        foreach (var note in args.Notes)
        {
            if (!_selectedNotes.Add(note))
                _selectedNotes.Remove(note);
        }

        NotifySelectionChanged();
    }

    // 격자 밖(검색/삭제/교체 창, 통계 창 등)에서 선택 집합을 통째로 교체한다.
    // source 는 격자가 강조 색을 고르는 데만 쓴다. 선택 자체의 의미는 같다.
    public void SetNoteSelection(IEnumerable<BmsNote> notes, NoteSelectionSource source = NoteSelectionSource.Grid)
    {
        _selectedNotes.Clear();
        foreach (var note in notes)
        {
            _selectedNotes.Add(note);
        }
        NotifySelectionChanged();

        // 격자 밖에서 고른 선택은 화면 밖에 있기 쉽다. 거기로 스크롤해 준다.
        if (source != NoteSelectionSource.Grid)
            RequestScrollToSelection();
    }

    // 선택한 자리로 격자를 스크롤해 달라고 뷰에 알린다. 0~1 비율로 넘긴다.
    //
    // 통계 창에서 키음 번호를 눌러도, 검색 창에서 조건 선택을 해도, 그 노트들이
    // 지금 보이는 구간 밖에 있으면 **화면에서는 아무 일도 안 일어난 것처럼 보인다.**
    // 선택은 됐는데 어디가 선택됐는지 알 길이 없어서 기능이 없는 줄 알게 된다.
    public event Action<double>? ScrollToRatioRequested;

    private void RequestScrollToSelection()
    {
        if (_selectedNotes.Count == 0)
            return;

        // 여러 개를 골랐으면 가장 앞선 노트를 기준으로 삼는다.
        var first = _selectedNotes.MinBy(n => n.Measure + n.Position);
        if (first is null)
            return;

        var measurePosition = first.Measure + first.Position;

        var ratio = OggDurationSeconds > 0
            ? Timeline.SecondsAt(measurePosition) / OggDurationSeconds
            : measurePosition / Math.Max(1, MeasureCount);

        ScrollToRatioRequested?.Invoke(Math.Clamp(ratio, 0, 1));
    }

    [RelayCommand]
    public void ClearNoteSelection() => SetNoteSelection(Array.Empty<BmsNote>());

    // 선택 여부와 관계없이 지정한 노트들을 지우고, 실제로 지워진 개수를 돌려준다.
    public int DeleteNotes(IReadOnlyList<BmsNote> notes)
    {
        if (notes.Count == 0) return 0;

        var targetList = ReferenceEquals(notes, Chart.Notes) ? notes.ToArray() : notes;
        var removed = 0;
        foreach (var note in targetList)
        {
            if (!Chart.Notes.Remove(note)) continue;

            _selectedNotes.Remove(note);
            removed++;
        }

        if (removed > 0)
        {
            NotifyNotesChanged();
            NotifySelectionChanged();
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
            NotifyNotesChanged();
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
            NotifyNotesChanged();

        return changed;
    }

    [RelayCommand]
    private void DeleteSelectedNotes()
    {
        if (_selectedNotes.Count == 0) return;

        // 바로 옆에 있는 DeleteNotes 가 같은 일을 한다. 루프를 두 벌 두면 한쪽만 고치게 된다.
        DeleteNotes(SelectedNotes);
    }

    // 선택한 노트를 통째로 옮긴다. **하나라도 못 가면 아무것도 옮기지 않는다.**
    //
    // 예전에는 노트마다 따로 판단해서, 앞이 막히면 막힌 것만 제자리에 남고 나머지는
    // 움직였다. 선택의 상대 간격이 무너져서 패턴 모양이 깨졌다(6번). 게다가
    // 자리 검사가 선택된 노트를 통째로 제외해서, 남은 노트 위로 겹쳐 올라갈 수도 있었다.
    // 겹치면 저장할 때 BmsWriter 가 같은 슬롯을 덮어써 한쪽이 조용히 사라진다(5번).
    [RelayCommand]
    private void MoveSelectedNotes(NoteMoveDirection direction)
    {
        if (_selectedNotes.Count == 0) return;

        var split = Math.Max(1, BeatSplit);
        var lanes = Chart.Lanes;

        // "수직위치 고정"이 켜져 있으면 시간 위치는 건드리지 않는다. 레인만 옮긴다.
        // (세로 뷰에서 세로축이 곧 시간축이라 체크박스 이름이 이렇게 붙어 있다)
        if (LockVerticalPosition && direction is NoteMoveDirection.TimeForward or NoteMoveDirection.TimeBackward)
            return;

        // 1단계: 옮길 자리를 전부 미리 구한다. 하나라도 못 구하면 그만둔다.
        var moves = new List<(BmsNote Note, int Measure, double Position, string LaneId)>(_selectedNotes.Count);

        foreach (var note in _selectedNotes)
        {
            var target = direction switch
            {
                NoteMoveDirection.TimeForward => OffsetInTime(note, 1, split),
                NoteMoveDirection.TimeBackward => OffsetInTime(note, -1, split),
                NoteMoveDirection.LanePrevious => OffsetInLane(note, -1, lanes),
                NoteMoveDirection.LaneNext => OffsetInLane(note, 1, lanes),
                _ => null,
            };

            if (target is not { } t)
                return;

            moves.Add((note, t.Measure, t.Position, t.LaneId));
        }

        // 2단계: 선택 밖의 노트와 부딪히는지, 옮긴 것끼리 겹치는지 확인한다.
        var occupied = new HashSet<(string, int, long)>();
        foreach (var note in Chart.Notes)
        {
            if (!_selectedNotes.Contains(note))
                occupied.Add(ToSlotKey(note.LaneId, note.Measure, note.Position));
        }

        foreach (var move in moves)
        {
            if (!occupied.Add(ToSlotKey(move.LaneId, move.Measure, move.Position)))
                return;
        }

        // 3단계: 다 통과했으니 한꺼번에 적용한다.
        foreach (var move in moves)
        {
            move.Note.Measure = move.Measure;
            move.Note.Position = move.Position;
            move.Note.LaneId = move.LaneId;
        }

        NotifyNotesChanged();
    }

    // 노트를 시간축으로 격자 한 칸만큼 **옮긴다.** 격자에 붙이지 않는다.
    //
    // 예전에는 Math.Round((Measure + Position) * split) 으로 위치를 다시 계산해서,
    // 현재 격자로 표현할 수 없는 노트는 옮기는 순간 자리가 바뀌었다.
    // 12분할로 찍은 3잇단음을 16분할 상태에서 건드리면 잇단음이 뭉개졌다.
    private (int Measure, double Position, string LaneId)? OffsetInTime(BmsNote note, int steps, int split)
    {
        var total = note.Measure + note.Position + ((double)steps / split);
        if (total < 0)
            return null;

        // 부동소수 오차로 마디 경계에서 한 칸 밀리지 않도록 여유를 두고 자른다.
        var measure = (int)Math.Floor(total + PositionEpsilon);
        var position = total - measure;
        if (position < PositionEpsilon)
            position = 0.0;

        if (measure < 0 || measure >= MeasureCount)
            return null;

        return (measure, position, note.LaneId);
    }

    private static (int Measure, double Position, string LaneId)? OffsetInLane(
        BmsNote note, int steps, IReadOnlyList<LaneDefinition> lanes)
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

        if (currentIndex == -1)
            return null;

        var newIndex = currentIndex + steps;
        if (newIndex < 0 || newIndex >= lanes.Count)
            return null;

        return (note.Measure, note.Position, lanes[newIndex].Id);
    }

    // 마디 내 위치를 같은 자리로 볼 허용오차. ToSlotKey 의 1/10000 눈금과 짝이다.
    private const double PositionEpsilon = 0.0001;

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

    // 실패하면 false. 예전에는 예외를 삼켜서 [키음 추가]가 조용히 아무 일도 안 했다.
    public bool AddWav(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            LastErrorMessage = "파일을 찾을 수 없습니다.";
            return false;
        }

        try
        {
            var key = GetNextWavKey();
            Chart.WavTable[key] = filePath;
            _keySoundPlayer.Preload(filePath);
            var item = new BmsWavItem { Key = key, FilePath = filePath };
            WavList.Add(item);
            SelectedWavItem = item;
            LastErrorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"WAV 추가 실패: {ex.Message}");
            return false;
        }
    }

    // 이 번호를 쓰는 노트 개수.
    public int CountNotesUsingWavKey(string key) =>
        Chart.Notes.Count(n => string.Equals(n.WavKey, key, StringComparison.OrdinalIgnoreCase));

    // 키음을 지운다. 그 번호를 쓰는 노트가 있으면 먼저 확인을 받는다.
    //
    // 예전에는 테이블에서만 지우고 노트는 그대로 뒀다. 남은 노트는 소리가 나지 않는
    // 유령 노트가 되고, 저장하면 #WAV 정의가 없는 번호를 가리키는 파일이 나온다.
    // 게다가 새 키음을 추가하면 비어 있는 그 번호가 다시 쓰여서,
    // 유령 노트들이 갑자기 엉뚱한 소리를 내기 시작한다.
    [RelayCommand]
    private async Task RemoveWavAsync()
    {
        if (SelectedWavItem is null) return;

        var key = SelectedWavItem.Key;
        var usedBy = CountNotesUsingWavKey(key);

        if (usedBy > 0)
        {
            if (ConfirmAsync is not { } confirm)
                return;

            var proceed = await confirm(
                $"#WAV{key} 를 쓰는 노트가 {usedBy}개 있습니다.\n\n" +
                "지우면 그 노트들은 소리가 나지 않는 유령 노트가 됩니다.\n" +
                "나중에 키음을 추가하면 이 번호가 다시 쓰여서, 그 노트들이 엉뚱한 소리를 내게 됩니다.\n\n" +
                "그래도 지울까요?");

            if (!proceed)
                return;
        }

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
        NotifyNotesChanged();
    }

    // 우클릭한 자리에서 가장 가까운 노트 하나를 지운다.
    //
    // 예전에는 허용오차가 0.005 로 고정이었다. 배치 허용오차(0.0001)의 50배라
    // 192분할처럼 촘촘하게 찍어두면 엉뚱한 옆 노트가 지워졌다. 게다가 Find 는
    // "가장 가까운"이 아니라 "처음 찾은" 것을 골라서 어느 게 지워질지 예측할 수 없었다.
    [RelayCommand]
    private void RemoveNote(NotePlacementArgs args)
    {
        // 격자 한 칸의 절반. 격자로 표현 못 하는 자리(잇단음)의 노트도 집을 수 있으면서,
        // 옆 칸까지 넘어가지는 않는 폭이다.
        var tolerance = Math.Max(PositionEpsilon, 0.5 / Math.Max(1, BeatSplit));

        BmsNote? nearest = null;
        var nearestDistance = double.MaxValue;

        foreach (var note in Chart.Notes)
        {
            if (note.Measure != args.Measure || note.LaneId != args.LaneId)
                continue;

            var distance = Math.Abs(note.Position - args.Position);
            if (distance > tolerance || distance >= nearestDistance)
                continue;

            nearest = note;
            nearestDistance = distance;
        }

        if (nearest is null)
            return;

        Chart.Notes.Remove(nearest);
        _selectedNotes.Remove(nearest);
        NotifyNotesChanged();
        NotifySelectionChanged();
    }
}
