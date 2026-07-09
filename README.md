# BMS Editer (BMS 에디터)

BMS(Be-Music Source) 차트를 시각적으로 확인하고 편집할 수 있는 **Avalonia UI** 기반의 리듬게임 채보 에디터입니다. 
OGG 배경 오디오 파형(Waveform) 로딩, 온셋(Onset) 분석에 따른 그리드 씽크 시각화, 그리고 실시간 WAV 키음 믹싱 재생 기능을 제공합니다.

---

## 🚀 주요 기능 (Key Features)

1. **BMS 차트 파싱 및 로드**
   - `.bms`, `.bme`, `.bml` 확장자 파일 지원.
   - **인코딩 자동 인식**: UTF-8 및 한국어 완성형(CP949) 디코딩 감지 기능 내장.
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

5. **최적화된 키음 재생**
   - 재생 위치 탐색 시 이진 탐색(Binary Search) 알고리즘을 사용한 틱 스캔을 수행하여 수천 개가 넘어가는 고밀도 채보에서도 실시간 오디오 트리거 성능 보장.

6. **BGA(배경 영상) 미리보기 연동**
   - `WebView2` 기반 `VideoPreviewControl`을 통해 MP4/WebM/MOV/AVI/MKV 등 배경 영상을 로드하고 미리보기.
   - 재생/일시정지/탐색(Seek) 시 채보 재생 위치와 영상 타임라인을 실시간 동기화.

7. **폴더 단위 미디어 자동 로드**
   - 폴더 선택 시 내부의 BMS 차트, OGG 배경음, 영상 파일을 확장자 기준으로 자동 탐색하여 한 번에 로드.
   - 폴더명과 동일한 파일명을 우선순위로 매칭.

8. **키음(WAV) 경로 탐색 보강**
   - BMS 파일 기준 상대 경로에 파일이 없을 경우, 같은 폴더(하위 폴더 포함) 내에서 동일한 파일명을 재귀적으로 탐색하여 자동 매칭.
   - 채보 제작자가 WAV 파일을 다른 하위 폴더로 옮겨 놓은 경우에도 키음 유실 없이 로드 가능.

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

---

## 📂 프로젝트 구조 (Project Structure)
- **`Models`**: `BmsChart`, `BmsNote`, `LaneDefinition` 등 차트 데이터 모델.
- **`ViewModels`**: MVVM 아키텍처 기반의 메인 윈도우 상태 관리 및 액션 흐름 제어.
- **`Services`**:
  - `BmsParser`: BMS 차트 데이터 파일 분석기.
  - `OggAudioPlayer`: `winmm.dll` 기반의 로우 레벨 PCM 재생 제어 엔진.
  - `OggPeakLoader`: 다운샘플링 피크 및 온셋 계산기.
- **`Views/Controls`**:
  - `TimelineControlBase`: 공통 타임라인 조절 파라미터를 담당하는 베이스 클래스.
  - `NoteGridControl`: 노트 배치 격자 렌더러.
  - `OggWaveformControl`: 배경 파형 및 온셋 가이드 라인 렌더러.
