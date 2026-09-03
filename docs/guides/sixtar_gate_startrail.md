# 식스타 게이트: 스타트레일 (Sixtar Gate: STARTRAIL) 연동 가이드

이 문서는 **BMS Editer**로 작성·편집한 BMS 채보 데이터를 **식스타 게이트: 스타트레일(Mono 기반)** 게임 내에 커스텀 차트 및 키음으로 주입(Inject)하는 기술 가이드입니다.

---

## 🎮 1. 개요 및 타겟 환경

* **대상 게임**: *Sixtar Gate: STARTRAIL*
* **런타임 엔진**: Unity (Mono 기반)
* **주요 도구**:
  * **디컴파일 및 정적 분석**: `ilspycmd`를 통한 `Assembly-CSharp.dll` 전체 C# 소스 역개설 및 분석
  * **모드 로더**: `MelonLoader` (Mono 래퍼 및 Harmony 패치)
  * **인스펙터**: `UnityExplorer`

---

## 🎛️ 2. BMS Editer 레인(Lane) ↔ 스타트레일 모드 매핑

BMS Editer의 7개 기본 레인(`16, 11, 12, 13, 14, 15, 18`)을 스타트레일의 각 모드(Solar / Lunar) 및 레인으로 매핑합니다.

| BMS Editer 레인 ID | BMS 채널 | Solar 모드 (4K) | Lunar 모드 (5K + Gate) | 비고 |
|:---:|:---:|:---:|:---:|:---|
| **16** | 스크래치 | Gate Lane (좌/공통) | Gate Lane (Blue Gate) | 게이트 기믹 트리거 |
| **11** | 1P Key 1 | Lane 1 | Lane 1 | 일반 건반 노트 |
| **12** | 1P Key 2 | Lane 2 | Lane 2 | 일반 건반 노트 |
| **13** | 1P Key 3 | (미사용 또는 중앙) | Lane 3 (중앙 건반) | 5키 모드의 중심축 |
| **14** | 1P Key 4 | Lane 3 | Lane 4 | 일반 건반 노트 |
| **15** | 1P Key 5 | Lane 4 | Lane 5 | 일반 건반 노트 |
| **18** | 1P Key 6 | Red Gate (특수) | Red Gate (특수 트리거) | 확장 기믹/시프트 레인 |

---

## ⏱️ 3. 시간 계산 공식 및 BPM 동기화

BMS Editer 내부 및 스타트레일의 틱(Tick) 기반 타이밍 계산은 아래 표준 공식을 따릅니다:

$$\text{time (초)} = \frac{\text{tick} \times 240}{\text{BPM} \times \text{Resolution}}$$

* **단순화 수식**: 4/4 박자 1마디 기준 `time = tick * 240 / bpm`
* BMS Editer의 `ChartTimeline.cs`는 곡 도중의 `#xxx03`(표준 BPM) 및 `#xxx08`(확장 BPM)을 정밀 계산하여 마디 위치를 시각(초)으로 1:1 변환하므로, 익스포트된 `.bms`의 노트 시간축을 게임의 오디오 타임라인에 오차 없이 일치시킬 수 있습니다.

---

## 💉 4. 커스텀 차트 및 키음 주입 (MelonLoader 워크플로우)

### 4.1 차트 파싱 데이터 메모리 바인딩
1. BMS Editer에서 작업한 `.bms` 파일을 게임 모드 디렉터리(`UserData/CustomCharts/`)에 배치합니다.
2. MelonLoader 모드 시작 시 `BmsParser` 로직을 통해 차트의 노트 배열(`BmsNote[]`)과 헤더를 메모리로 로드합니다.
3. 게임 내부의 차트 데이터 컨테이너(예: Track/Chart Data 클래스) 인스턴스를 Reflection 또는 Harmony 프리픽스 훅으로 가로채어 채보 구조체로 치환합니다.

### 4.2 키음(WAV/OGG) 주입 및 재생
* BMS Editer에서 사용된 `#WAV` 테이블의 오디오 파일들을 게임 내 AudioSource 믹서 또는 자체 오디오 풀러에 바인딩합니다.
* 노트를 타격하는 시점에 매핑된 키음 번호(Base-36 Key)를 조회하여 지연 없이 재생합니다.

---

## 🎨 5. 노트스킨 및 판정 파라미터 모딩 팁

* **Custom Note-skin Mod**: 게임 리소스 내 Sprite 및 Material 셰이더 프로퍼티를 교체하여 BMS Editer의 시각적 테마와 어울리는 커스텀 스킨을 주입할 수 있습니다.
* **All-Perfect Parameter Mod**: 판정 윈도우(Hit Window) 판정 범위를 일시적으로 확장하거나 터치 입력 판정 로직을 보정하여 채보 테스트 시 편의성을 극대화할 수 있습니다.
