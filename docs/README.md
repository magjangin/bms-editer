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
├── guides/                            # 🎮 작업 가이드 및 게임별 모딩·차트 주입 연동 가이드
│   ├── beat_sync_workflow.md          # 파형·BPM·재생 배속으로 박자 맞추는 작업 순서 가이드
│   ├── sixtar_gate_startrail.md       # 식스타 게이트: 스타트레일 (Mono) 커스텀 차트/키음 주입 가이드
│   ├── sixtar_gate_stargazer.md       # 식스타 게이트: 스타게이저 (Il2Cpp) 메타데이터/차트 주입 가이드
│   ├── muse_dash.md                   # [지원 제외] 뮤즈 대시 (Il2Cpp) 커스텀 채보 가이드 (작성 난이도 과다로 제외, 보존용)
│   ├── gunvolt_records_cychronicle.md # 건볼트 레코즈 사이크로니클 (Mono) 6레인 채보 및 플릭/페어리 가이드
│   └── deflate.md                     # DEFLATE 4레인(+드롭) 채보 및 파일명 기반 홀드 작성 가이드
├── specifications/                    # 사양 및 규격 정의서
│   ├── grid_specification.md          # 마디 내부 그리드 분할 규칙 및 기본 동작 사양서
│   ├── hold_pairing_spec.md           # 🔗 홀드 짝 맞추기 — 전략 3종·검사기·표시 사양 (게임 무관)
│   └── game_profiles.md               # 🎮 게임별 값 단일 표 — 레인·키 폭·판별 전략·검증 상태
└── issues/                            # 품질 관리 및 이슈 추적
    ├── known_issues.md                # 버그 해결 기록, 미해결 과제, 실물 검증 체크리스트
    └── authoring_time.md              # ⏱️ 채보 작성 시간 경고 · 실측 기록 · 개선 후보
```

---

## 📑 세부 문서 요약

### 1. 시스템 구조 및 원리 (`architecture/`)
* **[code_explanation.md](architecture/code_explanation.md)**: Models, Services, ViewModels, Views/Controls 내 모든 소스 파일의 책임과 아키텍처적 데이터 흐름을 상세히 설명하는 코드 설명서입니다.
* **[bpm_sync_principles.md](architecture/bpm_sync_principles.md)**: `ChartTimeline`을 통해 마디 위치 ↔ 절대 시각 변환을 단일화하고, 변박 및 가변 BPM 환경에서 그리드와 파형, 재생 헤드를 정밀 동기화하는 수학적 원리를 다룹니다.
* **[video_ogg_principles.md](architecture/video_ogg_principles.md)**: Win32 `waveOut` 저지연 오디오 스트리밍, `NVorbis` 기반 백그라운드 디코딩, 에너지 변화율 기반 오디오 온셋(Onset) 탐지, `WebView2` 영상 타임라인 실시간 락(Lock) 알고리즘을 설명합니다.

### 2. 작업 가이드 및 게임별 모딩 연동 (`guides/`)
* **[beat_sync_workflow.md](guides/beat_sync_workflow.md)**: 고정된 파형 위에 격자를 맞추는 작업 가이드입니다. **파형만 봐서는 어긋남을 가릴 수 없을 때 재생 배속(0.1x~1.0x)을 낮춰 귀로 확인하는 방법**, 음원 오프셋과 ⚡ 자동 맞춤(에디터 화면 전용), 증상별 원인 구분(음원 오프셋 / BPM 소수점 / BPM 변화·변박·`#STOP` / 온셋 신뢰도)을 다룹니다.
* **[sixtar_gate_startrail.md](guides/sixtar_gate_startrail.md)**: Unity Mono 기반의 *Sixtar Gate: STARTRAIL* (`sxtg2`)에서 BMS Editer 레인을 Solar(4K)/Lunar(5K+Gate) 모드에 매핑하고, 롱노트(`02`/`03`)와 게이트 개폐(`04`/`05`)를 주입하는 가이드입니다.
* **[sixtar_gate_stargazer.md](guides/sixtar_gate_stargazer.md)**: Il2Cpp 기반의 *Sixtar Gate: STARGAZER*에서 4방향 회전형 레인(`16, 12, 13, 11`), `#WAV` 파일명 기반 롱노트 판별, 분수 무손실 `Area/BeatInfo` 주입 가이드입니다.
* **[muse_dash.md](guides/muse_dash.md)**: **[지원 제외]** 뮤즈 대시 2레인 구조 매핑 및 모딩 아카이브 가이드입니다. 6자리 UID 체계와 출현 순서 전파(Cascade)로 인한 극심한 제작 난이도로 인해 공식 지원 대상에서 제외되었습니다 (보존용 아카이브).
* **[gunvolt_records_cychronicle.md](guides/gunvolt_records_cychronicle.md)**: Unity Mono 기반의 *GUNVOLT RECORDS Cychronicle* (`GRC2`)에서 좌/우 6레인 매핑(`16, 11, 12` vs `14, 15, 18`), 8방향 플릭(`03~0A`) 및 페어리 아크(`11~18`, `1A/1B`) 주입 가이드입니다.
* **[deflate.md](guides/deflate.md)**: *DEFLATE*의 플레이 4레인(`16, 11, 12, 13`) + 드롭 레인(`14`) 매핑, `DrumMode`(HiHat/KickSnare)가 같은 물리 레인을 나눠 쓰는 구조, **`#WAV` 파일명 키워드로 홀드 시작/끝을 판별하는 규칙**(부분 일치 · Tail 우선 · 가장 가까운 Tail)과 1마디 3840틱 시간 계산 가이드입니다.

