using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// 커스텀 곡 기능의 공유 계약. 서버 API DTO, 로컬 저장 모델, 설정, 인증, 입력 잠금.
// 서버(ai-chart-server) API 스펙: Docs/custom-song-api.md
// ─────────────────────────────────────────────────────────────

// ===== 서버 API DTO (ai-chart-server /api/v1) =====
// JsonUtility는 null 객체 필드를 기본 인스턴스로 채운다.
// 따라서 null 여부는 문자열 필드가 비어 있는지로 판단한다.

[Serializable]
public class ChartJobRequest
{
    public string youtubeUrl;
    public string difficulty;   // ChartDifficulty: "easy" | "normal" | "hard"
}

[Serializable]
public class ChartJobCreated    // POST /charts → 202
{
    public string jobId;
    public string status;       // 캐시 히트면 "completed"로 바로 올 수 있다
}

[Serializable]
public class ChartSong
{
    public string youtubeId;
    public string title;
    public string artist;       // null 가능
    public float duration;      // 초
    public float bpm;
    public string thumbnailUrl; // 절대 URL, null 가능. Bearer 필요
    public string audioUrl;     // 절대 URL (audio/mpeg). Bearer 필요
}

[Serializable]
public class ChartJobError
{
    public string code;         // download_failed | youtube_disabled | too_long | invalid_audio | analysis_failed | chart_invalid | timeout | internal
    public string message;
}

[Serializable]
public class ChartJobStatus     // GET /charts/{jobId}
{
    public string jobId;
    public string status;       // ChartJobState 참고
    public string difficulty;
    public string algorithmVersion;
    public ChartSong song;      // completed일 때만 유효
    public NoteList chart;      // completed일 때만 유효. {"Notes":[...]}
    public ChartJobError error; // failed일 때만 유효
}

public static class ChartJobState
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class ChartDifficulty
{
    public const string Easy = "easy";
    public const string Normal = "normal";
    public const string Hard = "hard";
    public static readonly string[] All = { Easy, Normal, Hard };

    // 곡 선택 화면의 별 개수 (1~5)
    public static int ToStars(string difficulty)
    {
        switch (difficulty)
        {
            case Easy: return 2;
            case Hard: return 4;
            default: return 3;
        }
    }
}

// ===== 로컬 저장 모델 =====
// 오디오와 썸네일은 곡(youtubeId)당 1벌, 채보는 (youtubeId, difficulty)별로 저장한다.
// persistentDataPath/CustomSongs/index.json                         → CustomSongIndex
// persistentDataPath/CustomSongs/{youtubeId}/audio.mp3
// persistentDataPath/CustomSongs/{youtubeId}/thumb.jpg
// persistentDataPath/CustomSongs/{youtubeId}/chart_{difficulty}.json → NoteList

[Serializable]
public class CustomSongMeta
{
    public string id;               // 목록 항목 키 = CustomSongMeta.MakeId(youtubeId, difficulty)
    public string youtubeId;
    public string difficulty;       // ChartDifficulty
    public string algorithmVersion; // 서버 채보 알고리즘 버전. 버전이 바뀌면 재요청할지 판단하는 데 쓴다
    public string title;
    public string artist;
    public string youtubeUrl;
    public float duration;
    public float bpm;
    public string addedAt;          // ISO-8601
    public Score score = new Score(); // 마지막 플레이 기록 (내장곡 Scores.json과 같은 정책)

    public static string MakeId(string youtubeId, string difficulty) => youtubeId + "_" + difficulty;
    public string DisplayName => $"{title} [{(difficulty ?? "").ToUpperInvariant()}]";
}

[Serializable]
public class CustomSongIndex
{
    public List<CustomSongMeta> Songs = new List<CustomSongMeta>();
}

// ===== 진행 단계 (UI 표시용) =====

public enum CUSTOM_SONG_STAGE
{
    SUBMITTING,   // POST /charts
    PROCESSING,   // 서버 처리 중 (polling). 서버가 progress를 주지 않으므로 경과 시간만 표시한다
    DOWNLOADING,  // mp3, thumbnail 다운로드
    DONE,
    FAILED
}

// ===== 설정 =====

public static class CustomSongConfig
{
    const string BaseUrlKey = "CustomSong.BaseUrl";
    public const string LocalBaseUrl = "http://localhost:8000/api/v1";
    public const string ProductionBaseUrl = "https://sushi-chain.aidengoldkr.dev/api/v1";
    public const float PollIntervalSec = 2f;
    public const float JobTimeoutSec = 600f;   // 서버의 작업당 최대 시간은 180초
    public const int RequestTimeoutSec = 30;
    public const int AssetTimeoutSec = 300;

    // 로컬 서버로 테스트하려면 BaseUrl = LocalBaseUrl로 덮어쓴다 (PlayerPrefs에 저장됨)
    public static string DefaultBaseUrl => ProductionBaseUrl;

    public static string BaseUrl
    {
        get => PlayerPrefs.GetString(BaseUrlKey, DefaultBaseUrl).TrimEnd('/');
        set => PlayerPrefs.SetString(BaseUrlKey, value);
    }
}

// ===== 인증 =====
// 모든 요청(asset 포함)에 Authorization: Bearer <JWT>가 필요하다. 없거나 틀리면 403.
// 키를 클라이언트에 하드코딩하지 않는다. 토큰은 ITokenProvider로 주입한다.

public interface ITokenProvider
{
    // 사용할 수 있는 토큰이 없으면 null 또는 빈 문자열을 반환한다
    string GetAccessToken();
    // 서버가 403을 반환했을 때 호출된다 (만료되었거나 잘못된 토큰)
    void OnTokenRejected();
}

// 플레이테스트용 장기 토큰. ai-chart-server/secrets/playtest-token.txt를
// Assets/Resources/CustomSongPlaytestToken.txt로 복사해 쓴다 (저장소에 커밋됨, 빌드에도 포함됨).
// 유출되면 서버에서 jti(secrets/playtest-jti.txt)로 폐기한다.
public static class PlaytestToken
{
    public const string ResourceName = "CustomSongPlaytestToken";
    static string cached;

    public static string Get()
    {
        if (cached != null) return cached;
        var asset = Resources.Load<TextAsset>(ResourceName);
        cached = asset != null ? asset.text.Trim() : "";
        return cached;
    }
}

// 기본 토큰은 플레이테스트 토큰이다. 그 토큰이 없을 때만 패널에서 붙여 넣은 개발용 토큰(PlayerPrefs)을 쓴다.
public class PlayerPrefsTokenProvider : ITokenProvider
{
    public const string Key = "CustomSong.DevToken";
    public string GetAccessToken()
    {
        string playtest = PlaytestToken.Get();
        return !string.IsNullOrEmpty(playtest) ? playtest : PlayerPrefs.GetString(Key, "").Trim();
    }
    public void OnTokenRejected() { }
    public static void Save(string token)
    {
        PlayerPrefs.SetString(Key, (token ?? "").Trim());
        PlayerPrefs.Save();
    }
}

// ===== 입력 잠금 =====
// URL 입력 패널이 열려 있는 동안 true. 키보드로 동작하는 기존 컴포넌트들
// (곡 선택 W/S/Enter, Back의 Escape, GameManager 디버그 W)은 이 값이 true면 입력을 무시한다.
public static class CustomSongInputLock
{
    public static bool IsLocked;
}
