# BMS Editor - 코드 설명서 (Code Explanation)

이 문서는 BMS 에디터 프로젝트를 구성하는 각 소스 코드 파일의 역할, 내부 아키텍처, 그리고 컴포넌트 간 데이터 흐름을 상세하게 설명합니다.

---

## 📂 1. Models (데이터 모델)

차트 데이터, 헤더 정보, 라인/노트 구조 및 레인 설정을 표현하는 핵심 객체 계층입니다.

| 파일명 | 역할 및 핵심 구조 |
| :--- | :--- |
| **[BmsChart.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsChart.cs)** | BMS 차트 전체 데이터의 컨테이너입니다. 헤더 메타데이터(`Header`), 노트 컬렉션(`Notes`), 키음 파일 테이블(`WavTable`), 확장 BPM 테이블(`BpmTable`), 마디 길이 변경 배율(`MeasureLengths`), BPM 변경 시퀀스(`BpmChanges`), 보존 원문 줄(`PreservedLines`), 조건문 포함 여부(`HasConditionalBlocks`)를 일괄 보관합니다. |
| **[BmsHeader.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsHeader.cs)** | 차트의 기본 메타데이터(곡 제목, 아티스트, 장르, 기본 BPM, 플레이어 모드, 판정 난이도 Rank, 표기 레벨 등)를 저장합니다. |
| **[BmsNote.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsNote.cs)** | 개별 노트 객체입니다. 속한 마디 번호(`Measure`), 레인 ID(`LaneId`), 마디 내 상대 위치(`Position`, 0.0~1.0), 키음 식별 키(`WavKey`), 분기 식별자(`BranchId`), 원본 줄 순서(`SourceLineOrder`)를 가집니다. |
| **[BpmChange.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BpmChange.cs)** | 곡 도중 발생하는 BPM 변화 이벤트 모델입니다. 마디 번호(`Measure`), 마디 내 상대 위치(`Position`), 변경될 새로운 BPM 수치(`Bpm`)를 들고 있습니다. |
| **[BmsRawLine.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsRawLine.cs)** | 에디터가 직접 수정하지 않는 BMS 원문 줄(BGM 레인, 미지원 채널, 주석, 조건 제어문 등)을 유실 없이 저장하기 위한 보존 모델입니다. 줄 순서(`Order`)와 분기 식별자(`BranchId`)를 추적합니다. |
| **[BmsWavItem.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsWavItem.cs)** | UI 팔레트 및 리스트 바인딩용 모델입니다. WAV 키(Base-36)와 파일 경로, 원본 텍스트, 경로 추측 여부(`IsPathGuessed`)를 관리합니다. |
| **[LaneDefinition.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/LaneDefinition.cs)** | 에디터에서 활성화할 건반 레인을 정의합니다. 기본적으로 1P 건반 채널(16: 스크래치, 11~15: 1P 건반 1~5, 18: 1P 건반 6) 구성을 생성합니다. |
| **[GameProfile.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/GameProfile.cs)** | 게임별 특화 설정(프로파일 ID, 표시명, 검증 상태, 활성 레인 및 키 폭, 홀드/게이트/아크 매칭 규칙 `HoldRule`)을 담는 모델입니다. |
| **[HoldModels.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/HoldModels.cs)** | 비파괴 파생 링크 표 모델입니다. 짝지어진 홀드 한 쌍을 가리키는 `HoldLink`(Head, Tail, Kind), 역할 판정 결과 `HoldRoleResult`, 진단 이슈 `HoldIssue` 등을 정의합니다. |

---

## ⚙️ 2. Services (비즈니스 로직 및 엔진)

파일 파싱, 직렬화 저장, 오디오 디코딩, 실시간 믹싱 재생 및 시간축 동기화를 담당하는 엔진 계층입니다.

