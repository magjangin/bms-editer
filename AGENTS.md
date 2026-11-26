# rhythm bms editer - Agent Guidelines & Project Handover

이 문서는 BMS 에디터 프로젝트(`rhythm bms editer`, 이전 명칭 `bms editer`)의 설계 의도, 구조적 원칙, 알려진 함정 및 빌드/테스트 규칙을 기술합니다.

---

## 1. 대상 게임 및 차트 환경
* **지원 및 호환 대상 게임**:
  - *Sixtar Gate: STARTRAIL* (Mono)
  - *Sixtar Gate: STARGAZER* (Il2Cpp)
  - *GUNVOLT RECORDS Cychronicle* (Mono)
  - *Muse Dash* (Il2Cpp / PC & Android)
  - *DEFLATE* (`dizzylab.castor`, Il2Cpp)
  - *PUMP IT UP RISE* (`PIURiseCustom`, Il2Cpp)
  - *UNBEATABLE*, *A Dance of Fire and Ice (ADOFAI)*
* **게임 런타임 및 시그니처 덤프 원칙**:
  - Mono 게임: `ilspycmd` 기반 C# 전체 역컴파일 로직 분석
  - Il2Cpp 게임: `SignatureDumper` 기반 스켈레톤 시그니처 정적 분석
* **차트 포맷**: 표준 BMS / BMSC 확장 포맷 (Base-36 / Base-16 WAV 정의, `#RANDOM`/`#IF`/`#SWITCH` 조건 분기 보존, 다중 BPM 변경, 변박 마디)
* **BMS 시간 계산 수식 표준**: `time = tick * 240 / bpm` (틱 기반 시간 변환)

---

## 2. 프로젝트 아키텍처 및 핵심 Rationale

### 1) 기술 스택
- **프레임워크**: .NET 10.0 (`net10.0`), C# 13 (`Nullable` 활성화)
- **UI 툴킷**: Avalonia UI 12.0.5 (Skia 렌더링, FluentTheme, CompiledBindings)
- **MVVM**: CommunityToolkit.Mvvm 8.4.0
- **오디오 / 비디오 엔진**:
  - `KeySoundPlayer`: Win32 `waveOut` 네이티브 헤더 직접 제어 + 무잠금 사전 디코딩 버퍼 믹싱
  - `OggAudioPlayer`: `NVorbis` 기반 PCM16 스트리밍
  - `VideoPreviewControl`: `Microsoft.Web.WebView2` (Chromium HWND) 가상 호스트 매핑 비디오 렌더러
- **데이터 저장소**: `%LocalAppData%\rhythm bms editer` (`AppDataPaths.cs`에서 구버전 `bms editer` 자동 마이그레이션)

### 2) 설계 의도 및 Rationale (왜 일반적인 방법 대신 이 우회 방식을 택했는가)
1. **단일 파일 배포 및 프로파일 포함 (`EmbeddedResource`)**:
   - `Profiles/*.json` 게임 프로파일은 Avalonia Resource가 아닌 `.csproj`의 `EmbeddedResource`로 탑재됩니다. Avalonia 런타임을 부트스트랩하지 않는 경량 CLI나 xUnit 단위 테스트에서도 오프라인으로 프로파일을 즉시 검증할 수 있도록 설계되었습니다.
2. **Skia 불변 브러시 동결 (`Immutable`)**:
   - 백그라운드 xUnit 테스트 스레드와 Avalonia UI 스레드 간 브러시 공유 시 발생하는 `ThreadAffinity` 충돌을 방지하기 위해, `NoteGridControl` 및 `OggWaveformControl`의 모든 정적 브러시/펜은 반드시 `.ToImmutable()`로 생성합니다.
3. **타임라인 길이 단일 공식 (`ComputeTimelineLength`)**:
   - 격자선 렌더링, 뷰포트 스크롤, 마우스 스크러빙이 각각 길이를 계산하면 오프셋 오차가 누적되어 소리와 격자가 어긋납니다. 타임라인 길이는 반드시 `TimelineControlBase.ComputeTimelineLength()` 단 한 곳에서만 계산해야 합니다.
4. **Space 단축키 포커스 독립성**:
   - DAW 및 리듬게임 에디터의 특성상 툴바/사이드바 버튼 클릭 후 포커스가 머물러 있어도 Space 키는 항상 재생/정지로 최우선 동작해야 합니다. 툴바 버튼들은 `Focusable="False"`로 유지하고, `TextBox` / `NumericUpDown` 활성 입력 중에만 Space를 입력 텍스트로 보존합니다.
