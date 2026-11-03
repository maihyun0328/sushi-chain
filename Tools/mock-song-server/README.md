# mock-song-server

`ai-chart-server`(실제 서버) 계약을 흉내 내는 로컬 mock. 실제 서버가 없어도 Unity 클라이언트의 커스텀 곡 흐름(요청 → polling → asset 다운로드 → 저장 → 플레이)을 끝까지 테스트할 수 있다.

- 계약의 기준은 `ai-chart-server/app`(라우트, `app/models/api.py`)이다. 클라이언트 연동 문서는 `Docs/custom-song-api.md`.
- 의존성 없음. Node 23.6 이상(.ts 직접 실행, type stripping).

## 실행

저장소 루트에서:

```bash
node Tools/mock-song-server/server.ts            # http://localhost:8000/api/v1
PORT=9000 node Tools/mock-song-server/server.ts  # 포트 변경 (asset URL도 같이 바뀐다)
```

클라이언트의 개발 빌드 기본 Base URL이 `http://localhost:8000/api/v1`이라 별도 설정 없이 붙는다. 다른 주소를 쓰려면 PlayerPrefs `CustomSong.BaseUrl`을 바꾼다. Unity에서 http로 요청하려면 Player Settings → "Allow downloads over HTTP"를 `Development builds only` 이상으로 둔다.

## 인증

`/api/v1` 아래 모든 경로(asset 포함, 없는 경로 포함)는 `Authorization: Bearer <아무 문자열>`이 있어야 한다. 없거나 비어 있으면 `403 {"detail":"forbidden"}`.

**mock은 JWT 서명, 만료, issuer/audience, scope를 검증하지 않는다.** 실제 서버는 모두 검증하고, 실패하면 똑같이 403을 준다. 그래서 mock에서 되던 토큰이 실제 서버에서는 403일 수 있다. 클라이언트에는 `PlayerPrefs` `CustomSong.DevToken`에 아무 값(예: `mock`)이나 넣으면 된다.

## 동작

| 요청 | 결과 |
|---|---|
| `POST /api/v1/charts {youtubeUrl, difficulty?}` | `202 {"jobId","status":"queued"}`, `Location` 헤더. `difficulty` 기본값 `normal` |
| 같은 youtubeId+difficulty가 진행 중 | 같은 `jobId`를 돌려준다 (in-flight dedup) |
| 같은 youtubeId+difficulty가 이미 완료 | 새 `jobId`, `status:"completed"`로 바로 응답 (캐시) |
| 잘못된 YouTube URL | `422 {"detail":"invalid_youtube_url"}` |
| 잘못된 difficulty / JSON / 모르는 필드 | `422 {"detail":"invalid_request","errors":[{loc,msg,type}]}` |
| URL에 `busy`가 든 **첫** 요청 | `503 {"detail":"queue_full"}` + `Retry-After: 2`. 그 다음부터는 정상 접수 |
| URL에 `fail` | 약 3초 뒤 `failed`, `error.code = "analysis_failed"` |
| URL에 `toolong` | 약 3초 뒤 `failed`, `error.code = "too_long"` (`toolong`이 `fail`보다 우선) |
| `GET /api/v1/charts/{jobId}` | `queued`(약 1초) → `processing`(약 5초) → `completed`. 없는 jobId는 `404 {"detail":"not_found"}` |
| `GET /api/v1/assets/{assetId}/audio` | `audio/mpeg`. 단일 `Range`를 지원한다(206, 범위 밖이면 416) |
| `GET /api/v1/assets/{assetId}/thumbnail` | `image/jpeg` |
| `GET /health` | `200 {"status":"ok"}` (인증 없음) |

- 상태는 메모리에만 있다. 재시작하면 job, 캐시, `busy` 플래그가 모두 초기화된다.
- `fail`, `busy`, `toolong`은 URL 문자열 어디에 있어도 된다. video ID가 11자 규칙을 지켜야 하므로 `https://youtu.be/failfailfai`, `https://www.youtube.com/shorts/toolong_xyz`, `https://youtu.be/busybusybus` 또는 `...watch?v=dQw4w9WgXcQ&mock=fail`처럼 쓴다.
- URL 검증은 서버의 `app/services/youtube_url.py`와 같은 규칙이다 (`youtu.be/{id}`, `youtube.com|www.|m.|music.` 의 `/watch?v=`, `/shorts/`, `/embed/`, `/live/`, 포트·userinfo 금지, 200자 이하).
- 완료 응답은 실제 서버와 같은 모양이다. 없는 필드는 `null`로 내려준다.
  - `algorithmVersion`: `"1.0.0-mock"` (실제 서버는 `"1.0.0"`)
  - `song.title`: `Mock Song {youtubeId}`, `artist`: `null`, `duration`: 173.479, `bpm`: 95
  - `chart`: `Assets/Resources/SOUTHSIDE FREESTYLE.json`의 `{"Notes":[...]}`를 그대로 넣는다
- `assetId` = `sha256("youtube:" + youtubeId)` 앞 32자(소문자 hex). 같은 곡이면 난이도와 상관없이 같다. 해당 곡의 job이 완료되기 전에는 asset이 404다.
- asset URL은 절대 URL `http://localhost:{PORT}/api/v1/assets/{assetId}/...`이다.
- 파일은 모든 곡이 같다.
  - audio ← `Assets/Package/BewhY (비와이) - SOUTHSIDE FREESTYLE (Official Video).mp3`
  - thumbnail ← `Assets/Plugins/Demigiant/DOTween/Editor/Imgs/Header.jpg` (저장소에 있는 유일한 jpg라서 쓴다. 512x164 DOTween 로고 이미지)
- 실제 서버와 다른 점: 429(요청 제한), `storage_full`/`youtube_disabled`/`busy` 503, `asset_expired`, 304/ETag는 흉내 내지 않는다. 실제 서버의 `queue_full` `Retry-After`는 30초다.

## curl 예시

```bash
B=http://localhost:8000/api/v1
A='Authorization: Bearer mock'

# 인증 없음 → 403
curl -s -X POST $B/charts -d '{}'

# 작업 생성 → 202 {"jobId":"...","status":"queued"}
JOB=$(curl -s -X POST $B/charts -H "$A" -H 'Content-Type: application/json' \
  -d '{"youtubeUrl":"https://www.youtube.com/watch?v=dQw4w9WgXcQ","difficulty":"hard"}' \
  | sed -E 's/.*"jobId":"([^"]+)".*/\1/')

# polling (약 6초 뒤 completed, song + chart 포함)
curl -s -H "$A" $B/charts/$JOB

# asset (completed 응답의 song.audioUrl / thumbnailUrl 그대로)
curl -s -H "$A" -o audio.mp3 "<audioUrl>"
curl -s -H "$A" -H 'Range: bytes=0-99' -D - -o /dev/null "<audioUrl>"   # 206

# 실패 / 큐 가득 참 / 잘못된 URL
curl -s -X POST $B/charts -H "$A" -d '{"youtubeUrl":"https://youtu.be/failfailfai"}'
curl -s -i -X POST $B/charts -H "$A" -d '{"youtubeUrl":"https://youtu.be/busybusybus"}'  # 첫 번째만 503
curl -s -X POST $B/charts -H "$A" -d '{"youtubeUrl":"https://example.com"}'             # 422
```
