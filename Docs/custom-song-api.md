# 커스텀 곡 API 연동 (클라이언트)

Sushi Chain 클라이언트(Unity)가 채보 생성 서버 `ai-chart-server`를 쓰는 방법을 정리한 문서다.

> **기준은 서버 저장소다.** 필드 이름, 상태 코드, 에러 코드의 최종 기준은 `ai-chart-server/app`(`app/api/charts.py`, `app/api/assets.py`, `app/models/api.py`, `app/core/constants.py`)이다. 이 문서는 그 계약을 클라이언트 관점에서 옮긴 것이다. 서로 다르면 서버 코드가 맞고, 이 문서와 `Assets/Scripts/CustomSong/CustomSongContracts.cs`를 고친다. 클라이언트가 계약을 바꾸지 않는다.

관련 파일:

- DTO, 설정, 인증 인터페이스: `Assets/Scripts/CustomSong/CustomSongContracts.cs`
- 요청과 polling, 다운로드: `CustomSongService.cs` / 로컬 저장: `CustomSongStore.cs`
- 로컬 mock 서버: `Tools/mock-song-server/server.ts` (사용법은 같은 폴더 `README.md`)

## 1. 흐름

```
Client                                              ai-chart-server (/api/v1)
  │ POST /charts {youtubeUrl, difficulty}  + Bearer   │
  │ ────────────────────────────────────────────────▶ │ 202 {jobId, status: queued | completed}
  │ GET /charts/{jobId}  (2초마다)          + Bearer   │
  │ ────────────────────────────────────────────────▶ │ 200 {status: queued | processing}
  │                    ...                            │
  │ GET /charts/{jobId}                     + Bearer   │
  │ ────────────────────────────────────────────────▶ │ 200 {status: completed, song, chart, algorithmVersion}
  │ GET song.audioUrl / song.thumbnailUrl   + Bearer   │  (곡당 1회, 로컬에 있으면 건너뜀)
  │ ────────────────────────────────────────────────▶ │ 200 audio/mpeg, image/jpeg
```

채보(`chart`)는 상태 응답에 인라인으로 들어온다. 별도 다운로드가 없다.

## 2. Base URL과 환경 전환

| 환경 | Base URL |
|---|---|
| 로컬 (실제 서버 또는 mock) | `http://localhost:8000/api/v1` |
| 운영 | `https://sushi-chain.aidengoldkr.dev/api/v1` |

- 클라이언트는 `CustomSongConfig.BaseUrl`을 쓴다. 값은 PlayerPrefs 키 **`CustomSong.BaseUrl`**에 저장되고, 끝의 `/`는 제거된다.
- 저장된 값이 없으면 `Debug.isDebugBuild`일 때 로컬, 아니면 운영 주소를 쓴다 (`CustomSongConfig.DefaultBaseUrl`).
- 바꾸는 방법: 코드에서 `CustomSongConfig.BaseUrl = CustomSongConfig.ProductionBaseUrl;` 또는 에디터 콘솔/디버그 메뉴에서 `PlayerPrefs.SetString("CustomSong.BaseUrl", "...")`. 지우려면 `PlayerPrefs.DeleteKey("CustomSong.BaseUrl")`.
- 로컬은 http다. Unity Player Settings → "Allow downloads over HTTP"를 `Development builds only` 이상으로 둔다.
- 로컬에서 **실제 서버**를 띄울 때 주의할 점 (서버 기본값이 운영용이다):
  - `REQUIRE_HTTPS=true`가 기본이라 http 요청은 403이다. 로컬에서는 `REQUIRE_HTTPS=false`.
  - asset URL은 `PUBLIC_BASE_URL`(기본 운영 도메인)로 만들어진다. 로컬에서는 `PUBLIC_BASE_URL=http://localhost:8000`.
  - 토큰의 issuer/audience가 서버 `AUTH_ISSUER`/`AUTH_AUDIENCE`와 같아야 한다.
- 모든 JSON은 UTF-8, 요청에는 `Content-Type: application/json`을 붙인다. 클라이언트는 `UnityWebRequest`를 쓰므로 CORS는 상관없다.

## 3. 인증

- `/api/v1` 아래 **모든 요청(asset 포함)**에 `Authorization: Bearer <JWT>`가 필요하다.
- 헤더가 없거나, 형식이 틀리거나, 서명/만료/issuer/audience/scope가 틀리면 **403 `{"detail":"forbidden"}`**. 서버는 401을 쓰지 않는다. 인증 전에는 없는 경로도 404가 아니라 403이다.
- 토큰: 수명이 짧은 JWT(EdDSA, 서버는 ES256/RS256도 허용). `exp - iat`가 3600초를 넘으면 서버가 거부한다.
- 필요한 scope (`scope` claim, 공백 구분):

