# 📚 BMS Editer 문서 보관소 (Documentation Index)

BMS Editer 프로젝트의 설계 아키텍처, 기술적 구현 원리, 게임별 모딩 연동 가이드, 기능 사양서, 이슈 트래커를 정리한 공식 문서 허브입니다.

---

## 📁 폴더별 문서 구성 (Structure)

```
docs/
├── README.md                          # [현재 파일] 문서 전체 인덱스 및 네비게이션 가이드
├── architecture/                      # 아키텍처 및 핵심 엔진 구현 원리
│   ├── code_explanation.md            # 소스 코드 전체 구조 및 클래스/모듈별 상세 설명서
│   ├── bpm_sync_principles.md         # BPM 변경(#xxx03/#xxx08) 및 변박(#xxx02) 동기화 원리
│   └── video_ogg_principles.md        # 로우레벨 오디오/비디오 연동 및 온셋(Onset) 탐지 원리
├── guides/                            # 🎮 게임별 모딩 및 차트 주입 연동 가이드
│   ├── sixtar_gate_startrail.md       # 식스타 게이트: 스타트레일 (Mono) 커스텀 차트/키음 주입 가이드
│   ├── sixtar_gate_stargazer.md       # 식스타 게이트: 스타게이저 (Il2Cpp) 메타데이터/차트 주입 가이드
│   └── muse_dash.md                   # 뮤즈 대시 (Il2Cpp) 커스텀 채보 매핑 및 영구 보존 가이드
├── specifications/                    # 사양 및 규격 정의서
│   └── grid_specification.md          # 마디 내부 그리드 분할 규칙 및 기본 동작 사양서
└── issues/                            # 품질 관리 및 이슈 추적
    └── known_issues.md                # 버그 해결 기록, 미해결 과제, 실물 검증 체크리스트
```

---

## 📑 세부 문서 요약

### 1. 시스템 구조 및 원리 (`architecture/`)
* **[code_explanation.md](architecture/code_explanation.md)**: Models, Services, ViewModels, Views/Controls 내 모든 소스 파일의 책임과 아키텍처적 데이터 흐름을 상세히 설명하는 코드 설명서입니다.
* **[bpm_sync_principles.md](architecture/bpm_sync_principles.md)**: `ChartTimeline`을 통해 마디 위치 ↔ 절대 시각 변환을 단일화하고, 변박 및 가변 BPM 환경에서 그리드와 파형, 재생 헤드를 정밀 동기화하는 수학적 원리를 다룹니다.
* **[video_ogg_principles.md](architecture/video_ogg_principles.md)**: Win32 `waveOut` 저지연 오디오 스트리밍, `NVorbis` 기반 백그라운드 디코딩, 에너지 변화율 기반 오디오 온셋(Onset) 탐지, `WebView2` 영상 타임라인 실시간 락(Lock) 알고리즘을 설명합니다.

### 2. 게임별 모딩 연동 가이드 (`guides/`)
* **[sixtar_gate_startrail.md](guides/sixtar_gate_startrail.md)**: Unity Mono 기반의 *Sixtar Gate: STARTRAIL*에서 BMS Editer 레인을 Solar(4K)/Lunar(5K+Gate) 모드에 매핑하고, MelonLoader 및 `time = tick * 240 / bpm` 공식을 이용해 커스텀 차트와 키음을 주입하는 가이드입니다.
* **[sixtar_gate_stargazer.md](guides/sixtar_gate_stargazer.md)**: Il2Cpp 기반의 *Sixtar Gate: STARGAZER*에서 `SignatureDumper` 정적 분석, 곡 제목 getter 훅, `INNER_TrackMetaData` 프로퍼티 조작을 통해 커스텀 BMS 채보를 주입하는 가이드입니다.
* **[muse_dash.md](guides/muse_dash.md)**: Il2Cpp 기반의 *Muse Dash* 2레인(지상/공중) 구조에 맞춘 채보 매핑, MelonLoader 래퍼 캐스트/자체 래퍼 아키텍처, 그리고 Goldberg Emulator 기반 오프라인 영구 보존(Archive) 인프라 연동 가이드입니다.

### 3. 규격 및 동작 사양 (`specifications/`)
* **[grid_specification.md](specifications/grid_specification.md)**: 마디당 기본 16분할(16비트 스냅) 그리드 렌더링 규칙, 확대/축소 비율, 주요 박자선(Beat Line) 구분 로직의 명세를 정의합니다.

### 4. 이슈 및 품질 관리 (`issues/`)
* **[known_issues.md](issues/known_issues.md)**: 37건의 잠재 이슈 중 33건의 해결 과정(조건 블록 보존, 키음 네이티브 믹싱, 렌더 무결성, 🎛️ 컨트롤 패널 추가 등)과 현재 남은 과제(Undo/Redo), 실물 테스트 확인 기록을 총망라합니다.
