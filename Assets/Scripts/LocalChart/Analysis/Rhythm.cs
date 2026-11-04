// ─────────────────────────────────────────────────────────────
// 템포/비트/온셋:
//   analysis._estimate_tempo  (librosa.feature.tempogram 청크 평균 + librosa.feature.tempo 로그정규 prior)
//   librosa.beat.beat_track   (__normalize_onsets, __beat_local_score, __beat_track_dp, __last_beat,
//                              __dp_backtrack, __trim_beats; numba 커널의 dtype/반올림 그대로)
//   librosa.onset.onset_detect / util.peak_pick (greedy)
//   analysis._fold_tempo / _refine_tempo / _peak_near / _downbeat_phase
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart.Analysis
{
    internal static class Rhythm
    {
        public const double TempoMin = 70.0;
        public const double TempoMax = 200.0;
        public const double TempoStartBpm = 120.0;
        public const double BeatTightness = 100.0;
        public const double TempogramSeconds = 8.0;

        public static int TempogramWin(int sr, int hop) => (int)(long)(TempogramSeconds * sr) / hop; // time_to_frames

        // ---- tempo ---------------------------------------------------------------------

        public static bool Any(float[] x)
        {
            for (int i = 0; i < x.Length; i++) if (x[i] != 0f) return true;
            return false;
        }

        // Returns raw tempo (not folded); tgMean / objective are outputs for diagnostics.
        public static double EstimateTempo(float[] env, int sr, int hop, int chunkFrames, out double[] tgMean, out double[] objective)
        {
            tgMean = null;
            objective = null;
            if (!Any(env)) return 0.0;
            int win = TempogramWin(sr, hop);
            int n = env.Length;
            int half = win / 2;
            // np.pad(mode="linear_ramp", end_values=0): linspace computed in float64, cast to float32
            var padded = new float[n + 2 * half];
            double stepL = (double)env[0] / half;
            double stepR = (double)env[n - 1] / half;
            for (int j = 0; j < half; j++) padded[j] = (float)(j * stepL);
            Array.Copy(env, 0, padded, half, n);
            for (int j = 0; j < half; j++) padded[half + n + j] = (float)((half - 1 - j) * stepR);

            var window = NumpyCompat.HannPeriodic(win);
            int nPad = NextFastLenReal(2 * win - 1);
            double fct = 1.0 / nPad;
            var total = new double[win];
            for (int start = 0; start < n; start += chunkFrames)
            {
                int stop = Math.Min(n, start + chunkFrames);
                int len = stop - start;
                var block = new double[(long)win * len]; // block[lag * len + j]
                int chunkStart = start;
                // 프레임마다 독립 (autocorrelate → max-norm → block의 서로 다른 열에 기록)
                ParallelRange.For(start, stop, () => new TempoScratch(nPad, win), (t, w) =>
                {
                    double[] re = w.Re, im = w.Im, oRe = w.OutRe, oIm = w.OutIm, col = w.Col;
                    for (int i = 0; i < nPad; i++) { re[i] = 0.0; im[i] = 0.0; }
                    for (int i = 0; i < win; i++) re[i] = padded[t + i] * window[i];
                    w.Plan.Transform(re, im, oRe, oIm, false);
                    for (int k = 0; k < nPad; k++)
                    {
                        double p = oRe[k] * oRe[k] + oIm[k] * oIm[k];
                        re[k] = p;
                        im[k] = 0.0;
                    }
                    // irfft uses only bins 0..nPad/2 (Hermitian); the power spectrum is symmetric already
                    for (int k = nPad / 2 + 1; k < nPad; k++) re[k] = re[nPad - k];
                    w.Plan.Transform(re, im, oRe, oIm, true);
                    double mx = 0.0;
                    for (int lag = 0; lag < win; lag++)
                    {
                        double v = oRe[lag] * fct;
                        col[lag] = v;
                        double a = Math.Abs(v);
                        if (lag == 0 || a > mx) mx = a;
                    }
                    if (mx < NumpyCompat.Float64Tiny) mx = 1.0;
                    int j = t - chunkStart;
                    for (int lag = 0; lag < win; lag++) block[(long)lag * len + j] = col[lag] / mx;
                });
                for (int lag = 0; lag < win; lag++)
                    total[lag] += NumpyCompat.PairwiseSum64(block, lag * len, len, 1);
            }
            tgMean = new double[win];
            for (int lag = 0; lag < win; lag++) tgMean[lag] = total[lag] / n;

            // librosa.feature.tempo(tg=tg_mean, start_bpm=120, std_bpm=1, max_tempo=320)
            var bpms = TempoFrequencies(win, hop, sr);
            int maxIdx = 0;
            for (int i = 0; i < win; i++) if (bpms[i] < 320.0) { maxIdx = i; break; }
            double log2Start = NumpyCompat.Log2(TempoStartBpm);
            objective = new double[win];
            for (int i = 0; i < win; i++)
            {
                double lp;
                if (i < maxIdx) lp = double.NegativeInfinity;
                else
                {
                    double d = (NumpyCompat.Log2(bpms[i]) - log2Start) / 1.0;
                    lp = -0.5 * (d * d);
                }
                double o = NumpyCompat.Log1p(1e6 * tgMean[i]) + lp;
                objective[i] = o;
            }
            // np.argmax: first maximum (NaN cannot occur here)
            int best = 0;
            for (int i = 1; i < win; i++) if (objective[i] > objective[best]) best = i;
            return bpms[best];
        }

        sealed class TempoScratch
        {
            public readonly FftPlan Plan;
            public readonly double[] Re, Im, OutRe, OutIm, Col;
            public TempoScratch(int nPad, int win)
            {
                Plan = new FftPlan(nPad);
                Re = new double[nPad]; Im = new double[nPad]; OutRe = new double[nPad]; OutIm = new double[nPad];
                Col = new double[win];
            }
        }

        public static double[] TempoFrequencies(int nBins, int hop, int sr)
        {
            var b = new double[nBins];
            b[0] = double.PositiveInfinity;
            for (int i = 1; i < nBins; i++) b[i] = 60.0 * sr / (hop * (double)i);
            return b;
        }

        // scipy.fft.next_fast_len(target, real=True): smallest 2^a 3^b 5^c >= target.
        public static int NextFastLenReal(int target)
        {
            if (target <= 6) return target;
            long best = long.MaxValue;
            for (long p5 = 1; p5 < 2L * target; p5 *= 5)
                for (long p35 = p5; p35 < 2L * target; p35 *= 3)
                {
                    long v = p35;
                    while (v < target) v *= 2;
                    if (v < best) best = v;
                }
            return (int)best;
        }

        public static double FoldTempo(double bpm)
        {
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0) return 0.0;
            while (bpm < TempoMin) bpm *= 2.0;
            while (bpm > TempoMax) bpm /= 2.0;
            return bpm;
        }

        // ---- beat tracking ----------------------------------------------------------------

        public sealed class BeatTrace
        {
            public double FramesPerBeat;
            public float[] NormEnv;
            public float[] LocalScore;
            public double[] CumScore;
            public int[] Backlink;
            public int Tail;
            public int[] UntrimmedFrames;
        }

        public static int[] BeatTrack(float[] env, int sr, int hop, double bpm, double tightness, BeatTrace trace)
        {
            if (!Any(env)) return new int[0];
            int n = env.Length;
            double frameRate = (double)sr / hop;
            double fpb = Math.Round(frameRate * 60.0 / bpm, MidpointRounding.ToEven);

            // __normalize_onsets: env / (std(ddof=1) + tiny), float32
            float mean = (float)((double)NumpyCompat.PairwiseSum32(env, 0, n, 1) / n);
            var sq = new float[n];
            for (int i = 0; i < n; i++) { float d = (float)(env[i] - mean); sq[i] = (float)(d * d); }
            float var1 = (float)((double)NumpyCompat.PairwiseSum32(sq, 0, n, 1) / Math.Max(n - 1, 0));
            float std = (float)Math.Sqrt(var1);
            float denom = (float)(std + NumpyCompat.Float32Tiny);
            var norm = new float[n];
            for (int i = 0; i < n; i++) norm[i] = (float)(env[i] / denom);

            // __beat_local_score (static tempo): float32 accumulator, float64 window
            int K = (int)(2 * fpb + 1);
            var window = new double[K];
            for (int k = 0; k < K; k++)
            {
                double a = -fpb + k;
                double v = a * 32.0 / fpb;
                window[k] = Math.Exp(-0.5 * (v * v));
            }
            var local = new float[n];
            int halfK = K / 2;
            for (int i = 0; i < n; i++)
            {
                float acc = 0f;
                int k0 = Math.Max(0, i + halfK - n + 1), k1 = Math.Min(i + halfK, K);
                for (int k = k0; k < k1; k++) acc = (float)((double)acc + window[k] * norm[i + halfK - k]);
                local[i] = acc;
            }

            // __beat_track_dp (float64 loop: localscore cast to float64, tightness float32)
            var cum = new double[n];
            var back = new int[n];
            double lmax = double.NegativeInfinity;
            for (int i = 0; i < n; i++) if (local[i] > lmax) lmax = local[i];
            double scoreThresh = 0.01 * lmax;
            bool firstBeat = true;
            double tight = (float)tightness;
            int halfStep = (int)Math.Round(fpb / 2, MidpointRounding.ToEven);
            double logFpb = Math.Log(fpb);
            back[0] = -1;
            cum[0] = local[0];
            for (int i = 0; i < n; i++)
            {
                double scoreI = local[i];
                double bestScore = double.NegativeInfinity;
                int beatLoc = -1;
                int lo = i - halfStep;
                int hiExcl = (int)(i - 2 * fpb - 1); // int() truncates toward zero
                for (int loc = lo; loc > hiExcl; loc--)
                {
                    if (loc < 0) break;
                    double d = Math.Log(i - loc) - logFpb;
                    double score = cum[loc] - tight * (d * d);
                    if (score > bestScore) { bestScore = score; beatLoc = loc; }
                }
                cum[i] = beatLoc >= 0 ? scoreI + bestScore : scoreI;
                if (firstBeat && scoreI < scoreThresh) back[i] = -1;
                else { back[i] = beatLoc; firstBeat = false; }
            }

            // __last_beat: median of local maxima of cumscore
            var isMax = new bool[n];
            for (int i = 1; i < n - 1; i++) isMax[i] = cum[i] > cum[i - 1] && cum[i] >= cum[i + 1];
            if (n >= 2) isMax[n - 1] = cum[n - 1] > cum[n - 2];
            var vals = new List<double>();
            for (int i = 0; i < n; i++) if (isMax[i]) vals.Add(cum[i]);
            double threshold;
            if (vals.Count == 0) threshold = double.NaN;
            else
            {
                vals.Sort();
                int c = vals.Count, h = c / 2;
                int l = (c % 2 == 1) ? h : h - 1;
                double med = (vals[l] + vals[h]) / 2.0;
                threshold = 0.5 * med;
            }
            int tail = n - 1;
            for (int t = n - 1; t >= 0; t--)
            {
                if (isMax[t] && cum[t] >= threshold) { tail = t; break; }
            }
            var beats = new bool[n];
            for (int t = tail; t >= 0; t = back[t]) beats[t] = true;

            if (trace != null)
            {
                trace.FramesPerBeat = fpb;
                trace.NormEnv = norm;
                trace.LocalScore = local;
                trace.CumScore = cum;
                trace.Backlink = back;
                trace.Tail = tail;
                trace.UntrimmedFrames = Indices(beats);
            }

            // __trim_beats(trim=True)
            var bl = new List<float>();
            for (int i = 0; i < n; i++) if (beats[i]) bl.Add(local[i]);
            int nb = bl.Count;
            // np.convolve(localscore[beats], hanning(5)) full, then [2 : n + 2]
            double[] w = { 0.0, 0.5, 1.0, 0.5, 0.0 };
            int fullLen = nb + 4;
            int s0 = 2, s1 = Math.Min(fullLen, n + 2);
            double sumSq = 0.0;
            int cnt = 0;
            for (int idx = s0; idx < s1; idx++)
            {
                double acc = 0.0;
                for (int j = 0; j < 5; j++)
                {
                    int src = idx - j;
                    if (src >= 0 && src < nb) acc += bl[src] * w[j];
                }
                sumSq += acc * acc;
                cnt++;
            }
            double thr = 0.5 * Math.Pow(sumSq / cnt, 0.5);
            var trimmed = (bool[])beats.Clone();
            int a0 = 0;
            while (a0 < n && local[a0] <= thr) { trimmed[a0] = false; a0++; }
            int a1 = n - 1;
            while (a1 >= 0 && local[a1] <= thr) { trimmed[a1] = false; a1--; }
            return Indices(trimmed);
        }

        private static int[] Indices(bool[] b)
        {
            var l = new List<int>();
            for (int i = 0; i < b.Length; i++) if (b[i]) l.Add(i);
            return l.ToArray();
        }

        public static double RefineTempo(double[] beatTimes, double fallback)
        {
            if (beatTimes.Length < 8) return fallback;
            var ibi = new double[beatTimes.Length - 1];
            for (int i = 0; i < ibi.Length; i++) ibi[i] = beatTimes[i + 1] - beatTimes[i];
            double med = NumpyCompat.Median64(ibi);
            if (med <= 0) return fallback;
            var good = new List<double>();
            foreach (double v in ibi) if (Math.Abs(v - med) <= 0.15 * med) good.Add(v);
            if (good.Count < 4) return fallback;
            var g = good.ToArray();
            double mean = NumpyCompat.PairwiseSum64(g, 0, g.Length, 1) / g.Length;
            return FoldTempo(60.0 / mean);
        }

        // ---- onsets ---------------------------------------------------------------------

        // librosa.onset.onset_detect(onset_envelope=env, normalize=False, backtrack=False, units="frames")
        public static int[] OnsetDetect(float[] env, int sr, int hop)
        {
            if (!Any(env)) return new int[0];
            for (int i = 0; i < env.Length; i++) if (float.IsNaN(env[i]) || float.IsInfinity(env[i])) return new int[0];
            int preMax = (int)Math.Ceiling(NumpyCompat.FloorDiv(0.03 * sr, hop));
            int postMax = (int)Math.Ceiling(NumpyCompat.FloorDiv(0.00 * sr, hop) + 1);
            int preAvg = (int)Math.Ceiling(NumpyCompat.FloorDiv(0.10 * sr, hop));
            int postAvg = (int)Math.Ceiling(NumpyCompat.FloorDiv(0.10 * sr, hop) + 1);
            int wait = (int)Math.Ceiling(NumpyCompat.FloorDiv(0.03 * sr, hop));
            const double delta = 0.07;
            return PeakPickGreedy(env, preMax, postMax, preAvg, postAvg, delta, wait);
        }

        // __peak_pick_greedy (numba): np.max / np.mean over slices, mean with a float32 accumulator.
        public static int[] PeakPickGreedy(float[] x, int preMax, int postMax, int preAvg, int postAvg, double delta, int wait)
        {
            int n = x.Length;
            var peaks = new bool[n];
            peaks[0] = x[0] >= Max(x, 0, Math.Min(postMax, n));
            peaks[0] &= x[0] >= NumbaMean(x, 0, Math.Min(postAvg, n)) + delta;
            int i = peaks[0] ? wait + 1 : 1;
            while (i < n)
            {
                float maxn = Max(x, Math.Max(0, i - preMax), Math.Min(i + postMax, n));
                peaks[i] = x[i] == maxn;
                if (!peaks[i]) { i++; continue; }
                double avgn = NumbaMean(x, Math.Max(0, i - preAvg), Math.Min(i + postAvg, n));
                peaks[i] &= x[i] >= avgn + delta;
                if (!peaks[i]) { i++; continue; }
                i += wait + 1;
            }
            return Indices(peaks);
        }

        private static float Max(float[] x, int a, int b)
        {
            float m = x[a];
            for (int i = a + 1; i < b; i++) if (x[i] > m) m = x[i];
            return m;
        }

        // numba array.mean(): float32 accumulator, float64 division, result float32.
        private static float NumbaMean(float[] x, int a, int b)
        {
            float c = 0f;
            for (int i = a; i < b; i++) c = (float)(c + x[i]);
            return (float)((double)c / (b - a));
        }

        // ---- downbeat -------------------------------------------------------------------

        public static float[] PeakNear(float[] env, int[] frames, int radius = 2)
        {
            if (frames.Length == 0 || env.Length == 0) return new float[0];
            var o = new float[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                float m = float.NegativeInfinity;
                for (int d = -radius; d <= radius; d++)
                {
                    int j = Math.Min(Math.Max(frames[i] + d, 0), env.Length - 1);
                    float v = env[j];
                    if (d == -radius || v > m) m = v;
                }
                o[i] = m;
            }
            return o;
        }

        public static int DownbeatPhase(int[] beatFrames, float[] low, float[] mid, float[] harmonicChange, out double[] scores)
        {
            scores = new double[0];
            if (beatFrames.Length == 0) return 0;
            var a = PeakNear(low, beatFrames);
            var b = PeakNear(mid, beatFrames);
            var c = PeakNear(harmonicChange, beatFrames);
            var src = new float[beatFrames.Length];
            for (int i = 0; i < src.Length; i++) src[i] = (float)((float)(a[i] - b[i]) + c[i]);
            int phases = Math.Min(4, beatFrames.Length);
            scores = new double[phases];
            int best = 0;
            for (int p = 0; p < phases; p++)
            {
                int cnt = (src.Length - p + 3) / 4;
                scores[p] = NumpyCompat.Mean32(src, p, cnt, 4);
                if (scores[p] > scores[best]) best = p;
            }
            return best;
        }
    }
}