5. **재생 커서 전용 층 (`TimelineControlBase.PlaybackCursorLayer`)**:
   - 재생 틱은 초당 60번 옵니다. 커서를 부모 `Render`에서 그리면 그때마다 노트·격자·파형 전체를 다시 기록해 곡이 빽빽할수록 끊깁니다. 커서는 얇은 자식 층에 그리고 `TranslateTransform`으로 옮깁니다. `PlaybackPositionSeconds`를 `AffectsRender`에 다시 넣으면 안 됩니다. `InvalidateArrange`로 옮기지도 마십시오(`LayoutUpdated`가 매 틱 불려 WebView2 COM 호출로 이어집니다).
6. **노트 그리기 색인은 참조로 무효화 (`NoteGridControl.NoteRenderIndex`)**:
   - 뷰모델은 노트가 바뀌면 항상 새 배열을 줍니다(`NotifyNotesChanged`). 색인은 배열·레인·시간축의 **참조**만 비교합니다. 노트를 제자리에서 고치면서 같은 배열을 다시 주는 경로를 만들면 화면이 갱신되지 않습니다.
7. **UI 스레드에서 디코딩·디스크 쓰기 금지**:
   - 재생 판정이 UI 타이머에서 돌기 때문에 UI가 멈추면 키음이 뭉칩니다. 키음 디코딩·파형 계산·자동 저장 쓰기는 백그라운드에서 합니다. 재생 중 노트는 `ScheduleIfDecoded`(안 풀렸으면 건너뜀), 사용자가 직접 듣는 소리는 `Play`(풀리는 대로 재생)를 씁니다.
8. **키음은 재생 위치 기준으로 예약 (`KeySoundPlayer.ScheduleIfDecoded`)**:
   - 믹서는 40ms 버퍼 3개를 앞서 채웁니다. 재생 중 노트를 그냥 넣으면 대기열 뒤에 붙어 배경음보다 늦습니다. `waveOutGetPosition`으로 얻은 재생 프레임에 남은 시간을 더해 시작 프레임을 정하고, 믹서가 그 프레임에 섞습니다. 앞서 보는 양은 `KeySoundLookaheadSeconds`(0.25초, 실제 시간)이고 대기열보다 커야 합니다. 두 장치의 시계가 돌기 전의 위치 값은 믿을 수 없으므로 그동안은 예약하지 않습니다. 시작 지점 바로 뒤의 노트를 위해 `OggAudioPlayer.Play`의 `leadInSeconds`로 곡 앞에 0.2초 무음을 넣으며, 무음 동안 `GetPlayedSeconds`는 음수입니다.
9. **재생 관련 변경은 실제 출력으로 측정**:
   - 무음 PCM이나 단위 테스트로는 키음 타이밍·음량을 알 수 없습니다. 실제로 `d7775f9`는 테스트를 전부 통과하면서 키음을 37ms에서 111ms로 늦췄습니다. 왼쪽 채널 배경음 클릭과 오른쪽 채널 키음 클릭을 프로세스 루프백(`VAD\\Process_Loopback`)으로 녹음해 시차를 재십시오. 시스템 전체 루프백은 다른 앱 소리가 섞입니다. 곡 중간만 재지 말고 재생 시작 직후, 40ms 미만의 짧은 소리, 곡 끝 경계를 함께 재십시오.
10. **조건 블록 저장은 "블록 밖 = 마디 순, 블록 안 = 원문 순" (`BmsWriter.Write`)**:
    - 제어 줄을 다른 줄과 함께 마디 순으로 섞으면 여러 마디에 걸친 `#IF` 사이로 블록 밖 줄이 끼어들어 조건부가 됩니다(2026-11-23 수정 전 실제 동작). 블록 안 줄(`IsControlFlow || BranchId > 0`)은 `Order`(원문 줄 번호)로만 정렬하고, 원문 줄이 없는 갈래 노트는 같은 갈래의 마지막 줄 바로 뒤에 둡니다.
