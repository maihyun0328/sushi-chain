# 게임플레이 레이턴시 개선

브랜치: `Feature/Latency` (base: `Feature/LocalChart`)

## 발견한 문제

| # | 위치 | 문제 | 영향 |
|---|---|---|---|
| 1 | `JsonManager.SpawnCheck` | 스폰 시각을 `AudioSource.time`으로 판단. 이 값은 DSP 버퍼(1024 샘플)를 보낼 때마다 한 번씩만 바뀐다 | 노트마다 0~21ms 오차 |
| 2 | `ShortNote/LongNote.Move` | 위치를 `speed * Time.deltaTime`로 누적 | 스폰 프레임 오차와 프레임 드랍 오차가 위치에 계속 남음 |
| 3 | `JsonManager.distance = 8.67` | 실제 스폰→판정선 거리는 9.67 (x ±11 → ±1.33) | 판정 정점이 `StartTime + 1/speed`로 밀림. EASY 222ms, NORMAL 125ms, HARD 87ms 늦음 |
| 4 | `SpawnCheck` | 프레임당 노트 1개만 스폰 | 동시치기·밀집 구간에서 노트가 프레임 단위로 밀림 |
| 5 | `JudgeManager` | `Input.GetKeyDown` 프레임 폴링 + 노트 위치로 판정 | 판정이 프레임 간격만큼 흔들림 (60fps 기준 최대 16.7ms) |
| 6 | `GetSpawnTime` | 입력 보정을 `- InputCalibration`으로 적용 | 부호가 반대. 늦게 치는 사람일수록 노트가 더 일찍 옴 |
| 7 | `GameManager.Update` | 매 프레임 `Debug.Log` 호출 | 프레임 시간이 늘어남 (에디터, 개발 빌드) |
| 8 | ProjectSettings | DSP 버퍼 1024 ("Best performance") | 출력 지연이 커짐 |

## 변경

- **`Timing/DspClockEstimator.cs`** (순수 C#)
  - 계단처럼 바뀌는 `dspTime`을 realtime 기준으로 보간해 연속 시계로 만든다.
  - 최근 2초 동안의 `dsp - realtime` 값 중 최대값을 offset으로 쓴다.
  - 오디오가 멈추거나 리셋되면 다시 맞춘다.
- **`Timing/SongClock.cs`**
  - `PlayScheduled`로 곡을 시작한다.
  - `SongTime`은 프레임마다 한 번 계산하고 단조 증가한다. `GameTime = SongTime - InputCalibration`.
  - 부팅할 때 DSP 버퍼를 256으로 설정한다. `PlayerPrefs "Audio.DspBufferSize"`로 바꿀 수 있고, 0이면 프로젝트 기본값을 쓴다.
- **`Timing/TimedKeyInput.cs`**
  - 눌림 여부는 계속 레거시 `Input.GetKeyDown`으로 판단한다. 키 설정이 그대로 동작한다.
  - 눌린 시각은 같은 프레임의 Input System 이벤트 timestamp에서 가져온다.
  - 맞는 이벤트가 없으면 프레임 시각을 쓴다.
- **노트**
  - `Init(NoteData)`로 노트 시각을 받는다.
  - 위치는 매 프레임 `judgeX ± (noteTime - GameTime) * speed`로 다시 계산한다.
  - 판정은 `|inputTime - noteTime| * speed`로 계산한다. 기존 판정 범위(월드 거리 기준 perfect 0.2, great 0.5, good 0.8, range 1)는 그대로다.
- **스폰**: 도착 `MoveTime` 전이 된 노트를 한 프레임에 모두 스폰한다.
- **시작 리드인**
  - 첫 노트가 스폰 위치에서 출발할 수 있을 만큼 곡 시작을 늦춘다.
  - 최소 0.1초, 최대 MoveTime(EASY 2.15초).
- **보정**: 메트로놈 탭 시각도 timestamp를 dsp 시간축으로 바꿔서 기록한다. 보정값은 노트 도착과 판정에 같은 부호로 적용한다.

## 검증

- `Tests/Timing/DspClockEstimatorTest.cs`: 시뮬레이션. 실행은 `localchart-test.sh Assets/Scripts/Timing/DspClockEstimator.cs Tests/Timing/DspClockEstimatorTest.cs`.

| 조건 | 원본 dspTime p99 | 보간 p99 | 보간 max |
|---|---|---|---|
| 1024 @48k, 60fps (기존 설정) | 21.15ms | 0.84ms | 1.10ms |
| 256 @48k, 60fps | 5.28ms | 0.14ms | 0.25ms |
| 512 @48k, 30fps (고부하) | 10.56ms | 1.01ms | 1.36ms |

- 1초 오디오 정지 후 재개: 100ms 안에 다시 맞춤.
- 보간값이 거꾸로 가는 경우: 0회.
- Play Mode 검증은 아직 하지 않았다. 아래 체크리스트를 따른다.

## Play Mode 체크리스트

1. Console에 `[SongClock] DSP buffer 256 x N` 로그가 찍히는지 확인한다. 소리가 끊기면 512로 올린다: `PlayerPrefs.SetInt("Audio.DspBufferSize", 512)`.
2. 내장곡 1곡을 플레이하고 판정 타이밍을 확인한다. 문제 #3이 고쳐져서, 이전보다 노트가 1/speed초 일찍 판정선에 온다.
3. 옵션 → 입력 보정을 다시 측정한다. 부호가 바뀌었으므로 예전 체감과 다를 수 있다.
4. 동시치기, 롱노트 시작과 끝 판정을 확인한다.

## 참고

- 오디오 재생 시각 보간 (AudioSource.time 지터, latency offset): https://itch.io/blog/744936/the-right-way-to-interpolate-the-audio-playback-time-unity
- dspTime 기준 비트 추적: https://noggindecay.itch.io/joseph-kills-lucy/devlog/648577/devlog-2-coding-beat-tracking
- Android timeSamples 지연과 DSP Buffer "Best/Good latency": https://discussions.unity.com/t/audiosource-time-innaccurate-in-android/665249
- 입력 timestamp 기반 판정, latency compensation: https://discussions.unity.com/t/rhythm-game-sync-issue/926644
- `AudioConfiguration.dspBufferSize`, `AudioSettings.Reset`: https://docs.unity3d.com/ScriptReference/AudioConfiguration-dspBufferSize.html
- Input System `CallbackContext.time` (입력 생성 시각, realtimeSinceStartup 시간축): https://docs.unity3d.com/Packages/com.unity.inputsystem@1.12/api/UnityEngine.InputSystem.InputAction.CallbackContext.html
