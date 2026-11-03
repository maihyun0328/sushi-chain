// ─────────────────────────────────────────────────────────────
// 곡 재생 시계. 노트 위치, 스폰, 판정은 모두 이 시계를 기준으로 한다.
//
// - 곡은 AudioSource.PlayScheduled(dspStart)로 시작한다.
//   Play()는 다음 DSP 버퍼에 들어가므로 시작 시각이 버퍼 단위로 흔들린다.
//   예약 재생은 dsp 시간축에서 정확한 샘플 위치에 시작한다.
// - SongTime = 보간된 dsp - dspStart. 프레임 시작 시점에 한 번 계산하고, 그 프레임 안에서는 모든 노트가 같은 값을 쓴다.
// - GameTime = SongTime - InputCalibration.
//   보정값(탭이 메트로놈보다 늦은 정도)만큼 노트가 늦게 도착한다. 판정도 같은 시간축을 쓴다.
// - 시작할 때 DSP 버퍼를 줄여 출력 지연을 낮춘다.
//   기본값 1024 샘플은 Unity의 "Best performance" 설정이다.
//   PlayerPrefs "Audio.DspBufferSize"로 바꿀 수 있고, 0이면 프로젝트 기본값을 유지한다.
// ─────────────────────────────────────────────────────────────
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class SongClock : MonoBehaviour
{
    public static SongClock i;

    public const string DspBufferPrefKey = "Audio.DspBufferSize";
    public const int DefaultDspBufferSize = 256; // Unity "Best latency" 프리셋과 같은 값

    readonly DspClockEstimator estimator = new DspClockEstimator();

    public bool IsRunning { get; private set; }
    public double DspStart { get; private set; }
    // 이번 프레임 시작 시점의 곡 위치(초). 오디오 믹스 시간축 기준이며, 단조 증가한다.
    public double SongTime { get; private set; }
    public double GameTime => SongTime - Calibration;

    static double Calibration => GameManager.i != null ? GameManager.i.InputCalibration : 0.0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (i != null) return;
        ApplyDspBufferSize();
        var go = new GameObject("[SongClock]");
        DontDestroyOnLoad(go);
        go.AddComponent<SongClock>();
        go.AddComponent<TimedKeyInput>();
    }

    static void ApplyDspBufferSize()
    {
        int target = PlayerPrefs.GetInt(DspBufferPrefKey, DefaultDspBufferSize);
        var config = AudioSettings.GetConfiguration();
        if (target > 0 && config.dspBufferSize != target)
        {
            config.dspBufferSize = target;
            if (!AudioSettings.Reset(config))
                Debug.LogWarning($"[SongClock] DSP buffer {target} 적용 실패, 기본값 사용");
        }
        AudioSettings.GetDSPBufferSize(out int length, out int buffers);
        int rate = AudioSettings.outputSampleRate;
        Debug.Log($"[SongClock] DSP buffer {length} x {buffers} @ {rate}Hz " +
                  $"(버퍼 1개 {1000.0 * length / rate:F1}ms, 전체 {1000.0 * length * buffers / rate:F1}ms)");
    }

    void Awake()
    {
        if (i != null && i != this) { Destroy(this); return; }
        i = this;
        Tick();
    }

    void Update() => Tick();

    void Tick()
    {
        estimator.Sample(AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble);
        if (!IsRunning) return;
        double t = SongTimeAt(Time.realtimeSinceStartupAsDouble);
        if (t > SongTime) SongTime = t;
    }

    public double RealtimeToDsp(double realtime) =>
        estimator.HasEstimate ? estimator.ToDsp(realtime) : AudioSettings.dspTime;

    public double SongTimeAt(double realtime) => RealtimeToDsp(realtime) - DspStart;
    public double GameTimeAt(double realtime) => SongTimeAt(realtime) - Calibration;

    // leadIn초 뒤에 곡을 시작한다. 그동안 SongTime은 음수에서 0으로 다가간다.
    public void StartSong(AudioSource source, double leadIn)
    {
        estimator.Sample(AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble);
        DspStart = AudioSettings.dspTime + leadIn;
        source.PlayScheduled(DspStart);
        IsRunning = true;
        SongTime = SongTimeAt(Time.realtimeSinceStartupAsDouble);
    }

    public void Stop()
    {
        IsRunning = false;
    }
}
