# PUMP IT UP RISE BMS 작성 및 연동 가이드

이 문서는 모드 프로젝트 `PUMP IT UP RISE-unity-il2cpp`(이 저장소 밖의 별도 저장소)의 `mod/PIURiseCustom/CustomChartParsers.cs`와 `CustomPackManager.cs` 구현을 바탕으로, **PUMP IT UP RISE에 주입할 커스텀 BMS 채보 작성 규칙**을 안내합니다.

> [!NOTE]
> **🔶 코드 확인**: PUMP IT UP RISE 전용 게임 프로파일(`pump_it_up_rise`)이 추가되어, `#WAV` 파일명의 `홀드 시작`/`끝` 키워드 기반으로 롱노트가 자동 페어링되며 격자에 반투명 몸통(Body)이 렌더링됩니다.
> 짝 규칙은 게임 모드 파서 코드(`CustomChartParsers.ParseBms`)를 그대로 옮긴 것입니다. 실제 인게임 플레이 확인 전이므로 검증 상태는 `Source`(코드 확인)입니다.
> 차트 상단에 `#BMSEDITER_PROFILE pump_it_up_rise` 헤더를 넣거나 메인 화면 콤보박스에서 프로파일을 선택해 작업하십시오.
> 프로파일 사양 상세 → **[🎮 게임 프로파일 단일 표](../specifications/game_profiles.md#pump-it-up-rise-pump_it_up_rise--filenamekeyword--replacepending)**

---

## 🦶 1. 채널 ↔ 레인 매핑 (`MapBmsChannelToPiuLane`)

**채널이 곧 발판(레인)입니다.** BMS 에디터 좌→우 첫 5개 레인(`16, 11, 12, 13, 14`)을 펌프 5키에 순서대로 매핑합니다.

| BMS Editer 레인 ID | BMS 채널 | 펌프 레인 인덱스 | 발판 방향 | 비고 |
|:---:|:---:|:---:|:---:|:---|
| **16** (1열 빨강) | `#xxx16` | `0` | **↙ (좌하)** | 1번 발판 |
| **11** (2열 흰색) | `#xxx11` | `1` | **↖ (좌상)** | 2번 발판 |
| **12** (3열 파랑) | `#xxx12` | `2` | **◎ (중앙)** | 3번 발판 |
| **13** (4열 흰색) | `#xxx13` | `3` | **↗ (우상)** | 4번 발판 |
| **14** (5열 파랑) | `#xxx14` | `4` | **↘ (우하)** | 5번 발판 |

> [!IMPORTANT]
> * **에디터의 `15`, `18` 레인은 쓰지 마십시오.** `MapBmsChannelToPiuLane` 매핑표에 없어 게임에서 경고 없이 무시됩니다.
> * 표준 BMS LN 채널(`51`~`59`)과 `#LNOBJ`는 파서가 읽지 않습니다. 홀드는 아래 3절의 `#WAV` 파일명 키워드 방식으로 찍으십시오.

---

## 🎵 2. `#WAV` 샘플 정의 규칙 (2자리 / 3자리 키음 자동 지원)

> [!NOTE]
> `ParseBms`는 기본 2자리(`#WAV01`~`#WAVZZ`)로 동작하며, `#WAV` 키 중 3자리(`#WAV001`~`#WAVZZZ`)가 하나라도 감지되면 데이터 라인 길이가 3의 배수일 때 자동으로 3자리 슬롯으로 전환합니다. (`#BPM001` 확장 BPM도 함께 지원)

일반 단노트 키음과 홀드용 키음 구성 예시(`hwa/<곡>/hwa2.bms`, 3자리 키음 기준):

| `#WAV` ID | 샘플 파일명 예시 | 역할 |
|:---:|:---|:---|
| `001`~`005` | `C5.wav`, `D5.wav`, `E5.wav`, `G5.wav`, `A5.wav` | 일반 단타(`X`, `0x43`) |
| `006` | `홀드 시작.wav` | 롱노트 시작(`M`, `0x57`) |
| `007` | `홀드 끝.wav` | 롱노트 끝(`W`, `0x5F`) — 사이는 자동 몸통(`H`, `0x5B`) |

---

## 🎯 3. 롱노트(Hold) 작성 규칙 — `#WAV` 파일명 키워드 + `ReplacePending`

파서는 `#WAV` 파일명에서 **공백(` `), 밑줄(`_`), 하이픈(`-`)을 모두 제거한 소문자**에서 키워드를 검사합니다.

### 3.1 인식 키워드 (대소문자 무관 · 부분 일치 · 시작 우선)

| 구분 | 정규화 키워드 | 파일명 예시 |
|:---|:---|:---|
| **Head (홀드 시작)** | `홀드시작`, `holdstart`, `longstart`, `lnstart` | `홀드 시작.wav`, `hold_start.wav`, `long-start.wav`, `lnstart.wav` |
| **Tail (홀드 끝)** | `홀드끝`, `holdend`, `longend`, `lnend` | `홀드 끝.wav`, `hold_end.wav`, `long-end.wav`, `lnend.wav` |
| 그 외 | — | 일반 단타(`X`) |

> [!IMPORTANT]
> * **시작 키워드를 먼저 검사합니다(`tailFirst: false`).** 한 파일명에 시작과 끝 키워드가 둘 다 들어 있으면 **시작(Head)**으로 판정됩니다. (DEFLATE와 반대)

### 3.2 짝 맞추기 규칙 (`ReplacePending`)

1. 레인마다 열린 홀드를 기억하는 칸(`lnOpenRow[lane]`)이 **하나뿐**입니다.
2. 끝이 오기 전에 같은 레인에서 시작이 또 나오면 **앞 시작을 덮어써서 버리고** 새 시작을 기억합니다(`ReplacedHead` 진단).
3. 끝이 나오면 현재 열린 시작과 짝을 지어 `M`(시작) → `H`(몸통) → `W`(끝) 스텝을 채우고 대기 칸을 비웁니다.
4. 시작과 끝이 같은 행(`row <= startRow`)에 겹치면 롱노트 대신 단타(`X`)로 변환합니다.
5. 열린 시작 없이 끝만 나오면(`OrphanTail`) 해당 위치가 비어 있을 때 **일반 단타(`X`)로 강등**합니다.
6. 곡 끝까지 닫히지 않은 시작(`OrphanHead`)은 스텝을 생성하지 않고 **무시**합니다.

---

## ⏱️ 4. 시간 계산 및 변속 / 해상도

* **마디와 박**: 1마디 = `4.0 × 마디 길이 배율(#xxx02)` 박으로 누적 계산합니다.
* **해상도**: 1박당 `16`분할(64분음표, `split = 16`) 그리드로 양자화하여 `Step_t` 블록을 구성합니다.
* **BPM 변경**: `#BPM` 기본값 외에 채널 `03`(16진수)과 `08`(`#BPMxx` 확장 표)을 모두 지원하며, BPM이 바뀌는 행마다 UCS 멀티블록으로 분할해 변속을 반영합니다.

---

## 📁 5. 커스텀 곡 폴더 규격 (`hwa/`)

게임 폴더 아래 `hwa/<곡 폴더>/` 또는 `hwa/<앨범 폴더>/<곡 폴더>/`에 배치합니다(`CustomPackManager.cs`).

* 차트: `*.bms` / `*.bme` / `*.bml` (또는 `*.ucs`)
* BGM: `music.ogg` / `*.mp3` / `*.flac` (키음 `.wav`와 구분하기 위해 OGG/MP3 권장)
* BGA: `video.mp4` / `*.webm` (인게임 음소거 자동 재생)
* 자켓 / 로딩 썸네일: `Thumbnail.png` / `cover.png` / `jacket.png`, 첫 번째 16:9 이미지는 로딩 씬 썸네일로 자동 선택
* 메타데이터: `info.txt` (`title`, `artist`, `bpm`, `level`/`single`, `double` — BMS 헤더보다 우선)

---

## 📝 6. 실제 BMS 텍스트 예시

```bms
*---------------------- HEADER FIELD
#PLAYER 1
#GENRE POP / ARRANGE
#TITLE Summertime (Arrange ver.)
#ARTIST 화영왕
#BPM 125
#PLAYLEVEL 10
#RANK 3
#BMSEDITER_PROFILE pump_it_up_rise

#WAV001 C5.wav
#WAV002 D5.wav
#WAV003 E5.wav
#WAV004 G5.wav
#WAV005 A5.wav
#WAV006 홀드 시작.wav
#WAV007 홀드 끝.wav
#STAGEFILE Thumbnail.png

*---------------------- MAIN DATA FIELD
* 마디 001: ↙(16) → ↖(11) → ◎(12) → ↗(13) 4분 단타 (3자리 슬롯)
#00116:001000000000
#00111:000002000000
#00112:000000003000
#00113:000000000004

* 마디 002: ↘(14) 3/4마디 길이 롱노트 (006=시작, 007=끝)
#00214:006000000007
```
