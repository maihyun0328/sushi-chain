// Input 트랙 단위 테스트: AudioDownmix + SwrResampler.
// 실행: ROOT=<worktree> bash localchart-test.sh Tests/LocalChart/ResamplerTests.cs [-- <golden dir>]
// golden: Tests/LocalChart/tools/make_input_golden.py (ffmpeg -ac 1 -ar 22050 출력)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SushiChain.LocalChart.Input;

static class ResamplerTests
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS " : "  FAIL ") + what);
        if (!ok) failures++;
    }

    static int Main(string[] args)
    {
        string golden = args.Length > 0 ? args[0]
            : Path.Combine(Environment.GetEnvironmentVariable("ROOT") ?? ".", "Tests", "LocalChart", "golden", "input");

        Console.WriteLine("[determinism]");
        TestDeterminism();
        Console.WriteLine("[length formula]");
        TestLength(golden);
        Console.WriteLine("[frequency preservation]");
        TestSine();
        Console.WriteLine("[downmix]");
        TestDownmix();
        Console.WriteLine("[ffmpeg parity] " + golden);
        TestGolden(golden);

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    // ───────── 결정성 ─────────

    static float[] Noise(int n, int seed)
    {
        var rng = new Random(seed);
        var x = new float[n];
        for (int i = 0; i < n; i++) x[i] = (float)(rng.NextDouble() * 2 - 1) * 0.5f;
        return x;
    }

    static bool BitEqual(float[] a, float[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false;
        return true;
    }

    static void TestDeterminism()
    {
        float[] x = Noise(48000 * 2, 42);
        float[] a = SwrResampler.Resample(x, 48000, 22050);
        float[] b = SwrResampler.Resample(x, 48000, 22050);
        Check(BitEqual(a, b), "same input twice → bit-identical");

        // 여러 스레드에서 동시에 (static 가변 상태 없음)
        var results = new float[8][];
        Parallel.For(0, results.Length, k =>
            results[k] = SwrResampler.Resample(x, k % 2 == 0 ? 48000 : 44100, 22050));
        bool ok = true;
        float[] r44 = SwrResampler.Resample(x, 44100, 22050);
        for (int k = 0; k < results.Length; k++) ok &= BitEqual(results[k], k % 2 == 0 ? a : r44);
        Check(ok, "8 parallel threads → bit-identical to serial");

        float[] st = Noise(44100 * 2, 7);
        Check(BitEqual(AudioDownmix.ToMono(st, 2), AudioDownmix.ToMono(st, 2)), "downmix deterministic");
        Check(BitEqual(SwrResampler.Resample(x, 22050, 22050), x), "same rate → copy");
    }

    // ───────── 길이 ─────────

    // 길이 공식 (SwrResampler.OutputLength): 거울 패딩 후 sampleIndex + L <= avail 인 위치 수.
    // 비율 <= 2이면 ceil(N * out / in)과 같고, 더 크게 줄이면 끝 거울이 짧아져 1개 적을 수 있다.
    // 정답은 ffmpeg이 실제로 낸 개수(lengths.txt)다.
    static void TestLength(string dir)
    {
        string[] rows = File.ReadAllLines(Path.Combine(dir, "lengths.txt"));
        int checkedRows = 0, bad = 0, ceilMatches = 0;
        foreach (string row in rows)
        {
            if (row.StartsWith("#") || row.Trim().Length == 0) continue;
            string[] f = row.Split(' ');
            int r = int.Parse(f[0]), n = int.Parse(f[1]), expected = int.Parse(f[2]);
            if (n <= SwrResampler.FilterLength(r, 22050))
            {
                // 필터보다 짧은 입력은 swr가 다른 경로(초기 버퍼 대기)를 탄다. 패리티 대상 아님
                Console.WriteLine($"    skip {r} Hz N={n} (<= filter length {SwrResampler.FilterLength(r, 22050)})");
                continue;
            }
            float[] y = SwrResampler.Resample(Noise(n, n), r, 22050);
            int predicted = SwrResampler.OutputLength(n, r, 22050);
            checkedRows++;
            if (y.Length != expected || predicted != expected)
            {
                bad++;
                Console.WriteLine($"    {r} Hz N={n}: Resample {y.Length}, OutputLength {predicted}, ffmpeg {expected}");
            }
            if (((long)n * 22050 + r - 1) / r == expected) ceilMatches++;
        }
        Check(checkedRows > 0 && bad == 0, $"Resample().Length == OutputLength() == ffmpeg for {checkedRows} (rate, N) pairs" +
                                           $" (ceil(N*out/in) alone matches {ceilMatches})");
    }

    // ───────── 주파수 보존 ─────────

    static float[] Sine(double hz, int rate, double seconds, double amp = 0.5)
    {
        int n = (int)Math.Round(rate * seconds);
        var x = new float[n];
        for (int i = 0; i < n; i++) x[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate));
        return x;
    }

    // 단일 주파수 DFT 진폭 (Goertzel과 같은 값, Hann 창)
    static double Tone(float[] y, double hz, int rate, int from, int len)
    {
        double re = 0, im = 0, wsum = 0;
        for (int i = 0; i < len; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / len);
            double ph = 2 * Math.PI * hz * (from + i) / rate;
            re += w * y[from + i] * Math.Cos(ph);
            im -= w * y[from + i] * Math.Sin(ph);
            wsum += w;
        }
        return 2 * Math.Sqrt(re * re + im * im) / wsum;
    }

    static int ZeroCrossings(float[] y, int from, int len)
    {
        int c = 0;
        for (int i = from + 1; i < from + len; i++)
            if ((y[i - 1] < 0) != (y[i] < 0)) c++;
        return c;
    }

    static void TestSine()
    {
        foreach (int rate in new[] { 44100, 48000, 16000 })
        foreach (double hz in new[] { 440.0, 1000.0, 5000.0 })
        {
            float[] y = SwrResampler.Resample(Sine(hz, rate, 2.0), rate, 22050);
            int from = 2205, len = 22050; // 가장자리 거울 구간을 피한 1초
            double amp = Tone(y, hz, 22050, from, len);
            double off = Tone(y, hz * 1.1, 22050, from, len);
            int zc = ZeroCrossings(y, from, len);
            double expectZc = 2 * hz * len / 22050.0;
            bool ok = Math.Abs(amp - 0.5) < 0.005 && off < 1e-3 && Math.Abs(zc - expectZc) <= 2;
            Check(ok, $"{rate}→22050 {hz} Hz: amp {amp:F5} (0.5), off-bin {off:E1}, zero-crossings {zc} (~{expectZc:F0})");
        }
        // Nyquist(11025) 위 성분은 걸러져야 한다. swr 기본(cutoff 0.97)은 전이 대역이 Nyquist 위로 걸쳐 있어
        // 12 kHz는 ffmpeg도 -36.5 dB만 줄인다(측정). 13 kHz부터는 ffmpeg -91.5 dB.
        double Alias(double hz)
        {
            float[] hi = SwrResampler.Resample(Sine(hz, 48000, 2.0), 48000, 22050);
            return 20 * Math.Log10(Tone(hi, 22050 - hz, 22050, 2205, 22050) / 0.5 + 1e-30);
        }
        double a12 = Alias(12000), a13 = Alias(13000);
        Check(Math.Abs(a12 - -36.49) < 0.1, $"48000 12 kHz alias: {a12:F2} dB (ffmpeg -36.49 dB)");
        Check(a13 < -85, $"48000 13 kHz alias: {a13:F1} dB (ffmpeg -91.5 dB, < -85 dB)");
    }

    // ───────── 다운믹스 ─────────

    static void TestDownmix()
    {
        float[] st = { 0.5f, 0.25f, -1f, 1f };
        float[] m = AudioDownmix.ToMono(st, 2);
        float h = (float)Math.Sqrt(0.5);
        Check(m.Length == 2 && m[0] == 0.5f * h + 0.25f * h && m[1] == 0f, $"stereo = (L+R)*sqrt(1/2): {m[0]:R}");
        float[] s51 = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f };
        float v = AudioDownmix.ToMono(s51, 6)[0];
        // ffmpeg -ac 1 on the same 5.1 frame printed 1.0621321
        Check(Math.Abs(v - 1.0621321f) < 1e-6f, $"5.1 = sqrt(1/2)(FL+FR) + FC + 0.5(SL+SR), LFE dropped: {v:R}");
        Check(AudioDownmix.ToMono(new float[] { 1, 2, 3 }, 1).SequenceEqual(new float[] { 1, 2, 3 }), "mono passthrough");
    }

    // ───────── ffmpeg golden ─────────

    static void TestGolden(string dir)
    {
        string json = File.ReadAllText(Path.Combine(dir, "cases.json"));
        var cases = Regex.Matches(json,
            "\"name\":\\s*\"(?<n>[^\"]+)\",\\s*\"rate\":\\s*(?<r>\\d+),\\s*\"channels\":\\s*(?<c>\\d+)");
        Check(cases.Count > 0, $"{cases.Count} golden cases");
        foreach (Match c in cases)
        {
            string name = c.Groups["n"].Value;
            int rate = int.Parse(c.Groups["r"].Value), ch = int.Parse(c.Groups["c"].Value);
            byte[] inBytes = File.ReadAllBytes(Path.Combine(dir, name + ".in.s16"));
            var x = new float[inBytes.Length / 2];
            for (int i = 0; i < x.Length; i++) x[i] = BitConverter.ToInt16(inBytes, 2 * i) / 32768f;
            byte[] outBytes = File.ReadAllBytes(Path.Combine(dir, name + ".out.f32"));
            var g = new float[outBytes.Length / 4];
            Buffer.BlockCopy(outBytes, 0, g, 0, outBytes.Length);

            float[] mono = AudioDownmix.ToMono(x, ch);
            // 참고: arm64 ffmpeg 9.0.1의 stereo 다운믹스는 fma(L, c, R*c)였다 (clang 축약). 그렇게 흉내 내면
            // synth_22050_st가 100% 일치하지만, 서버(x86)와 Unity(IL2CPP)는 다를 수 있어 C 의미(곱 각각 반올림)를 쓴다.
            float[] y = SwrResampler.Resample(mono, rate, 22050);

            int n = Math.Min(y.Length, g.Length);
            int exact = 0; double maxAbs = 0, sumSq = 0, sigSq = 0; long maxUlp = 0; int worst = -1;
            for (int i = 0; i < n; i++)
            {
                double d = Math.Abs((double)y[i] - g[i]);
                if (d == 0) exact++;
                if (d > maxAbs) { maxAbs = d; worst = i; }
                sumSq += d * d; sigSq += (double)g[i] * g[i];
                long ulp = Math.Abs((long)BitConverter.SingleToInt32Bits(y[i]) - BitConverter.SingleToInt32Bits(g[i]));
                if (Math.Sign(y[i]) == Math.Sign(g[i]) && ulp > maxUlp) maxUlp = ulp;
            }
            double snr = sigSq > 0 && sumSq > 0 ? 10 * Math.Log10(sigSq / sumSq) : double.PositiveInfinity;
            bool ok = y.Length == g.Length && maxAbs < 1e-5;
            Check(ok, $"{name,-18} len {y.Length}/{g.Length}  exact {exact}/{n} ({100.0 * exact / Math.Max(n, 1):F1}%)  " +
                      $"max|d| {maxAbs:E2} @{worst}  maxUlp {maxUlp}  err SNR {snr:F1} dB");
        }
    }
}