| scope | 쓰는 곳 |
|---|---|
| `charts:create` | `POST /charts` |
| `charts:read` | `GET /charts/{jobId}` |
| `assets:read` | `GET /assets/{assetId}/audio`, `/thumbnail` |

- 클라이언트는 키를 하드코딩하지 않는다. 토큰은 `ITokenProvider`로 주입한다 (`CustomSongService.TokenProvider`, 기본값 `PlayerPrefsTokenProvider`).
  - `GetAccessToken()`이 비어 있으면 요청을 보내지 않고 "인증 토큰이 없습니다" 오류를 낸다.
  - 403을 받으면 `OnTokenRejected()`를 호출하고 실패 처리한다. 자동 재시도는 하지 않는다.
- 같은 IP에서 인증 실패가 반복되면(기본 300초에 20회) 서버는 토큰을 보지 않고 계속 403을 준다. 만료된 토큰으로 polling을 계속 돌리지 않는다.

### 개발용 토큰 발급

HTTP 발급 엔드포인트는 없다. 서버 저장소의 오프라인 스크립트로 만든다.

```bash
cd ai-chart-server
uv run python scripts/issue_dev_token.py \
  --private-key ./secrets/dev-keys/private.pem \
  --sub dev-player \
  --scopes "charts:create charts:read assets:read" \
  --ttl 3600
```

- `--ttl` 기본 900초, **최대 3600초**. 만료되면 다시 발급한다.
- issuer/audience는 `AUTH_ISSUER`/`AUTH_AUDIENCE` 환경 변수, 없으면 운영 기본값을 쓴다. 서버 설정과 맞춘다.
- `ENV=production`이면 스크립트가 실행을 거부한다.
- 출력된 토큰을 클라이언트에 넣는다: `PlayerPrefsTokenProvider.Save(token)` (PlayerPrefs 키 `CustomSong.DevToken`).
- 개인 키(`secrets/dev-keys/private.pem`)는 커밋하거나 빌드에 넣지 않는다.
- mock 서버는 서명을 검증하지 않으므로 아무 문자열이나 넣어도 된다.

## 4. `POST /charts` — 작업 생성

```json
{ "youtubeUrl": "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "difficulty": "normal" }
```

| 필드 | 타입 | 설명 |
|---|---|---|
| `youtubeUrl` | string | 200자 이하 YouTube URL |
| `difficulty` | string | `easy` \| `normal` \| `hard`. 생략하면 `normal` |

모르는 필드를 넣으면 422다(`extra="forbid"`). `ChartJobRequest` 외의 필드를 보내지 않는다.

**202 Accepted** (`ChartJobCreated`)

```json
{ "jobId": "a0c6e257-c40f-453f-b046-cf617ea9b243", "status": "queued" }
```

- 같은 youtubeId + difficulty + algorithmVersion의 채보가 서버 캐시에 있으면 `status`가 바로 `"completed"`로 온다. 이때도 `GET /charts/{jobId}`를 한 번 호출해 결과를 받는다.
- 같은 요청이 진행 중이면 기존 `jobId`를 돌려준다. 그 외에는 요청할 때마다 새 `jobId`가 나온다.
- **`jobId`는 임시 조회용이다.** 로컬 저장 키로 쓰지 않는다 (키는 `youtubeId` + `difficulty`).

### URL 검증 규칙

서버(`app/services/youtube_url.py`)가 최종 판단한다. 클라이언트 `CustomSongService.IsValidYoutubeUrl`은 같은 규칙으로 미리 걸러서 불필요한 요청을 줄인다.

- scheme `http` 또는 `https`. 공백, 백슬래시, 제어 문자, 비ASCII 문자는 거부한다 (클라이언트는 앞뒤 공백을 trim하고 보낸다).
- host: `youtu.be`, `youtube.com`, `www.youtube.com`, `m.youtube.com`, `music.youtube.com`. 포트, userinfo(`@`)는 거부.
- 형태: `youtu.be/{id}`, `/watch?v={id}`, `/shorts/{id}`, `/embed/{id}`, `/live/{id}` (끝 `/` 허용). 추가 query(`&t=`, `&list=`, `?si=`)는 무시한다.
- video ID: `^[A-Za-z0-9_-]{11}$`. `v`가 서로 다른 값으로 여러 개면 거부한다.

