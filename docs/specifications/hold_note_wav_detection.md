# 🎵 WAV 파일명 기반 홀드(롱노트) 자동 감지 및 페어링 설계

> [!NOTE]
> 이 문서는 **#WAV 파일명(오디오 리소스 이름)에 담긴 명명 규칙을 분석하여 롱노트(Hold/Long Note)의 시작과 끝을 자동으로 판별하고 짝(Pair)을 맞추는 기능**에 대한 설계 및 아이디어 제안서입니다.
> 
> 배경 분석 및 작성 시간 이슈: [⏱️ 채보 작성 시간 — 경고와 개선 과제](../issues/authoring_time.md)

---

## 1. 배경 및 문제 의식

* **현실의 채보 제작 방식:**
  * 표준 BMS 규격(채널 `51~59` 또는 `#LNOBJ`) 외에도, 상용 리듬게임 모딩 및 커스텀 채보(예: *Muse Dash*, *Sixtar Gate: STARGAZER* 등)에서는 **롱노트의 시작과 끝을 일반 건반 채널에 두고 `#WAV` 키음 파일명으로 구별**하는 경우가 많습니다.
  * 사운드 디자이너 및 차트 제작자들은 샘플 슬라이싱 시 통상적으로 `_start.wav`, `_end.wav` (또는 `_tail`, `_st`, `_ed`, `_rel` 등)과 같은 일정한 접미사/키워드 규칙을 사용합니다.
* **현재 에디터의 한계:**
  * 에디터가 키음 파일명의 의미를 알지 못하므로 모든 노트가 `NoteType.Normal`(일반 단노트) 점으로만 보입니다.
  * 이로 인해 롱노트 구간이 화면에서 시각적으로 이어지지 않고, 시작/끝 짝이 어긋났을 때 게임을 실행해 로그를 확인하기 전까지는 오류를 잡을 수 없습니다.

---

## 2. WAV 파일명 패턴 감지 규칙 (Naming Conventions)

키음 목록(`WavList` / `Chart.WavTable`)이 로드될 때, 파일명(확장자 제외)의 끝부분 또는 구분자 뒤의 토큰을 검사하여 **Start 키음**과 **End 키음**을 1:1로 짝짓는 사전을 구성합니다.

### A. 표준 접미사 페어링 테이블

| 구분 | 주요 키워드 (대소문자 무관) | 파일명 예시 |
|:---|:---|:---|
| **Start (시작)** | `_start`, `-start`, `_st`, `_begin`, `_head`, `_in`, `_s` | `synth_start.wav`, `vox_st.ogg`, `guitar_s.wav` |
| **End (종료)** | `_end`, `-end`, `_ed`, `_tail`, `_rel`, `_release`, `_out`, `_e` | `synth_end.wav`, `vox_ed.ogg`, `guitar_e.wav` |
| **Loop (루프)** | `_loop`, `-loop`, `_lp` | `pad_loop.wav` (지속음 홀드 대상) |

### B. 게임별 특수 UID / 프리픽스 패턴 확장
* **뮤즈 대시 (UID 패턴):**
  * 파일명 앞 6자리 UID 중 특정 종류 코드 분석 (예: `xx02xx`=롱노트, `xx04xx`=샌드백).
  * 동일 UID 계열에서 홀수 번째=시작, 짝수 번째=끝 규칙과 결합.
* **스타게이저:**
  * 파일명 내 `_ln_` 또는 `_hold_` 태그 검출.

---

## 3. 자동 감지 및 페어링 알고리즘 (2-Pass Pipeline)

```
[BMS 파일 / 키음 로드]
       │
       ▼
【Pass 1: 키음 분석 (WavPair Dictionary 구축)】
  파일명 정규식 분석 ──▶ BaseName 일치하는 (StartKey ↔ EndKey) 페어 매핑
       │
       ▼
【Pass 2: 차트 타임라인 스캔 (Pairing Engine)】
  레인별 시간순 노트 순회:
    - StartKey 노트를 만나면: 대기 큐(pendingStarts[lane])에 등록
    - 동일 레인에서 대응하는 EndKey 노트를 만나면:
        1) Start 노트를 NoteType.LongStart로 승격
        2) End 노트의 위치(Measure, Position)를 Start 노터의 End 지점으로 흡수
        3) 독립 End 노트는 목록에서 제거 (단일 롱노트 객체화)
       │
       ▼
【Pass 3: 짝 검사기 (Pair Linter)】
  짝이 맞지 않는 고아(Orphan) 노트 감지 시 UI 경고 마커 표시
```

### 핵심 C# 구현 아이디어 (의사 코드)

```csharp
// 1. 키음 페어 모델
public sealed record WavHoldPair(string BaseName, string StartKey, string EndKey);

// 2. 파일명 정규식
[GeneratedRegex(@"^(.*)[_-](start|st|begin|head|in|s)$", RegexOptions.IgnoreCase)]
private static partial Regex StartPattern();

[GeneratedRegex(@"^(.*)[_-](end|ed|tail|rel|release|out|e)$", RegexOptions.IgnoreCase)]
private static partial Regex EndPattern();

// 3. 차트 노트 스캔 및 결합
var pendingStarts = new Dictionary<string, BmsNote>(); // LaneId -> StartNote

foreach (var note in chart.Notes.OrderBy(n => n.Measure + n.Position))
{
    // Start 키음인 경우
    if (pairLookup.TryGetEndKey(note.WavKey, out var expectedEndKey))
    {
        pendingStarts[note.LaneId] = note;
    }
    // 직전 Start가 기다리던 End 키음인 경우
    else if (pendingStarts.TryGetValue(note.LaneId, out var startNote)
             && pairLookup.GetExpectedEndKey(startNote.WavKey) == note.WavKey)
    {
        // 롱노트로 변환 및 끝점 결합
        startNote.Type = NoteType.LongStart;
        startNote.EndMeasure = note.Measure;
        startNote.EndPosition = note.Position;

        // 종단 노트는 흡수
        consumedNotes.Add(note);
        pendingStarts.Remove(note.LaneId);
    }
}
```

---

## 4. 에디터 인터랙션 및 활용 효과

1. **시각화 (Visual Feedback):**
   * 에디터가 파일을 열자마자 `NoteGridControl`에서 롱노트 구간이 **반투명 몸통(Body Bar)과 캡**으로 즉시 이어져 보임.
   * 작업자가 "이게 롱노트로 제대로 묶였는지" 한눈에 확인 가능.
2. **에디트 모드 자동 완성 (Smart Placement):**
   * 키음 팔레트에서 `xxx_start.wav`를 선택하고 그리드에 노트를 찍은 뒤 위로 드래그하면,
   * 에디터가 끝점에 대응하는 `xxx_end.wav`를 자동으로 배정하여 짝이 절대 깨지지 않음.
3. **짝 검사기(Pair Linter) 연동:**
   * Start만 찍히고 End가 없거나, 반대로 Start 없이 End만 단독으로 찍힌 고아 노트를 에디터 내에서 실시간 경고 표시.
   * 게임을 켜서 로그를 볼 필요 없이 **에디터 안에서 1초 만에 오류 검증 완료**.
4. **기존 파일 호환성 보장:**
   * 저장 시에는 원본 차트의 방식(단노트 2개 형태 또는 규격 채널)에 맞춰 완벽하게 복원 직렬화.
