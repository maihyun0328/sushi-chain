using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

// 커스텀 곡 서버 통신 (ai-chart-server /api/v1).
// POST /charts → GET /charts/{jobId} polling → 채보 검증 → 오디오/썸네일 다운로드(없을 때만) → 로컬 저장.
// 코루틴 안의 모든 예외는 Guard가 잡아 Fail로 보낸다. IsBusy는 항상 해제된다.
public class CustomSongService : MonoBehaviour
{
    public static CustomSongService i;

    static ITokenProvider tokenProvider = new PlayerPrefsTokenProvider();
    public static ITokenProvider TokenProvider
    {
        get => tokenProvider;
        set => tokenProvider = value ?? new PlayerPrefsTokenProvider();
    }

    const int MaxQueueFullRetries = 2;
    const float DefaultRetryAfterSec = 5f;
    const float MaxRetryAfterSec = 60f;
    const float ProgressTickSec = 0.5f;
    const string TempDirPrefix = ".tmp_";

    // 서버(app/services/youtube_url.py) 규칙과 맞춘다: http(s), 허용 host, watch?v= / shorts / embed / live / youtu.be,
    // 포트·userinfo·공백 금지, 200자 이하. scheme이 없으면 NormalizeYoutubeUrl이 https://를 붙인다.
    const int MaxYoutubeUrlLength = 200;
    static readonly Regex YoutubeRegex = new Regex(
        @"^https?://(?:" +
            @"(?:(?:www|m|music)\.)?youtube\.com/(?:" +
                @"watch\?(?:[^#]*&)?v=[A-Za-z0-9_-]{11}(?:[&#][^\s]*)?" +
                @"|(?:shorts|embed|live)/[A-Za-z0-9_-]{11}/?(?:[?#][^\s]*)?)" +
            @"|youtu\.be/[A-Za-z0-9_-]{11}/?(?:[?#][^\s]*)?" +
        @")$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Serializable] class ErrorDetail { public string detail; }

    Coroutine routine;
    UnityWebRequest inFlight;
    string tempDir;
    int generation;   // Cancel 시 증가. 이전 실행의 콜백/후속 처리를 막는다
    string error;     // 단계 함수들이 실패 시 채운다
    Action<CUSTOM_SONG_STAGE, float> cbProgress;
    Action<string> cbError;

    public bool IsBusy { get; private set; }

    void Awake()
    {
        if (i != null && i != this)
        {
            Destroy(gameObject);
            return;
        }
        i = this;
    }

    // 비활성화되면 코루틴이 멈추므로 상태를 정리한다
    void OnDisable()
    {
        if (IsBusy) Cancel();
    }

    void OnDestroy()
    {
        if (i == this)
        {
            Cancel();
            i = null;
        }
    }

    public static bool IsValidYoutubeUrl(string url)
    {
        string normalized = NormalizeYoutubeUrl(url);
        if (normalized.Length == 0 || normalized.Length > MaxYoutubeUrlLength) return false;
        return YoutubeRegex.IsMatch(normalized);
    }

    // 서버는 scheme 없는 URL을 422로 거절하므로 "youtu.be/..." 같은 입력에 https://를 붙여 보낸다
    static string NormalizeYoutubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        string u = url.Trim();
        if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            u = "https://" + u;
        return u;
    }

    public void RequestSong(string youtubeUrl, string difficulty,
        Action<CUSTOM_SONG_STAGE, float> onProgress,
        Action<CustomSongMeta> onDone,
        Action<string> onError)
    {
        if (IsBusy)
        {
            SafeInvoke(() => onError?.Invoke("이미 곡을 요청하는 중입니다."));
            return;
        }

        string diff = string.IsNullOrWhiteSpace(difficulty) ? ChartDifficulty.Normal : difficulty.Trim().ToLowerInvariant();
        string fail = null;
        if (!IsValidYoutubeUrl(youtubeUrl)) fail = "올바른 YouTube URL이 아닙니다.";
        else if (!CustomSongStore.IsValidDifficulty(diff)) fail = $"지원하지 않는 난이도입니다: {difficulty}";
        else if (string.IsNullOrEmpty(GetToken())) fail = "인증 토큰이 없습니다. 토큰을 입력하세요.";
        if (fail != null)
        {
            SafeInvoke(() => onProgress?.Invoke(CUSTOM_SONG_STAGE.FAILED, 0f));
            SafeInvoke(() => onError?.Invoke(fail));
            return;
        }

        CleanStaleTemp();
        error = null;
        IsBusy = true;
        cbProgress = onProgress;
        cbError = onError;
        int gen = ++generation;
        var started = StartCoroutine(Guard(gen, Run(gen, NormalizeYoutubeUrl(youtubeUrl), diff, onProgress, onDone)));
        // 첫 yield 전에 동기적으로 실패/완료했을 수 있다
        routine = IsBusy && gen == generation ? started : null;
    }

