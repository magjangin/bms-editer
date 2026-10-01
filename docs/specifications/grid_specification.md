# BMS Editor - 기본 그리드 분할 명세서 (Grid Split Specification)

이 문서는 BMS 에디터의 마디(Measure) 내부 그리드 분할 규칙에 대한 핵심 동작 사양과, 그 규칙이 구현된 코드 위치를 정의합니다.

> 코드를 이 문서에 복사해 두지 않습니다. 복사본은 코드가 바뀔 때마다 조용히 어긋났고(클릭 배치 코드가 `OnPointerPressed` 에서 `PlacementAt` 으로 옮겨진 뒤에도 옛 모양이 남아 있었습니다),
> 문서를 믿고 코드를 고치는 사람을 헷갈리게 합니다. 규칙은 여기서 말로 정하고, 코드는 아래 링크로 직접 보십시오.

---

## 📌 기본 요구 사항: 마디당 16개 그리드 선 배치
BMS 에디터가 처음 구동되거나 차트가 초기화될 때, **마디와 마디 사이(1마디 공간)에는 무조건 16개의 그리드 선(16분할선)**이 생성되어야 합니다.

* **정의**: 4/4 박자를 기준으로 한 마디를 16등분하는 그리드(16분음표 단위 격자)가 초기 기준이 됩니다.
* **목적**: 채보 제작자가 노트를 배치할 때 가장 대중적인 단위인 16비트(16분음표) 격자에 정확히 맞물려(Snap) 배치되도록 하기 위함입니다.

---

## 💻 규칙이 구현된 자리

### 1. 기본값과 박자선 판별 — [`TimelineControlBase`](../../bms%20editer/Views/Controls/TimelineControlBase.cs)

* 격자 분할 수 `BeatSplit` 의 기본값은 **16** 입니다(`BeatSplitProperty` 의 초기값). 한 마디에 박자선을 몇 개 그을지 정하는 `GridMeasure`(화면의 `비트 / 마디` 중 "마디" 칸)의 기본값은 4 입니다.
* 격자선 하나가 마디선인지 · 박자선인지 · 보조선인지는 `ClassifyGridLine` 이 정합니다.
  * `index` 가 `BeatSplit` 의 배수면 **마디선**입니다.
  * 아니면 `IsMeasureBeatLine` — `BeatSplit` 이 `GridMeasure` 의 배수이고 `index` 가 `BeatSplit / GridMeasure` 의 배수일 때 **박자선**입니다.
  * 나머지는 **보조선**입니다.

### 2. 격자선을 만드는 엔진 — `TimelineControlBase.EnumerateGridLines`

예전에는 컨트롤마다 `240 / (Bpm * split)` 수식을 직접 돌려 계산했으나, 변박(`#xxx02`)과 BPM 변경(`#xxx03`/`#xxx08`)이 발생할 때 파형과 격자가 어긋나는 문제가 있었습니다.
현재는 `EnumerateGridLines` 가 `ChartTimeline`(`EffectiveTimeline`)을 통해 정확한 시각을 계산하고 선을 **단일 공급**합니다.

* **배경 음원이 있으면** 격자 칸을 `0, 1/split, 2/split …` 마디 위치로 차례로 훑으며, 각 위치의 시각을 `ChartTimeline.SecondsAt` 에 묻습니다. 시각이 음원 길이를 넘으면 멈춥니다.
  화면 위치는 `시각 / 음원 길이` 비율을 타임라인 길이에 곱해 구합니다.
* **음원이 없으면** 마디 높이(`RowHeight × VerticalZoom × 간격 배율`)를 그대로 곱해 위치를 구합니다. 마지막 마디의 닫는 선까지 그립니다.
* 세로 뷰는 아래가 0초라 위치를 뒤집습니다(`ToTimelinePosition`).

`NoteGridControl` 과 `OggWaveformControl` 은 이 열거 결과를 받아 그리기만 하므로 동기화 기준이 완전히 일원화되어 있습니다.
격자선 계산이 이 함수 밖에서 다시 생기지 않게 하십시오. 규칙은 [`GridLineTests`](../../bms%20editer.Tests/GridLineTests.cs) 가 좌표로 고정합니다.

### 3. 마우스 클릭 배치 시의 스냅 처리 — [`NoteGridControl.PlacementAt`](../../bms%20editer/Views/Controls/NoteGridControl.cs)

편집 모드에서 좌클릭으로 노트를 찍거나 우클릭으로 지울 때, 누른 자리를 레인·마디·마디 안 위치로 바꾸는 일은 `PlacementAt` **한 곳**이 맡습니다.
클릭한 자리를 마디 위치로 되돌리는 일은 격자를 그린 것과 **같은 시간축**(`ChartTimeline.MeasurePositionAt`)이 맡습니다.
예전처럼 `240 / Bpm` 을 직접 쓰면 BPM 변화·변박 뒤쪽에서 클릭한 자리와 찍히는 자리가 어긋납니다.

* **마디 수 밖을 누르면 찍지 않습니다.** 음원이 마디 수보다 긴 차트에서 곡 뒤쪽을 눌렀을 때, 누른 자리 대신 마지막 마디 끝 칸에 노트가 생기던 문제를 막기 위해서입니다(2026-09-28, [resolved_issues H-2](../issues/resolved_issues.md#-h-2026-09-28-점검에서-찾아-고친-것)).
* **"격자에 맞추기"가 켜져 있으면** 클릭 위치를 `BeatSplit` 격자에 반올림합니다. 맨 끝 칸의 뒤쪽 절반은 반올림이 마디 경계를 넘지만, 거부하지 않고 **마지막 칸으로 당겨 줍니다.**
* **"격자에 맞추기"를 끄면** 클릭한 자리에 그대로 찍습니다(잇단음 등). 마지막 칸 이후로는 넘어가지 않게 자릅니다.
* 결과는 [`NotePlacementArgs`](../../bms%20editer/Models/BmsNote.cs) 로 뷰모델에 넘어갑니다. 이 규칙은 [`NoteEditingTests`](../../bms%20editer.Tests/NoteEditingTests.cs) 의 "마디 수 밖을 누르면 찍지 않는다"가 고정합니다.

---

## ✂️ 그 밖의 편집 규칙

* 우클릭 삭제는 같은 마디·레인에서 **격자 반 칸 안의 가장 가까운 노트 하나**를 지웁니다(`MainWindowViewModel.RemoveNote`).
* 방향키 이동은 격자 한 칸만큼 **옮길 뿐 격자에 다시 붙이지 않습니다.** 12분할로 찍은 잇단음을 16분할 상태에서 옮겨도 잇단음이 유지됩니다.
