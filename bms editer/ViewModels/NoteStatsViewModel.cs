using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using bms_editer.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bms_editer.ViewModels;

public sealed record LaneNoteStat(string LaneId, int Count);

public sealed record WavNoteStat(string Key, string FileName, int Count);

// 노트 통계 창의 상태 모델. **집계해서 보여주기만 한다.**
//
// 편집 결과가 곧바로 보이도록 메인 뷰모델의 노트 변경 알림을 구독해 다시 집계한다.
// 개수가 0인 항목은 목록에서 빼고 실제로 쓰인 레인/키음만 보여준다.
// 키음은 등록된 #WAV 목록이 아니라 노트가 실제로 가리키는 번호를 기준으로 센다.
// (수백 개짜리 키음 테이블에서 쓰지 않는 번호가 목록을 가득 채우는 걸 막는다)
//
// 한때 "번호를 눌러 그 노트를 격자에서 선택"하는 기능이 여기 있었으나 걷어냈다.
// 화면에서 명령까지 닿는 길이 두 번 조용히 끊어졌고, 고른 노트가 화면 밖이면
// 눌렸는지조차 알 수 없어 쓸 만한 상태가 못 됐다. 나중에 제대로 다시 만든다.
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

        var laneCounts = new Dictionary<string, int>();
        var wavCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in chart.Notes)
        {
            laneCounts[note.LaneId] = laneCounts.GetValueOrDefault(note.LaneId) + 1;
            wavCounts[note.WavKey] = wavCounts.GetValueOrDefault(note.WavKey) + 1;
        }

        Stats = chart.Lanes
            .Select(lane => new LaneNoteStat(lane.Header, laneCounts.GetValueOrDefault(lane.Id)))
            .Where(stat => stat.Count > 0)
            .ToList();

        WavStats = wavCounts
            .Select(kv => new WavNoteStat(
                kv.Key,
                fileNames.TryGetValue(kv.Key, out var fileName) ? fileName : "(등록되지 않은 번호)",
                kv.Value))
            .OrderBy(stat => stat.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TotalCount = chart.Notes.Count;
    }
}
