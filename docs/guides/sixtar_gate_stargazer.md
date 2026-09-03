# 식스타 게이트: 스타게이저 (Sixtar Gate: STARGAZER) 연동 가이드

이 문서는 **BMS Editer**에서 작업한 BMS 차트를 **식스타 게이트: 스타게이저(Il2Cpp 기반)** 환경에서 정적 시그니처 분석과 메타데이터 훅을 거쳐 주입하는 기술 가이드입니다.

---

## 🔬 1. 개요 및 타겟 환경

* **대상 게임**: *Sixtar Gate: STARGAZER*
* **런타임 엔진**: Unity (Il2Cpp 기반)
* **핵심 분석 도구**:
  * **정적 시그니처 덤퍼**: `SignatureDumper` (독립 콘솔 도구, MelonLoader 생성 Il2Cpp 어셈블리를 `System.Reflection`으로 분석하여 `.cs` 스켈레톤 추출)
  * **모드 로더**: `MelonLoader` (Il2Cpp 도메인 관리 및 언매니지드 포인터 핸들링)

---

## 🧩 2. 메타데이터 객체 수정 (`INNER_TrackMetaData`)

Il2Cpp 환경에서는 C# 리플렉션을 직접 사용하는 대신 덤프된 타입 스켈레톤의 내부 프로퍼티 구조를 파악하고 필드 오프셋/접근자를 조작해야 합니다.

### 2.1 곡 정보 조작 및 Title Getter 훅
* 스타게이저 내부의 곡 선택 및 차트 로딩 루틴에서 곡 제목을 반환하는 `getter` 메서드를 가로챕니다.
* 커스텀 BMS의 메타데이터(`#TITLE`, `#ARTIST`, `#BPM`)를 런타임에 동적으로 주입합니다.

### 2.2 `INNER_TrackMetaData` 프로퍼티 활용
* 곡 객체의 내부 메타데이터를 관리하는 `INNER_TrackMetaData` 프로퍼티에 접근하여, 기본 수록곡 목록에 커스텀 곡/채보 엔트리를 안전하게 등록하거나 기존 엔트리의 채보 데이터 경로를 사용자 지정 BMS 파일로 스왑합니다.

---

## 📊 3. BMS Editer 차트 데이터 변환 파이프라인

BMS Editer로 작성된 데이터 모델(`BmsChart`, `BmsNote`)은 스타게이저의 Il2Cpp 내부 차트 구조체로 다음과 같이 매핑됩니다:

```
[BMS Editer]                                 [Sixtar Gate: STARGAZER]
BmsHeader (BPM, Title, Artist)  ───────>    INNER_TrackMetaData / Title Getter
BmsNote (Measure, Position, LaneId) ───>    Il2Cpp Note Event Buffer (Time, Lane)
WavTable (Key, FilePath)        ───────>    Native Audio Buffer / Sound Bank
```

1. **시간 변환**:
   $$\text{time (초)} = \text{Timeline.SecondsAt}(\text{measure} + \text{position})$$
   BMS Editer의 `ChartTimeline`을 통해 마디 위치를 절대 초(Seconds) 단위로 변환합니다.
2. **Il2Cpp 네이티브 배열 할당**:
   변환된 노트 시퀀스를 Il2Cpp의 네이티브 포인터 메모리 블록에 할당하여 가비지 컬렉터의 간섭 없이 안정적으로 공급합니다.

---

## 🛠️ 4. Il2Cpp 안정성 확보 팁

1. **독립 도구 분리 원칙**: `SignatureDumper`와 같은 메타데이터 분석 도구는 게임 런타임에 혼합하지 않고 사전에 `.cs` 스켈레톤을 추출하는 용도로만 독립 운용합니다.
2. **언매니지드 메모리 정합성**: 노트를 연속 타건할 때 Il2Cpp 객체 래퍼가 소멸되지 않도록 참조를 유지하고, BMS Editer의 원자적 저장(`SafeFileWriter`) 기능을 활용해 차트 파싱 도중 파일 락(Lock) 충돌을 방지합니다.