11. **키음 확장자 대체 (`BmsParser.AlternateAudioNames`)**:
    - `.wav`↔`.ogg` 만 서로 바꿔 찾습니다(디코더가 읽는 두 형식). 바꿔 찾은 경로는 `IsPathGuessed` 라 저장 때 원문 이름을 지킵니다. 하위 폴더 색인은 `MediaIndexMaxDepth`(4)·`MediaIndexMaxFiles`(2만 개)까지만 훑습니다(Rationale 21).
12. **스위치/케이스 분기 스택 격리 (`switchStack`)**:
    - `#SWITCH` 구문 안의 다중 `#CASE`/`#DEF`는 내부에서 새 `branchId`를 할당받지만, 구문이 종료되는 `#ENDSW`에서는 반드시 부모 분기 ID(최상위는 0)로 복구되어야 합니다. `switchStack`(`Stack<bool>`)을 도입하여 분기 내 케이스 교체 및 블록 종료 시 정상 중립 분기로 복원하도록 보장합니다.
13. **마지막 작업 폴더 기억 (`RememberFolder`) 및 드래그 앤 드롭**:
    - 파일 열기 대화상자에서 매번 루트로 돌아가는 사용성 불편을 방지하기 위해 파일/폴더를 열거나 저장할 때마다 마지막 경로를 메모리에 보존하여 다음 탐색기의 `SuggestedStartLocation`으로 사용합니다.
14. **문서를 여는 동안 홀드 짝짓기 미루기 (`DeferHoldRecompute`)**:
    - 열기 한 번에 노트 알림·키음 목록 교체·프로파일 적용이 각자 `RecomputeHolds`를 불러 곡 전체 짝짓기가 4~5번 돌았습니다. `LoadDocument`는 `using var _ = DeferHoldRecompute();`로 묶어 끝에 한 번만 짝짓습니다. 여러 단계를 거치는 새 경로(일괄 편집 등)도 같은 방식으로 묶으십시오.
15. **열기 안내는 `DocumentNotice`, 저장 안내는 `LastWarningMessage`**:
    - `LastWarningMessage`는 저장 직후 창이 대화상자로 띄우는 값이고 다음 저장이 덮어씁니다. 열 때 알게 된 것(없는 키음, 폴더에 차트가 여럿)은 상태 표시줄의 `DocumentNotice`에 넣습니다. 문서를 비우면 지워지므로 열기 **뒤에** `AddDocumentNotice`로 덧붙여야 합니다.
16. **줄바꿈은 `.gitattributes`(`* text=auto`)가 관리**:
    - 한때 7개 파일이 `\r\r\n`으로 저장돼 git이 바이너리로 취급했고 diff가 통째 교체로 보였습니다. 파이썬 등으로 파일을 쓸 때 텍스트 모드로 읽은 뒤 `\r\n`을 덧붙이지 마십시오. `git ls-files --eol`에서 소스가 `i/lf`인지 확인하십시오.
17. **Undo/Redo 히스토리 및 부분 클래스 구조 (`MainWindowViewModel.UndoRedo.cs` / `NoteGridControl.*.cs`)**:
    - 6개 노트 편집 경로(`PlaceNote`·`RemoveNote`·`DeleteNotes`·`MoveSelectedNotes`·`ReplaceWavKey`·`CopyNotesByMeasureOffset`)는 `NoteSnapshot[]` + 선택 인덱스로 `MainWindowViewModel.UndoRedo.cs` 에 기록됩니다(100단계, `ResetDocumentState` 시 초기화). 새 노트 편집 경로를 추가할 때는 반드시 스냅숏 캡처·기록을 연결하십시오.
    - 기록마다 곡 전체를 두 벌 들지 않도록, 이번 "편집 전"이 앞 기록의 "편집 후"와 같으면 그 배열을 공유합니다(`RecordNoteHistory`). 스냅숏 배열은 기록된 뒤 절대 제자리에서 고치면 안 됩니다. 재생 중에는 `CanUndo`/`CanRedo` 가 false 입니다.
    - `NoteGridControl` 은 렌더링(`NoteGridControl.cs`)·색인 캐시(`NoteGridControl.Indexing.cs`, `NoteRenderIndex`·`HoldRenderIndex`·`HoldProblemRenderIndex`)·포인터 입력(`NoteGridControl.Input.cs`)으로 분리되어 있습니다.
