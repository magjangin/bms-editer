# BMS Editor (`bms editer`) - Agent Guidelines & Project Handover

이 문서는 BMS 에디터 프로젝트(`bms editer`)의 설계 의도, 구조적 원칙, 알려진 함정 및 빌드/테스트 규칙을 기술합니다.

---

## 1. 대상 게임 및 차트 환경
* **지원 및 호환 대상 게임**:
  - *Sixtar Gate: STARTRAIL* (Mono)
  - *Sixtar Gate: STARGAZER* (Il2Cpp)
  - *GUNVOLT RECORDS Cychronicle* (Mono)
  - *Muse Dash* (Il2Cpp / PC & Android)
  - *DEFLATE* (`dizzylab.castor`, Il2Cpp)
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

### 2) 설계 의도 및 Rationale (왜 일반적인 방법 대신 이 우회 방식을 택했는가)
1. **단일 파일 배포 및 프로파일 포함 (`EmbeddedResource`)**:
   - `Profiles/*.json` 게임 프로파일은 Avalonia Resource가 아닌 `.csproj`의 `EmbeddedResource`로 탑재됩니다. Avalonia 런타임을 부트스트랩하지 않는 경량 CLI나 xUnit 단위 테스트에서도 오프라인으로 프로파일을 즉시 검증할 수 있도록 설계되었습니다.
2. **Skia 불변 브러시 동결 (`Immutable`)**:
   - 백그라운드 xUnit 테스트 스레드와 Avalonia UI 스레드 간 브러시 공유 시 발생하는 `ThreadAffinity` 충돌을 방지하기 위해, `NoteGridControl` 및 `OggWaveformControl`의 모든 정적 브러시/펜은 반드시 `.ToImmutable()`로 생성합니다.
3. **타임라인 길이 단일 공식 (`ComputeTimelineLength`)**:
   - 격자선 렌더링, 뷰포트 스크롤, 마우스 스크러빙이 각각 길이를 계산하면 오프셋 오차가 누적되어 소리와 격자가 어긋납니다. 타임라인 길이는 반드시 `TimelineControlBase.ComputeTimelineLength()` 단 한 곳에서만 계산해야 합니다.
4. **Space 단축키 포커스 독립성**:
   - DAW 및 리듬게임 에디터의 특성상 툴바/사이드바 버튼 클릭 후 포커스가 머물러 있어도 Space 키는 항상 재생/정지로 최우선 동작해야 합니다. 툴바 버튼들은 `Focusable="False"`로 유지하고, `TextBox` / `NumericUpDown` 활성 입력 중에만 Space를 입력 텍스트로 보존합니다.

---

## 3. 시도했다가 실패한 것들 (Dead Ends / Failed Attempts)
* ❌ **렌더링 뷰포트 컬링 단순화 시도 (`8b44887` -> `10e1937` 롤백)**:
  - 뷰포트 외부 격자선과 노트를 거칠게 잘라내려다가 격자선 누락 및 끝 마디 노트 소실 현상 발생. 렌더 무결성이 최우선이므로 현재의 Skia Canvas 안전 클리핑과 계산된 `minPos/maxPos` 버퍼 구간 컬링을 엄격히 유지해야 합니다.
* ❌ **컨트롤 패널 리스트박스 ItemTemplate 내부 Command 바인딩**:
  - Avalonia Compiled Binding 환경에서 부모 ViewModel 조상 바인딩이 조용히 끊어지는 현상 발생. 목록 항목 내부가 아니라 목록 외부 전용 버튼(`SelectedItem` 바인딩) 및 더블클릭 이벤트 핸들러 방식을 유지해야 합니다.
* ❌ **재생 정지 클릭 시 포커스 전파 방치**:
  - 재생 중 정지하려고 에디터 화면을 누를 때 클릭 이벤트가 격자로 흘러들어가 노트를 덮어쓰거나 우클릭 삭제되는 현상 발생. 터널링 단계에서 `e.Handled = true`로 차단하고 ViewModel 차원에서 `IsPlaying` 시 노트 배치/삭제를 가드해야 합니다.

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
  *(현재 443개 단위/스모크 테스트 100% 통과 필수)*
* **배포 산출물 빌드**:
  ```powershell
  dotnet publish -c Release -p:PublishProfile=win-x64
  ```
