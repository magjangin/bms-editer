# BMS Editer (BMS 에디터)

BMS(Be-Music Source) 차트를 시각적으로 확인하고 편집할 수 있는 **Avalonia UI** 기반의 리듬게임 채보 에디터입니다. 
OGG 배경 오디오 파형(Waveform) 로딩, 온셋(Onset) 분석에 따른 그리드 씽크 시각화, 그리고 WAV 키음 미리듣기 기능을 제공합니다.

---

## 🚀 주요 기능 (Key Features)

1. **BMS 차트 파싱 및 로드**
   - `.bms`, `.bme`, `.bml` 확장자 파일 지원.
   - **인코딩 자동 인식**: BOM → UTF-8(엄격) → Shift_JIS(CP932) / 한국어 완성형(CP949) 순으로 판별.
     CP932와 CP949는 서로의 바이트를 오류 없이 삼키므로, `#WAV` 파일명이 실제로 폴더에 있는지를 1순위 증거로 씁니다.
     읽어낸 인코딩은 저장까지 이어져서 원본이 UTF-8로 갈아치워지지 않습니다.
   - 2자리/3자리 키음 배치가 혼용된 차트 데이터 자동 파싱.

2. **실시간 저수준 오디오 재생**
   - `NVorbis` VorbisReader 기반 OGG 디코딩.
   - Win32 `waveOut` API P/Invoke 직접 제어 방식을 사용하여 리소스 점유와 오디오 지연을 줄이고 실시간 재생/스크러빙 지원.

3. **고속 오디오 분석 및 파형 가시화**
   - 초당 샘플 밀도를 분석하여 전체 배경 오디오 파형 출력.
   - 음량이 순간적으로 튀는 정도를 탐지하여 온셋(Onset) 마커를 시각적으로 표시 (박자 및 마디 씽크 조율 편리).

4. **유연한 커스텀 그리드 컨트롤**
   - `NoteGridControl` 및 `OggWaveformControl`이 단일 베이스 클래스(`TimelineControlBase`)를 공유하는 구조로 구현되어 리렌더링 및 레이아웃 처리 최적화.
   - 가로형 뷰(Horizontal View) 및 세로형 뷰(Vertical View) 레이아웃 전환 지원.
   - BPM 변화에 유동적인 마디 비율 조절 및 재생바 추적 자동 스크롤링.
   - **`ChartTimeline` 이 "마디 위치 ↔ 시각" 변환을 한 곳에서 맡습니다.** 곡 도중의 BPM 변화(`#xxx03`/`#xxx08`)와
     4/4가 아닌 마디(`#xxx02`)를 격자·노트·클릭·재생이 모두 같은 기준으로 계산합니다.

5. **단축키**
   - `Space` 재생/정지 · `Delete` 선택 삭제 · `Esc` 선택 해제 · 방향키 노트 이동
   - `Ctrl+N` 새로 만들기 · `Ctrl+O` 열기 · `Ctrl+S` 저장 · `Ctrl+Shift+S` 다른 이름으로 저장
   - `Ctrl`/`Shift` + 드래그로 기존 선택에 더하기 (편집 모드에서도 됩니다)

6. **키음 다중 채널(폴리포닉) 믹싱 재생**
   - 재생 위치 탐색 시 이진 탐색(Binary Search)으로 "지금 울릴 노트"를 찾으므로, 수천 개가 넘는 고밀도 채보에서도 노트를 고르는 비용은 일정합니다.
   - **사전 PCM 디코딩 & 실시간 믹싱**: WAV (8/16/24/32bit PCM, IEEE Float) 및 OGG 파일을 44100Hz Stereo PCM으로 백그라운드 사전 디코딩(`WavDecoder`)하여 메모리에 캐싱합니다.
   - Win32 `waveOut` 기반 백그라운드 스트리밍 믹서(`KeySoundPlayer`)를 통해, 같은 자리의 화음이나 빠른 연속 타건 시에도 앞선 소리가 끊기지 않고 부드럽게 합산(Saturation Clamping Mix)되어 함께 울립니다.