| 파일명 | 역할 및 핵심 기술 |
| :--- | :--- |
| **[BmsParser.cs](../../bms%20editer/Services/BmsParser.cs)** | 파싱 파이프라인 Core입니다. `BmsParseResult` 정의와 2패스 `Parse()` — 1패스에서 헤더·`#WAV` 정의를 모으고, 2패스에서 데이터 줄을 노트로 풀며 편집 대상이 아닌 줄은 원문 그대로 보존(`PreservedLines`)하고 조건 블록(`#RANDOM`/`#IF`/`#SWITCH`)의 분기 맥락(`BranchId`)을 추적합니다. 아래 4개 파티션으로 나뉘어 있습니다. |
| **[BmsParser.Patterns.cs](../../bms%20editer/Services/BmsParser.Patterns.cs)** | BMS 문법 정규식을 한곳에 모은 파티션입니다. 모든 `[GeneratedRegex]` 선언(헤더·`#WAV`·데이터 줄·제어문)과, 1패스에서 이미 읽어간 헤더인지 판별해 원문 보존 대상을 가리는 `IsConsumedHeader` 가 있습니다. 데이터 줄 정규식은 규격을 넘는 4자리 마디도 받습니다. |
| **[BmsParser.Channels.cs](../../bms%20editer/Services/BmsParser.Channels.cs)** | 시간축 채널 해석 파티션입니다. 마디 길이 배율(`02`)·직접 BPM(`03`)·`#BPMxx` 참조 BPM(`08`)을 읽어 차트에 담고, 슬롯 열거(`EnumerateSlots`)와 2자리/3자리 키음 분할 크기 판정(`DetermineChunkSize`)을 담당합니다. |
| **[BmsParser.MediaPaths.cs](../../bms%20editer/Services/BmsParser.MediaPaths.cs)** | 미디어 경로 해석 파티션입니다. 폴더를 재귀로 훑어 파일명 색인을 만들고, 적힌 자리에 파일이 없으면 같은 이름을 하위 폴더에서 찾아 붙입니다. 그 **추측 여부를 `guessed` 로 구분해 알려주어** 호출한 쪽이 추측 결과를 저장 파일에 박지 않게 합니다. |
| **[BmsParser.Encoding.cs](../../bms%20editer/Services/BmsParser.Encoding.cs)** | 인코딩 감지 파티션입니다. 바이트를 한 번만 읽고 BOM → 엄격 UTF-8 → CP932/CP949 순으로 가릅니다. CP932와 CP949는 서로의 바이트를 오류 없이 삼키므로, **`#WAV`/`#BMP` 파일명이 실제로 폴더에 있는 개수**(1순위)와 반각 가타카나·제어 문자 점수(2순위)로 판정합니다. 고른 인코딩은 저장에 그대로 쓰입니다. |
| **[BmsWriter.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/BmsWriter.cs)** | 차트 모델을 규격에 맞는 BMS 텍스트로 직렬화합니다. 마디 내 노트 위치들의 최소공배수(LCM)를 이용해 무손실 분할 해상도를 계산하고, 원문 보존 줄(`PreservedLines`)과 분기별 노트를 원본 순서대로 안전하게 복원 출력합니다. |
| **[SafeFileWriter.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/SafeFileWriter.cs)** | 원자적(Atomic) 파일 저장 서비스입니다. 같은 디렉터리의 임시 파일(`.tmp`)에 완전히 기록한 뒤에만 원본을 교체하며, 직전 정상 저장본을 `.bak` 파일로 백업하여 충돌이나 예외 시에도 원본 손상을 원천 방지합니다. |
| **[ChartTimeline.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/ChartTimeline.cs)** | **"마디 위치 ↔ 절대 시각(초)" 변환을 전담하는 핵심 동기화 엔진**입니다. 곡 도중의 마디 길이 배율(`#xxx02`)과 BPM 변경(`#xxx03`/`#xxx08`)을 통합 누적 계산하여, 격자선·노트·파형·재생바가 동일한 시각 기준을 공유하도록 보장합니다. |
| **[KeySoundPlayer.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/KeySoundPlayer.cs)** | Win32 `waveOut` API 기반의 언매니지드 네이티브 헤더(`WAVEHDR`) 포인터를 직접 제어하는 실시간 다중 채널(폴리포닉) 키음 믹서입니다. 사전 디코딩된 PCM 캐시를 포화 가산(Saturation Clamping)으로 실시간 합산하여 화음과 고밀도 연타를 끊김 없이 재생합니다. |
| **[WavDecoder.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/WavDecoder.cs)** | WAV (8/16/24/32bit PCM, IEEE Float, `WAVE_FORMAT_EXTENSIBLE`) 및 OGG 오디오를 44100Hz 16-bit Stereo PCM 샘플 배열로 균일하게 디코딩하고 리샘플링합니다. |
| **[WavKey.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/WavKey.cs)** | Base-36 키음 번호(`01`~`ZZ`, `001`~`ZZZ`)의 파싱, 유효성 검증, 서식 변환 규칙을 단일화하여 뷰모델 및 파서 전반에서 키음 번호 일관성을 유지합니다. |
| **[OggDecoder.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/OggDecoder.cs)** | 배경 음악(OGG) 파일을 메모리에 단 1회만 PCM16으로 백그라운드 디코딩하여, 재생 엔진과 파형 분석기가 결과 데이터를 공유하게 합니다. |
| **[OggAudioPlayer.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/OggAudioPlayer.cs)** | `winmm.dll`의 `waveOut` 함수군을 P/Invoke 소스 생성(`[LibraryImport]`)으로 직접 호출하여 지연 시간을 최소화한 OGG 실시간 재생 및 탐색(Scrubbing)을 제공합니다. |
| **[OggPeakLoader.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/OggPeakLoader.cs)** | 디코딩된 PCM 신호의 진폭 피크(Peak) 배열을 고속 다운샘플링하고, 에너지 급증 구간을 분석하여 비트 온셋(Onset) 마커를 추출합니다. 버킷 누적 오차를 방지하는 정밀 비율 공식을 사용합니다. |
| **[GameProfileCatalog.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/Holds/GameProfileCatalog.cs)** | `Profiles/` 디렉터리의 내장 및 사용자 정의 JSON 프로파일을 로드하고, 차트의 헤더(`#BMSEDITER_PROFILE`) 또는 파일명/폴더 경로 힌트를 분석하여 알맞은 게임 프로파일을 매칭합니다. |
| **[HoldPairingEngine.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/Holds/HoldPairingEngine.cs)** | 프로파일 규칙에 따라 차트의 노트를 순회하며 롱노트/게이트/아크의 Head와 Tail을 매칭하는 핵심 페어링 파이프라인입니다. 짝지어진 `HoldLink` 컬렉션과 고아/짝 오류 이슈(`HoldIssue`)를 생성합니다. |
| **[HoldPolicies.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/Holds/HoldPolicies.cs)** | 짝 매칭 시의 중첩 및 순서 정책(`Fifo`, `Lifo`, `Alternate`, `ParityBreak` 등)을 실행하는 알고리즘 집합입니다. |
| **[HoldRoleClassifier.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/Holds/HoldRoleClassifier.cs)** | 노트의 키음 번호(`KeyValue`), 파일명 키워드(`FileNameKeyword`), 파일 기본명(`FileNameBase`), UID 접두사(`UidPrefix`) 등을 검사해 노트의 역할(Head, Tail, Standalone)을 분류합니다. |