18. **배경음 음량은 표본에 곱한다 (`OggAudioPlayer.ScalePcm16`) — `waveOutSetVolume` 금지**:
    - `waveOutSetVolume` 은 핸들이 아니라 **프로세스 오디오 세션 전체** 음량입니다(실측: 핸들 A 25% → 핸들 B 도 `0x40004000`). 배경음 슬라이더로 쓰면 키음까지 같이 줄어듭니다. 배경음은 음량을 곱한 PCM 사본을 재생하고(음량이 바뀔 때만 다시 만듦), 재생 중 음량 변경은 `RestartPlaybackSoon()` 으로 80ms 미뤘다가 현재 자리에서 다시 틉니다. 키음 음량은 `KeySoundPlayer` 믹서 게인입니다. 예외는 실행당 첫 재생 한 번 세션 음량을 100% 로 되돌리는 `ResetLegacySessionVolumeOnce` 뿐입니다(예전 버전이 낮춰 둔 값 복구).
19. **문서를 여는 모든 경로는 같은 두 관문을 지난다 (`LoadFolderMediaAsync` / `LoadCompanionMediaAsync`)**:
    - 버튼·`Ctrl+O`·끌어다 놓기·탐색기 열기·복구가 각자 확인과 음원 붙이기를 구현하면 한쪽이 빠집니다(실제로 폴더 드롭은 저장 확인 없이 작업을 버렸고, `Ctrl+O` 는 앞 곡 음원을 남겼습니다). 저장 확인은 `ConfirmDiscardIfNeededAsync` 로 **열 차트가 있다는 걸 안 뒤에**, 같은 폴더 음원·영상은 `LoadCompanionMediaAsync` 로 붙입니다. 같은 음원은 `IsSameOggLoaded`(경로+크기+수정 시각)로 다시 풀지 않습니다.
20. **저장 시 칸 충돌은 1920 분할 → 가까운 빈 칸 (`BmsWriter.FillSlots`)**:
    - 격자 없이 찍은 노트는 1/1920 마디보다 가까울 수 있습니다. 덮어쓰면 노트가 조용히 사라지므로, 위치가 다른 노트가 부딪히면 가장 가는 분할로 다시 나누고 그래도 부딪히면 옆 빈 칸으로 비킵니다. 부딪힘이 없으면 예전처럼 최소 분할을 씁니다(원본 줄 모양 보존).
21. **키음 경로 색인은 깊이 4·파일 2만 개에서 멈춘다 (`BmsParser.MediaIndexMaxDepth`/`MediaIndexMaxFiles`)**:
    - 열기는 UI 스레드에서 돕니다. 바탕 화면·드라이브 루트에 둔 차트에서 무제한 재귀하면 창이 멈춥니다. 한계를 늘릴 때는 열기를 백그라운드로 옮기는 것부터 하십시오.
22. **부동소수점 수치 무결성 및 디렉터리 색인 복원력 (`double.IsFinite` / 개별 파일 예외 가드)**:
    - 오디오 오프셋 등 수치 헤더 파싱/저장 시 `NaN`/`Infinity` 침투를 원천 차단하고, 키음 검색 시 단일 파일의 경로 예외가 전체 색인을 날리지 않도록 개별 파일 단위로 예외를 가드합니다.

---

## 3. 시도했다가 실패한 것들 (Dead Ends / Failed Attempts)
* ❌ **렌더링 뷰포트 컬링 단순화 시도 (`8b44887` -> `10e1937` 롤백)**:
  - 뷰포트 외부 격자선과 노트를 거칠게 잘라내려다가 격자선 누락 및 끝 마디 노트 소실 현상 발생. 렌더 무결성이 최우선이므로 현재의 Skia Canvas 안전 클리핑과 계산된 `minPos/maxPos` 버퍼 구간 컬링을 엄격히 유지해야 합니다.
* ❌ **컨트롤 패널 리스트박스 ItemTemplate 내부 Command 바인딩**:
  - Avalonia Compiled Binding 환경에서 부모 ViewModel 조상 바인딩이 조용히 끊어지는 현상 발생. 목록 항목 내부가 아니라 목록 외부 전용 버튼(`SelectedItem` 바인딩) 및 더블클릭 이벤트 핸들러 방식을 유지해야 합니다.