7. **BGA(배경 영상) 미리보기 연동**
   - `WebView2` 기반 `VideoPreviewControl`을 통해 MP4/WebM/MOV/AVI/MKV 등 배경 영상을 로드하고 미리보기.
   - 재생/일시정지/탐색(Seek) 시 채보 재생 위치와 영상 타임라인을 실시간 동기화.

8. **폴더 단위 미디어 자동 로드**
   - 폴더 선택 시 내부의 BMS 차트, OGG 배경음, 영상 파일을 확장자 기준으로 자동 탐색하여 한 번에 로드.
   - 폴더명과 동일한 파일명을 우선순위로 매칭.

9. **키음(WAV) 경로 탐색 보강**
   - BMS 파일 기준 상대 경로에 파일이 없을 경우, 같은 폴더(하위 폴더 포함) 내에서 동일한 파일명을 재귀적으로 탐색하여 자동 매칭.
   - 채보 제작자가 WAV 파일을 다른 하위 폴더로 옮겨 놓은 경우에도 키음 유실 없이 로드 가능.

10. **BMS 저장 기능**
   - 편집한 `Chart`(헤더/WAV 테이블/노트)를 `.bms` 텍스트 포맷으로 직렬화하여 저장.
   - 마디·레인별 노트 위치를 최소공배수 분할로 계산해 원본과 동일하게 복원 가능한 데이터 라인 생성.
   - 최초 저장 시 저장 대화상자를 띄우고, 이후에는 같은 경로로 즉시 저장("저장") 또는 다른 이름으로 저장 지원.
   - **원자적 저장**: 같은 폴더 임시 파일에 끝까지 쓴 뒤에만 바꿔치기합니다. 쓰다 실패해도 원본은 온전하고, 직전 내용이 `.bak` 으로 남습니다.
   - **조건 블록(`#RANDOM`/`#IF`/`#SWITCH`) 분기 보존**: 분기 식별자(`BranchId`)와 원본 줄 순서(`Order`)를 추적하여 각 갈래별 노트를 분리 저장하므로, 조건문이 든 차트도 패턴 합쳐짐이나 헤더 이동 없이 안전하게 편집/저장 가능합니다.

11. **노트 검색/삭제/교체**
   - 툴바의 🧰 버튼으로 여는 조건 검색 창. 결과를 격자에서 바로 보도록 모드리스로 동작합니다.
   - 조건: 대상 노트(선택/미선택 · 일반/롱 · 숨기기/보이기), 마디 범위, 키음 번호 범위(base-36 `01`~`ZZ`), 열(레인) 선택.
   - 조건에 맞는 노트를 한 번에 **선택 / 삭제**하거나, 키음 **번호를 일괄 변경**할 수 있습니다.

12. **실시간 노트 통계 & 컨트롤 패널 (스마트 집계기)**
   - 툴바의 📊 (향후 🎛️ 컨트롤 패널 / 🧮 스마트 집계기로 확장 예정) 버튼으로 여는 통계 창. 편집하는 동안 집계가 즉시 따라 갱신됩니다.
   - 개수가 0인 레인·키음은 목록에서 빼고, 노트가 실제로 가리키는 번호만 집계합니다.
   - **향후 계획 (컨트롤 패널 / 스마트 집계기)**: 단순 수치 집계를 넘어, 레인/키음 버튼 클릭 시 격자 내 해당 노트 일괄 선택/포커스, 키음 즉시 미리듣기, 일괄 교체/삭제를 한곳에서 처리하는 대시보드 공구함으로 확장 예정.

---

## 🛠 빌드 및 실행 방법 (Build & Run)

### 요구사항
- **.NET 10.0 SDK** 이상
- Windows OS (오디오 재생 엔진이 Win32 P/Invoke API를 사용합니다)

