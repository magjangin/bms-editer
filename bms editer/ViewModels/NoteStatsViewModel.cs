using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using bms_editer.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bms_editer.ViewModels;

public sealed record LaneNoteStat(string LaneId, int Count);

// 키음 한 줄. 눌러서 고를 수 있으므로 "지금 고른 줄"인지도 함께 들고 있는다.
//
// 눌린 표시가 남지 않으면, 고른 노트가 화면 밖에 있을 때 눌렀는지조차 알 수 없다.
public sealed partial class WavNoteStat : ObservableObject
{
    public WavNoteStat(string key, string fileName, int count)
    {
        Key = key;
        FileName = fileName;
        Count = count;
    }

    public string Key { get; }
    public string FileName { get; }
    public int Count { get; }

    [ObservableProperty] private bool _isSelected;
}

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

    // 목록에서 번호를 누르면 그 키음을 쓰는 노트를 격자에서 바로 선택한다.
    // 어디에 찍혀 있는지 눈으로 확인하고, 이어서 검색 창의 "선택" 필터로
    // 삭제·번호 바꾸기·마디 옮겨 복사까지 그대로 이어 갈 수 있다.
    //
    // 출처를 Stats 로 넘겨서 격자가 빨강으로 그리게 한다. 훑어보려고 켠 선택이
    // 손으로 고른 선택(노랑)과 섞이면 어느 쪽을 편집하는 중인지 헷갈린다.
    // 방금 무엇을 골랐는지. 고른 노트가 화면 밖에 있으면 격자만 봐서는 눌렀는지도 알 수 없다.
    [ObservableProperty] private string _statusMessage = "👆 아래 줄을 누르면 그 키음의 노트가 격자에서 빨갛게 선택됩니다.";

    [RelayCommand]
    private void SelectByWavKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        var matches = Owner.Chart.Notes
            .Where(note => string.Equals(note.WavKey, key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Owner.SetNoteSelection(matches, NoteSelectionSource.Stats);

        // 누른 줄에 표시를 남긴다. 고른 노트가 화면 밖이어도 무엇을 눌렀는지는 보인다.
        foreach (var stat in WavStats)
            stat.IsSelected = string.Equals(stat.Key, key, StringComparison.OrdinalIgnoreCase);

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