## 5. `GET /charts/{jobId}` — 상태 조회 (`ChartJobStatus`)

진행 중:

```json
{ "jobId": "...", "status": "processing", "difficulty": "normal",
  "song": null, "chart": null, "error": null, "algorithmVersion": null }
```

완료:

```json
{
  "jobId": "a0c6e257-c40f-453f-b046-cf617ea9b243",
  "status": "completed",
  "difficulty": "normal",
  "algorithmVersion": "1.0.0",
  "song": {
    "youtubeId": "dQw4w9WgXcQ",
    "title": "Never Gonna Give You Up",
    "artist": null,
    "duration": 213.04,
    "bpm": 113.2,
    "thumbnailUrl": "https://sushi-chain.aidengoldkr.dev/api/v1/assets/9f1c.../thumbnail",
    "audioUrl": "https://sushi-chain.aidengoldkr.dev/api/v1/assets/9f1c.../audio"
  },
  "chart": { "Notes": [ { "dir": 0, "type": 0, "StartTime": 2.512, "EndTime": 0 } ] },
  "error": null
}
```

실패:

```json
{ "jobId": "...", "status": "failed", "difficulty": "normal",
  "error": { "code": "too_long", "message": "The song is longer than the 300 second limit." } }
```

| 필드 | 설명 |
|---|---|
| `status` | `queued` \| `processing` \| `completed` \| `failed` (`ChartJobState`). **`progress` 필드는 없다** |
| `difficulty` | 요청한 난이도 |
| `algorithmVersion` | completed일 때만. 서버 채보 알고리즘 버전 |
| `song.youtubeId` | 영상 ID. 로컬 저장 키 |
| `song.artist` | `null` 가능 |
| `song.duration`, `song.bpm` | 초, BPM (소수) |
| `song.thumbnailUrl` | 절대 URL, `null` 가능 (썸네일 없음) |
| `song.audioUrl` | 절대 URL |
| `chart` | `{"Notes":[...]}`, 기존 `NoteList`와 같은 포맷 (7절) |
| `error.code`, `error.message` | failed일 때만. `message`는 영어, 사용자에게 그대로 보여주지 않는다 |

`JsonUtility`는 `null` 객체를 기본 인스턴스로 채우므로, 값 존재 여부는 문자열이 비어 있는지로 판단한다 (`song.youtubeId`, `song.thumbnailUrl`, `error.code`). 없는 jobId는 `404 {"detail":"not_found"}`이고, 응답에는 `Cache-Control: no-store`가 붙는다.

### Polling 정책

- `CustomSongConfig.PollIntervalSec`(2초)마다 조회한다.
- 서버 작업 하나는 최대 180초다(넘으면 `timeout`으로 failed). 클라이언트는 큐 대기까지 감안해 `CustomSongConfig.JobTimeoutSec`(600초)에서 포기한다.
- 서버가 진행률을 주지 않으므로 UI는 `PROCESSING` 단계에서 **제출 후 경과 초**를 보여준다. 다운로드 단계(`DOWNLOADING`)만 0..1 진행률이 있다.
- 일시적인 네트워크 오류, 5xx, 429는 polling 중에는 다음 주기에 다시 시도한다. 403과 404는 즉시 실패 처리한다.

## 6. 상태 코드와 클라이언트 메시지

HTTP 오류 본문은 항상 `{"detail": ...}`이다. `detail`은 보통 문자열 코드이고, 요청 검증 실패만 `{"detail":"invalid_request","errors":[{"loc","msg","type"}]}` 형태다. 오류 응답에는 내부 정보가 없으므로 `detail` 코드로 분기한다.

