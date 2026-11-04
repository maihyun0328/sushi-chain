using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SushiChain.LocalChart;
using SushiChain.LocalChart.Input;
using UnityEngine;
using UnityEngine.Networking;

// 로컬 오디오 파일(mp3/wav/ogg) → 온디바이스 채보 생성 → 커스텀 곡 저장소에 등록.
// YouTube URL 경로(CustomSongService)와 독립적이다. 서버를 쓰지 않는다.
//
// 1. 검증(확장자, 크기) → sha256 (백그라운드) → youtubeId = "local_" + sha 앞 12자
// 2. 저장소에 오디오가 없으면 임시 폴더로 복사 (백그라운드)
// 3. 그 파일을 UnityWebRequestMultimedia로 디코딩 → clip.GetData (메인 스레드)
//    백그라운드 Task: 다운믹스 → 22050 Hz 리샘플 → AudioAnalyzer.Analyze → ChartEngine.Generate
//    (분석 결과는 sha별로 세션 동안 메모리에 캐시 → 같은 파일의 다른 난이도는 디코딩/분석을 건너뛴다)
// 4. 메인 스레드: 임시 폴더에 채보 작성 → 오디오/채보를 최종 위치로 이동 → index.json Upsert → onDone
// Cancel()은 generation을 올리고 CancellationToken을 취소한다. 늦게 끝난 Task의 결과는 버린다.
public class LocalChartService : MonoBehaviour
{
    public static LocalChartService i;

    public const string ImportFolderName = "Import";
    public const string IdPrefix = "local_";
    public const long MaxFileBytes = 100L * 1024 * 1024;
    public const float MaxSongSeconds = 300f;        // 서버 max_song_seconds
    public const float DurationTolerance = 0.5f;     // 서버 DURATION_TOLERANCE_S
    public const float MinSongSeconds = 5f;
    const int MaxCachedFeatures = 8;
    const string TempDirPrefix = ".tmp_local_";

    public static string ImportDir => Path.Combine(Application.persistentDataPath, ImportFolderName);
    public static string AlgorithmVersion => LocalChartConstants.ChartAlgoVersion + "-local";

    // 세션 캐시: sha256 → AudioFeatures (메인 스레드에서만 접근)
    readonly Dictionary<string, AudioFeatures> featureCache = new Dictionary<string, AudioFeatures>();
    readonly LinkedList<string> cacheOrder = new LinkedList<string>();

    Coroutine routine;
    UnityWebRequest inFlight;
    AudioClip loadingClip;
    CancellationTokenSource cts;
    string tempDir;
    int generation;
    Action<LOCAL_CHART_STAGE, float> cbProgress;
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

    // Import 폴더를 만들고 그 안의 오디오 파일 목록(이름순)을 돌려준다
    public static List<string> ListImportFiles(int max = 200)
    {
        var result = new List<string>();
        try
        {
            string dir = ImportDir;
            Directory.CreateDirectory(dir);
            foreach (string path in Directory.GetFiles(dir))
            {
                if (CustomSongAudio.Normalize(Path.GetExtension(path)) == null) continue;
                if (Path.GetFileName(path).StartsWith(".")) continue;
                result.Add(path);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LocalChartService] Import 폴더를 읽을 수 없습니다: {e.Message}");
        }
        return result;
    }

