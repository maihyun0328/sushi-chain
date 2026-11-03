using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

// 커스텀 곡 로컬 저장소.
// persistentDataPath/CustomSongs/index.json                          → CustomSongIndex
// persistentDataPath/CustomSongs/{youtubeId}/audio.{ext}, thumb.jpg   → 곡당 1벌 (난이도 공유). ext는 meta.audioExt (기본 mp3)
// persistentDataPath/CustomSongs/{youtubeId}/chart_{difficulty}.json  → 난이도별 채보
public static class CustomSongStore
{
    public const string IndexFileName = "index.json";
    public const string AudioFileName = "audio.mp3";   // 서버 곡 (audioExt = mp3)
    public const string ThumbFileName = "thumb.jpg";

    // 경로에 넣기 전에 반드시 검사한다 (path traversal 방지)
    static readonly Regex YoutubeIdRegex = new Regex(@"^[A-Za-z0-9_-]{6,20}$", RegexOptions.CultureInvariant);

    public static string RootDir => Path.Combine(Application.persistentDataPath, "CustomSongs");
    static string IndexPath => Path.Combine(RootDir, IndexFileName);

    public static bool IsValidYoutubeId(string youtubeId) =>
        !string.IsNullOrEmpty(youtubeId) && YoutubeIdRegex.IsMatch(youtubeId);

    public static bool IsValidDifficulty(string difficulty) =>
        !string.IsNullOrEmpty(difficulty) && Array.IndexOf(ChartDifficulty.All, difficulty) >= 0;

    static bool IsValidMeta(CustomSongMeta meta) =>
        meta != null && IsValidYoutubeId(meta.youtubeId) && IsValidDifficulty(meta.difficulty);

    static string RequireId(string youtubeId)
    {
        if (!IsValidYoutubeId(youtubeId))
            throw new ArgumentException($"잘못된 youtubeId: {youtubeId}", nameof(youtubeId));
        return youtubeId;
    }

    public static string ChartFileName(string difficulty)
    {
        if (!IsValidDifficulty(difficulty))
            throw new ArgumentException($"잘못된 difficulty: {difficulty}", nameof(difficulty));
        return "chart_" + difficulty + ".json";
    }

    public static string SongDir(string youtubeId) => Path.Combine(RootDir, RequireId(youtubeId));
    public static string AudioPath(string youtubeId) => Path.Combine(SongDir(youtubeId), AudioFileName);
    public static string AudioPath(string youtubeId, string audioExt) =>
        Path.Combine(SongDir(youtubeId), AudioFileNameFor(audioExt));
    public static string AudioPath(CustomSongMeta meta) => AudioPath(meta.youtubeId, meta.AudioExtOrDefault);

    // 허용하지 않는 확장자는 예외 (path traversal 방지)
    public static string AudioFileNameFor(string audioExt)
    {
        string ext = CustomSongAudio.Normalize(audioExt);
        if (ext == null) throw new ArgumentException($"지원하지 않는 오디오 형식: {audioExt}", nameof(audioExt));
        return "audio." + ext;
    }
    public static string ThumbPath(string youtubeId) => Path.Combine(SongDir(youtubeId), ThumbFileName);
    public static string ChartPath(string youtubeId, string difficulty) =>
        Path.Combine(SongDir(youtubeId), ChartFileName(difficulty));

    // 0바이트 파일은 중단된 다운로드로 보고 없는 것으로 취급
    public static bool HasAudio(string youtubeId) => HasAudio(youtubeId, CustomSongAudio.DefaultExt);

    public static bool HasAudio(string youtubeId, string audioExt)
    {
        if (!IsValidYoutubeId(youtubeId) || CustomSongAudio.Normalize(audioExt) == null) return false;
        try
        {
            var info = new FileInfo(AudioPath(youtubeId, audioExt));
            return info.Exists && info.Length > 0;
        }
        catch (Exception) { return false; }
    }

    public static bool HasAudio(CustomSongMeta meta) =>
        meta != null && HasAudio(meta.youtubeId, meta.AudioExtOrDefault);

    static bool HasChart(CustomSongMeta meta)
    {
        if (!IsValidMeta(meta)) return false;
        try { return File.Exists(ChartPath(meta.youtubeId, meta.difficulty)); }
        catch (Exception) { return false; }
    }

