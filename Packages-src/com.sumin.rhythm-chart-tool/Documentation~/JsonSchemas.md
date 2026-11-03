# JSON 스키마

이 패키지에는 두 종류의 JSON 이 있습니다.

| 종류 | 만드는 곳 | 읽는 곳 | 용도 |
|---|---|---|---|
| **분석 JSON** | `Python~/analyze.py` | Unity 에디터(`ChartImporter`) | 오디오 분석 결과. 임시 파일이라 보통 직접 다룰 일이 없습니다. |
| **차트 JSON** | `ChartJson.ToJson` / 차트 인스펙터의 "내보내기" | `ChartJson.FromJson` / "가져오기" | 차트(분석 데이터 + 노트)를 에셋 밖으로 옮기거나 런타임에서 읽을 때. |

두 JSON 은 각자 `schemaVersion` 을 가지며 서로 독립적입니다. 현재 둘 다 `1` 입니다.

---

## 1. 분석 JSON (`analyze.py` 출력)

```json
{
  "schemaVersion": 1,
  "bpm": 127.97,
  "offset": 0.0054,
  "duration": 60.5,
  "beats": [0.4644, 0.952, 1.4164],
  "onsets": [
    { "time": 0.2322, "strength": 0.181, "band": "high" },
    { "time": 0.4644, "strength": 1.0,   "band": "low"  }
  ]
}
```

| 필드 | 타입 | 설명 |
|---|---|---|
| `schemaVersion` | int | 분석 JSON 의 버전. 에디터는 `1` 만 읽습니다. 다르면 "지원하지 않는 분석 JSON 버전" 오류. |
| `bpm` | number | 곡 전체에 적용하는 **고정** BPM. 박자를 못 찾으면 `0`. |
| `offset` | number | BPM 그리드의 첫 선 위치(초). `0` 이상 한 박 길이 미만. 그리드 선은 `offset + k × 60/bpm`. |
| `duration` | number | 곡 길이(초). |
| `beats` | number[] | 박자 추적기(librosa `beat_track`)가 찾은 **실제 박자 시각**(초). 고정 그리드와 다를 수 있습니다(아래 참고). |
| `onsets` | object[] | 타격 시점 목록(시각 오름차순). |
| `onsets[].time` | number | 곡 시작 기준 시각(초). |
| `onsets[].strength` | number | 0~1. 곡 안에서의 **상대적인** 세기(onset 세기의 95번째 백분위수를 1.0 으로 정규화, 넘으면 1.0). |
| `onsets[].band` | string | `"low"` / `"mid"` / `"high"`. 해당 시점에 가장 두드러진 주파수 대역(저 <200Hz, 중 200Hz~2kHz, 고 ≥2kHz). |

### `beats` 와 `bpm`/`offset` 의 관계

`beats` 는 추적기가 실제로 짚은 박자라서 프레임(약 23ms)에 양자화되어 있고, 곡에 따라 한 박 정도 위상이 밀리기도 합니다.
반면 `bpm`/`offset` 은 이 값을 시드로 삼아 더 세밀한 onset envelope 위에서 그리드 점수가 가장 높은 값으로 다시 맞춘 것입니다.
**에디터의 비트 그리드, 스냅, 노트 생성은 `bpm`/`offset` 만 사용하며 `beats` 는 쓰지 않습니다.**
`beats` 를 그리드로 쓰지 마세요. 필요하면 `offset + k × 60/bpm` 으로 직접 계산하세요.

### 명령줄에서 직접 실행

```bash
python analyze.py <오디오 경로> <출력 JSON 경로> [--separate]
```

- stdout: 진행 상황을 `PROGRESS 0.40` 형식의 한 줄씩 출력합니다(0.0 → 1.0).
- 성공은 종료 코드 `0`. 실패(없는 파일, 읽을 수 없는 오디오, 인자 오류, 누락된 패키지 등)는 stderr 에 `ERROR: ...` 를 쓰고 종료 코드 `1`.
- `--separate`: demucs 로 드럼을 분리해 onset 을 검출합니다. 기본은 꺼짐이며 `requirements-separate.txt` 설치가 필요합니다. **이 옵션은 아직 실제로 실행해 보지 못했습니다.**
- 출력 파일은 임시 파일에 쓴 뒤 교체하므로 중간에 취소돼도 깨진 JSON 이 남지 않습니다.

---

## 2. 차트 JSON

`ChartJson.ToJson(ChartData)` 가 만들고 `ChartJson.FromJson(json, ChartData)` / `ChartJson.CreateFromJson(json)` 이 읽습니다. 에디터가 아닌 런타임에서도 쓸 수 있습니다.
오디오 클립과 프리셋 참조는 Unity 오브젝트라 JSON 에 들어가지 않습니다(가져오기는 대상 차트의 클립과 프리셋을 유지합니다).