| HTTP | `detail` | 언제 | 클라이언트 처리 / 메시지 |
|---|---|---|---|
| 202 | — | 작업 접수 | polling 시작 |
| 200 | — | 상태 조회, asset | — |
| 206 | — | asset Range 응답 | — |
| 403 | `forbidden` | 토큰 없음/만료/잘못됨, scope 부족, https 아님 | `OnTokenRejected()` 호출. "인증이 만료되었거나 올바르지 않습니다. 토큰을 다시 설정해 주세요." |
| 404 | `not_found` | 없는 jobId 또는 asset | "요청한 작업을 찾을 수 없습니다. 다시 요청해 주세요." |
| 413 | `payload_too_large` | 본문이 너무 큼 | "요청이 너무 큽니다." (정상 사용에서는 나오지 않음) |
| 422 | `invalid_youtube_url` | 서버가 URL을 거부 | "올바른 YouTube URL이 아닙니다." |
| 422 | `invalid_request` | difficulty 오류, 모르는 필드, JSON 오류 | "요청 형식이 올바르지 않습니다." (클라이언트 버그) |
| 429 | (rate limit) | 요청 제한. `Retry-After` 헤더 | "요청이 너무 많습니다. 잠시 후 다시 시도해 주세요." 생성 제한은 기본 시간당 20회 |
| 503 | `queue_full` | 큐가 가득 참. `Retry-After` 헤더 | "서버가 바쁩니다. 잠시 후 다시 시도해 주세요." |
| 503 | `busy` | 같은 요청이 동시에 들어옴. `Retry-After: 2` | `Retry-After`만큼 기다렸다가 1회 재시도 |
| 503 | `storage_full`, `queue_unavailable` | 서버 상태 문제 | "서버를 일시적으로 사용할 수 없습니다. 잠시 후 다시 시도해 주세요." |
| 503 | `youtube_disabled` | 서버에서 YouTube 다운로드가 꺼짐 | "지금은 YouTube 곡을 만들 수 없습니다." |
| 500 | `internal_error` | 서버 오류 | "서버 오류가 발생했습니다." |

`POST /charts`의 503/429는 자동 재시도하지 않고 사용자에게 알린다. 단, `busy`는 짧게 1회 재시도해도 된다. 재시도할 때는 `Retry-After`(초)를 지킨다.

### 작업 실패 코드 (`error.code`)

| code | 의미 | 클라이언트 메시지 |
|---|---|---|
| `download_failed` | 오디오를 받지 못함 (비공개, 지역 제한, 삭제 등) | "영상을 내려받지 못했습니다. 공개된 영상인지 확인해 주세요." |
| `youtube_disabled` | 서버에서 YouTube 다운로드가 꺼짐 | "지금은 YouTube 곡을 만들 수 없습니다." |
| `too_long` | 곡이 서버 제한(기본 300초)보다 김 | "곡이 너무 깁니다. 5분 이하 영상을 사용해 주세요." |
| `invalid_audio` | 오디오를 읽을 수 없음 | "오디오를 읽을 수 없는 영상입니다." |
| `analysis_failed` | 오디오 분석 실패 | "곡을 분석하지 못했습니다. 다른 곡을 시도해 주세요." |
| `chart_invalid` | 쓸 만한 채보를 만들지 못함 | "이 곡으로는 채보를 만들 수 없습니다." |
| `timeout` | 작업이 180초를 넘음 | "곡 생성 시간이 초과되었습니다. 잠시 후 다시 시도해 주세요." |
| `asset_expired` | 완료 후 서버 보관 기간이 지나 파일이 지워짐 | "서버에서 파일이 만료되었습니다. 다시 요청해 주세요." |
| `internal` 또는 모르는 코드 | 서버 내부 오류 | "곡 생성에 실패했습니다." |

`asset_expired`는 서버 코드(`app/services/jobs.py`)에는 있지만 처음 공유된 계약 목록에는 없던 코드다. 모르는 코드는 `internal`처럼 처리하면 된다.

## 7. Asset 다운로드

- URL은 상태 응답의 `song.audioUrl`, `song.thumbnailUrl`을 **그대로** 쓴다. 절대 URL이며 클라이언트가 조합하지 않는다.
  - 형태: `{base}/assets/{assetId}/audio`, `{base}/assets/{assetId}/thumbnail`. `assetId`는 32자리 소문자 hex.
- **두 URL 모두 `Authorization: Bearer` 헤더가 필요하다.** `UnityWebRequestMultimedia.GetAudioClip`이나 `UnityWebRequestTexture`에 URL만 넘기면 403이다. 반드시 `SetRequestHeader("Authorization", ...)`를 붙여서 받는다.
- `assetId`는 youtubeId 단위로 같다. easy/normal/hard가 오디오와 썸네일을 공유한다. 그래서 로컬에 이미 있으면 다시 받지 않는다 (8절).
- audio: `audio/mpeg`. `Range`(단일 범위)를 지원한다(206/416). 클라이언트는 전체 파일을 한 번에 받는다. 응답에는 `ETag`와 `Cache-Control: private, max-age=86400`이 붙는다.
- thumbnail: 보통 `image/jpeg`. 서버에 있는 파일 형식에 따라 `image/png`, `image/webp`가 올 수도 있다. `thumbnailUrl`이 `null`이면 받지 않고 기본 이미지를 쓴다. 썸네일 다운로드 실패는 곡 저장을 막지 않는다.
- asset 요청에는 별도 rate limit(기본 600초에 120회)이 있다.
- 받은 파일은 임시 파일에 쓴 뒤 이동한다. 중간에 실패하면 반쯤 받은 파일이 남지 않게 한다.

