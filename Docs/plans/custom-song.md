# 커스텀 곡 추가 — 구현 계획

브랜치: `Feature/CustomSong`

## 목표

곡 선택 화면에 **커스텀 곡 추가** 버튼을 만든다.

1. 버튼을 누르면 YouTube URL 입력 패널이 열린다.
2. URL을 서버에 보내면 서버가 채보(JSON), 썸네일, mp3를 만들어 준다.
3. 받은 파일을 로컬에 저장하고, 곡 목록에 추가해 바로 플레이할 수 있게 한다.

서버는 별도로 개발 중이다. 이번 범위는 **클라이언트 베이스**, **API 계약 문서**, **로컬 테스트용 mock 서버**다.

## 핵심 결정

| 결정 | 이유 |
|---|---|
| **비동기 Job 방식 API**: `POST /songs` → `jobId`, 이후 `GET /songs/{jobId}` polling | YouTube 다운로드와 채보 생성은 수십 초~수 분 걸린다. 동기 요청은 timeout이 난다. |
| **결과는 URL로 전달**: `audioUrl`, `thumbnailUrl`, `chartUrl` | mp3를 base64로 JSON에 넣으면 크기가 커진다. URL 방식은 CDN/S3로 바로 옮길 수 있다. |
| **채보 포맷은 기존 `NoteList`와 동일** (`{"Notes":[{dir,type,StartTime,EndTime}]}`) | 기존 게임 로직을 그대로 재사용한다. |
| **씬(YAML)을 수정하지 않음**: UI는 `RuntimeInitializeOnLoadMethod`로 런타임에 생성 | Editor가 열린 상태에서 씬 파일을 직접 편집하면 충돌 위험이 크다. 나중에 프리팹으로 옮기기 쉽다. |
| **로컬 저장소**: `persistentDataPath/CustomSongs/{id}/` | 오프라인 재플레이가 가능하고, 서버 재요청이 필요 없다. |
| **입력 잠금 플래그** `CustomSongInputLock.IsLocked` | URL에 들어가는 `w`, Enter, Esc가 기존 키보드 핸들러를 건드리지 않게 막는다. |
| **서버 주소는 `PlayerPrefs`**, 기본값 `http://localhost:8787` | 서버 배포 전까지 로컬에서 개발한다. |

## 공유 계약 (먼저 작성 완료)

`Assets/Scripts/CustomSong/CustomSongContracts.cs`에 DTO, 로컬 모델, 진행 단계 enum, 설정, 입력 잠금이 있다.

## 트랙별 공개 API (병렬 작업 간 인터페이스)

### Track A — 네트워크 + 로컬 저장소

파일:
- `Assets/Scripts/CustomSong/CustomSongStore.cs`
- `Assets/Scripts/CustomSong/CustomSongService.cs`

```csharp
public static class CustomSongStore {
    string RootDir { get; }                       // persistentDataPath/CustomSongs
    string SongDir(string id);
    string ChartPath(string id); string AudioPath(string id); string ThumbPath(string id);
    CustomSongIndex LoadIndex();                  // 없으면 빈 인덱스
    void SaveIndex(CustomSongIndex index);
    void Upsert(CustomSongMeta meta);             // id 기준으로 교체 또는 추가
    List<CustomSongMeta> GetSongs();
    NoteList LoadChart(string id);
    Texture2D LoadThumbnail(string id);           // 없으면 null
    IEnumerator LoadAudio(string id, Action<AudioClip> onLoaded, Action<string> onError); // file:// + AudioType.MPEG
    void SaveScore(string id, Score score);
}
public class CustomSongService : MonoBehaviour {
    public static CustomSongService i;
    public bool IsBusy { get; }
    public void RequestSong(string youtubeUrl,
        Action<CUSTOM_SONG_STAGE, float> onProgress,  // progress 0~1
        Action<CustomSongMeta> onDone,
        Action<string> onError);
    public void Cancel();
    public static bool IsValidYoutubeUrl(string url);
}
```

