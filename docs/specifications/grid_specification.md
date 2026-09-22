# BMS Editor - 기본 그리드 분할 명세서 (Grid Split Specification)

이 문서는 BMS 에디터의 마디(Measure) 내부 그리드 분할 규칙에 대한 핵심 동작 사양과 관련 핵심 코드 구현 스니펫을 정의합니다.

---

## 📌 기본 요구 사항: 마디당 16개 그리드 선 배치
BMS 에디터가 처음 구동되거나 차트가 초기화될 때, **마디와 마디 사이(1마디 공간)에는 무조건 16개의 그리드 선(16분할선)**이 생성되어야 합니다.

* **정의**: 4/4 박자를 기준으로 한 마디를 16등분하는 그리드(16분음표 단위 격자)가 초기 기준이 됩니다.
* **목적**: 채보 제작자가 노트를 배치할 때 가장 대중적인 단위인 16비트(16분음표) 격자에 정확히 맞물려(Snap) 배치되도록 하기 위함입니다.

---

## 💻 핵심 코드 구현 스니펫 (Core Code Snippets)

### 1. 기본값 정의 (TimelineControlBase.cs)
격자 분할 수를 담당하는 `BeatSplit` 속성의 기본값은 의존성 프로퍼티(StyledProperty) 정의 단계에서 **`16`**으로 지정되어 있습니다.

```csharp
// bms editer/Views/Controls/TimelineControlBase.cs

public abstract class TimelineControlBase : Control
{
    // 그리드 분할 수 Property 정의 (기본값: 16)
    public static readonly StyledProperty<int> BeatSplitProperty =
        AvaloniaProperty.Register<TimelineControlBase, int>(nameof(BeatSplit), 16);

    public int BeatSplit
    {
        get => GetValue(BeatSplitProperty);
        set => SetValue(BeatSplitProperty, value);
    }

    // 특정 인덱스의 선이 주요 박자 선(Beat Line)인지 판별하는 헬퍼 함수
    protected static bool IsMeasureBeatLine(int index, int split, int gridMeasure)
    {
        if (gridMeasure <= 0 || split < gridMeasure || split % gridMeasure != 0)
            return false;

        return Mod(index, split / gridMeasure) == 0;
    }

    protected static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
```

---

### 2. 그리드 격자선 생성 엔진 (TimelineControlBase.cs - EnumerateGridLines)
예전에는 컨트롤마다 `240 / (Bpm * split)` 수식을 직접 돌려 계산했으나, 변박(`#xxx02`)과 BPM 변경(`#xxx03`/`#xxx08`)이 발생할 때 파형과 격자가 어긋나는 문제가 있었습니다.
현재는 `TimelineControlBase`의 **`EnumerateGridLines`**가 `ChartTimeline`(`EffectiveTimeline`)을 통해 정확한 시각을 계산하고 선을 단일 공급합니다.

```csharp
// bms editer/Views/Controls/TimelineControlBase.cs

protected IEnumerable<GridLine> EnumerateGridLines(double timelineLength)
{
    var split = Math.Max(1, BeatSplit);

    if (DurationSeconds > 0 && Bpm > 0)
    {
        // 배경 음원이 있을 때: ChartTimeline에 기반하여 각 분할선의 초(Seconds) 도출
        var timeline = EffectiveTimeline;
        for (var index = 0; ; index++)
        {
            var seconds = timeline.SecondsAt((double)index / split);
            if (seconds > DurationSeconds)
                yield break;

            var position = ToTimelinePosition(seconds / DurationSeconds, timelineLength);
            if (position < -0.5 || position > timelineLength + 0.5)
                continue;

            yield return new GridLine(
                position,
                ClassifyGridLine(index, split),
                index / split,
                seconds);
        }
    }
    else
    {
        // 배경 음악이 없을 때: 마디 높이(RowHeight) 기반 열거
        var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
        for (var measure = 0; measure <= MeasureCount; measure++)
        {
            for (var beat = 0; beat < split; beat++)
            {
                var measurePosition = measure + (beat / (double)split);
                var offset = measurePosition * rowHeight;
                var position = IsHorizontalView ? offset : timelineLength - offset;

                if (position < -0.5 || position > timelineLength + 0.5)
                    continue;

                yield return new GridLine(position, ClassifyGridLine(beat, split), measure, 0);
            }
        }
    }
}
```

`NoteGridControl`과 `OggWaveformControl`은 이 열거 결과를 받아 화면에 그리기만 수행하므로 동기화 기준이 완전히 일원화되어 있습니다.

---

### 3. 마우스 클릭 배치 시의 16분할 스냅 처리 (NoteGridControl.cs)
에디터 위에서 마우스 좌클릭/우클릭으로 노트를 배치하거나 지울 때도 `BeatSplit` 기반 격자에 스냅 보정되어 위치가 결정됩니다.
클릭한 자리를 마디 위치로 되돌리는 일은 격자를 그린 것과 **같은 시간축**(`ChartTimeline.MeasurePositionAt`)이 맡습니다.
예전처럼 `240 / Bpm` 을 직접 쓰면 BPM 변화·변박 뒤쪽에서 클릭한 자리와 찍히는 자리가 어긋납니다.

```csharp
// bms editer/Views/Controls/NoteGridControl.cs — OnPointerPressed (편집 모드)

var tPos = IsHorizontalView ? point.Position.X : point.Position.Y;
var split = Math.Max(1, BeatSplit); // 기본값: 16

var ratio = Math.Clamp(IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength)), 0.0, 1.0);

// 클릭한 자리를 마디 위치로 되돌린다. 그리는 쪽과 같은 시간축을 쓴다.
var clickedMeasurePosition = DurationSeconds > 0
    ? EffectiveTimeline.MeasurePositionAt(ratio * DurationSeconds)
    : ratio * MeasureCount;

if (SnapToGrid)
{
    var totalStepIndex = (int)Math.Round(clickedMeasurePosition * split);

    // 맨 끝을 클릭해도 곡 마지막 격자 칸에 찍히도록 당겨 준다.
    totalStepIndex = Math.Clamp(totalStepIndex, 0, (MeasureCount * split) - 1);

    measure = totalStepIndex / split;
    position = (double)(totalStepIndex % split) / split;
}
else
{
    // "격자에 맞추기" 체크를 끄면 클릭한 자리에 그대로 찍는다(잇단음 등).
    var clamped = Math.Clamp(clickedMeasurePosition, 0, MeasureCount - (1.0 / split));
    measure = (int)Math.Floor(clamped);
    position = clamped - measure;
}
```

* 우클릭 삭제는 같은 마디·레인에서 **격자 반 칸 안의 가장 가까운 노트 하나**를 지웁니다(`MainWindowViewModel.RemoveNote`).
* 방향키 이동은 격자 한 칸만큼 **옮길 뿐 격자에 다시 붙이지 않습니다.** 12분할로 찍은 잇단음을 16분할 상태에서 옮겨도 잇단음이 유지됩니다.