    // 파일이 없거나 깨져 있으면 빈 인덱스
    public static CustomSongIndex LoadIndex()
    {
        try
        {
            if (!File.Exists(IndexPath)) return new CustomSongIndex();
            var index = JsonUtility.FromJson<CustomSongIndex>(File.ReadAllText(IndexPath));
            if (index == null) return new CustomSongIndex();
            if (index.Songs == null) index.Songs = new List<CustomSongMeta>();
            index.Songs.RemoveAll(s => s == null || string.IsNullOrEmpty(s.id));
            foreach (var s in index.Songs)
            {
                if (s.score == null) s.score = new Score();
                s.audioExt = s.AudioExtOrDefault; // 예전 항목(필드 없음)이나 잘못된 값은 mp3
            }
            return index;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CustomSongStore] index.json 로드 실패, 빈 인덱스 사용: {e.Message}");
            return new CustomSongIndex();
        }
    }

    public static void SaveIndex(CustomSongIndex index)
    {
        Directory.CreateDirectory(RootDir);
        // 임시 파일에 쓰고 교체해 중간에 끊겨도 인덱스가 깨지지 않게 한다
        string tmp = IndexPath + ".tmp";
        File.WriteAllText(tmp, JsonUtility.ToJson(index ?? new CustomSongIndex(), true));
        if (File.Exists(IndexPath)) File.Delete(IndexPath);
        File.Move(tmp, IndexPath);
    }

    // id 기준으로 교체 또는 추가. 기존 항목이 있으면 점수는 유지한다
    public static void Upsert(CustomSongMeta meta)
    {
        if (meta == null || string.IsNullOrEmpty(meta.id)) return;
        var index = LoadIndex();
        int at = index.Songs.FindIndex(s => s.id == meta.id);
        if (at >= 0)
        {
            Score prev = index.Songs[at].score;
            if (prev != null) meta.score = prev;
            index.Songs[at] = meta;
        }
        else
        {
            if (meta.score == null) meta.score = new Score();
            index.Songs.Add(meta);
        }
        SaveIndex(index);
    }

    // 채보와 오디오 파일이 실제로 있는 항목만 반환
    public static List<CustomSongMeta> GetSongs()
    {
        var songs = LoadIndex().Songs;
        songs.RemoveAll(s => !HasChart(s) || !HasAudio(s));
        return songs;
    }

    public static NoteList LoadChart(CustomSongMeta meta)
    {
        if (!IsValidMeta(meta)) return null;
        try
        {
            string path = ChartPath(meta.youtubeId, meta.difficulty);
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<NoteList>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CustomSongStore] 채보 로드 실패 ({meta.id}): {e.Message}");
            return null;
        }
    }

    // 없으면 null
    public static Texture2D LoadThumbnail(string youtubeId)
    {
        if (!IsValidYoutubeId(youtubeId)) return null;
        try
        {
            string path = ThumbPath(youtubeId);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            return tex;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CustomSongStore] 썸네일 로드 실패 ({youtubeId}): {e.Message}");
            return null;
        }
    }

    // 호출 측에서 StartCoroutine으로 실행
    public static IEnumerator LoadAudio(CustomSongMeta meta, Action<AudioClip> onLoaded, Action<string> onError)
    {
        if (meta == null || !HasAudio(meta))
        {
            onError?.Invoke("오디오 파일이 없습니다.");
            yield break;
        }

        UnityWebRequest req;
        try
        {
            // 공백 등이 포함된 경로도 올바른 file:// URI로 변환
            string uri = new Uri(AudioPath(meta)).AbsoluteUri;
            req = UnityWebRequestMultimedia.GetAudioClip(uri, CustomSongAudio.ToAudioType(meta.AudioExtOrDefault));
        }
        catch (Exception e)
        {
            onError?.Invoke($"오디오 로드 실패: {e.Message}");
            yield break;
        }

        using (req)
        {
            UnityWebRequestAsyncOperation op = null;
            string sendError = null;
            try { op = req.SendWebRequest(); }
            catch (Exception e) { sendError = e.Message; }
            if (sendError != null)
            {
                onError?.Invoke($"오디오 로드 실패: {sendError}");
                yield break;
            }
            yield return op;

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"오디오 로드 실패: {req.error}");
                yield break;
            }

            AudioClip clip = null;
            try { clip = DownloadHandlerAudioClip.GetContent(req); }
            catch (Exception e) { Debug.LogWarning($"[CustomSongStore] AudioClip 생성 실패: {e.Message}"); }
            if (clip == null)
            {
                onError?.Invoke("오디오를 읽을 수 없습니다.");
                yield break;
            }
            // 디코딩이 끝나기 전에 재생하면 isPlaying이 false라 GameManager가 즉시 GameClear 처리한다
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            while (clip.loadState == AudioDataLoadState.Loading) yield return null;
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                UnityEngine.Object.Destroy(clip);
                onError?.Invoke("오디오를 디코딩할 수 없습니다.");
                yield break;
            }
            clip.name = meta.id;
            onLoaded?.Invoke(clip);
        }
    }

    public static void SaveScore(string id, Score score)
    {
        if (string.IsNullOrEmpty(id)) return;
        var index = LoadIndex();
        var meta = index.Songs.Find(s => s.id == id);
        if (meta == null)
        {
            Debug.LogWarning($"[CustomSongStore] 점수 저장 대상 곡이 없습니다: {id}");
            return;
        }
        meta.score = score ?? new Score();
        SaveIndex(index);
    }
}
