using System;
using System.Collections.Generic;
using bms_editer.Models;

namespace bms_editer.Views.Controls;

public sealed partial class NoteGridControl
{
    private NoteRenderIndex? _noteRenderIndex;
    private HoldRenderIndex? _holdRenderIndex;
    private HoldProblemRenderIndex? _holdProblemRenderIndex;
    private IReadOnlyList<LaneDefinition>? _laneIndexMapSource;
    private Dictionary<string, int> _laneIndexMap = new(StringComparer.Ordinal);

    // 노트를 시간 순으로 세워 둔 그리기 전용 색인.
    //
    // 예전에는 Render 마다 노트 전부를 돌며 레인을 문자열로 찾고 시각을 다시 계산했다.
    // 화면에 수십 개만 보여도 곡 전체(수만 개) 비용을 냈다. 이제 이진 탐색으로 첫 노트를 찾고
    // 화면을 지나면 멈춘다. 뷰모델은 노트가 바뀔 때마다 새 배열을 주므로 참조만 비교하면 된다.
    private sealed class NoteRenderIndex
    {
        public required IReadOnlyList<BmsNote> Source { get; init; }
        public required IReadOnlyList<LaneDefinition> Lanes { get; init; }
        public required Services.ChartTimeline Timeline { get; init; }
        public required BmsNote[] Notes { get; init; }
        public required int[] LaneIndices { get; init; }
        public required double[] MeasurePositions { get; init; }
        public required double[] Seconds { get; init; }

        public int Count => Notes.Length;

        public int FirstVisible(in NoteAxis axis, double lowPos, double highPos) =>
            FindFirstVisible(MeasurePositions, Seconds, axis, lowPos, highPos);
    }

    // 홀드 몸통 그리기 전용 색인. 매 프레임 HoldLinks 전체의 레인·시각을 다시 계산하지 않도록 캐시한다.
    private sealed class HoldRenderIndex
    {
        public required IReadOnlyList<HoldLink> Source { get; init; }
        public required IReadOnlyList<LaneDefinition> Lanes { get; init; }
        public required Services.ChartTimeline Timeline { get; init; }
        public required int[] HeadLanes { get; init; }
        public required int[] TailLanes { get; init; }
        public required double[] HeadMeasurePositions { get; init; }
        public required double[] TailMeasurePositions { get; init; }
        public required double[] HeadSeconds { get; init; }
        public required double[] TailSeconds { get; init; }
        public required string[] Kinds { get; init; }

        public int Count => Kinds.Length;
    }

    // 홀드 오류(짝이 어긋난 노트) 그리기 전용 색인. 매 프레임 SecondsAt·레인 조회를 반복하지 않는다.
    private sealed class HoldProblemRenderIndex
    {
        public required IReadOnlyList<BmsNote> Source { get; init; }
        public required IReadOnlyList<LaneDefinition> Lanes { get; init; }
        public required Services.ChartTimeline Timeline { get; init; }
        public required int[] LaneIndices { get; init; }
        public required double[] MeasurePositions { get; init; }
        public required double[] Seconds { get; init; }

        public int Count => LaneIndices.Length;

        public int FirstVisible(in NoteAxis axis, double lowPos, double highPos) =>
            FindFirstVisible(MeasurePositions, Seconds, axis, lowPos, highPos);
    }

    // 노트 하나의 타임라인 좌표를 구하는 데 필요한 값. 노트마다 속성을 다시 읽지 않으려고 묶어 둔다.
    private readonly record struct NoteAxis(
        bool UsesSeconds, bool IsHorizontal, double DurationSeconds, double MeasureThickness, double TimelineLength)
    {
        public double Position(double measurePosition, double seconds)
        {
            if (UsesSeconds)
            {
                var ratio = seconds / DurationSeconds;
                return IsHorizontal ? ratio * TimelineLength : (1.0 - ratio) * TimelineLength;
            }

            var offset = measurePosition * MeasureThickness;
            return IsHorizontal ? offset : TimelineLength - offset;
        }
    }

    private NoteAxis CreateNoteAxis(double timelineLength) => new(
        DurationSeconds > 0 && Bpm > 0,
        IsHorizontalView,
        DurationSeconds,
        RowHeight * VerticalZoom * GetGridSpacingScale(),
        timelineLength);

    // 시간 순으로 처음 화면에 들어오는 항목 인덱스. 시간 축 방향은 뷰에 따라 뒤집힌다.
    private static int FindFirstVisible(
        double[] measurePositions, double[] seconds, in NoteAxis axis, double lowPos, double highPos)
    {
        var low = 0;
        var high = measurePositions.Length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var position = axis.Position(measurePositions[middle], seconds[middle]);
            var isBeforeView = axis.IsHorizontal ? position < lowPos : position > highPos;

            if (isBeforeView)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private NoteRenderIndex GetNoteRenderIndex(IReadOnlyList<BmsNote> notes, IReadOnlyList<LaneDefinition> lanes)
    {
        var timeline = EffectiveTimeline;
        if (_noteRenderIndex is { } cached
            && ReferenceEquals(cached.Source, notes)
            && ReferenceEquals(cached.Lanes, lanes)
            && ReferenceEquals(cached.Timeline, timeline))
        {
            return cached;
        }

        var laneMap = GetLaneIndexMap(lanes);
        var drawable = new List<(double MeasurePosition, BmsNote Note, int Lane)>(notes.Count);

        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            if (laneMap.TryGetValue(note.LaneId, out var lane))
                drawable.Add((note.Measure + note.Position, note, lane));
        }

        drawable.Sort(static (a, b) => a.MeasurePosition.CompareTo(b.MeasurePosition));

        var ordered = new BmsNote[drawable.Count];
        var laneIndices = new int[drawable.Count];
        var measurePositions = new double[drawable.Count];
        var seconds = new double[drawable.Count];

        for (var i = 0; i < drawable.Count; i++)
        {
            ordered[i] = drawable[i].Note;
            laneIndices[i] = drawable[i].Lane;
            measurePositions[i] = drawable[i].MeasurePosition;
            seconds[i] = timeline.SecondsAt(drawable[i].MeasurePosition);
        }

        _noteRenderIndex = new NoteRenderIndex
        {
            Source = notes,
            Lanes = lanes,
            Timeline = timeline,
            Notes = ordered,
            LaneIndices = laneIndices,
            MeasurePositions = measurePositions,
            Seconds = seconds,
        };

        return _noteRenderIndex;
    }

