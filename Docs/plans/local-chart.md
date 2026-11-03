# 온디바이스 채보 생성 — 구현 계획

## 목표

로컬 오디오 파일(mp3/wav/ogg)을 고르면 서버 없이 게임 안에서 채보를 만든다. YouTube URL 입력은 지금처럼 서버를 쓴다.

결과는 서버(ai-chart-server)와 같아야 한다. 같은 PCM과 같은 seed key를 넣으면 같은 NoteList가 나와야 한다.

## 브랜치

```
Feature/CustomSong
└─ Feature/LocalChart                 통합 브랜치 (계약, 통합, Unity 연동 마무리)
   ├─ Feature/LocalChart-Engine       app/chart/* 포팅 (grid, candidates, density, longnotes, lanes, validate, params, PCG64)
   ├─ Feature/LocalChart-Analysis     app/audio/analysis.py 포팅 (STFT, HPSS, mel flux, tempo, beat_track, onset_detect, chroma, downbeat)
   └─ Feature/LocalChart-Input        Unity 연동 (파일 선택, 디코딩, 22050Hz mono 리샘플, 백그라운드 실행, 저장, UI 탭)
```

세 브랜치는 서로 다른 파일만 수정한다. 각각 `.worktrees/<name>`에서 병렬로 작업한 뒤 `Feature/LocalChart`로 머지한다.

## 계약

- `Assets/Scripts/LocalChart/LocalChartContracts.cs`
  - `AudioFeatures`, `LocalNote`, 상수, `SeedKeys`
- `Assets/Scripts/LocalChart/SushiChain.LocalChart.asmdef`
  - 순수 C#: `noEngineReferences`, netstandard2.1
- 진입점
  - `AudioAnalyzer.Analyze(float[] mono22050)` → `AudioFeatures`
  - `ChartEngine.Generate(AudioFeatures, string difficulty, string seedKey)` → `List<LocalNote>`

## 검증

- **parity 테스트**는 Unity 없이 실행한다. Unity 내장 .NET 6 런타임과 Roslyn을 쓴다(`localchart-test.sh`).
- **정답 데이터**는 `ai-chart-server/.venv`의 Python으로 서버 코드를 직접 실행해서 만든다. 입력 PCM, 중간 산출물, 최종 NoteList가 대상이다.
- **Engine 트랙**: 같은 AudioFeatures를 넣으면 NoteList가 완전히 같아야 한다. 시간은 소수 3자리 반올림 값 기준이다.
- **Analysis 트랙**: 같은 PCM을 넣었을 때 최종 채보가 같아야 한다. 중간값에서 float 오차가 생기는 지점은 서버 측(chart-server)과 허용 기준을 정해서 기록한다.
- **실제 mp3 입력**: Unity 디코더(FMOD)와 ffmpeg/soxr은 같은 mp3에서도 샘플이 다르다. 따라서 서버 결과와 비교하지 않고 validate 통과 여부로만 판단한다. 서버 측 의견도 같다.

## 범위 밖

- YouTube를 기기에서 직접 다운로드하는 기능 (기존 서버 경로 유지)
- 모바일 성능 최적화 (Burst/Jobs)

## 포팅 기준선 (v3, 2026-11-04 갱신)

ai-chart-server는 git으로 관리되지 않으므로, 기준선은 파일 해시로 고정한다. CHART_ALGO_VERSION=1.0.0, FEATURE_VERSION=3.
v3 변경 사항: HPSS 청크 시작점을 짝수로 맞췄다(`_map_time_chunks`의 `lo -= lo % 2`). 이제 청크 처리 결과가 한 번에 처리한 결과와 비트 단위로 같다. 따라서 C# 구현은 청크 없이 전체를 한 번에 처리한다.
seed key(upload)는 `upload:<원본 파일 바이트 sha256>|<difficulty>|1.0.0` 형식이다. 디코딩한 PCM의 해시가 아니다.
포팅 명세는 `ai-chart-server/docs/PORTING.md`, golden 데이터는 `ai-chart-server/tests/golden/`에 있다.

```
e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  app/audio/__init__.py
773f5c0c48c8968a4fc99a0def47e09779dd9f45bc23ed932927ac8c0c0877f1  app/audio/analysis.py
eedc0d99715ded7ca572d7c2c55781b8f48a60ec8d65b9a5ac67af733817d7b3  app/audio/decode.py
d239d16cda71eb9de2c78db1b085de52d25bbad884198da353e7aa09d5fb4850  app/audio/features.py
fd585914ebc43f2bca90c1cb9d3bac18f38bff05494e8ac9f1fa9b0bfb007449  app/audio/synth.py
e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  app/chart/__init__.py
d7499cb8da56b3bbfd2c938c10dac96bd75cb101a9f5b13ccc150a72c504c6bf  app/chart/candidates.py
45ea5c0d6d6f196c232823abfb2838345825cc2a38459cb3d21d7811bc2e5edf  app/chart/density.py
aa62401957c58de56309c9a33c95c25bc59fe8748e756bccb80979172bf92a41  app/chart/generator.py
2773cde9c8331b7be346bcad789ca77a5a5b96ac150aae7c8fc982fc9b09d320  app/chart/grid.py
511cd0ad7fe1226d930f57e0abe2857a624f73f831ecdda2a4f0d539eb8316e0  app/chart/lanes.py
ebcb0bd6d0b211ee1a8afed3bafa61cb9635630b2261fbcfcfd30929ece7823d  app/chart/longnotes.py
17a19c6a087b047d35f36c3ed36870c5b2282ec16a92604d26dbcbcdc066735a  app/chart/params.py
904999fd9eb2b3fe06def89268ebfc16e35d60103cfdc7be1dda32d604f581b2  app/chart/serialize.py
9ad4f95d4345b5da6fa3967ef233c7797f626d827afb1f0806380c2838be31f9  app/chart/validate.py
bf2cea8cd89f86fb7ecce5687181376334175bb54e17e369c699d7362a7d0e35  app/core/constants.py
752f1d34eb538b02421faa6a78f9a65a7f32ea8656a0827e98fecbc70f163dbb  app/models/notes.py
eeab13730520aa4397f3f6a4e4dc7d0fa8b58ea18277854b6ed4eb85a4e8ac5f  tests/golden/input.f32
574d0a121086dcd6e812e624cd5bb6788655cd0cf8c68ee31727f948471f876e  tests/golden/chart_easy.json
d424d6cf4c4dfdc898c28bf9906832b8a8ebbe1bec43d6076b3d3ec56f16555e  tests/golden/chart_hard.json
582c5f68aea21e8f04135dda4182e22ee227aa7bfcc20269b68b56583faa9b90  tests/golden/chart_normal.json
50510b84aef9a313e62dd5c81e10ad4f995ec3154fa5d910a0fc7f319e95cb5e  scripts/make_golden.py
```
