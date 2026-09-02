# BMS Editor - 코드 설명서 (Code Explanation)

이 문서는 BMS 에디터 프로젝트를 구성하는 각 소스 코드 파일의 역할과 설계 구조를 설명합니다.

## 📂 프로젝트 구조 (Project Structure)

### 1. Models (데이터 모델)
차트 데이터와 설정을 표현하는 핵심 객체들입니다.
* **[BmsChart.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsChart.cs)**: BMS 차트 전체 정보를 보관합니다. 헤더 메타데이터, 키음/배경 이미지 맵, 마디당 박자 길이 변경 사항, 그리고 노트 목록을 관리합니다.
* **[BmsHeader.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsHeader.cs)**: 차트의 메타데이터(제목, 아티스트, 장르, 기본 BPM, 플레이어 모드, 판정 난이도 등)를 저장합니다.
* **[BmsNote.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsNote.cs)**: 개별 노트 객체입니다. 속해 있는 마디 번호, 레인(Lane) ID, 마디 내 상대 위치(0.0~1.0), WAV 키(Base-36), 노트 타입(일반, 롱노트 시작/끝 등) 정보를 가집니다.
* **[BmsWavItem.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/BmsWavItem.cs)**: 키음 목록 뷰에 바인딩하기 위한 모델로, WAV 키음 고유 식별 키와 로컬 파일 경로를 담고 있습니다.
* **[LaneDefinition.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Models/LaneDefinition.cs)**: 에디터에서 활성화할 노트 레인을 정의합니다. 기본적으로 1P 7키+스크래치 채널(16: 스크래치, 11~15 및 18: 건반) 세팅을 생성합니다.

### 2. Services (비즈니스 로직 및 엔진)
입출력, 오디오 재생 및 분석을 처리하는 백엔드 서비스 그룹입니다.
* **[BmsParser.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/BmsParser.cs)**: BMS 파일의 텍스트 줄을 파싱하여 차트 모델로 채웁니다. UTF-8과 CP949(한국어) 인코딩 자동 감지 및 중복/3자리 키음 채널의 분할 단위를 자동으로 결정합니다. 컴파일 타임 최적화를 위해 정규식을 `[GeneratedRegex]` 소스 생성기로 컴파일합니다.
* **[BmsWriter.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/BmsWriter.cs)**: 편집된 차트를 BMS 텍스트 포맷으로 변환해 저장합니다. 노트의 정확한 비율(0.0~1.0)을 유실 없이 직렬화하기 위해 마디 내 노트 위치들의 최소공배수(LCM)를 이용해 최적의 데이터 분할 길이를 자동으로 계산합니다.
* **[OggAudioPlayer.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/OggAudioPlayer.cs)**: 배경 음악(OGG)을 디코딩하고 Win32 `waveOut` API P/Invoke를 사용하여 지연 시간(Latency)을 최소화한 실시간 재생 및 탐색(Scrubbing)을 구현합니다. 성능 향상을 위해 소스 생성 P/Invoke인 `[LibraryImport]`를 사용합니다.
* **[OggPeakLoader.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Services/OggPeakLoader.cs)**: 오디오 파형 출력을 위해 OGG 데이터를 고속 다운샘플링하여 피크(Peak) 진폭 배열을 생성하며, 드럼 비트나Onset 타격점을 탐지(Onset detection)해 파형 위에 격자 씽크 가이드를 그릴 수 있도록 돕습니다.

### 3. ViewModels (MVVM 뷰모델)
화면의 상태 및 사용자 인터랙션 흐름을 제어합니다.
* **[MainWindowViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/MainWindowViewModel.cs)**: 메인 에디터 화면의 모든 상태(현재 차트, 재생 중 위치, 줌 배율, 가로/세로 뷰 옵션 등)를 관리하고 노트 배치, 삭제, 선택, 이동 명령(Command)들을 제공합니다. 오프라인 음원 믹서 역할을 수행하며, `PlaySound` API로 키음 재생을 비동기 수행합니다.
* **[NoteSearchViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/NoteSearchViewModel.cs)**: 검색/삭제/교체 창의 상태 모델입니다. 대상 노트(선택 여부·롱 여부·숨김 여부), 마디 범위, 키음 번호 범위(base-36 두 자리), 열(레인) 조건을 조합해 노트를 걸러내고 선택·삭제·키음 번호 일괄 변경을 수행합니다.
* **[NoteStatsViewModel.cs](file:///h:/source/repos/bms%20editer/bms%20editer/ViewModels/NoteStatsViewModel.cs)**: 레인별 노트 개수와 키음별 사용 빈도를 집계합니다. 메인 뷰모델의 노트 변경 알림과 키음 목록 변경을 구독해 편집하는 즉시 다시 집계하며(창을 닫을 때 `Dispose`로 구독 해제), 개수가 0인 항목은 빼고 실제로 쓰인 레인·키음만 보여줍니다. 키음은 등록된 `#WAV` 테이블이 아니라 노트가 실제로 가리키는 번호를 기준으로 세므로, 쓰지 않는 번호가 목록을 채우지 않습니다. *(향후 **🎛️ 컨트롤 패널 / 🧮 스마트 집계기**로 확장되어 실시간 집계 + 인터랙티브 일괄 선택/조작 공구함으로 통합될 예정입니다.)*

### 4. Views / Controls (UI 뷰 및 커스텀 컨트롤)
사용자에게 렌더링되고 입력을 받는 프레젠테이션 레이어입니다.
* **[MainWindow.axaml / MainWindow.axaml.cs](file:///h:/source/repos/bms%20editer/bms%20editer/MainWindow.axaml)**: 에디터의 메인 레이아웃 및 파일 열기/저장 창 처리, 키 입력 감지(방향키 이동, Delete 키 제거)를 담당합니다.
* **[TimelineControlBase.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/Controls/TimelineControlBase.cs)**: 격자판과 파형 드로잉 컨트롤의 공통 베이스 컨트롤입니다. 줌, 격자 단위 계산 기능, 재생바 커서 및 싱크 경고 점멸 플래시 그리기와 같이 공통된 화면 그리기 연출을 한 곳에서 모아 처리합니다.
* **[NoteStatsWindow.axaml / NoteStatsWindow.axaml.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/NoteStatsWindow.axaml)**: 툴바의 📊 버튼으로 여는 노트 통계 창입니다. 편집하면서 집계가 따라 움직이도록 모드리스로 띄웁니다.
* **[NoteSearchWindow.axaml / NoteSearchWindow.axaml.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/NoteSearchWindow.axaml)**: 툴바의 🧰 버튼으로 여는 검색/삭제/교체 창입니다. 결과를 격자에서 바로 확인할 수 있도록 모달이 아닌 모드리스로 띄우며, 이미 열려 있으면 새 창을 만들지 않고 기존 창을 앞으로 가져옵니다.
* **[NoteGridControl.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/Controls/NoteGridControl.cs)**: 실질적인 건반형 격자판과 배치된 노트를 그리는 커스텀 컨트롤입니다. 좌클릭(노트 배치 / 드래그 선택 영역 지정) 및 우클릭(노트 삭제) 등 상세한 격자 조작과 단축키 이동 동작을 연동합니다.
* **[OggWaveformControl.cs](file:///h:/source/repos/bms%20editer/bms%20editer/Views/Controls/OggWaveformControl.cs)**: 로드된 오디오 파형 및 박자 탐지점(Onset)을 에디터 격자 위치에 정확히 매칭시켜 파형 가이드라인을 그리는 컨트롤입니다. 휠 스크러빙 및 조작 편의성을 지원합니다.