---

## 🖥️ 3. ViewModels (MVVM 뷰모델)

사용자 입력 명령 처리, 화면 바인딩 상태 유지 및 창 간 통신을 제어하는 프레젠테이션 로직 계층입니다.

| 파일명 | 역할 및 특징 |
| :--- | :--- |
| **[MainWindowViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/MainWindowViewModel.cs)** | 메인 화면의 중심 뷰모델(Core)입니다. 차트 상태, 마디 수 계산, BPM 및 타임라인(`ChartTimeline`) 연동, UI 뷰/격자 바인딩 속성, 변경 추적(`Dirty Tracking`) 및 `IDisposable` 수명주기 해제를 총괄합니다. |
| **[MainWindowViewModel.Playback.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/MainWindowViewModel.Playback.cs)** | 오디오 및 키음 재생 파티션입니다. `Play`, `Stop`, `TogglePlayback`, 스크러빙(`ScrubPreview`/`ScrubCommit`), `waveOut` 하드웨어 클럭 기준의 재생 위치 갱신 및 이진 탐색 기반 실시간 키음 동기화 트리거를 담당합니다. |
| **[MainWindowViewModel.FileIO.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/MainWindowViewModel.FileIO.cs)** | 파일 및 미디어 입출력 파티션입니다. BMS 열기/저장, 원본 인코딩 보존 및 손실 방지 UTF-8 폴백, OGG 비동기 디코딩 로드, 비디오 파일 연결, 새 파일 초기화 및 변경사항 폐기 확인 콜백을 담당합니다. |
| **[MainWindowViewModel.Editing.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/MainWindowViewModel.Editing.cs)** | 채보 노트 편집 및 선택 파티션입니다. 노트 배치/삭제/이동(경계 체크 및 충돌 감지 포함), 마디 단위 복사, 키음 일괄 교체, 다중 선택 상태(`SelectedNotes`) 및 키음 팔레트 등록/삭제를 담당합니다. |
| **[MainWindowViewModel.Holds.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/MainWindowViewModel.Holds.cs)** | 게임 프로파일 및 홀드 페어링 파티션입니다. 현재 차트의 프로파일 로드/전환(`ActiveProfile`), 짝 맞추기 엔진 재계산 및 `HoldLinks`/`HoldIssues` 컬렉션 갱신, 미검증 프로파일 경고 상태를 총괄합니다. |
| **[ControlPanelViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/ControlPanelViewModel.cs)** | **🎛️ 컨트롤 패널** 전용 뷰모델입니다. `NoteStatsViewModel`의 집계 로직을 상속받아, 고른 레인/키음의 격자 노트 일괄 선택, 화면 밖 대상 위치로 자동 스크롤(포커스), 키음 즉시 미리듣기, 번호 일괄 교체 및 2단계 확인 후 일괄 삭제를 수행합니다. |
| **[NoteStatsViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/NoteStatsViewModel.cs)** | **📊 통계 창(보기 전용)**의 뷰모델입니다. 실제로 쓰인 레인과 키음별 노트 수를 단일 루프로 고속 집계하며, 편집 알림을 실시간 구독하여 수치가 동기화됩니다. |
| **[NoteSearchViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/NoteSearchViewModel.cs)** | 조건 검색/삭제/교체 모드리스 창의 뷰모델입니다. 마디 범위, 키음 범위, 레인 필터를 조합해 노트를 조건 검색하고 격자에서 즉시 선택·일괄 삭제·번호 변경을 실행합니다. |
| **[WavPaletteViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/WavPaletteViewModel.cs)** | 키음 팔레트 창의 뷰모델입니다. 등록된 WAV 키음 목록과 각 키음의 사용 횟수를 모니터링하고 미리듣기 및 선택 상태를 동기화합니다. |
| **[OwnerObservingViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/OwnerObservingViewModel.cs)** | 보조 창(통계, 컨트롤 패널, 팔레트 등)들이 메인 뷰모델의 이벤트를 구독하고 창이 닫힐 때 안전하게 구독 해제(`Dispose`)할 수 있도록 돕는 추상 기본 클래스입니다. |
| **[BulkObservableCollection.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/BulkObservableCollection.cs)** | 대량의 아이템을 추가/교체할 때 매 아이템마다 변경 알림이 발생하는 성능 저하를 막고, 일괄 처리 후 단 1회의 `Reset` 알림만 발생시키는 특수 컬렉션입니다. |

