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
