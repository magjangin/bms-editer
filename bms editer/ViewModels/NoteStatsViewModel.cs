using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using bms_editer.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bms_editer.ViewModels;

public sealed record LaneNoteStat(string LaneId, int Count);

// 키음 한 줄. 목록에서 고르면 그 번호를 쓰는 노트가 격자에서 선택된다.
//
// 고른 줄을 표시하는 일은 ListBox 가 대신한다. 여기서 IsSelected 같은 걸 들고 있을 필요가 없다.
public sealed record WavNoteStat(string Key, string FileName, int Count);

// 노트 통계 창의 상태 모델.
//
// 편집 결과가 곧바로 보이도록 메인 뷰모델의 노트 변경 알림을 구독해 다시 집계한다.
// 개수가 0인 항목은 목록에서 빼고 실제로 쓰인 레인/키음만 보여준다.
// 키음은 등록된 #WAV 목록이 아니라 노트가 실제로 가리키는 번호를 기준으로 센다.
// (수백 개짜리 키음 테이블에서 쓰지 않는 번호가 목록을 가득 채우는 걸 막는다)
public sealed partial class NoteStatsViewModel : OwnerObservingViewModel
{
    [ObservableProperty] private IReadOnlyList<LaneNoteStat> _stats = Array.Empty<LaneNoteStat>();
    [ObservableProperty] private IReadOnlyList<WavNoteStat> _wavStats = Array.Empty<WavNoteStat>();
    [ObservableProperty] private int _totalCount;

    public NoteStatsViewModel(MainWindowViewModel owner) : base(owner)
    {
        Refresh();
    }

    public bool HasLaneStats => Stats.Count > 0;
    public bool HasWavStats => WavStats.Count > 0;

    partial void OnStatsChanged(IReadOnlyList<LaneNoteStat> value) => OnPropertyChanged(nameof(HasLaneStats));

    partial void OnWavStatsChanged(IReadOnlyList<WavNoteStat> value) => OnPropertyChanged(nameof(HasWavStats));

    protected override void OnOwnerPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(MainWindowViewModel.Notes))
            Refresh();
    }

    // 키음을 추가·삭제하면 번호에 딸린 파일명 표시가 달라진다.
    protected override void OnWavListChanged() => Refresh();

    private void Refresh()
    {
        var chart = Owner.Chart;

        // 키음 번호 -> 파일명. 같은 번호가 두 번 정의돼 있으면 마지막 것을 쓴다.
        var fileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Owner.WavList)
        {
            fileNames[item.Key] = Path.GetFileName(item.FilePath);
        }

        Stats = chart.Lanes
            .Select(lane => new LaneNoteStat(lane.Header, chart.Notes.Count(n => n.LaneId == lane.Id)))
            .Where(stat => stat.Count > 0)
            .ToList();

        WavStats = chart.Notes
            .GroupBy(note => note.WavKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WavNoteStat(
                group.Key,
                fileNames.TryGetValue(group.Key, out var fileName) ? fileName : "(등록되지 않은 번호)",
                group.Count()))
            .OrderBy(stat => stat.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TotalCount = chart.Notes.Count;
    }

    // 목록에서 지금 고른 줄. ListBox 의 SelectedItem 이 그대로 들어온다.
    //
    // 예전에는 줄마다 Button 을 만들고 명령을 물려야 했는데, 그 물리는 길이 조용히 끊어져
    // 눌러도 아무 일이 없었다. 목록에서 "고른다"는 건 ListBox 가 원래 하는 일이라
    // 중간 배선이 아예 없어졌다.
    [ObservableProperty] private WavNoteStat? _selectedWavStat;

    partial void OnSelectedWavStatChanged(WavNoteStat? value)
    {
        // 목록을 다시 만들면 선택이 풀린다. 그때 격자 선택까지 건드리지는 않는다.
        if (value is not null)
            SelectByWavKey(value.Key);
    }

    // 방금 무엇을 골랐는지. 고른 노트가 화면 밖에 있으면 격자만 봐서는 골랐는지도 알 수 없다.
    [ObservableProperty] private string _statusMessage = "👇 아래 목록에서 줄을 고르면 그 키음의 노트가 격자에서 빨갛게 선택됩니다.";

    // 고른 키음을 쓰는 노트를 격자에서 선택한다.
    // 어디에 찍혀 있는지 눈으로 확인하고, 이어서 검색 창의 "선택" 필터로
    // 삭제·번호 바꾸기·마디 옮겨 복사까지 그대로 이어 갈 수 있다.
    //
    // 출처를 Stats 로 넘겨서 격자가 빨강으로 그리게 한다. 훑어보려고 켠 선택이
    // 손으로 고른 선택(노랑)과 섞이면 어느 쪽을 편집하는 중인지 헷갈린다.
    [RelayCommand]
    private void SelectByWavKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        var matches = Owner.Chart.Notes
            .Where(note => string.Equals(note.WavKey, key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Owner.SetNoteSelection(matches, NoteSelectionSource.Stats);

        if (matches.Count == 0)
        {
            StatusMessage = $"#{key} 를 쓰는 노트가 없습니다.";
            return;
        }

        var first = matches.MinBy(n => n.Measure + n.Position)!;
        var last = matches.MaxBy(n => n.Measure + n.Position)!;

        StatusMessage = first.Measure == last.Measure
            ? $"#{key} 노트 {matches.Count}개를 빨갛게 선택했습니다. ({first.Measure}마디)"
            : $"#{key} 노트 {matches.Count}개를 빨갛게 선택했습니다. ({first.Measure}~{last.Measure}마디)";
    }
}
