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

#### [렌더링 구현 코드 스니펫]
```csharp
// NoteGridControl.cs 및 OggWaveformControl.cs 공통
var secondsPerStep = 240.0 / (Bpm * split);
for (var index = 0; ; index++)
{
    var seconds = index * secondsPerStep;
    if (seconds > DurationSeconds) return;

    var ratio = seconds / DurationSeconds;
    // 고정된 timelineLength 상에서 ratio 비율에 맞춰 선의 위치(tPos)를 도출
    var tPos = IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
    
    // ... tPos 위치에 마디/박자 선 그리기 수행 ...
}
```

---

### 2. 파형(Waveform)의 고정 맵핑
- 배경 음악 파형(`OggWaveformControl`)은 오디오 버퍼의 실제 인덱스를 화면 픽셀 비율에 1:1 대응하여 그립니다.
- 따라서 **BPM이 아무리 바뀌어도 오디오 파형 자체는 늘어나거나 줄어들지 않고 제자리에 유지**됩니다.
- 이 구조 덕분에 사용자는 고정된 실제 오디오 파형 진폭(볼륨이 피크를 치는 지점)과 움직이는 마디 격자선을 비교하며, 마디 시작선이 드럼 비트나 온셋(Onset) 마커와 정확히 겹치도록 **BPM을 조율하는 작업**이 가능해집니다.

#### 이 대조가 얼마나 정밀한가

BPM을 맞춘다는 것은 결국 "마디선과 어택이 겹쳐 보이는가"를 눈으로 판정하는 일입니다.
그래서 **파형이 표현할 수 있는 시간 단위가 곧 BPM 판정의 정밀도**가 됩니다.

| 항목 | 값 | 근거 |
|------|-----|------|
| 버킷 한 칸 | 2.5ms | `OggPeakLoader.DefaultPeaksPerSecond = 400` |
| 화면 블록 한 칸 | 1px (세로 줌 8에서 약 3.9ms) | `OggWaveformControl.BlockLength` |
| 온셋 마커 오차 | 2ms 안쪽 (합성 클릭 트랙 실측) | `WaveformOnsetTests` |

BPM 141이면 16분음표가 106ms입니다. 예전 해상도(12.5ms)로는 첫 어택을 고를 때부터 12%가 흔들렸고,
그 오차는 마디가 쌓일수록 그대로 커져서 106마디 끝에서는 마디 하나가 통째로 밀렸습니다.

세 가지 규칙이 이 정밀도를 지탱합니다.

1. **버킷 ↔ 시각 규칙은 한 군데서만 정의한다** — `OggPeakLoader.GetBucketRatio`.
   같은 규칙을 그리는 쪽에서 다시 쓰다가 네 번 어긋났습니다. (`WaveformTimeAxisTests` 주석 참고)
2. **화면 블록은 구간을 집계한다** — `OggPeakLoader.GetBlockSourceRange` 가 돌려주는 `[Start, End)` 안의 최댓값을 씁니다.
   블록마다 버킷을 하나만 찍어 읽으면, 줌을 줄였을 때 버킷 대부분이 버려져 드럼 한 방이 화면에서 사라집니다.
3. **마커는 봉우리가 아니라 어택의 앞날을 가리킨다** — 소리가 이미 커진 지점(flux 꼭대기)에서
   에너지가 오르기 시작한 지점까지 되짚고, 버킷 두 칸 사이를 보간해 버킷보다 잘게 잡습니다.

#### 곡이 격자에서 벗어나 있을 때 (음원 오프셋)

여기까지가 다 맞아도 화면이 어긋나 보일 수 있습니다. **곡 자체가 격자에 안 맞는 경우**입니다.

실제로 겪은 곡(BPM 141, 171초, 48kHz)에서 잰 값입니다.

| 확인 | 결과 |
|------|------|
| 디코딩이 앞을 잘라먹나 | 아니오. NVorbis `TotalSamples` 8,239,344 = `OggDecoder` 프레임 수 |
| 파형과 격자가 다른 공식을 쓰나 | 아니오. 둘 다 `seconds / DurationSeconds → ratio → 픽셀` |
| BPM 141 이 틀렸나 | 아니오. 171초 전 구간 잔차 -0.6 ~ -2.9ms, 드리프트 없음 |
| 곡이 격자에서 벗어났나 | **예. 강한 어택 366개의 16분 격자 대비 중앙 편차 -23.1ms** |

이 곡은 앞에 한 마디(1.7021s) 무음을 두고 시작하는데 실제 소리는 1.6792s 에서 나옵니다.
22.9ms, 세로 줌 8 에서 6px 입니다. 눈에 잘 안 띄는 크기인데, 이 상태로 BPM 을 맞추려 들면
**맞는 BPM 을 틀렸다고 판단하고 고치게 됩니다.**