### Track B — UI (버튼 + URL 입력 패널) + 부트스트랩

파일:
- `Assets/Scripts/CustomSong/CustomSongBootstrap.cs`
- `Assets/Scripts/CustomSong/CustomSongPanel.cs`

- 부트스트랩은 씬 로드 후 `CustomSongService` GameObject를 만들고, `UIManager.i.Songs` 아래에 버튼과 패널을 런타임으로 만든다.
- 버튼은 마우스 클릭과 `C` 키로 동작한다. 패널은 InputField, 요청/취소 버튼, 진행률, 에러 문구, 완료 시 썸네일과 제목을 표시한다.
- 패널이 열려 있는 동안 `CustomSongInputLock.IsLocked = true`. Esc를 누르면 패널을 닫는다.
- 완료되면 `KeyboardScrollComponent.ReloadSongs()`를 호출한다.

### Track C — 게임플레이 통합

수정 파일: `GameManager`, `UIManager`, `KeyboardScrollComponent`, `BackComponent`, `JsonManager`

- `GameManager.GameStart(NoteList notes, AudioClip clip, CustomSongMeta custom)`를 공통 진입점으로 둔다. 기존 `GameStart(int index)`는 이 함수를 호출하도록 래핑한다.
- `GameManager.CurrentCustom`이 null이 아니면 `GameClear`가 `CustomSongStore.SaveScore`로 저장한다. null이면 기존 `JsonManager.SavePoint`.
- `UIManager.GameStartCustom(CustomSongMeta meta, NoteList notes, AudioClip clip)`: 기존 Transition을 재사용한다.
- `KeyboardScrollComponent`:
  - 목록은 내장곡 뒤에 커스텀곡을 이어 붙인다.
  - `public void ReloadSongs()`를 추가한다.
  - Enter로 커스텀곡을 고르면 오디오를 비동기로 로드한 뒤 시작한다.
  - `IsLocked`이면 입력을 무시한다.
  - Scores 개수가 곡 수보다 적으면 보정한다 (기존 버그 #3).
- `BackComponent`와 `GameManager`의 디버그 `W` 처리: `IsLocked`이면 무시한다.

### Track D — API 문서 + mock 서버

파일:
- `Docs/custom-song-api.md`
- `Tools/mock-song-server/server.ts` (Node 26 type stripping, 의존성 없음)
- `Tools/mock-song-server/README.md`

- 계약을 그대로 구현한다. 진행률은 약 6초 동안 시뮬레이션한다.
- 기존 `SOUTHSIDE FREESTYLE` mp3와 채보, placeholder 썸네일을 제공한다.
- URL에 `fail`이 들어 있으면 실패 응답을 낸다 (에러 UI 테스트용).

## 실행 순서

1. **병렬**: A, B, C, D는 서로 다른 파일만 소유한다. 이 계약에 맞춰 동시에 구현한다.
2. **통합**: 전체 컴파일 체크 (Unity 내장 Roslyn, `compile-check.sh`), 트랙 간 시그니처 불일치를 수정하고, mock 서버로 curl E2E를 확인한다.
3. **리뷰**: diff 리뷰 후 보고.

## 검증 범위와 한계

- 자동 검증: C# 컴파일, mock 서버 API (curl).
- **Unity Play Mode의 실제 UI 동작은 사용자가 Editor에서 수동으로 확인해야 한다.** Editor가 열려 있으면 batchmode를 실행할 수 없다.
- `ProjectSettings` → Player → "Allow downloads over HTTP"가 현재 `Not Allowed`다. localhost http 테스트를 하려면 `Development builds only` 이상으로 바꿔야 한다.

## 범위 밖 (후속)

- 커스텀 곡 삭제 UI
- 곡 목록에 썸네일 표시
- 서버 인증
- 내장곡 Scores.json을 id 기반으로 마이그레이션