### 3. 규격 및 동작 사양 (`specifications/`)
* **[grid_specification.md](specifications/grid_specification.md)**: 마디당 기본 16분할(16비트 스냅) 그리드 렌더링 규칙, 확대/축소 비율, 주요 박자선(Beat Line) 구분 로직의 명세를 정의합니다.
* **[hold_pairing_spec.md](specifications/hold_pairing_spec.md)**: 🔗 **홀드 짝 맞추기 사양.** 키음이 든 "역할"을 읽어 홀드 시작·끝을 짝짓고, 몸통으로 이어 그리고, 어긋난 자리를 편집 중에 지목합니다. 역할은 **네 자리**(슬롯 코드 값 · 파일명 키워드 · 파일명 기본명 · 파일명 UID)에서 읽고, 짝은 **다섯 정책** 중 하나로 짓습니다. 핵심 결정은 **"게임은 소비하고 에디터는 보존한다"** — `Chart.Notes` 를 건드리지 않는 파생 링크 표라서 저장 왕복이 안전합니다. **게임 이름이 나오지 않습니다.**
* **[game_profiles.md](specifications/game_profiles.md)**: 🎮 **게임별 값 단일 표.** 레인 · 키 폭 · 판별 방식 · 짝 정책 · 검증 상태(✅ 플레이 확인 / 🔶 코드 확인 / ⚠️ 가정)와 실제 게임 차트 대조 결과를 한 곳에서만 관리합니다. 값의 진실은 프로파일 JSON 이고, 이 표는 그것을 옮긴 것입니다. 게임 추가 = 이 표 한 줄 + JSON 하나 + 가이드 하나.

### 4. 이슈 및 품질 관리 (`issues/`)
* **[known_issues.md](issues/known_issues.md)**: 핵심 과제 해결(조건 블록, 키음 믹싱, 렌더링, 🎛️ 컨트롤 패널, 게임 프로파일 및 홀드 자동 페어링)과 315개 단위 테스트 현황, 남은 과제(Undo/Redo, 키음 지연, 조건 블록 안 헤더, 비디오 오버플로우 등)를 총망라합니다.
* **[authoring_time.md](issues/authoring_time.md)**: ⏱️ **채보 작성 시간 분석과 개선 과제.** 실측 기록(뮤즈 대시 340노트 = 약 4시간)과 병목 원인 분석, 이를 줄이려고 v0.1.4에 넣은 프로파일 기반 홀드 자동 페어링/몸통 렌더링, 그리고 향후 과제를 다룹니다. v0.1.4 이후의 작성 시간은 아직 재지 않았습니다.

