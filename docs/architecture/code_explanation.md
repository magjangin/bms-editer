# BMS Editor - 코드 설명서 (Code Explanation)

이 문서는 BMS 에디터 프로젝트를 구성하는 각 소스 코드 파일의 역할, 내부 아키텍처, 그리고 컴포넌트 간 데이터 흐름을 설명합니다.
링크는 저장소 안의 상대 경로라 GitHub 에서도, 다른 PC 에 받은 사본에서도 열립니다.

---

## 📂 1. Models (데이터 모델)

차트 데이터, 헤더 정보, 라인/노트 구조 및 레인 설정을 표현하는 핵심 객체 계층입니다.

| 파일명 | 역할 및 핵심 구조 |
| :--- | :--- |
| **[BmsChart.cs](../../bms%20editer/Models/BmsChart.cs)** | BMS 차트 전체 데이터의 컨테이너입니다. 헤더(`Header`), 노트(`Notes`), 키음 표(`WavTable`), 확장 BPM 표(`BpmTable`), 마디 길이 배율(`MeasureLengths`), BPM 변화(`BpmChanges`), 보존 원문 줄(`PreservedLines`), 조건 블록 포함 여부(`HasConditionalBlocks`)를 보관합니다. (`HasConditionalBlocks` 의 코드 주석은 "이 표시를 보고 저장을 막는다" 고 적혀 있지만, 지금은 저장을 막는 곳이 없습니다.) 읽어 온 차트를 옮기는 자리는 `ReplaceContentWith` 한 곳입니다(컬렉션이 늘면 여기만 고칩니다). |
| **[BmsHeader.cs](../../bms%20editer/Models/BmsHeader.cs)** | 제목·아티스트·장르·기본 BPM·플레이어 모드·Rank·레벨과, 에디터 전용 헤더 두 개 — 음원 오프셋(`AudioOffsetMs` = `#BMSEDITER_OFFSET`)과 게임 프로파일(`ProfileId` = `#BMSEDITER_PROFILE`) — 를 저장합니다. 경로로 추정한 프로파일은 여기 넣지 않습니다. |
| **[BmsNote.cs](../../bms%20editer/Models/BmsNote.cs)** | 개별 노트입니다. 마디(`Measure`), 레인(`LaneId`), 마디 내 위치(`Position`, 0.0~1.0), 키음(`WavKey`), 조건 블록 갈래(`BranchId`), 원본 줄 순서(`SourceLineOrder`)를 가집니다. 선택·이동·복사에 쓰는 작은 레코드(`NotePlacementArgs`, `NoteSelectionArgs`, `NoteCopyResult`)도 여기 있습니다. |
| **[BpmChange.cs](../../bms%20editer/Models/BpmChange.cs)** | 곡 도중 BPM 변화 한 건(`Measure`, `Position`, `Bpm`)입니다. `#xxx03`·`#xxx08` 어느 쪽으로 왔든 같은 모양으로 다룹니다. |
| **[BmsRawLine.cs](../../bms%20editer/Models/BmsRawLine.cs)** | 에디터가 직접 편집하지 않는 원문 줄(BGM, 미지원 채널, 주석, 조건 제어문 등)을 유실 없이 보관합니다. 줄 순서(`Order`), 갈래(`BranchId`), 제어문 여부(`IsControlFlow`)를 추적합니다. |
| **[BmsWavItem.cs](../../bms%20editer/Models/BmsWavItem.cs)** | 키음 목록의 한 줄입니다. 키, 재생에 쓰는 실제 경로(`FilePath`), 파일에 적혀 있던 원문(`SourceText`), 경로 추측 여부(`IsPathGuessed`)를 가집니다. 추측한 경로는 저장할 때 원문으로 되돌려 씁니다. |
| **[LaneDefinition.cs](../../bms%20editer/Models/LaneDefinition.cs)** | 편집 레인을 정의합니다. 기본은 1P 건반 채널 `16, 11, 12, 13, 14, 15, 18` 입니다. |
| **[GameProfile.cs](../../bms%20editer/Models/GameProfile.cs)** | 게임 프로파일(JSON) 모델입니다. id·표시명·검증 상태(`ProfileVerification` = `Assumed`/`Source`/`Playtested`)·경로 힌트·홀드 규칙(`HoldRule`)과, 규칙 안의 판별 방식(`HoldRoleSource` = `KeyValue`/`FileNameKeyword`/`FileNameBase`/`FileNameUid`)·짝 정책(`HoldPairingPolicy` 5종)·범위(`HoldScope`)·UID 표(`UidTable`)를 정의합니다. |
| **[HoldModels.cs](../../bms%20editer/Models/HoldModels.cs)** | 비파괴 파생 링크 모델입니다. 짝 한 쌍 `HoldLink(Head, Tail, Kind)`, 진단 `HoldDiagnostic`(종류 `HoldDiagnosticKind` 6가지 · 수준 `Error`/`Warning` · 노트 · 메시지), 결과 묶음 `HoldPairingResult` 를 정의합니다. `Chart.Notes` 는 건드리지 않습니다. |