## 8. 채보 포맷

기존 내장곡 `NoteList`(`Assets/Resources/*.json`)와 같다. 받은 `chart`를 그대로 저장하고 `JsonUtility.FromJson<NoteList>`로 읽는다.

```json
{ "Notes": [ { "dir": 0, "type": 0, "StartTime": 2.512, "EndTime": 0 } ] }
```

- 시간은 초, 소수 3자리. 첫 노트는 2초 이후에 나온다.
- `dir`, `type`, `EndTime`(롱노트) 의미는 내장곡과 같다.
- 왼쪽과 오른쪽을 동시에 치는 노트가 드물게 있다. 게임 로직이 이를 처리해야 한다.
- `Notes`가 비어 있으면 잘못된 응답으로 보고 저장하지 않는다.

## 9. 로컬 저장 구조

`Application.persistentDataPath` 아래:

```
CustomSongs/
  index.json                       → CustomSongIndex { Songs: [CustomSongMeta...] }
  {youtubeId}/
    audio.mp3                      곡당 1벌. 이미 있으면 다운로드를 건너뛴다
    thumb.jpg                      곡당 1벌. 없을 수 있다 (실제 형식이 png여도 이름은 thumb.jpg, LoadImage가 둘 다 읽는다)
    chart_easy.json                난이도별 NoteList
    chart_normal.json
    chart_hard.json
```

- 메타 항목 하나 = (youtubeId, difficulty). `CustomSongMeta.id = MakeId(youtubeId, difficulty)` = `"{youtubeId}_{difficulty}"`.
- 메타에는 `youtubeId`, `difficulty`, `algorithmVersion`, `title`, `artist`, `youtubeUrl`, `duration`, `bpm`, `addedAt`, `score`를 저장한다.
- `CustomSongStore.Upsert(meta)`는 `id` 기준으로 덮어쓰되 **기존 score는 유지**한다.
- 곡 목록(`CustomSongStore.GetSongs()`)에는 채보와 오디오 파일이 모두 있는 항목만 나온다.
- 서버 `jobId`와 `assetId`는 저장하지 않는다. 둘 다 서버 측 식별자이고 수명이 정해져 있다.

### algorithmVersion 정책

- 서버의 채보 캐시 키는 youtubeId + difficulty + algorithmVersion이다. 서버가 알고리즘을 올리면 같은 곡도 채보가 달라질 수 있다.
- 클라이언트는 받은 `algorithmVersion`을 메타에 저장만 한다. **로컬 채보를 자동으로 무효화하거나 재요청하지 않는다.** 저장된 곡은 서버 없이도 계속 플레이할 수 있어야 한다.
- 같은 곡과 난이도를 다시 요청하면 새 채보와 `algorithmVersion`으로 덮어쓴다. 오디오와 썸네일은 이미 있으면 다시 받지 않는다. score는 유지한다.
- 나중에 "새 버전 채보 있음" 표시나 일괄 갱신이 필요하면, 저장된 `algorithmVersion`을 비교하는 데 쓴다. 판단 기준은 문자열 불일치다.

## 10. 로컬 테스트 순서

1. mock: `node Tools/mock-song-server/server.ts` (포트 8000). 토큰은 아무 문자열(`PlayerPrefsTokenProvider.Save("mock")`).
2. 실제 서버(로컬): `ai-chart-server/docker-compose.yml`(`docs/` 참고)로 띄우고, 3절대로 토큰을 발급해 넣는다. `REQUIRE_HTTPS=false`, `PUBLIC_BASE_URL=http://localhost:8000`.
3. 운영: `CustomSongConfig.BaseUrl = CustomSongConfig.ProductionBaseUrl`, 운영 issuer가 발급한 토큰.

mock에서 시험할 수 있는 시나리오: 정상 완료, 캐시 히트(같은 곡/난이도 재요청 시 바로 completed), `fail`(analysis_failed), `toolong`(too_long), `busy`(첫 요청 503 queue_full + Retry-After), 인증 헤더 없음(403), 잘못된 URL(422).