```json
{
  "schemaVersion": 1,
  "bpm": 128.0,
  "offset": 0.005,
  "duration": 60.5,
  "laneCount": 4,
  "generationSeed": 12345,
  "beats": [0.464, 0.952, 1.416],
  "onsets": [
    { "time": 0.232, "strength": 0.181, "band": "High" },
    { "time": 0.464, "strength": 1.0,   "band": "Low"  }
  ],
  "difficulties": [
    { "difficulty": "Easy",
      "notes": [
        { "time": 0.474, "lane": 2, "holdLength": 0.0, "type": "Tap" },
        { "time": 0.943, "lane": 1, "holdLength": 0.0, "type": "Tap" }
      ] },
    { "difficulty": "Normal",
      "notes": [
        { "time": 0.474, "lane": 0, "holdLength": 0.0,   "type": "Tap"  },
        { "time": 8.210, "lane": 1, "holdLength": 0.469, "type": "Hold" }
      ] },
    { "difficulty": "Hard",   "notes": [] },
    { "difficulty": "Expert", "notes": [] }
  ]
}
```

> 예시는 읽기 쉽게 줄이고 반올림했습니다. 실제 출력은 Unity `JsonUtility` 가 `float` 을 쓰는 방식 때문에 `0.004999999888241291` 처럼 긴 소수가 나올 수 있습니다. 숫자로 읽으면 됩니다.

### 필드

| 필드 | 타입 | 설명 |
|---|---|---|
| `schemaVersion` | int | 차트 JSON 의 버전(현재 `1`). |
| `bpm` | number | 고정 BPM. `0` 이상(`0` 이면 그리드 없음). |
| `offset` | number | BPM 그리드의 첫 선(초). |
| `duration` | number | 곡 길이(초). **0 보다 커야 합니다.** |
| `laneCount` | int | 레인 수. **4~6**. |
| `generationSeed` | int | 노트 생성 기본 시드. 난이도마다 여기서 다른 시드가 파생됩니다. |
| `beats` | number[] | 분석 JSON 의 `beats` 와 같음(박자 추적기의 실제 박자). |
| `onsets` | object[] | 분석 JSON 의 `onsets` 와 같으나 `band` 가 `"Low"`/`"Mid"`/`"High"`(첫 글자 대문자). |
| `difficulties` | object[] | 난이도별 노트. 보통 `Easy`, `Normal`, `Hard`, `Expert` 순서로 4개. |
| `difficulties[].difficulty` | string | `"Easy"` / `"Normal"` / `"Hard"` / `"Expert"`. |
| `difficulties[].notes` | object[] | 그 난이도의 노트(시간, 레인 순). |
| `notes[].time` | number | 노트 시각(초, 곡 시작 기준). 0 이상. |
| `notes[].lane` | int | 레인 번호(0 부터). `laneCount` 미만. |
| `notes[].holdLength` | number | 롱노트 유지 시간(초). `Tap` 이면 `0`. 0 이상. |
| `notes[].type` | string | `"Tap"` 또는 `"Hold"`. |

노트의 규칙(에디터가 지키는 것): 같은 레인에서 두 노트(롱노트는 `time` ~ `time + holdLength`)가 겹치지 않고, 노트는 `0` ~ `duration` 안에 있습니다.
JSON 가져오기는 레인 범위, 시각, 길이, enum 값만 검사하며 겹침은 검사하지 않습니다.

### 가져오기 검증

`FromJson` 은 먼저 모두 검사하고, 통과했을 때만 대상 차트를 바꿉니다. 실패하면 차트는 그대로입니다.

| 상황 | 예외 |
|---|---|
| JSON 이 아니거나 비어 있음, `beats`/`onsets`/`difficulties` 없음, `duration` ≤ 0, `bpm` < 0, `laneCount` 가 4~6 밖, 노트의 레인/시각/길이가 범위 밖, 알 수 없는 enum 문자열 | `System.IO.InvalidDataException` |
| 현재 코드보다 **새로운** `schemaVersion` | `System.NotSupportedException` |
| 현재보다 **오래된** `schemaVersion` 인데 마이그레이션이 등록되어 있지 않음 | `System.NotSupportedException` |
| 마이그레이션 결과의 `schemaVersion` 이 한 단계 올라가지 않음 | `System.IO.InvalidDataException` |

### 마이그레이션 훅

스키마가 바뀌면 이전 파일을 읽을 수 있도록 단계별 변환을 등록합니다. 함수는 `schemaVersion = n` 인 JSON 문자열을 받아 `n+1` 인 JSON 문자열을 돌려줘야 합니다.

```csharp
using SuminTools.RhythmChart;

// 예: schemaVersion 1 → 2 (가상의 변경)
ChartJsonMigrations.Register(1, json =>
{
    // json 문자열을 변환해서 "schemaVersion": 2 로 올린 문자열을 돌려준다
    return Upgrade1To2(json);
});

ChartJson.FromJson(oldJson, chart);   // 필요한 단계를 순서대로 거쳐 현재 버전으로 읽는다
```

현재 버전이 `1` 이라 이 패키지에 등록된 변환은 아직 없습니다(훅은 EditMode 테스트로 검증했습니다).