`AudioOffsetSeconds` 가 이걸 받습니다. 격자와 노트는 차트 시간축 위에 그대로 두고,
**음원에서 나온 것만** 밉니다. 미는 대상은 세 가지이고 전부 같은 변환을 거쳐야 합니다.

| 대상 | 어디서 |
|------|--------|
| 파형 | `OggWaveformControl.GetBucketShift` — 같은 화면 자리에서 그만큼 앞쪽 버킷을 읽습니다 |
| 온셋 마커 | `TimelineControlBase.AudioRatio` |
| 재생 커서 | `TimelineControlBase.AudioRatio`, 자동 스크롤은 `MainWindowViewModel.PlaybackCursorRatio` |

반대로 화면에서 음원으로 돌아가는 길(스크럽, 키음 타이밍)에서는 빼야 합니다.
`ScrubPreview`/`ScrubCommit` 은 `AudioSecondsAtRatio` 로 빼고, `PlayNotesInTimeRange` 는
노트 시각이 차트 시간축이므로 재생 위치에 오프셋을 **더해서** 맞춥니다.

자동 검출(`TryDetectAudioOffsetMs`)은 후보를 **16분음표의 ±절반 안에서만** 훑습니다.
온셋은 격자에 대해 주기적이라 한 칸 밀어도 점수가 같습니다. 범위를 열어 두면 이 곡에서
-23ms 대신 +80ms(= -23 + 106.4) 가 뽑혔습니다. 0 에 가장 가까운 답이 옳은 답입니다.

---

#### 약한 마커를 세우지 않는 이유

검출된 봉우리를 전부 그리면 안 됩니다. 실제 음원(BPM 141, 171초)에서 세기별로
16분 격자에 붙는 비율(10ms 이내)을 재 보면 이렇게 갈립니다.

| 세기 | 개수 | 격자 위 |
|------|------|---------|
| 0.00 ~ 0.20 | 193 | 13% |
| 0.20 ~ 0.35 | 583 | 17% |
| 0.35 ~ 0.50 | 306 | 31% |
| 0.50 ~ 0.75 | 315 | 55% |
| 0.75 ~ 1.00 | 366 | **86%** |

약한 봉우리는 리버브 꼬리나 지속음의 출렁임이지 박자가 아닙니다. 보기에 지저분한 정도가
아니라 **틀린 기준선**이라, 격자 옆에 세워 두면 BPM 을 맞출 때 엉뚱한 선에 눈이 갑니다.

격자를 맞추는 기준으로 쓸 것은 **진하게 그려지는 것(세기 0.45 초과)** 입니다.
`MinOnsetStrength = 0.12` 는 v0.1.0 과 같은 마커 밀도를 내는 값이고, 이걸 올리면
화면은 깨끗해지지만 v0.1.0 과 다른 그림이 됩니다.

파형 자체는 격자를 맞추기 위한 배경이지 주인공이 아닙니다. 진폭 계수(`0.58`)와
채우기 투명도(`125`)를 v0.1.0 그대로 두는 이유가 그것입니다. 피크와 RMS 를 따로 겹쳐
진하게 그려 봤더니 파형이 주인공이 되어 정작 그 위의 마디선과 마커가 묻혔습니다.

---

### 3. 배치된 노트(Notes)의 위치 동기화
이미 그리드 상에 배치된 노트들 또한 BPM이 변경되면 새로운 격자 위치에 맞춰 화면상의 좌표(`tPos`)가 동적으로 변경되어 함께 이동합니다.
노트의 초 단위 절대 위치 계산 공식은 다음과 같습니다:

$$\text{seconds} = (\text{Measure} + \text{Position}) \times \frac{240.0}{\text{BPM}}$$

#### [노트 위치 계산 코드 스니펫]
```csharp
// NoteGridControl.cs
private double ComputeNoteTPos(BmsNote note, double timelineLength)
{
    if (DurationSeconds > 0 && Bpm > 0)
    {
        var secondsPerMeasure = 240.0 / Bpm;
        var seconds = (note.Measure + note.Position) * secondsPerMeasure;
        var ratio = seconds / DurationSeconds;
        
        // 최종적으로 파형과 동일하게 오디오 비율(ratio) 기준으로 화면 상 위치 연산
        return IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
    }
    // ...
}
```

- **상호 작용**: BPM이 커지면 `seconds`가 짧아져 노트가 화면 왼쪽(세로뷰의 경우 아래쪽)으로 수축 이동하고, 격자선도 동일한 비율로 수축하므로 **노트가 자신이 속한 격자선 위에 완벽히 밀착해 함께 움직이는 시각적 효과**를 내게 됩니다.