    private Dictionary<string, int> GetLaneIndexMap(IReadOnlyList<LaneDefinition> lanes)
    {
        if (ReferenceEquals(_laneIndexMapSource, lanes))
            return _laneIndexMap;

        var map = new Dictionary<string, int>(lanes.Count, StringComparer.Ordinal);
        for (var i = 0; i < lanes.Count; i++)
            map.TryAdd(lanes[i].Id, i);

        _laneIndexMapSource = lanes;
        _laneIndexMap = map;
        return map;
    }

    private HoldRenderIndex GetHoldRenderIndex(IReadOnlyList<HoldLink> links, IReadOnlyList<LaneDefinition> lanes)
    {
        var timeline = EffectiveTimeline;
        if (_holdRenderIndex is { } cached
            && ReferenceEquals(cached.Source, links)
            && ReferenceEquals(cached.Lanes, lanes)
            && ReferenceEquals(cached.Timeline, timeline))
        {
            return cached;
        }

        var laneMap = GetLaneIndexMap(lanes);
        var validCount = 0;
        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (laneMap.ContainsKey(link.Head.LaneId) && laneMap.ContainsKey(link.Tail.LaneId))
                validCount++;
        }

        var headLanes = new int[validCount];
        var tailLanes = new int[validCount];
        var headMeasurePositions = new double[validCount];
        var tailMeasurePositions = new double[validCount];
        var headSeconds = new double[validCount];
        var tailSeconds = new double[validCount];
        var kinds = new string[validCount];

        var idx = 0;
        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (!laneMap.TryGetValue(link.Head.LaneId, out var headLane)
                || !laneMap.TryGetValue(link.Tail.LaneId, out var tailLane))
                continue;

            var headMP = link.Head.Measure + link.Head.Position;
            var tailMP = link.Tail.Measure + link.Tail.Position;

            headLanes[idx] = headLane;
            tailLanes[idx] = tailLane;
            headMeasurePositions[idx] = headMP;
            tailMeasurePositions[idx] = tailMP;
            headSeconds[idx] = timeline.SecondsAt(headMP);
            tailSeconds[idx] = timeline.SecondsAt(tailMP);
            kinds[idx] = link.Kind;
            idx++;
        }

        _holdRenderIndex = new HoldRenderIndex
        {
            Source = links,
            Lanes = lanes,
            Timeline = timeline,
            HeadLanes = headLanes,
            TailLanes = tailLanes,
            HeadMeasurePositions = headMeasurePositions,
            TailMeasurePositions = tailMeasurePositions,
            HeadSeconds = headSeconds,
            TailSeconds = tailSeconds,
            Kinds = kinds,
        };

        return _holdRenderIndex;
    }

    private HoldProblemRenderIndex GetHoldProblemRenderIndex(IReadOnlyList<BmsNote> problems, IReadOnlyList<LaneDefinition> lanes)
    {
        var timeline = EffectiveTimeline;
        if (_holdProblemRenderIndex is { } cached
            && ReferenceEquals(cached.Source, problems)
            && ReferenceEquals(cached.Lanes, lanes)
            && ReferenceEquals(cached.Timeline, timeline))
        {
            return cached;
        }

        var laneMap = GetLaneIndexMap(lanes);
        var drawable = new List<(double MeasurePosition, int Lane)>(problems.Count);

        for (var i = 0; i < problems.Count; i++)
        {
            var note = problems[i];
            if (laneMap.TryGetValue(note.LaneId, out var lane))
                drawable.Add((note.Measure + note.Position, lane));
        }

        drawable.Sort(static (a, b) => a.MeasurePosition.CompareTo(b.MeasurePosition));

        var laneIndices = new int[drawable.Count];
        var measurePositions = new double[drawable.Count];
        var seconds = new double[drawable.Count];

        for (var i = 0; i < drawable.Count; i++)
        {
            laneIndices[i] = drawable[i].Lane;
            measurePositions[i] = drawable[i].MeasurePosition;
            seconds[i] = timeline.SecondsAt(drawable[i].MeasurePosition);
        }

        _holdProblemRenderIndex = new HoldProblemRenderIndex
        {
            Source = problems,
            Lanes = lanes,
            Timeline = timeline,
            LaneIndices = laneIndices,
            MeasurePositions = measurePositions,
            Seconds = seconds,
        };

        return _holdProblemRenderIndex;
    }
}
