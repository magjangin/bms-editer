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

12. **실시간 노트 통계 (📊) 와 컨트롤 패널 (🎛️)**
   - 툴바의 **📊** 는 **보기 전용** 통계 창입니다. 숫자만 확인할 때 씁니다.
   - 툴바의 **🎛️** 는 같은 집계를 물려받아 **손대는 것까지 잇는 공구함**입니다. 둘 다 모드리스라 편집하는 동안 집계가 즉시 따라 갱신됩니다.
   - 개수가 0인 레인·키음은 목록에서 빼고, 노트가 실제로 가리키는 번호만 집계합니다. (두 창이 같은 집계를 쓰므로 숫자가 갈라지지 않습니다)
   - **레인/키음별 일괄 선택 · 포커스**: 줄을 고르고 [레인 노트 선택]·[키음 노트 선택](또는 줄을 두 번 클릭)을 누르면 해당 노트를 격자에서 한꺼번에 선택하고, 그 노트가 화면 밖이면 **그 자리로 스크롤**합니다.
   - **키음 미리듣기**: 줄을 고르면 바로 들려주고([고르면 미리듣기] 체크), [미리듣기 ▶]로 다시 들을 수 있습니다.
   - **번호 일괄 교체 · 일괄 삭제**: 고른 번호를 쓰는 노트의 키음 번호를 한꺼번에 바꾸거나 지웁니다. 되돌리기가 아직 없으므로 **삭제는 먼저 확인**을 받고, 교체 후에는 바뀐 번호 줄을 고른 채로 두어 곧바로 이어 작업할 수 있습니다.
   - 편집으로 집계가 다시 돌아도 고르고 있던 줄은 번호로 다시 잡으므로 선택이 풀리지 않습니다.

13. **재생 배속 조절(0.1x ~ 1.0x)과 키음 끄기/켜기**
   - 오른쪽 패널의 **재생 배속** 슬라이더로 배경음을 늦춰 들을 수 있습니다. 파형만 봐서는 가릴 수 없는
     10~30ms 어긋남을 귀로 잡을 때 씁니다. 배속을 낮추면 어긋남의 절대 길이가 그대로 늘어납니다.
   - 늦추는 방식이 **재생 장치의 샘플레이트를 낮추는 것**이라 배경음은 음정도 함께 내려갑니다.
     키음(`#WAV`)은 별도 믹서로 나가므로 **원래 속도·원래 음정 그대로** 울립니다.
   - **🔊 키음 소리 켜짐 / 🔇 끄기** 토글로 BGM 만 남겨 놓고 들을 수 있습니다.
   - 자세한 사용 순서는 [박자 맞추기 작업 가이드](docs/guides/beat_sync_workflow.md)에 있습니다.

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
  - `WavKey`: 키음 번호(base-36)의 파싱·서식·자릿수 판별 규칙. 뷰모델들이 여기 하나만 봅니다.
  - `OggDecoder`: OGG를 PCM16으로 한 번만 푸는 곳. 재생과 파형이 그 결과를 나눠 씁니다.
  - `OggAudioPlayer`: `winmm.dll` 기반의 로우 레벨 PCM 배경음 재생 엔진.
  - `OggPeakLoader`: 다운샘플링 피크 및 온셋 계산기.
- **`Views/Controls`**:
  - `TimelineControlBase`: 공통 타임라인 조절 파라미터를 담당하는 베이스 클래스.
  - `NoteGridControl`: 노트 배치 격자 렌더러.
  - `OggWaveformControl`: 배경 파형 및 온셋 가이드 라인 렌더러.

---

## 📖 문서 (Documents)

전체 문서의 개요 및 목차는 **[docs/README.md](docs/README.md)** 에서 확인할 수 있습니다.

| 분류 | 문서 | 내용 |
|------|------|------|
| **목차** | [README.md](docs/README.md) | 문서 전체 인덱스 및 디렉터리 가이드. |
| **작업 가이드** | [beat_sync_workflow.md](docs/guides/beat_sync_workflow.md) | **파형·BPM·재생 배속으로 박자 맞추는 순서.** 파형만으로 어긋남이 안 보일 때 배속을 낮춰 귀로 확인합니다. |
| **모딩 가이드** | [sixtar_gate_startrail.md](docs/guides/sixtar_gate_startrail.md) | 식스타 게이트: 스타트레일 (Mono) 커스텀 차트/키음 주입 가이드. |
| **모딩 가이드** | [sixtar_gate_stargazer.md](docs/guides/sixtar_gate_stargazer.md) | 식스타 게이트: 스타게이저 (Il2Cpp) 메타데이터 및 차트 주입 가이드. |
| **모딩 가이드** | [muse_dash.md](docs/guides/muse_dash.md) | 뮤즈 대시 (Il2Cpp) 커스텀 채보 매핑 및 영구 보존(Archive) 가이드. |
| **모딩 가이드** | [gunvolt_records_cychronicle.md](docs/guides/gunvolt_records_cychronicle.md) | 건볼트 레코즈 사이크로니클 (Mono) 6레인 채보 및 플릭/페어리 가이드. |
| **코드 설명서** | [code_explanation.md](docs/architecture/code_explanation.md) | 소스 파일별 역할, 구조 및 컴포넌트 상세 설명서. |
| **품질/이슈** | [known_issues.md](docs/issues/known_issues.md) | **남은 일과 이미 고친 것.** 1~3차 점검에서 나온 37건 중 33건 해결. 실사용으로 확인할 항목도 여기 있습니다. |
| **사양** | [grid_specification.md](docs/specifications/grid_specification.md) | 마디 내부 그리드 분할 규칙과 기본 동작 사양. |
| **원리** | [bpm_sync_principles.md](docs/architecture/bpm_sync_principles.md) | BPM 변경에 따른 격자·파형 동기화 원리. |
| **원리** | [video_ogg_principles.md](docs/architecture/video_ogg_principles.md) | 비디오·OGG 연동 원리. |