    public void Cancel()
    {
        generation++;
        if (routine != null)
        {
            try { StopCoroutine(routine); } catch (Exception) { }
            routine = null;
        }
        DisposeInFlight();
        CleanTemp();
        error = null;
        cbProgress = null;
        cbError = null;
        IsBusy = false;
    }

    // ───────── 실행 / 예외 가드 ─────────

    // 중첩 IEnumerator를 직접 펼쳐 실행해 어느 단계에서 던진 예외도 잡는다
    IEnumerator Guard(int gen, IEnumerator root)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            if (gen != generation) yield break;
            object current = null;
            bool hasNext;
            string crash = null;
            try
            {
                hasNext = stack.Peek().MoveNext();
                if (hasNext) current = stack.Peek().Current;
            }
            catch (Exception e)
            {
                hasNext = false;
                crash = DescribeException(e, false);
                Debug.LogException(e);
            }

            if (crash != null)
            {
                if (gen == generation)
                {
                    error = crash;
                    Fail(gen);
                }
                yield break;
            }
            if (!hasNext) { stack.Pop(); continue; }
            if (current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return current;
        }

        // Create 실패 등으로 Run이 Done/Fail 없이 끝나면 여기서 Fail 처리한다. IsBusy가 남지 않게 한다
        if (gen == generation && IsBusy)
        {
            error ??= "알 수 없는 오류가 발생했습니다.";
            Fail(gen);
        }
    }

    IEnumerator Run(int gen, string youtubeUrl, string difficulty,
        Action<CUSTOM_SONG_STAGE, float> onProgress,
        Action<CustomSongMeta> onDone)
    {
        string baseUrl = CustomSongConfig.BaseUrl;
        void Progress(CUSTOM_SONG_STAGE stage, float value)
        {
            if (gen == generation) SafeInvoke(() => onProgress?.Invoke(stage, value));
        }

        // 1. 작업 생성 (queue_full이면 Retry-After만큼 기다렸다가 최대 2번 재시도)
        Progress(CUSTOM_SONG_STAGE.SUBMITTING, 0f);
        string body = JsonUtility.ToJson(new ChartJobRequest { youtubeUrl = youtubeUrl, difficulty = difficulty });
        string jobId = null;
        for (int attempt = 0; ; attempt++)
        {
            var req = Create(() =>
            {
                var r = new UnityWebRequest(baseUrl + "/charts", UnityWebRequest.kHttpVerbPOST);
                r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                r.downloadHandler = new DownloadHandlerBuffer();
                r.SetRequestHeader("Content-Type", "application/json");
                return r;
            });
            if (req == null) yield break;
            yield return Send(req, CustomSongConfig.RequestTimeoutSec, null);

            float retryAfter = -1f;
            if (error == null)
            {
                var created = TryParse<ChartJobCreated>(req.downloadHandler.text);
                if (created == null || string.IsNullOrEmpty(created.jobId))
                    error = "서버 응답이 올바르지 않습니다. (jobId 없음)";
                else
                    jobId = created.jobId;
            }
            else if (attempt < MaxQueueFullRetries && TryGetRetryWait(req, out float wait))
            {
                retryAfter = wait;
            }
            long code = req.responseCode;
            DisposeInFlight();

            if (retryAfter < 0f) break;
            error = null;
            Debug.Log($"[CustomSongService] 서버 혼잡/요청 한도(HTTP {code}). {retryAfter:0}초 후 재시도 ({attempt + 1}/{MaxQueueFullRetries})");
            yield return new WaitForSecondsRealtime(retryAfter);
        }
        if (error != null) { Fail(gen); yield break; }

        // 2. polling. 서버 progress가 없으므로 경과 시간(초)을 진행 값으로 보낸다.
        //    POST가 이미 completed를 돌려줘도 payload를 받으려면 GET이 한 번 필요하다.
        float startedAt = Time.realtimeSinceStartup;
        Progress(CUSTOM_SONG_STAGE.PROCESSING, 0f);
        ChartJobStatus done = null;
        while (true)
        {
            var req = Create(() => UnityWebRequest.Get(baseUrl + "/charts/" + UnityWebRequest.EscapeURL(jobId)));
            if (req == null) yield break;
            yield return Send(req, CustomSongConfig.RequestTimeoutSec, null);
            ChartJobStatus status = null;
            if (error == null)
            {
                status = TryParse<ChartJobStatus>(req.downloadHandler.text);
                if (status == null || string.IsNullOrEmpty(status.status)) error = "서버 응답이 올바르지 않습니다.";
            }
            else if (req.responseCode == 404)
            {
                error = "서버에서 작업을 찾을 수 없습니다. 다시 요청해 주세요.";
            }
            // 조회 한도(429)는 실패로 끝내지 않고 Retry-After만큼 쉬었다가 계속 polling한다
            float pollWait = CustomSongConfig.PollIntervalSec;
            if (error != null && TryGetRetryWait(req, out float limitWait))
            {
                Debug.Log($"[CustomSongService] 조회 한도(HTTP {req.responseCode}). {limitWait:0}초 후 다시 조회합니다.");
                error = null;
                pollWait = Mathf.Max(pollWait, limitWait);
            }
            DisposeInFlight();
            if (error != null) break;

            if (status == null) { /* 한도 대기 후 다시 조회 */ }
            else if (status.status == ChartJobState.Completed) { done = status; break; }
            else if (status.status == ChartJobState.Failed)
            {
                error = DescribeJobError(status.error);
                break;
            }

            // queued | processing | 조회 한도 대기
            float waitUntil = Time.realtimeSinceStartup + pollWait;
            while (true)
            {
                float elapsed = Time.realtimeSinceStartup - startedAt;
                Progress(CUSTOM_SONG_STAGE.PROCESSING, elapsed);
                if (elapsed > CustomSongConfig.JobTimeoutSec)
                {
                    error = "곡 생성 시간이 초과되었습니다. 잠시 후 다시 시도해 주세요.";
                    break;
                }
                float left = waitUntil - Time.realtimeSinceStartup;
                if (left <= 0f) break;
                yield return new WaitForSecondsRealtime(Mathf.Min(ProgressTickSec, left));
            }
            if (error != null) break;
        }
        if (error != null) { Fail(gen); yield break; }

        // 3. 응답 검증
        ChartSong song = done.song;
        if (song == null || !CustomSongStore.IsValidYoutubeId(song.youtubeId))
        {
            error = "서버 응답의 곡 정보가 올바르지 않습니다.";
            Fail(gen); yield break;
        }
        if (string.IsNullOrEmpty(song.audioUrl))
        {
            error = "서버 응답에 오디오 주소가 없습니다.";
            Fail(gen); yield break;
        }
        if (!string.IsNullOrEmpty(done.difficulty) && done.difficulty != difficulty)
            Debug.LogWarning($"[CustomSongService] 요청 난이도({difficulty})와 응답 난이도({done.difficulty})가 다릅니다.");

        NoteList chart = ValidateChart(done.chart, out string chartError);
        if (chart == null)
        {
            error = chartError;
            Fail(gen); yield break;
        }

        string youtubeId = song.youtubeId;
        bool needAudio = !CustomSongStore.HasAudio(youtubeId);
        bool needThumb = !string.IsNullOrEmpty(song.thumbnailUrl) && !File.Exists(CustomSongStore.ThumbPath(youtubeId));

        // 4. 임시 폴더에 다운로드 (오디오/썸네일은 곡당 1번만)
        try
        {
            tempDir = Path.Combine(CustomSongStore.RootDir, TempDirPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }
        catch (Exception e)
        {
            error = $"저장 폴더를 만들 수 없습니다: {e.Message}";
            Fail(gen); yield break;
        }
        string tmpAudio = Path.Combine(tempDir, CustomSongStore.AudioFileName);
        string tmpThumb = Path.Combine(tempDir, CustomSongStore.ThumbFileName);
        string tmpChart = Path.Combine(tempDir, CustomSongStore.ChartFileName(difficulty));

        Progress(CUSTOM_SONG_STAGE.DOWNLOADING, 0f);
        float audioWeight = needAudio ? (needThumb ? 0.9f : 1f) : 0f;

        if (needAudio)
        {
            // 에셋 다운로드 한도(429)는 Retry-After만큼 기다렸다가 최대 MaxQueueFullRetries번 재시도
            for (int attempt = 0; ; attempt++)
            {
                var req = Create(() =>
                {
                    var r = UnityWebRequest.Get(Resolve(baseUrl, song.audioUrl));
                    r.downloadHandler = new DownloadHandlerFile(tmpAudio) { removeFileOnAbort = true };
                    return r;
                });
                if (req == null) yield break;
                yield return Send(req, CustomSongConfig.AssetTimeoutSec,
                    p => Progress(CUSTOM_SONG_STAGE.DOWNLOADING, Mathf.Clamp01(p) * audioWeight));
                float wait = -1f;
                if (error != null && attempt < MaxQueueFullRetries && TryGetRetryWait(req, out float w)) wait = w;
                DisposeInFlight();
                if (wait < 0f) break;
                error = null;
                TryDelete(tmpAudio);
                Debug.Log($"[CustomSongService] 오디오 다운로드 한도. {wait:0}초 후 재시도 ({attempt + 1}/{MaxQueueFullRetries})");
                yield return new WaitForSecondsRealtime(wait);
            }
            if (error == null && !IsNonEmptyFile(tmpAudio))
                error = "받은 오디오 파일이 비어 있습니다.";
            if (error != null) { error = "오디오 다운로드 실패: " + error; Fail(gen); yield break; }
        }

        // 썸네일 실패는 무시하고 진행
        bool thumbOk = false;
        if (needThumb)
        {
            var req = Create(() =>
            {
                var r = UnityWebRequest.Get(Resolve(baseUrl, song.thumbnailUrl));
                r.downloadHandler = new DownloadHandlerFile(tmpThumb) { removeFileOnAbort = true };
                return r;
            });
            if (req != null)
            {
                yield return Send(req, CustomSongConfig.RequestTimeoutSec,
                    p => Progress(CUSTOM_SONG_STAGE.DOWNLOADING, audioWeight + Mathf.Clamp01(p) * (1f - audioWeight)));
                DisposeInFlight();
            }
            if (error != null)
            {
                Debug.LogWarning($"[CustomSongService] 썸네일 다운로드 실패 (무시): {error}");
                error = null;
                TryDelete(tmpThumb);
            }
            else thumbOk = File.Exists(tmpThumb);
        }
        Progress(CUSTOM_SONG_STAGE.DOWNLOADING, 1f);

        // 5. 임시 파일 → 최종 위치로 이동 → 인덱스 등록
        CustomSongMeta meta;
        try
        {
            File.WriteAllText(tmpChart, JsonUtility.ToJson(chart));

            Directory.CreateDirectory(CustomSongStore.SongDir(youtubeId));
            if (needAudio) MoveReplace(tmpAudio, CustomSongStore.AudioPath(youtubeId));
            if (thumbOk) MoveReplace(tmpThumb, CustomSongStore.ThumbPath(youtubeId));
            MoveReplace(tmpChart, CustomSongStore.ChartPath(youtubeId, difficulty));

            meta = new CustomSongMeta
            {
                id = CustomSongMeta.MakeId(youtubeId, difficulty),
                youtubeId = youtubeId,
                difficulty = difficulty,
                algorithmVersion = done.algorithmVersion ?? "",
                title = string.IsNullOrEmpty(song.title) ? youtubeId : song.title,
                artist = song.artist ?? "",
                youtubeUrl = youtubeUrl,
                duration = song.duration,
                bpm = song.bpm,
                addedAt = DateTime.UtcNow.ToString("o"),
                score = new Score(),
            };
            CustomSongStore.Upsert(meta); // 기존 항목이 있으면 점수 유지
        }
        catch (Exception e)
        {
            error = $"곡 저장에 실패했습니다: {e.Message}";
            Fail(gen); yield break;
        }
        CleanTemp();

        if (gen != generation) yield break;
        routine = null;
        IsBusy = false;
        cbProgress = null;
        cbError = null;
        Progress(CUSTOM_SONG_STAGE.DONE, 1f);
        generation++; // 완료 후 Guard 종료
        SafeInvoke(() => onDone?.Invoke(meta));
    }

    // ───────── HTTP ─────────

    // 요청 생성 중 예외(잘못된 BaseUrl 등)를 잡는다. 실패하면 error를 채우고 null 반환
    UnityWebRequest Create(Func<UnityWebRequest> factory)
    {
        UnityWebRequest req = null;
        try
        {
            req = factory();
            string token = GetToken();
            if (string.IsNullOrEmpty(token))
            {
                req.Dispose();
                error = "인증 토큰이 없습니다. 토큰을 입력하세요.";
                return null;
            }
            req.SetRequestHeader("Authorization", "Bearer " + token);
            req.SetRequestHeader("Accept", "application/json");
            return req;
        }
        catch (Exception e)
        {
            try { req?.Dispose(); } catch (Exception) { }
            error = DescribeException(e);
            return null;
        }
    }

    // 요청을 보내고 결과를 검사한다. 실패하면 error를 채운다. 요청 정리는 호출 측(DisposeInFlight)이 한다.
    IEnumerator Send(UnityWebRequest req, int timeoutSec, Action<float> onTick)
    {
        inFlight = req;
        UnityWebRequestAsyncOperation op = null;
        string sendError = null;
        try
        {
            req.timeout = timeoutSec;
            op = req.SendWebRequest();
        }
        catch (Exception e)
        {
            sendError = DescribeException(e);
        }
        if (sendError != null)
        {
            error = sendError;
            yield break;
        }

        while (!op.isDone)
        {
            onTick?.Invoke(req.downloadProgress);
            yield return null;
        }
        onTick?.Invoke(1f);

        switch (req.result)
        {
            case UnityWebRequest.Result.Success:
                break;
            case UnityWebRequest.Result.ProtocolError:
                error = DescribeHttpError(req);
                break;
            default:
                bool timeout = req.error != null &&
                               (req.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                req.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0);
                error = timeout
                    ? "서버 응답 시간이 초과되었습니다. 네트워크 상태를 확인해 주세요."
                    : $"서버에 연결할 수 없습니다. 네트워크와 서버 주소({CustomSongConfig.BaseUrl})를 확인해 주세요. ({req.error})";
                break;
        }
    }

    string DescribeHttpError(UnityWebRequest req)
    {
        long code = req.responseCode;
        string detail = ReadDetail(req);
        switch (code)
        {
            case 401:
            case 403:
                try { TokenProvider.OnTokenRejected(); }
                catch (Exception e) { Debug.LogException(e); }
                return "인증 토큰이 유효하지 않거나 만료되었습니다. 토큰을 다시 입력하세요.";
            case 404:
                return "요청한 항목을 서버에서 찾을 수 없습니다.";
            case 422:
                return string.IsNullOrEmpty(detail)
                    ? "올바른 YouTube URL이 아닙니다."
                    : $"올바른 YouTube URL이 아닙니다. ({detail})";
            case 429:
            {
                // 플레이테스트 토큰은 모든 플레이어가 한도를 공유한다 (채보 생성 시간당 20회 등)
                float wait = RawRetryAfter(req.GetResponseHeader("Retry-After"));
                return wait >= 60f
                    ? $"요청 한도를 초과했습니다. 약 {Mathf.CeilToInt(wait / 60f)}분 후 다시 시도해 주세요."
                    : "요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.";
            }
            case 503:
                switch (detail)
                {
                    case "queue_full":
                    case "busy":
                        return "서버가 혼잡합니다. 잠시 후 다시 시도해 주세요.";
                    case "storage_full":
                        return "서버 저장 공간이 부족합니다. 잠시 후 다시 시도해 주세요.";
                    case "youtube_disabled":
                        return "현재 서버에서 YouTube 곡 생성을 지원하지 않습니다.";
                    default:
                        return "서버를 일시적으로 사용할 수 없습니다. 잠시 후 다시 시도해 주세요.";
                }
            default:
                return string.IsNullOrEmpty(detail)
                    ? $"서버 오류가 발생했습니다. (HTTP {code})"
                    : $"서버 오류가 발생했습니다. (HTTP {code}: {detail})";
        }
    }

    static string DescribeJobError(ChartJobError err)
    {
        string code = err?.code;
        switch (code)
        {
            case "download_failed": return "YouTube에서 음원을 받지 못했습니다. 영상이 비공개이거나 지역 제한이 있는지 확인해 주세요.";
            case "youtube_disabled": return "현재 서버에서 YouTube 곡 생성을 지원하지 않습니다.";
            case "too_long": return "곡이 너무 깁니다. 더 짧은 영상을 사용해 주세요.";
            case "invalid_audio": return "음원을 읽을 수 없습니다. 다른 영상을 사용해 주세요.";
            case "analysis_failed": return "음원 분석에 실패했습니다. 다른 곡으로 시도해 주세요.";
            case "chart_invalid": return "이 곡으로는 채보를 만들 수 없습니다. 다른 곡으로 시도해 주세요.";
            case "timeout": return "서버 처리 시간이 초과되었습니다. 잠시 후 다시 시도해 주세요.";
            case "internal": return "서버 내부 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.";
        }
        if (!string.IsNullOrEmpty(err?.message)) return $"곡 생성에 실패했습니다: {err.message}";
        return "서버에서 곡 생성에 실패했습니다.";
    }

    static string DescribeException(Exception e, bool creatingRequest = true)
    {
        if (e is InvalidOperationException && e.Message != null &&
            e.Message.IndexOf("Insecure connection", StringComparison.OrdinalIgnoreCase) >= 0)
            return "HTTP 연결이 차단되었습니다. Player Settings → Allow downloads over HTTP를 켜거나 HTTPS 서버 주소를 사용하세요.";
        if (creatingRequest && (e is UriFormatException || e is ArgumentException))
            return $"서버 주소가 올바르지 않습니다 ({CustomSongConfig.BaseUrl}): {e.Message}";
        return $"요청 중 오류가 발생했습니다: {e.GetType().Name}: {e.Message}";
    }

    static bool IsRetryable503(string detail) => detail == "queue_full" || detail == "busy";

    static float ParseRetryAfter(string header) => Mathf.Min(RawRetryAfter(header), MaxRetryAfterSec);

    // Retry-After 헤더(초). 없거나 형식이 틀리면 기본값
    static float RawRetryAfter(string header)
    {
        if (!string.IsNullOrEmpty(header) &&
            float.TryParse(header.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float sec) && sec > 0f)
            return sec;
        return DefaultRetryAfterSec;
    }

    // 429(요청 한도) 또는 재시도 가능한 503이고, 기다릴 시간이 MaxRetryAfterSec 이하면 재시도한다.
    // 한도가 시간 단위로 걸린 경우(Retry-After가 길면)는 기다리지 않고 실패 처리한다.
    static bool TryGetRetryWait(UnityWebRequest req, out float wait)
    {
        wait = -1f;
        bool retryable = req.responseCode == 429 ||
                         (req.responseCode == 503 && IsRetryable503(ReadDetail(req)));
        if (!retryable) return false;
        float raw = RawRetryAfter(req.GetResponseHeader("Retry-After"));
        if (raw > MaxRetryAfterSec) return false;
        wait = raw;
        return true;
    }

    // {"detail":"..."} 의 문자열 detail. 목록 형태(검증 오류)면 null
    static string ReadDetail(UnityWebRequest req)
    {
        if (!(req.downloadHandler is DownloadHandlerBuffer)) return null;
        string text;
        try { text = req.downloadHandler.text; }
        catch (Exception) { return null; }
        var parsed = TryParse<ErrorDetail>(text);
        return string.IsNullOrEmpty(parsed?.detail) ? null : parsed.detail;
    }

    // ───────── 채보 검증 ─────────

    // 노트 1개 이상, dir/type 일치, 시간 값 정상, StartTime 순 정렬(아니면 안정 정렬)
    static NoteList ValidateChart(NoteList chart, out string chartError)
    {
        chartError = null;
        if (chart == null || chart.Notes == null || chart.Notes.Count == 0)
        {
            chartError = "받은 채보가 비어 있습니다.";
            return null;
        }
        bool sorted = true;
        for (int n = 0; n < chart.Notes.Count; n++)
        {
            NoteData note = chart.Notes[n];
            if (note == null) { chartError = "받은 채보 형식이 올바르지 않습니다."; return null; }
            int type = (int)note.type;
            int dir = (int)note.dir;
            if (type < 0 || type > 3 || dir < 0 || dir > 1 ||
                note.dir != (type <= (int)SUSHI_TYPE.L2 ? NOTE_DIRECT.LEFT : NOTE_DIRECT.RIGHT))
            {
                chartError = $"받은 채보의 노트 방향/종류가 맞지 않습니다. (#{n})";
                return null;
            }
            if (float.IsNaN(note.StartTime) || float.IsInfinity(note.StartTime) || note.StartTime < 0f ||
                float.IsNaN(note.EndTime) || float.IsInfinity(note.EndTime) ||
                (note.EndTime != 0f && note.EndTime <= note.StartTime))
            {
                chartError = $"받은 채보의 노트 시간이 올바르지 않습니다. (#{n})";
                return null;
            }
            if (n > 0 && chart.Notes[n - 1].StartTime > note.StartTime) sorted = false;
        }
        if (!sorted)
        {
            Debug.LogWarning("[CustomSongService] 채보가 StartTime 순이 아니어서 정렬합니다.");
            chart.Notes = chart.Notes.OrderBy(x => x.StartTime).ToList(); // OrderBy는 안정 정렬
        }
        return chart;
    }

    // ───────── 정리 ─────────

    void Fail(int gen)
    {
        if (gen != generation) return;
        DisposeInFlight();
        CleanTemp();
        routine = null;
        IsBusy = false;
        generation++; // 이후 Guard 단계와 중복 Fail 방지
        string msg = error ?? "알 수 없는 오류가 발생했습니다.";
        error = null;
        Debug.LogWarning($"[CustomSongService] {msg}");
        var onProgress = cbProgress;
        var onError = cbError;
        cbProgress = null;
        cbError = null;
        SafeInvoke(() => onProgress?.Invoke(CUSTOM_SONG_STAGE.FAILED, 0f));
        SafeInvoke(() => onError?.Invoke(msg));
    }

    void DisposeInFlight()
    {
        if (inFlight == null) return;
        try
        {
            if (!inFlight.isDone) inFlight.Abort();
            inFlight.Dispose();
        }
        catch (Exception) { }
        inFlight = null;
    }

    void CleanTemp()
    {
        if (string.IsNullOrEmpty(tempDir)) return;
        try
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CustomSongService] 임시 폴더 삭제 실패: {e.Message}");
        }
        tempDir = null;
    }

    // 이전 실행이 강제 종료되어 남은 임시 폴더 정리
    static void CleanStaleTemp()
    {
        try
        {
            string root = CustomSongStore.RootDir;
            if (!Directory.Exists(root)) return;
            foreach (string dir in Directory.GetDirectories(root, TempDirPrefix + "*"))
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    static void MoveReplace(string src, string dest)
    {
        if (File.Exists(dest)) File.Delete(dest);
        File.Move(src, dest);
    }

    static bool IsNonEmptyFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length > 0;
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }

    static string GetToken()
    {
        try { return TokenProvider?.GetAccessToken()?.Trim(); }
        catch (Exception e)
        {
            Debug.LogException(e);
            return null;
        }
    }

    static void SafeInvoke(Action action)
    {
        try { action(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // 서버는 절대 URL을 주지만, 상대 경로가 오면 BaseUrl 기준으로 변환
    static string Resolve(string baseUrl, string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        return url.StartsWith("/") ? baseUrl + url : baseUrl + "/" + url;
    }

    static T TryParse<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonUtility.FromJson<T>(json); }
        catch (Exception) { return null; }
    }
}
