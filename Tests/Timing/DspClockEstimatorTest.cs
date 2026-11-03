// DspClockEstimator 시뮬레이션 테스트 (Unity 없이 실행).
// 오디오 스레드가 버퍼 단위로 dspTime을 갱신하고, 게임 프레임이 불규칙 간격으로 그 값을 읽는 상황을 만든다.
// 그 상태에서 "프레임 시각의 실제 오디오 위치" 대비 오차를 두 방식으로 비교한다.
//   (a) dspTime을 그대로 읽은 값
//   (b) 추정기 보간값
using System;
using System.Collections.Generic;
using System.Linq;

static class DspClockEstimatorTest
{
    static int failures;

    static void Check(bool ok, string msg) { if (!ok) { failures++; Console.WriteLine("FAIL " + msg); } }

    static (double meanAbs, double p99, double max) Stats(List<double> e)
    {
        var a = e.Select(Math.Abs).OrderBy(x => x).ToList();
        return (a.Average(), a[(int)(a.Count * 0.99)], a[a.Count - 1]);
    }

    static void Run(string name, int buffer, int rate, double fpsMin, double fpsMax, double ppm, int seed)
    {
        var rng = new Random(seed);
        double tick = (double)buffer / rate;
        double drift = 1 + ppm * 1e-6;
        double audioStartRt = 0.37;                 // 오디오 시계 원점 (임의)
        Func<double, double> trueDsp = rt => (rt - audioStartRt) * drift;
        Func<double, double> reportedDsp = rt => Math.Floor(trueDsp(rt) / tick) * tick;

        var est = new DspClockEstimator();
        var errRaw = new List<double>();
        var errEst = new List<double>();
        double rt = 1.0, prevEstimate = double.NegativeInfinity;
        int backwards = 0;
        while (rt < 200.0)                           // 약 3분 20초 곡
        {
            rt += 1.0 / (fpsMin + rng.NextDouble() * (fpsMax - fpsMin));
            est.Sample(reportedDsp(rt), rt);
            if (rt < 2.0) continue;                  // 워밍업
            double truth = trueDsp(rt);
            double e = est.ToDsp(rt);
            errRaw.Add(reportedDsp(rt) - truth);
            errEst.Add(e - truth);
            if (e < prevEstimate) backwards++;
            prevEstimate = e;
        }
        var r = Stats(errRaw);
        var s = Stats(errEst);
        Console.WriteLine($"{name,-34} raw dspTime: mean {r.meanAbs * 1000,5:F2}ms p99 {r.p99 * 1000,5:F2}ms max {r.max * 1000,5:F2}ms | " +
                          $"estimator: mean {s.meanAbs * 1000,5:F2}ms p99 {s.p99 * 1000,5:F2}ms max {s.max * 1000,5:F2}ms | backwards {backwards}");
        Check(s.p99 < r.p99, name + ": estimator p99 must beat raw dspTime");
        Check(s.max < 2.0e-3, name + $": estimator max error {s.max * 1000:F2}ms >= 2ms");
    }

    static void Discontinuity()
    {
        // 오디오가 1초 멈췄다가 재개 (dsp 정지 → realtime만 진행)
        var est = new DspClockEstimator();
        double tick = 256.0 / 48000;
        double rt = 0, dspBase = 0;
        for (; rt < 3; rt += 0.004) est.Sample(Math.Floor((rt - dspBase) / tick) * tick, rt);
        dspBase = 1.0;   // 이후 dsp = rt - 1.0 (1초 뒤처짐)
        double t0 = rt + 1.0;
        for (rt = t0; rt < t0 + 0.1; rt += 0.004) est.Sample(Math.Floor((rt - dspBase) / tick) * tick, rt);
        double err = est.ToDsp(rt) - (rt - dspBase);
        Console.WriteLine($"discontinuity (1s stall)             error after 100ms: {err * 1000:F2}ms");
        Check(Math.Abs(err) < 0.01, "estimator must re-lock within 100ms after an audio stall");
    }

    static int Main()
    {
        Run("256 @48k, 144-240fps", 256, 48000, 144, 240, 30, 1);
        Run("256 @48k, 55-65fps", 256, 48000, 55, 65, -50, 2);
        Run("1024 @48k (old), 55-65fps", 1024, 48000, 55, 65, 20, 3);
        Run("1024 @44.1k (old), 300-1000fps", 1024, 44100, 300, 1000, 0, 4);
        Run("512 @48k, 25-35fps (heavy load)", 512, 48000, 25, 35, 100, 5);
        Discontinuity();
        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
        return failures == 0 ? 0 : 1;
    }
}