    // 상대 경로는 Import 폴더 기준. 앞뒤 공백과 따옴표(Finder "경로 복사")는 제거
    public static string ResolvePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string p = input.Trim().Trim('"', '\'').Trim();
        if (p.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try { p = new Uri(p).LocalPath; } catch (Exception) { }
        }
        if (p.StartsWith("~/") || p == "~")
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            if (!string.IsNullOrEmpty(home)) p = Path.Combine(home, p.Length > 2 ? p.Substring(2) : "");
        }
        try
        {
            if (!Path.IsPathRooted(p)) p = Path.Combine(ImportDir, p);
            return Path.GetFullPath(p);
        }
        catch (Exception) { return p; }
    }

    public void RequestLocalFile(string path, string difficulty,
        Action<LOCAL_CHART_STAGE, float> onProgress,
        Action<CustomSongMeta> onDone,
        Action<string> onError)
    {
        if (IsBusy)
        {
            SafeInvoke(() => onError?.Invoke("이미 채보를 만드는 중입니다."));
            return;
        }

        string diff = string.IsNullOrWhiteSpace(difficulty) ? ChartDifficulty.Normal : difficulty.Trim().ToLowerInvariant();
        string fullPath = ResolvePath(path);
        string ext = CustomSongAudio.Normalize(Path.GetExtension(fullPath));
        string fail = null;
        long size = 0;
        if (fullPath.Length == 0) fail = "오디오 파일 경로를 입력하세요.";
        else if (ext == null) fail = "지원하지 않는 파일 형식입니다. (mp3, wav, ogg)";
        else if (!CustomSongStore.IsValidDifficulty(diff)) fail = $"지원하지 않는 난이도입니다: {difficulty}";
        else
        {
            try
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists) fail = "파일을 찾을 수 없습니다: " + fullPath;
                else
                {
                    size = info.Length;
                    if (size == 0) fail = "파일이 비어 있습니다.";
                    else if (size > MaxFileBytes) fail = $"파일이 너무 큽니다. ({size / (1024 * 1024)} MB, 최대 {MaxFileBytes / (1024 * 1024)} MB)";
                }
            }
            catch (Exception e) { fail = $"파일을 열 수 없습니다: {e.Message}"; }
        }
        if (fail != null)
        {
            SafeInvoke(() => onProgress?.Invoke(LOCAL_CHART_STAGE.FAILED, 0f));
            SafeInvoke(() => onError?.Invoke(fail));
            return;
        }

        CleanStaleTemp();
        IsBusy = true;
        cbProgress = onProgress;
        cbError = onError;
        cts = new CancellationTokenSource();
        int gen = ++generation;
        var started = StartCoroutine(Run(gen, cts.Token, fullPath, ext, diff, onProgress, onDone));
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
        if (cts != null)
        {
            try { cts.Cancel(); } catch (Exception) { }
            cts.Dispose();
            cts = null;
        }
        DisposeInFlight();
        CleanTemp();
        cbProgress = null;
        cbError = null;
        IsBusy = false;
    }

    // ───────── 실행 ─────────

    // 백그라운드 Task 결과. Task 안에서는 Unity API를 호출하지 않는다
    sealed class WorkResult
    {
        public AudioFeatures Features;
        public NoteList Chart;
    }

    IEnumerator Run(int gen, CancellationToken ct, string path, string ext, string difficulty,
        Action<LOCAL_CHART_STAGE, float> onProgress, Action<CustomSongMeta> onDone)
    {
        // 백그라운드 단계가 바꾸는 진행 단계 (메인 스레드가 매 프레임 읽는다)
        int workerStage = -1;
        LOCAL_CHART_STAGE shownStage = LOCAL_CHART_STAGE.FAILED;
        float shownProgress = -1f;
        void Progress(LOCAL_CHART_STAGE stage, float value)
        {
            if (gen != generation) return;
            if (stage == shownStage && Mathf.Approximately(value, shownProgress)) return;
            shownStage = stage;
            shownProgress = value;
            SafeInvoke(() => onProgress?.Invoke(stage, value));
        }

        // 단계별 소요 시간 (끝나면 한 줄로 로그를 남긴다)
        var total = System.Diagnostics.Stopwatch.StartNew();
        var lap = System.Diagnostics.Stopwatch.StartNew();
        var timings = new System.Text.StringBuilder();
        void Lap(string stage)
        {
            timings.Append(stage).Append(' ').Append(lap.ElapsedMilliseconds).Append("ms, ");
            lap.Restart();
        }

        // 1. sha256 (큰 파일도 메인 스레드를 막지 않게 백그라운드에서)
        Progress(LOCAL_CHART_STAGE.READING, 0f);
        Task<string> hashTask = Task.Run(() => Sha256Hex(path, ct), ct);
        while (!hashTask.IsCompleted) yield return null;
        if (gen != generation) yield break;
        if (!TryGetResult(hashTask, out string sha, out string hashError))
        {
            Fail(gen, "파일을 읽을 수 없습니다: " + hashError);
            yield break;
        }
        string youtubeId = IdPrefix + sha.Substring(0, 12);
        if (!CustomSongStore.IsValidYoutubeId(youtubeId))
        {
            Fail(gen, "곡 ID를 만들 수 없습니다.");
            yield break;
        }
        string seedKey = SeedKeys.ForLocalFile(sha, difficulty);
        Lap("hash");

        // 2. 저장소에 오디오가 없으면 임시 폴더로 먼저 복사하고, 디코딩은 그 복사본(안전한 파일명)에서 한다.
        //    원본 경로에 #, %, 유니코드 등이 있어도 file:// URI 문제가 없다.
        bool needAudio = !CustomSongStore.HasAudio(youtubeId, ext);
        string tmpAudio, tmpChart, finalAudio, finalChart, songDir;
        try
        {
            tempDir = Path.Combine(CustomSongStore.RootDir, TempDirPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            tmpAudio = Path.Combine(tempDir, CustomSongStore.AudioFileNameFor(ext));
            tmpChart = Path.Combine(tempDir, CustomSongStore.ChartFileName(difficulty));
            songDir = CustomSongStore.SongDir(youtubeId);
            finalAudio = CustomSongStore.AudioPath(youtubeId, ext);
            finalChart = CustomSongStore.ChartPath(youtubeId, difficulty);
        }
        catch (Exception e)
        {
            Fail(gen, $"저장 폴더를 만들 수 없습니다: {e.Message}");
            yield break;
        }
        if (needAudio)
        {
            Task copy = Task.Run(() => File.Copy(path, tmpAudio, true), ct);
            while (!copy.IsCompleted) yield return null;
            if (gen != generation) yield break;
            if (!TryGetResult(copy, out string copyError))
            {
                Fail(gen, "오디오 파일을 복사할 수 없습니다: " + copyError);
                yield break;
            }
        }
        string decodePath = needAudio ? tmpAudio : finalAudio;
        if (needAudio) Lap("copy");

        // 3. 같은 파일을 이미 분석했으면 디코딩/분석을 건너뛴다
        featureCache.TryGetValue(sha, out AudioFeatures cached);
        float[] pcm = null;
        int channels = 0, frequency = 0;
        if (cached == null)
        {
            Progress(LOCAL_CHART_STAGE.READING, 0.1f);
            UnityWebRequest req;
            try
            {
                req = UnityWebRequestMultimedia.GetAudioClip(new Uri(decodePath).AbsoluteUri, CustomSongAudio.ToAudioType(ext));
                if (req.downloadHandler is DownloadHandlerAudioClip dh)
                {
                    dh.streamAudio = false;   // GetData를 쓰려면 전체를 메모리에 디코딩해야 한다
                    dh.compressed = false;
                }
                inFlight = req;
            }
            catch (Exception e)
            {
                Fail(gen, $"오디오를 열 수 없습니다: {e.Message}");
                yield break;
            }

            UnityWebRequestAsyncOperation op = null;
            string sendError = null;
            try { op = req.SendWebRequest(); }
            catch (Exception e) { sendError = e.Message; }
            if (sendError != null) { Fail(gen, $"오디오를 열 수 없습니다: {sendError}"); yield break; }
            while (!op.isDone)
            {
                Progress(LOCAL_CHART_STAGE.READING, 0.1f + 0.4f * Mathf.Clamp01(req.downloadProgress));
                yield return null;
            }
            if (gen != generation) yield break;
            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = req.error;
                DisposeInFlight();
                Fail(gen, $"오디오를 읽을 수 없습니다: {err}");
                yield break;
            }

            AudioClip clip = null;
            try { clip = DownloadHandlerAudioClip.GetContent(req); }
            catch (Exception e) { Debug.LogWarning($"[LocalChartService] AudioClip 생성 실패: {e.Message}"); }
            DisposeInFlight();
            if (clip == null) { Fail(gen, "오디오를 디코딩할 수 없습니다. 파일이 손상되었거나 지원하지 않는 코덱입니다."); yield break; }
            loadingClip = clip;

            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            while (clip.loadState == AudioDataLoadState.Loading)
            {
                Progress(LOCAL_CHART_STAGE.READING, 0.6f);
                yield return null;
            }
            if (gen != generation) yield break;
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                Fail(gen, "오디오를 디코딩할 수 없습니다.");
                yield break;
            }

            channels = clip.channels;
            frequency = clip.frequency;
            int frames = clip.samples;
            float seconds = frequency > 0 ? (float)frames / frequency : 0f;
            string clipError = null;
            if (channels < 1 || frequency <= 0 || frames <= 0) clipError = "오디오에 샘플이 없습니다.";
            else if (seconds > MaxSongSeconds + DurationTolerance)
                clipError = $"곡이 너무 깁니다. ({seconds:0}초, 최대 {MaxSongSeconds:0}초)";
            else if (seconds < MinSongSeconds) clipError = $"곡이 너무 짧습니다. ({seconds:0.0}초, 최소 {MinSongSeconds:0}초)";
            if (clipError == null)
            {
                // 메인 스레드에서 샘플을 복사한다 (GetData는 메인 스레드 전용)
                try
                {
                    pcm = new float[(long)frames * channels];
                    if (!clip.GetData(pcm, 0)) clipError = "오디오 샘플을 읽을 수 없습니다.";
                }
                catch (OutOfMemoryException) { clipError = "메모리가 부족합니다. 더 짧은 곡을 사용해 주세요."; }
                catch (Exception e) { clipError = $"오디오 샘플을 읽을 수 없습니다: {e.Message}"; }
            }
            DestroyLoadingClip();
            if (clipError != null) { pcm = null; Fail(gen, clipError); yield break; }
            Lap($"decode({frequency}Hz x{channels})");
        }
        else Lap("decode(캐시)");

        // 4. 백그라운드: 다운믹스/리샘플/분석(캐시 미스일 때만) → 채보 생성
        Progress(cached == null ? LOCAL_CHART_STAGE.ANALYZING : LOCAL_CHART_STAGE.GENERATING, 0f);
        float[] pcmLocal = pcm;
        pcm = null;
        int ch = channels, rate = frequency;
        string workerTimings = "";
        Task<WorkResult> work = Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var t = new System.Text.StringBuilder();
            AudioFeatures features = cached;
            if (features == null)
            {
                Volatile.Write(ref workerStage, (int)LOCAL_CHART_STAGE.ANALYZING);
                float[] mono = LocalAudioPrep.ToMono22050(pcmLocal, ch, rate, ct);
                pcmLocal = null;
                t.Append("resample ").Append(sw.ElapsedMilliseconds).Append("ms, ");
                sw.Restart();
                ct.ThrowIfCancellationRequested();
                features = AudioAnalyzer.Analyze(mono);
                if (features == null) throw new ChartGenerationException("analysis returned no features");
                t.Append("analyze ").Append(sw.ElapsedMilliseconds).Append("ms, ");
                sw.Restart();
            }
            ct.ThrowIfCancellationRequested();
            Volatile.Write(ref workerStage, (int)LOCAL_CHART_STAGE.GENERATING);
            List<LocalNote> notes = ChartEngine.Generate(features, difficulty, seedKey);
            ct.ThrowIfCancellationRequested();
            t.Append("generate ").Append(sw.ElapsedMilliseconds).Append("ms, ");
            workerTimings = t.ToString();
            return new WorkResult { Features = features, Chart = ToNoteList(notes) };
        }, ct);

        while (!work.IsCompleted)
        {
            int s = Volatile.Read(ref workerStage);
            if (s >= 0) Progress((LOCAL_CHART_STAGE)s, 0f);
            yield return null;
        }
        if (gen != generation) yield break;
        if (!TryGetResult(work, out WorkResult result, out string workError))
        {
            Fail(gen, workError);
            yield break;
        }
        if (cached == null) CacheFeatures(sha, result.Features);
        lap.Restart();
        timings.Append(workerTimings);

        NoteList chart = result.Chart;
        string chartError = ValidateChart(chart, result.Features.Duration);
        if (chartError != null) { Fail(gen, chartError); yield break; }

        // 5. 저장: 임시 폴더 → 최종 위치 → index.json
        Progress(LOCAL_CHART_STAGE.SAVING, 0f);
        CustomSongMeta meta;
        try
        {
            File.WriteAllText(tmpChart, JsonUtility.ToJson(chart));
            Directory.CreateDirectory(songDir);
            if (needAudio) MoveReplace(tmpAudio, finalAudio);
            MoveReplace(tmpChart, finalChart);

            string title = Path.GetFileNameWithoutExtension(path);
            meta = new CustomSongMeta
            {
                id = CustomSongMeta.MakeId(youtubeId, difficulty),
                youtubeId = youtubeId,
                difficulty = difficulty,
                algorithmVersion = AlgorithmVersion,
                title = string.IsNullOrWhiteSpace(title) ? youtubeId : title.Trim(),
                artist = "",
                youtubeUrl = "",
                duration = (float)result.Features.Duration,
                bpm = (float)result.Features.Tempo,
                addedAt = DateTime.UtcNow.ToString("o"),
                score = new Score(),
                audioExt = ext,
            };
            CustomSongStore.Upsert(meta); // 기존 항목이 있으면 점수 유지
        }
        catch (Exception e)
        {
            Fail(gen, $"곡 저장에 실패했습니다: {e.Message}");
            yield break;
        }
        CleanTemp();
        Lap("save");
        Debug.Log($"[LocalChartService] {difficulty} {result.Features.Duration:0.0}s 곡, 총 {total.ElapsedMilliseconds}ms: " +
                  timings.ToString().TrimEnd(' ', ','));

        if (gen != generation) yield break;
        routine = null;
        IsBusy = false;
        cbProgress = null;
        cbError = null;
        DisposeCts();
        Progress(LOCAL_CHART_STAGE.DONE, 1f);
        generation++;
        SafeInvoke(() => onDone?.Invoke(meta));
    }

    // ───────── 백그라운드 결과 / 오류 메시지 ─────────

    static bool TryGetResult<T>(Task<T> task, out T value, out string error)
    {
        value = default;
        if (!TryGetResult(task, out error)) return false;
        value = task.Result;
        return true;
    }

    static bool TryGetResult(Task task, out string error)
    {
        error = null;
        if (task.Status == TaskStatus.RanToCompletion) return true;
        if (task.IsCanceled) { error = "취소되었습니다."; return false; }
        Exception e = task.Exception?.GetBaseException() ?? new Exception("알 수 없는 오류");
        Debug.LogException(e);
        error = DescribeWorkerException(e);
        return false;
    }

    static string DescribeWorkerException(Exception e)
    {
        switch (e)
        {
            case OperationCanceledException _: return "취소되었습니다.";
            case ChartGenerationException g: return $"이 곡으로는 채보를 만들 수 없습니다. ({g.Message})";
            case NotImplementedException _: return "온디바이스 채보 생성기가 아직 포함되지 않은 빌드입니다.";
            case OutOfMemoryException _: return "메모리가 부족합니다. 더 짧은 곡을 사용해 주세요.";
            case FileNotFoundException _:
            case DirectoryNotFoundException _: return "파일을 찾을 수 없습니다.";
            case UnauthorizedAccessException _: return "파일에 접근할 권한이 없습니다.";
            case IOException io: return $"파일 입출력 오류: {io.Message}";
            default: return $"채보 생성 중 오류가 발생했습니다: {e.GetType().Name}: {e.Message}";
        }
    }

    static string Sha256Hex(string path, CancellationToken ct)
    {
        using (var sha = SHA256.Create())
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        {
            var buf = new byte[1 << 16];
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.TransformBlock(buf, 0, n, null, 0);
            }
            sha.TransformFinalBlock(buf, 0, 0);
            var sb = new StringBuilder(64);
            foreach (byte b in sha.Hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    // ───────── 채보 변환 / 검증 ─────────

    // LocalNote(double 초) → NoteData(float 초). EndTime 0 = 숏노트
    static NoteList ToNoteList(List<LocalNote> notes)
    {
        var list = new NoteList();
        if (notes == null) return list;
        foreach (LocalNote n in notes)
        {
            list.Notes.Add(new NoteData
            {
                dir = (NOTE_DIRECT)n.Dir,
                type = (SUSHI_TYPE)n.Type,
                StartTime = (float)n.StartTime,
                EndTime = (float)n.EndTime,
            });
        }
        return list;
    }

    // CustomSongService.ValidateChart와 같은 규칙 + 곡 길이 안쪽
    static string ValidateChart(NoteList chart, double duration)
    {
        if (chart == null || chart.Notes == null || chart.Notes.Count == 0) return "채보가 비어 있습니다. 다른 곡으로 시도해 주세요.";
        for (int n = 0; n < chart.Notes.Count; n++)
        {
            NoteData note = chart.Notes[n];
            int type = (int)note.type, dir = (int)note.dir;
            if (type < 0 || type > 3 || dir < 0 || dir > 1 ||
                note.dir != (type <= (int)SUSHI_TYPE.L2 ? NOTE_DIRECT.LEFT : NOTE_DIRECT.RIGHT))
                return $"생성된 채보의 노트 방향/종류가 맞지 않습니다. (#{n})";
            if (float.IsNaN(note.StartTime) || float.IsInfinity(note.StartTime) || note.StartTime < 0f ||
                float.IsNaN(note.EndTime) || float.IsInfinity(note.EndTime) ||
                (note.EndTime != 0f && note.EndTime <= note.StartTime))
                return $"생성된 채보의 노트 시간이 올바르지 않습니다. (#{n})";
            if (n > 0 && chart.Notes[n - 1].StartTime > note.StartTime)
                return $"생성된 채보가 시간 순이 아닙니다. (#{n})";
        }
        if (duration > 0 && chart.Notes[chart.Notes.Count - 1].StartTime > duration + DurationTolerance)
            return "생성된 채보가 곡 길이를 벗어납니다.";
        return null;
    }

    // ───────── 캐시 / 정리 ─────────

    void CacheFeatures(string sha, AudioFeatures features)
    {
        if (features == null) return;
        if (featureCache.ContainsKey(sha)) cacheOrder.Remove(sha);
        featureCache[sha] = features;
        cacheOrder.AddLast(sha);
        while (cacheOrder.Count > MaxCachedFeatures)
        {
            featureCache.Remove(cacheOrder.First.Value);
            cacheOrder.RemoveFirst();
        }
    }

    void Fail(int gen, string message)
    {
        if (gen != generation) return;
        DisposeInFlight();
        CleanTemp();
        routine = null;
        IsBusy = false;
        generation++;
        DisposeCts();
        string msg = string.IsNullOrEmpty(message) ? "알 수 없는 오류가 발생했습니다." : message;
        Debug.LogWarning($"[LocalChartService] {msg}");
        var onProgress = cbProgress;
        var onError = cbError;
        cbProgress = null;
        cbError = null;
        SafeInvoke(() => onProgress?.Invoke(LOCAL_CHART_STAGE.FAILED, 0f));
        SafeInvoke(() => onError?.Invoke(msg));
    }

    void DisposeCts()
    {
        if (cts == null) return;
        cts.Dispose();
        cts = null;
    }

    void DisposeInFlight()
    {
        if (inFlight != null)
        {
            try
            {
                if (!inFlight.isDone) inFlight.Abort();
                inFlight.Dispose();
            }
            catch (Exception) { }
            inFlight = null;
        }
        DestroyLoadingClip();
    }

    void DestroyLoadingClip()
    {
        if (loadingClip == null) return;
        try { Destroy(loadingClip); } catch (Exception) { }
        loadingClip = null;
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
            // 취소 직후 백그라운드 복사가 아직 파일을 쥐고 있을 수 있다. 다음 요청 때 CleanStaleTemp가 지운다
            Debug.LogWarning($"[LocalChartService] 임시 폴더 삭제 실패: {e.Message}");
        }
        tempDir = null;
    }

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

    static void SafeInvoke(Action action)
    {
        try { action(); }
        catch (Exception e) { Debug.LogException(e); }
    }
}

// 로컬 채보 생성 진행 단계 (UI 표시용)
public enum LOCAL_CHART_STAGE
{
    READING,     // 해시 + 디코딩 (오디오 읽는 중)
    ANALYZING,   // 다운믹스/리샘플/분석 (분석 중)
    GENERATING,  // 채보 생성 중
    SAVING,      // 저장 중
    DONE,
    FAILED
}