---

## 🎨 4. Views & Controls (UI 화면 및 커스텀 컨트롤)

Avalonia UI 기반의 렌더링 파이프라인과 네이티브 인터랙션을 담당하는 화면 계층입니다.

| 파일명 | 역할 및 렌더링 메커니즘 |
| :--- | :--- |
| **[MainWindow.axaml](../../bms%20editer/MainWindow.axaml)** | 최상위 윈도우의 레이아웃입니다. 메뉴바, 툴바, 게임 프로파일 콤보박스 및 미검증 배너, 가로/세로 뷰 스위처, 사이드 패널 구성을 정의합니다. 코드 비하인드는 아래 6개 파티션으로 나뉘어 있습니다(`MainWindowViewModel` 과 같은 분할 방식). |
| **[MainWindow.axaml.cs](../../bms%20editer/MainWindow.axaml.cs)** | 코드 비하인드 Core입니다. 뷰모델 생성과 이벤트 배선, 저장 안 한 작업을 지키는 창 닫기 확인(`Closing` 은 await 할 수 없어 취소 후 재시도), 창 제목 갱신, 그리고 뷰모델 변경 알림을 각 파티션으로 보내는 라우팅을 담당합니다. |
| **[MainWindow.FileIO.cs](../../bms%20editer/MainWindow.FileIO.cs)** | 파일·미디어 대화상자 파티션입니다. BMS/OGG/비디오/WAV 선택, 폴더 열기 시 차트·음원·영상 자동 탐색(`FindBestFile`), 저장 경로 선택과 제목→안전한 파일명 변환, 저장 실패·경고 보고를 담당합니다. |
| **[MainWindow.Input.cs](../../bms%20editer/MainWindow.Input.cs)** | 키보드 입력 파티션입니다. 창 전체에서 받는 단축키(Space 재생, Delete 삭제, Esc 선택 해제, 방향키 이동, Ctrl+S/Shift+S/O/N)와, 텍스트 입력·목록·슬라이더에 포커스가 있을 때 단축키를 양보하는 `IsWithin<T>` 판정을 담당합니다. |
| **[MainWindow.ToolWindows.cs](../../bms%20editer/MainWindow.ToolWindows.cs)** | 모드리스 보조 창 파티션입니다. 검색·통계·컨트롤 패널·키음 팔레트를 종류당 하나만 띄우고, 창이 닫힐 때 뷰모델 구독을 반드시 해제해 누수를 막습니다(`ShowToolWindow<TWindow>`). |
| **[MainWindow.Scrubbing.cs](../../bms%20editer/MainWindow.Scrubbing.cs)** | 포인터 스크러빙 파티션입니다. 휠 클릭 드래그로 재생 위치를 끌고(뗄 때 한 번만 커밋), 파형 컨트롤의 스크럽 요청을 받고, Tunnel 단계에서 창 전체의 클릭을 미리 가로채 재생 중 즉시 정지시킵니다. |
| **[MainWindow.Viewport.cs](../../bms%20editer/MainWindow.Viewport.cs)** | 스크롤·방향 파티션입니다. 가로/세로 전환 시 레이아웃 방향 교체, 검색·통계 창에서 고른 노트 자리로 격자 이동(레이아웃 완료를 기다려 한 박자 뒤 실행), 재생 커서 자동 추적, 타임라인 길이 계산을 담당합니다. |
| **[TimelineControlBase.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/Controls/TimelineControlBase.cs)** | `NoteGridControl`과 `OggWaveformControl`이 상속하는 베이스 컨트롤입니다. 줌 배율, 스크롤 오프셋, `BeatSplit`(기본 16분할) 기준 격자선 위치 열거(`EnumerateGridLines`), 재생 헤드 커서 및 싱크 경고 점멸 플래시 렌더링을 일원화하여 공유합니다. |
| **[NoteGridControl.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/Controls/NoteGridControl.cs)** | 채보 격자판과 노트를 그리는 핵심 드로잉 컨트롤입니다. Skia 캔버스 클리핑 기반으로 무결성 렌더링을 보장하며, 좌클릭 노트 배치/드래그 선택, 우클릭 삭제, 선택 노트 강조뿐 아니라 **`HoldLinks` 기반의 반투명 홀드/게이트/아크 몸통(Body) 및 연결선 드로잉**을 수행합니다. |
| **[OggWaveformControl.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/Controls/OggWaveformControl.cs)** | 배경 음악 파형과 온셋 타격선 가이드를 그리는 컨트롤입니다. 마우스 휠 스크러빙 및 재생 위치 클릭 탐색 인터랙션을 제공합니다. |
| **[VideoPreviewControl.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/Controls/VideoPreviewControl.cs)** | Windows 네이티브 `WebView2` HWND를 기반으로 BGA 배경 비디오(MP4, WebM 등)를 로드하고 재생 타임라인과 밀리초 단위로 실시간 동기화합니다. |
| **[ControlPanelWindow.axaml / .cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/ControlPanelWindow.axaml)** | 툴바의 🎛️ 버튼으로 띄우는 모드리스 컨트롤 패널입니다. 레인/키음 목록을 클릭하여 즉시 격자 포커싱 및 미리듣기를 수행할 수 있습니다. |
| **[ConfirmWindow.axaml / .cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/ConfirmWindow.axaml)** | 저장되지 않은 변경사항 확인이나 대량 삭제 시 호출되는 3지선다(저장 / 저장 안 함 / 취소) 대화상자입니다. |