### 빌드 명령어
프로젝트 루트 폴더에서 아래 명령을 실행합니다.

```powershell
dotnet build "bms editer/bms editer.csproj"
```

### 실행 명령어
```powershell
dotnet run --project "bms editer/bms editer.csproj"
```

### 배포 명령어
`publish/win-x64/bms editer.exe` 하나로 떨어지는 self-contained 단일 실행 파일을 만듭니다.
.NET 런타임이 필요 없으므로 이 exe만 복사하면 실행됩니다.

```powershell
dotnet publish "bms editer/bms editer.csproj" -p:PublishProfile=win-x64
```

배포 설정(`RuntimeIdentifier`, `SelfContained`, `PublishSingleFile`,
`IncludeNativeLibrariesForSelfExtract`)은 `bms editer/Properties/PublishProfiles/win-x64.pubxml`에
들어 있습니다. 명령줄에 플래그를 직접 붙이지 마세요 — 특히
`IncludeNativeLibrariesForSelfExtract`를 빠뜨리면 단일 exe가 아니라
네이티브 DLL 4개와 `runtimes` 폴더가 함께 흩어져 나옵니다.

> 배포본에서 PDB/XML을 빼는 처리는 csproj의 `ExcludeSymbolsFromPublish` 타겟이 맡습니다.
> `bin/`의 PDB는 그대로 남아 크래시 스택 심볼 분석에는 영향이 없습니다.

---

## 📂 프로젝트 구조 (Project Structure)
- **`Models`**: `BmsChart`, `BmsNote`, `LaneDefinition` 등 차트 데이터 모델.
- **`ViewModels`**: MVVM 아키텍처 기반의 메인 윈도우 상태 관리 및 액션 흐름 제어.
- **`Services`**:
  - `BmsParser`: BMS 차트 데이터 파일 분석기.
  - `BmsWriter`: `Chart` 데이터를 `.bms` 텍스트 포맷으로 직렬화하는 저장 엔진.
  - `SafeFileWriter`: 임시 파일에 다 쓴 뒤에만 바꿔치기하는 원자적 저장.
  - `ChartTimeline`: 마디 위치 ↔ 시각 변환. BPM 변화와 마디 길이를 여기서만 다룹니다.
  - `WavDecoder`: WAV/OGG 파일을 44100Hz 16-bit Stereo PCM으로 통일 디코딩.
  - `KeySoundPlayer`: Win32 `waveOut` 기반 실시간 다중 채널(폴리포닉) 키음 믹서 플레이어.
  - `OggDecoder`: OGG를 PCM16으로 한 번만 푸는 곳. 재생과 파형이 그 결과를 나눠 씁니다.
  - `OggAudioPlayer`: `winmm.dll` 기반의 로우 레벨 PCM 배경음 재생 엔진.
  - `OggPeakLoader`: 다운샘플링 피크 및 온셋 계산기.
- **`Views/Controls`**:
  - `TimelineControlBase`: 공통 타임라인 조절 파라미터를 담당하는 베이스 클래스.
  - `NoteGridControl`: 노트 배치 격자 렌더러.
  - `OggWaveformControl`: 배경 파형 및 온셋 가이드 라인 렌더러.

---

## 📖 문서 (Documents)

| 문서 | 내용 |
|------|------|
| [known_issues.md](docs/known_issues.md) | **남은 일과 이미 고친 것.** 1~3차 점검에서 나온 37건 중 33건 해결. 실사용으로 확인할 항목도 여기 있습니다. |
| [specification.md](docs/specification.md) | 마디 내부 그리드 분할 규칙과 기본 동작 사양. |
| [code_explanation.md](docs/code_explanation.md) | 소스 파일별 역할과 설계 구조. |
| [bpm_sync_principles.md](docs/bpm_sync_principles.md) | BPM 변경에 따른 격자·파형 동기화 원리. |
| [video_ogg_principles.md](docs/video_ogg_principles.md) | 비디오·OGG 연동 원리. |
