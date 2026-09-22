# BMS Editor - BPM 설정에 따른 그리드 및 파형 동기화 원리 (BPM & Grid Sync Principles)

이 문서는 에디터 내에서 BPM(Beats Per Minute) 값을 변경하거나 설정할 때, 오디오 파형과 마디 격자선(Grid Lines)이 화면에 어떻게 상호작용하며 실시간 반영되는지 설명합니다.

---

## 📌 핵심 요약 (Core Concept)
1. **오디오 파형의 물리적 형태는 고정**됩니다. (OGG 곡의 총 길이와 시간대별 파형 진폭은 불변)
2. **그리드 격자선(마디/박자 선)은 BPM에 따라 유동적으로 신축**됩니다. (BPM이 높을수록 간격이 좁아지고, 낮을수록 넓어짐)
3. 에디터는 오디오 시간 축(Seconds)을 매개체로 파형과 격자선을 동기화하여, 사용자가 시각적·청각적 타격점(Onset)에 격자선을 정확하게 수동 튜닝할 수 있도록 돕습니다.

---

## 💻 상세 연동 메커니즘

### 1. 마디 격자선의 동적 압축 및 팽창
에디터 화면의 총 길이(Timeline Length)는 로드된 배경 오디오의 전체 시간(`DurationSeconds`)을 기준으로 결정됩니다.
이 고정된 화면 길이 내에서 그리드 선이 그려지는 간격(`secondsPerStep`)은 아래와 같은 수식으로 계산되어 BPM에 반비례하여 변경됩니다:

$$\text{secondsPerStep} = \frac{240.0}{\text{BPM} \times \text{BeatSplit}}$$

* **BPM이 올라갈 때 (BPM ↑)**
  - `secondsPerStep`이 작아집니다.
  - 마디선 간의 간격이 **시각적으로 좁아집니다 (그리드 압축)**.
  - 고정된 오디오 길이 안에 더 많은 마디선이 렌더링됩니다.
* **BPM이 내려갈 때 (BPM ↓)**
  - `secondsPerStep`이 커집니다.
  - 마디선 간의 간격이 **시각적으로 넓어집니다 (그리드 팽창)**.
  - 고정된 오디오 길이 안에 더 적은 마디선이 렌더링됩니다.

#### [현대적 렌더링 구현: ChartTimeline 및 EnumerateGridLines]
초기에는 `240.0 / (Bpm * split)` 단순 수식을 썼으나, 곡 중간의 BPM 변경(`#xxx03`/`#xxx08`) 및 변박(`#xxx02`)이 발생하면 뒤쪽 마디가 전부 밀리는 문제가 있었습니다.
현재는 **`ChartTimeline` 엔진**이 곡 전체의 BPM 변화 지점과 마디 배율을 누적한 시간표를 작성하고, `TimelineControlBase.EnumerateGridLines`가 이를 조회하여 격자선을 열거합니다:

```csharp
// TimelineControlBase.cs EnumerateGridLines()
var timeline = EffectiveTimeline; // ChartTimeline 인스턴스
for (var index = 0; ; index++)
{
    // index / split 마디 위치의 절대 초(Seconds)를 ChartTimeline에서 일원화 조회
    var seconds = timeline.SecondsAt((double)index / split);
    if (seconds > DurationSeconds) yield break;

    var position = ToTimelinePosition(seconds / DurationSeconds, timelineLength);
    // ... 격자선 생성 및 반환 ...
}
```

---

### 2. 파형(Waveform)의 고정 맵핑
- 배경 음악 파형(`OggWaveformControl`)은 오디오 버퍼의 실제 인덱스를 화면 픽셀 비율에 1:1 대응하여 그립니다.
- 따라서 **BPM이 아무리 바뀌어도 오디오 파형 자체는 늘어나거나 줄어들지 않고 제자리에 유지**됩니다.
- 이 구조 덕분에 사용자는 고정된 실제 오디오 파형 진폭(볼륨이 피크를 치는 지점)과 움직이는 마디 격자선을 비교하며, 마디 시작선이 드럼 비트나 온셋(Onset) 마커와 정확히 겹치도록 **BPM을 조율하는 작업**이 가능해집니다.

---

### 3. 배치된 노트(Notes)의 위치 동기화
이미 그리드 상에 배치된 노트들 또한 BPM이 변경되면 새로운 격자 위치에 맞춰 화면상의 좌표(`tPos`)가 동적으로 변경되어 함께 이동합니다.
BPM 이 하나뿐이고 모든 마디가 4/4 인 차트라면 노트의 절대 시각은 아래와 같습니다.

$$\text{seconds} = (\text{Measure} + \text{Position}) \times \frac{240.0}{\text{BPM}}$$

실제 코드는 이 식을 직접 쓰지 않고 **격자와 같은 `ChartTimeline`** 에 묻습니다. 그래야 BPM 변화·변박이 있는 차트에서도 노트가 제 격자선 위에 남습니다.

#### [노트 위치 계산 코드 스니펫]
```csharp
// NoteGridControl.cs
private double ComputeNoteTPos(BmsNote note, double timelineLength)
{
    if (DurationSeconds > 0 && Bpm > 0)
    {
        // 노트의 시각도 격자와 같은 곳에서 구해야 둘이 어긋나지 않는다.
        var seconds = EffectiveTimeline.SecondsAt(note.Measure + note.Position);
        return ToTimelinePosition(seconds / DurationSeconds, timelineLength);
    }

    // 음원이 없으면 마디 높이가 곧 화면 높이다.
    var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
    var totalOffset = (note.Measure + note.Position) * rowHeight;
    return IsHorizontalView ? totalOffset : (timelineLength - totalOffset);
}
```

- **상호 작용**: BPM이 커지면 `seconds`가 짧아져 노트가 화면 왼쪽(세로뷰의 경우 아래쪽)으로 수축 이동하고, 격자선도 동일한 비율로 수축하므로 **노트가 자신이 속한 격자선 위에 밀착해 함께 움직입니다.**
- 클릭한 자리를 마디 위치로 되돌릴 때도 같은 시간축의 역함수(`ChartTimeline.MeasurePositionAt`)를 씁니다. 키음 재생 시각도 `SecondsAt` 으로 구합니다.
- 홀드 몸통의 양 끝도 각각 `ComputeNoteTPos` 로 구합니다. 마디 단위 선형 보간으로 늘리지 않습니다.

---

### 4. 음원 오프셋과의 관계
음원 오프셋(`#BMSEDITER_OFFSET`, 오른쪽 패널의 「음원 오프셋」)은 **격자와 노트를 움직이지 않습니다.**
음원에서 나온 것(파형 · 온셋 마커 · 재생 커서)만 그만큼 밀어 그립니다(`TimelineControlBase.AudioRatio`).
**파형**을 눌러 재생 위치를 옮길 때는 반대로 오프셋을 빼서 음원 안의 위치로 되돌립니다(`TimelineControlBase.AudioSecondsAtRatio`).
격자에서 가운데 버튼으로 끄는 스크럽(`MainWindow.Scrubbing`)과 재생바 자동 스크롤(`MainWindow.Viewport`)은 오프셋을 빼지 않습니다. 오프셋이 0 이 아니면 그만큼 커서가 누른 자리에서 떨어집니다([known_issues](../issues/known_issues.md#-2026-09-22-점검에서-찾은-것-확인-필요)).
오프셋은 에디터 전용 헤더라 게임은 읽지 않습니다 → [beat_sync_workflow.md](../guides/beat_sync_workflow.md).