* ❌ **헤드리스 테스트에서 `RunJobs`로 재생 타이머 돌리기**:
  - `DispatcherTimer`는 진짜 메인 루프에서만 돌아서 커서가 0에 머뭅니다. 재생 경로는 `HeadlessUnitTestSession.Dispatch(async ...)` 안에서 `await`로 양보해야 합니다(`RenderingPerformanceRegressionTests` 참고).
* ❌ **키음 지연을 줄이려고 장치만 계속 열어 두기 (`d7775f9`)**:
  - 무음 버퍼가 대기열에 항상 차서 지연이 37ms에서 111ms로 늘었습니다. 장치 유지는 예약(Rationale 8)과 함께여야 합니다.
* ❌ **재생 정지 클릭 시 포커스 전파 방치**:
  - 재생 중 정지하려고 에디터 화면을 누를 때 클릭 이벤트가 격자로 흘러들어가 노트를 덮어쓰거나 우클릭 삭제되는 현상 발생. 터널링 단계에서 `e.Handled = true`로 차단하고 ViewModel 차원에서 `IsPlaying` 시 노트 배치/삭제를 가드해야 합니다.
* ❌ **Avalonia 12 `DragEventArgs.Data` 구형 API 직접 접근 시도**:
  - Avalonia 12.0.5에서는 `e.Data`가 `e.DataTransfer`(`IDataTransfer`)로 개편되었으며, `DataFormats.Files` 대신 `DataFormat.File`과 `TryGetFiles()`, `IStorageItem.TryGetLocalPath()`를 사용하는 현대적 API로 구현해야 합니다.
* ❌ **미리듣기(`KeySoundPlayer.Play`)까지 디코딩 중복 제거**:
  - 재생 중 노트(`ScheduleIfDecoded`)는 파일당 백그라운드 작업 하나로 묶었지만, 미리듣기에도 같은 묶음을 쓰면 재생 예약이 먼저 풀기 시작한 파일을 사용자가 눌렀을 때 소리가 조용히 사라집니다. 미리듣기는 드물고 하나하나 들려야 하므로 예전처럼 둡니다.
* ❌ **`waveOutSetVolume` 으로 배경음 음량만 따로 조절**:
  - Vista 이후 이 API 는 프로세스 세션 음량이라 키음(`KeySoundPlayer`)까지 같이 줄었습니다(v0.1.6~v0.1.7). 핸들을 달리 열어도 같습니다. 표본에 곱하는 방식만 독립적입니다(Rationale 18).
* ❌ **헤드리스 창 테스트에서 고친(dirty) 문서를 든 채 `window.Close()`**:
  - "저장하고 닫을까요?" 대화상자가 떠서 테스트 호스트가 끝나지 않습니다(5분 넘게 멈춤). 닫기 전에 `vm.MarkClean()` 하십시오.
* ❌ **`CommunityToolkit.Mvvm` `OnPropertyChanging(ref T)` 부분 메서드 시도**:
  - 소스 생성기가 `ref` 형태의 부분 메서드를 지원하지 않으므로, 유효성 검사 및 클램핑이 필요한 프로퍼티는 수동 getter/setter와 `SetProperty`를 사용하는 것이 가장 안정적입니다.

---

## 4. 빌드 및 테스트 규칙
* **빌드**:
  ```powershell
  dotnet build
  ```
  *(빌드 경고 0개, 오류 0개 필수)*
* **테스트**:
  ```powershell
  dotnet test
  ```
  *(현재 557개 단위/스모크 테스트 100% 통과 필수. 테스트는 CRLF·waveOut 을 전제하므로 Windows 에서 돌립니다)*
* **CI**: `.github/workflows/ci.yml` 이 push·PR 마다 Windows 에서 `-warnaserror` 빌드와 전체 테스트를 돌립니다. 게임 DLL이 필요 없는 프로젝트라 클라우드 세션에서도 이 CI 결과로 검증할 수 있습니다.
* **릴리즈**: `csproj` `<Version>`(4곳)과 `CHANGELOG.md` `## [<버전>]` 을 함께 올려 main 에 머지한 뒤 `v<버전>` 태그를 밀면 `.github/workflows/release.yml` 이 테스트 → 단일 exe 게시 → `bms-editer-v<버전>-win-x64.zip`(SHA256 포함) 릴리즈를 만듭니다. 둘이 다르면 멈춥니다.
* **배포 산출물 빌드**:
  ```powershell
  dotnet publish "bms editer/bms editer.csproj" -p:PublishProfile=win-x64
  ```