---

## ⚙️ 2. Services (비즈니스 로직 및 엔진)

파일 파싱, 직렬화 저장, 오디오 디코딩, 실시간 믹싱 재생 및 시간축 동기화를 담당하는 엔진 계층입니다.

| 파일명 | 역할 및 핵심 기술 |
| :--- | :--- |
| **[BmsParser.cs](../../bms%20editer/Services/BmsParser.cs)** | 파싱 파이프라인 Core입니다. `BmsParseResult` 정의와 2패스 `Parse()` — 1패스에서 헤더·`#WAV` 정의를 모으고, 2패스에서 데이터 줄을 노트로 풀며 편집 대상이 아닌 줄은 원문 그대로 보존(`PreservedLines`)합니다. 조건 블록(`#RANDOM`/`#IF`/`#SWITCH`)의 갈래(`BranchId`)도 여기서 추적합니다. 아래 4개 파티션으로 나뉘어 있습니다. |
| **[BmsParser.Patterns.cs](../../bms%20editer/Services/BmsParser.Patterns.cs)** | BMS 문법 정규식을 모은 파티션입니다. 모든 `[GeneratedRegex]` 선언과, 1패스에서 이미 읽어간 헤더인지 판별하는 `IsConsumedHeader` 가 있습니다. 데이터 줄 정규식은 규격을 넘는 4자리 마디도 받습니다. |
| **[BmsParser.Channels.cs](../../bms%20editer/Services/BmsParser.Channels.cs)** | 시간축 채널 해석 파티션입니다. 마디 길이(`02`)·직접 BPM(`03`)·`#BPMxx` 참조 BPM(`08`)을 읽어 차트에 담고, 2자리/3자리 키음 분할 크기 판정(`DetermineChunkSize`)을 담당합니다. `#STOP`(`09`)은 아직 읽지 않습니다. |
| **[BmsParser.MediaPaths.cs](../../bms%20editer/Services/BmsParser.MediaPaths.cs)** | 미디어 경로 해석 파티션입니다. 차트 폴더를 하위까지 훑어 파일명 색인을 만들고, 적힌 자리에 파일이 없으면 같은 이름을 찾아 붙입니다. 그 **추측 여부를 `guessed` 로 알려** 추측 결과가 저장 파일에 박히지 않게 합니다. |
| **[BmsParser.Encoding.cs](../../bms%20editer/Services/BmsParser.Encoding.cs)** | 인코딩 감지 파티션입니다. 바이트를 한 번만 읽고 BOM → 엄격 UTF-8 → CP932/CP949 순으로 가릅니다. CP932와 CP949는 서로의 바이트를 오류 없이 삼키므로, **`#WAV`/`#BMP` 파일명이 실제로 폴더에 있는 개수**(1순위)와 반각 가타카나·제어 문자 점수(2순위)로 판정합니다. 고른 인코딩은 저장에 그대로 쓰입니다. |
| **[BmsWriter.cs](../../bms%20editer/Services/BmsWriter.cs)** | 차트 모델을 BMS 텍스트로 직렬화합니다. 마디 안 노트 위치들의 최소공배수(LCM)로 분할 해상도를 정하고(상한 1920), 편집한 건반 줄과 보존한 원문 줄을 마디 순으로 합쳐 씁니다. 조건 줄과 갈래 노트는 (마디, 원래 줄 번호) 순으로 같은 정렬에 섞여 들어갑니다. |
| **[SafeFileWriter.cs](../../bms%20editer/Services/SafeFileWriter.cs)** | 원자적 저장입니다. 같은 폴더의 임시 파일(`.tmp`)에 끝까지 쓴 뒤에만 원본을 바꿔치기하고, 직전 내용을 `.bak` 으로 남깁니다. 네트워크 드라이브처럼 `File.Replace` 를 못 쓰는 곳에서는 `.bak` 없이 `Move(overwrite)` 로 물러납니다. |
| **[ChartTimeline.cs](../../bms%20editer/Services/ChartTimeline.cs)** | **"마디 위치 ↔ 절대 시각(초)" 변환을 전담**합니다. 마디 길이 배율(`#xxx02`)과 BPM 변화(`#xxx03`/`#xxx08`)를 누적해 격자선·노트·클릭·키음 재생이 같은 시각 기준을 쓰게 합니다(`SecondsAt` / `MeasurePositionAt`). |
| **[KeySoundPlayer.cs](../../bms%20editer/Services/KeySoundPlayer.cs)** | Win32 `waveOut` 기반 다중 채널(폴리포닉) 키음 믹서입니다. 네이티브 버퍼 3개(각 40ms)를 백그라운드 스레드가 채우며, 사전 디코딩된 PCM 을 포화 가산(Saturation Clamping)으로 합칩니다. 디코딩에 실패한 파일은 `PlaySound` 로 물러납니다. 약 120ms(빈 버퍼 3개) 조용하면 장치를 닫고 다음 키음 때 다시 엽니다. 디코딩 결과는 파일 경로별로 앱이 꺼질 때까지 캐시하며(실패도 `null` 로 기억), 미리 읽기가 끝나기 전에 울린 키음은 `Play` 를 부른 스레드(재생 중이면 UI 스레드)에서 그 자리에서 디코딩합니다. 관련 문제: [P-4 · P-9 · P-10](../issues/known_issues.md#-재생할-때-걸리는-것-2026-09-24-점검) |
| **[WavDecoder.cs](../../bms%20editer/Services/WavDecoder.cs)** | WAV(8/16/24/32bit PCM, IEEE Float, `WAVE_FORMAT_EXTENSIBLE`)와 OGG 를 44100Hz 16-bit Stereo PCM 으로 통일합니다(선형 보간 리샘플링). |
| **[WavKey.cs](../../bms%20editer/Services/WavKey.cs)** | Base-36 키음 번호(`01`~`ZZ`, `001`~`ZZZ`)의 파싱·서식·자릿수 판별 규칙 한 곳입니다. 뷰모델들이 여기 하나만 봅니다. |
| **[OggDecoder.cs](../../bms%20editer/Services/OggDecoder.cs)** | 배경 음악(OGG)을 `NVorbis` 로 한 번만 PCM16 으로 풉니다. 재생과 파형이 그 결과를 나눠 씁니다. 실제로 받아낸 표본 수로 길이를 다시 잡습니다. |
| **[OggAudioPlayer.cs](../../bms%20editer/Services/OggAudioPlayer.cs)** | `winmm.dll` 의 `waveOut` 을 `[LibraryImport]` 로 직접 호출하는 배경음 재생기입니다. 재생 배속은 장치 샘플레이트를 바꿔서 구현하고(음정도 함께 바뀜), 커서는 장치가 실제로 재생한 위치(`waveOutGetPosition`)를 기준으로 합니다. |
| **[OggPeakLoader.cs](../../bms%20editer/Services/OggPeakLoader.cs)** | 디코딩된 PCM 에서 파형 피크(초당 최대 80칸, 최대 2만 칸)와 온셋 값을 뽑습니다. 버킷 ↔ 시각 규칙(`GetBucketRatio` / `GetBucketRange`)을 여기 한 곳에만 둡니다. |
| **[Holds/GameProfileCatalog.cs](../../bms%20editer/Services/Holds/GameProfileCatalog.cs)** | exe 에 내장된 프로파일(`Profiles/*.json`, EmbeddedResource)과 exe 옆 `profiles\` 폴더의 덮어쓰기를 읽고, 읽을 때 규칙을 검사합니다(`LoadErrors`). 차트 경로의 폴더 이름으로 게임을 추정합니다(`DetectFromPath`). |
| **[Holds/HoldPairingEngine.cs](../../bms%20editer/Services/Holds/HoldPairingEngine.cs)** | 규칙마다 노트를 분류해 `BranchId`·레인별로 묶고 시간순으로 정렬한 뒤 정책에 넘겨, `HoldLink` 목록과 `HoldDiagnostic` 목록을 만듭니다. |
| **[Holds/HoldPolicies.cs](../../bms%20editer/Services/Holds/HoldPolicies.cs)** | 짝 정책 5가지(`NearestUnconsumedTail`, `NearestTailShared`, `FifoQueue`, `ReplacePending`, `Alternate`)의 구현입니다. 전부 실제 게임 모드 코드를 옮긴 것이고, 고아·버려진 시작·순서 뒤집힘(`ParityBreak`)·공유 끝·길이 0 을 진단합니다. |
| **[Holds/HoldRoleClassifier.cs](../../bms%20editer/Services/Holds/HoldRoleClassifier.cs)** | 노트 하나가 규칙에서 시작/끝/수열 구성원인지 정합니다. 키 값(`KeyValue`), 파일명 키워드(`FileNameKeyword`), 끝 표식을 뗀 기본명(`FileNameBase`), 파일명 앞 UID(`FileNameUid`, `UidTypeResolver`)를 읽습니다. |

---

## 🖥️ 3. ViewModels (MVVM 뷰모델)

사용자 입력 명령 처리, 화면 바인딩 상태 유지 및 창 간 통신을 제어하는 프레젠테이션 로직 계층입니다.

| 파일명 | 역할 및 특징 |
| :--- | :--- |
| **[MainWindowViewModel.cs](../../bms%20editer/ViewModels/MainWindowViewModel.cs)** | 메인 뷰모델 Core 입니다. 헤더·격자 바인딩 속성, 마디 수 계산(오디오·차트 중 큰 쪽을 바닥으로), `ChartTimeline` 연동, 음원 오프셋과 ⚡ 자동 맞춤(`TryDetectAudioOffsetMs`), 변경 추적(Dirty)과 `IDisposable` 해제를 맡습니다. |
| **[MainWindowViewModel.Playback.cs](../../bms%20editer/ViewModels/MainWindowViewModel.Playback.cs)** | 재생 파티션입니다. 재생/정지/토글, 스크러빙(`ScrubPreview` 는 커서만, `ScrubCommit` 은 뗄 때 한 번), 장치 클럭 기준 재생 위치 갱신(33ms 타이머), 이진 탐색으로 "지금 울릴 키음" 찾기를 담당합니다. |
| **[MainWindowViewModel.FileIO.cs](../../bms%20editer/ViewModels/MainWindowViewModel.FileIO.cs)** | 입출력 파티션입니다. BMS 열기/저장, 원본 인코딩 보존과 손실 시 UTF-8 폴백, OGG 비동기 디코딩(`LoadOggAsync`), 비디오 연결, 새 문서 초기화를 담당합니다. |
| **[MainWindowViewModel.Editing.cs](../../bms%20editer/ViewModels/MainWindowViewModel.Editing.cs)** | 편집·선택 파티션입니다. 노트 배치/삭제/이동(하나라도 못 가면 아무것도 안 옮김), 마디 단위 복사, 키음 일괄 교체, 선택 상태, 키음 추가/삭제를 담당합니다. 모든 편집은 `NotifyNotesChanged()` 한 곳을 지나고, 거기서 홀드 짝도 다시 계산합니다. |
| **[MainWindowViewModel.Holds.cs](../../bms%20editer/ViewModels/MainWindowViewModel.Holds.cs)** | 게임 프로파일·홀드 파티션입니다. 프로파일을 정하고(헤더 → 경로 추정 → 사용자 선택, `HoldProfileOrigin`), 짝 엔진 결과(`HoldLinks`, `HoldDiagnostics`, `HoldProblemNotes`, `HoldSummaryText`)를 화면에 내놓고, 검증 상태 문구와 `Assumed` 경고를 만듭니다. |
| **[ControlPanelViewModel.cs](../../bms%20editer/ViewModels/ControlPanelViewModel.cs)** | **🎛️ 컨트롤 패널** 뷰모델입니다. `NoteStatsViewModel` 의 집계를 물려받아, 고른 레인/키음 노트의 일괄 선택과 그 자리로 스크롤, 키음 미리듣기, 번호 일괄 교체, 확인 후 일괄 삭제를 수행합니다. 다시 집계해도 고른 줄을 번호로 되찾습니다. |
| **[NoteStatsViewModel.cs](../../bms%20editer/ViewModels/NoteStatsViewModel.cs)** | **📊 통계 창(보기 전용)** 뷰모델입니다. 실제로 쓰인 레인과 키음별 노트 수를 한 번의 순회로 집계하고, 편집 알림을 받아 다시 집계합니다. |
| **[NoteSearchViewModel.cs](../../bms%20editer/ViewModels/NoteSearchViewModel.cs)** | 🧰 검색/삭제/교체 창 뷰모델입니다. 선택 여부·노트 종류(롱 = 홀드 짝)·마디 범위·키음 범위·레인 조건으로 노트를 찾아 선택·삭제·번호 변경·마디 옮겨 복사를 실행합니다. |
| **[WavPaletteViewModel.cs](../../bms%20editer/ViewModels/WavPaletteViewModel.cs)** | 🎨 키음 팔레트 뷰모델입니다. 메인 뷰모델의 `SelectedWavItem` 을 그대로 읽고 써서 사이드바와 같은 항목을 가리키고, 검색어 필터·보기 모드(목록~아주 큰 아이콘)·고르면 미리듣기를 제공합니다. |
| **[OwnerObservingViewModel.cs](../../bms%20editer/ViewModels/OwnerObservingViewModel.cs)** | 보조 창 뷰모델들의 공통 바탕입니다. 메인 뷰모델 구독과 해제(`Dispose`)를 한 짝으로 묶어, 닫은 창의 뷰모델이 살아남지 않게 합니다. |
| **[BulkObservableCollection.cs](../../bms%20editer/ViewModels/BulkObservableCollection.cs)** | 한꺼번에 갈아끼우고 알림은 `Reset` 한 번만 내는 컬렉션입니다. 차트를 열 때 통계·팔레트가 키음 개수만큼 재집계하던 문제를 막습니다. |

---

## 🎨 4. Views & Controls (UI 화면 및 커스텀 컨트롤)

Avalonia UI 기반의 렌더링 파이프라인과 네이티브 인터랙션을 담당하는 화면 계층입니다.

| 파일명 | 역할 및 렌더링 메커니즘 |
| :--- | :--- |
| **[MainWindow.axaml](../../bms%20editer/MainWindow.axaml)** | 최상위 창의 레이아웃입니다. 메뉴·툴바·상태 표시줄, 파형+격자 편집면, 오른쪽 패널(비디오 · 헤더 · 게임 프로파일과 홀드 진단 목록 · 격자/재생/오프셋 설정 · 키음 목록)을 정의합니다. 코드 비하인드는 아래 6개 파일로 나뉩니다. |
| **[MainWindow.axaml.cs](../../bms%20editer/MainWindow.axaml.cs)** | 코드 비하인드 Core 입니다. 뷰모델 생성과 이벤트 배선, 저장 안 한 작업을 지키는 창 닫기 확인(`Closing` 은 await 할 수 없어 취소 후 재시도), 창 제목 갱신, 뷰모델 변경 알림 라우팅(비디오 동기 포함)을 담당합니다. |
| **[MainWindow.FileIO.cs](../../bms%20editer/MainWindow.FileIO.cs)** | 파일·미디어 대화상자 파티션입니다. BMS/OGG/비디오/WAV 선택, 폴더 열기 시 차트·음원·영상 자동 탐색(`FindBestFile`, 영상이 없으면 영상은 뗌), 저장 경로 선택과 제목 → 안전한 파일명 변환, 저장 실패·경고 보고를 담당합니다. |
| **[MainWindow.Input.cs](../../bms%20editer/MainWindow.Input.cs)** | 키보드 파티션입니다. 창 전체 단축키(Space 재생, Delete 삭제, Esc 선택 해제, 방향키 이동, Ctrl+S/Shift+S/O/N)와, 텍스트 입력·목록·슬라이더에 포커스가 있을 때 키를 양보하는 `IsWithin<T>` 판정을 담당합니다. |
| **[MainWindow.ToolWindows.cs](../../bms%20editer/MainWindow.ToolWindows.cs)** | 모드리스 보조 창 파티션입니다. 검색·통계·컨트롤 패널·키음 팔레트를 종류당 하나만 띄우고, 창이 닫힐 때 뷰모델 구독을 반드시 해제합니다(`ShowToolWindow<TWindow>`). |
| **[MainWindow.Scrubbing.cs](../../bms%20editer/MainWindow.Scrubbing.cs)** | 포인터 스크러빙 파티션입니다. 휠 클릭 드래그로 재생 위치를 끌고(뗄 때 한 번만 커밋), 파형 컨트롤의 스크럽 요청을 받고, Tunnel 단계에서 창 전체의 클릭을 가로채 재생 중 즉시 정지시킵니다. |
| **[MainWindow.Viewport.cs](../../bms%20editer/MainWindow.Viewport.cs)** | 스크롤·방향 파티션입니다. 가로/세로 전환, 검색·통계 창에서 고른 노트 자리로 격자 이동(레이아웃 완료를 기다려 한 박자 뒤), 재생 커서 자동 추적, 타임라인 길이 계산을 담당합니다. |
| **[Controls/TimelineControlBase.cs](../../bms%20editer/Views/Controls/TimelineControlBase.cs)** | `NoteGridControl` 과 `OggWaveformControl` 의 베이스입니다. 줌·마디 수·분할(`BeatSplit`, 기본 16)·BPM·음원 길이·오프셋 속성, `ChartTimeline` 기반 격자선 열거(`EnumerateGridLines`), 재생 커서와 BPM 변경 번쩍임을 공유합니다. |
| **[Controls/NoteGridControl.cs](../../bms%20editer/Views/Controls/NoteGridControl.cs)** | 채보 격자와 노트를 그립니다. 편집 모드 좌클릭 배치·우클릭 삭제, 드래그 범위 선택(Ctrl/Shift 로 더하기), 선택 강조(빨강), **`HoldLinks` 기반 홀드 몸통과 채널을 건너는 연결선**, 짝 문제 노트의 주황 점선 테두리를 그립니다. |
| **[Controls/OggWaveformControl.cs](../../bms%20editer/Views/Controls/OggWaveformControl.cs)** | 배경 음악 파형과 온셋 마커, 마디 번호·초 라벨을 그립니다. 좌클릭 드래그로 스크럽을 요청하며, 음원 오프셋만큼 밀린 위치를 음원 시각으로 되돌려 보냅니다. |
| **[Controls/VideoPreviewControl.cs](../../bms%20editer/Views/Controls/VideoPreviewControl.cs)** | Windows `WebView2`(HWND)로 BGA 영상을 재생하고, 재생 위치를 0.2초 간격으로 동기화합니다. 가상 호스트 매핑으로 로컬 파일을 엽니다. |
| **[ConfirmWindow.axaml / .cs](../../bms%20editer/Views/ConfirmWindow.axaml.cs)** | 공용 확인 대화상자입니다. 확인/취소 2지선다, 알림 전용, 저장/저장 안 함/취소 3지선다를 제공합니다. 창을 그냥 닫으면 `Cancel` 이 되도록 기본값을 0 으로 둡니다. |
| **[NoteSearchWindow.axaml / .cs](../../bms%20editer/Views/NoteSearchWindow.axaml.cs)** | 🧰 검색/삭제/교체 창입니다. Esc 로 닫힙니다. |
| **[NoteStatsWindow.axaml / .cs](../../bms%20editer/Views/NoteStatsWindow.axaml.cs)** | 📊 통계 창입니다. 누르는 것이 없는 보기 전용 창입니다. |
| **[ControlPanelWindow.axaml / .cs](../../bms%20editer/Views/ControlPanelWindow.axaml.cs)** | 🎛️ 컨트롤 패널입니다. 목록 항목에는 명령을 걸지 않고, 누르는 것은 전부 목록 밖 버튼입니다. 줄을 두 번 누르면 그 줄의 노트를 선택합니다. |
| **[WavPaletteWindow.axaml / .cs](../../bms%20editer/Views/WavPaletteWindow.axaml.cs)** | 🎨 키음 팔레트입니다. 여러 WAV 를 한 번에 추가할 수 있고, 고른 키음이 곧 편집 붓이라 창을 열면 편집 모드가 켜집니다. |

---

## 🧩 5. 그 밖의 파일

| 파일 | 역할 |
| :--- | :--- |
| **[Program.cs](../../bms%20editer/Program.cs) · [App.axaml.cs](../../bms%20editer/App.axaml.cs)** | 진입점입니다. 처리되지 않은 예외는 exe 옆 `crash.log` 에 남깁니다. |
| **[Profiles/*.json](../../bms%20editer/Profiles)** | 게임 프로파일 원본입니다. 값의 뜻은 [game_profiles.md](../specifications/game_profiles.md) 에 있습니다. |
| **[bms editer.csproj](../../bms%20editer/bms%20editer.csproj) · [win-x64.pubxml](../../bms%20editer/Properties/PublishProfiles/win-x64.pubxml)** | 빌드·배포 설정입니다. 배포는 self-contained 단일 exe 이고, PDB/XML 은 배포본에서 뺍니다. |
| **[bms editer.Tests/](../../bms%20editer.Tests)** | xUnit 테스트입니다. 헤드리스 Avalonia 로 창을 실제로 띄워 보는 스모크 테스트(`WindowSmokeTests`)와, PC 에 설치된 게임의 실제 차트를 게임 코드 오라클(`GameOracles`)과 비교하는 테스트(`RealGameChartTests`)가 포함됩니다. |
