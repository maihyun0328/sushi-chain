// ─────────────────────────────────────────────────────────────
// STFT 크기, RMS, 스펙트럼 중심, mel/chroma 필터뱅크, power_to_db, spectral flux, _robust_unit.
// 스펙트로그램 배열은 프레임 우선 배치다: S[t * bins + b].
// ─────────────────────────────────────────────────────────────
using System;

namespace SushiChain.LocalChart.Analysis
{
    internal static class Spectral
    {
        public const int NFft = 2048;
        public const int Bins = NFft / 2 + 1;
        private const float Eps32 = 1e-10f; // analysis._EPS as float32 (weak Python float vs float32 arrays)

        // analysis._stft_magnitude: |librosa.stft(x, n_fft=2048, hop, center=True, pad_mode="constant")| as float32.
        public static float[] StftMagnitude(float[] x, int hop, out int nFrames)
        {
            int half = NFft / 2;
            nFrames = 1 + x.Length / hop;
            var window = NumpyCompat.HannPeriodic(NFft);
            var plan = new FftPlan(NFft);
            var re = new double[NFft];
            var im = new double[NFft];
            var outRe = new double[NFft];
            var outIm = new double[NFft];
            var mag = new float[(long)nFrames * Bins];
            for (int t = 0; t < nFrames; t++)
            {
                int start = t * hop - half; // index into x of padded[t*hop]
                for (int i = 0; i < NFft; i++)
                {
                    int j = start + i;
                    float v = (j >= 0 && j < x.Length) ? x[j] : 0f;
                    re[i] = window[i] * v;
                    im[i] = 0.0;
                }
                plan.Transform(re, im, outRe, outIm, false);
                int o = t * Bins;
                for (int b = 0; b < Bins; b++)
                {
                    // complex128 -> complex64, then np.abs (hypotf) in float32
                    float r = (float)outRe[b], m = (float)outIm[b];
                    mag[o + b] = Hypot32(r, m);
                }
            }
            return mag;
        }

        // np.abs(complex64) as numpy's SIMD loop computes it (loops_unary_complex.dispatch.c.src):
        //   larger = max(|re|, |im|), smaller = min(...), ratio = smaller / larger (0 if larger == 0),
        //   |z| = sqrt(fma(ratio, ratio, 1)) * larger, all float32.
        public static float Hypot32(float a, float b)
        {
            float re = Math.Abs(a), im = Math.Abs(b);
            float larger = Math.Max(re, im), smaller = Math.Min(re, im);
            if (float.IsInfinity(larger)) return float.PositiveInfinity;
            float ratio = larger == 0f ? 0f : (float)(smaller / larger);
            float h = (float)Math.Sqrt(NumpyCompat.Fma32(ratio, ratio, 1f));
            return (float)(h * larger);
        }

        // librosa.feature.rms(S=S, frame_length=2048)[0] in float32 for one frame-major spectrogram.
        public static float[] RmsFromMagnitude(float[] S, int nFrames)
        {
            var outv = new float[nFrames];
            for (int t = 0; t < nFrames; t++) outv[t] = RmsFrame(S, t * Bins);
            return outv;
        }

        public static float RmsFrame(float[] S, int o)
        {
            float acc = (float)((float)(S[o] * S[o]) * 0.5f);
            for (int b = 1; b < Bins - 1; b++)
            {
                float v = S[o + b];
                acc = (float)(acc + (float)(v * v));
            }
            float last = S[o + Bins - 1];
            acc = (float)(acc + (float)((float)(last * last) * 0.5f));
            float power = (float)((float)(2f * acc) / 4194304f);
            return (float)Math.Sqrt(power);
        }

        // librosa.fft_frequencies(sr, n_fft) = np.fft.rfftfreq(n, 1/sr), cast to float32 by the server.
        public static float[] FftFrequencies32(int sr)
        {
            double d = 1.0 / sr;
            double val = 1.0 / (NFft * d);
            var f = new float[Bins];
            for (int k = 0; k < Bins; k++) f[k] = (float)(k * val);
            return f;
        }

        // Server centroid: where(sum > eps, (freqs @ mag) / max(sum, eps), 0) in float32.
        // freqs @ mag is an Accelerate sgemv; it is modelled as a sequential float32 FMA over bins
        // (bit-exact for Accelerate's 512-column blocks, may differ by ~1 ulp elsewhere).
        public static float[] Centroid(float[] mag, int nFrames, int sr, out float[] magSum, out float[] num)
        {
            var freqs = FftFrequencies32(sr);
            var c = new float[nFrames];
            magSum = new float[nFrames];
            num = new float[nFrames];
            for (int t = 0; t < nFrames; t++)
            {
                int o = t * Bins;
                float s = mag[o];
                for (int b = 1; b < Bins; b++) s = (float)(s + mag[o + b]);
                float n = 0f;
                for (int b = 0; b < Bins; b++) n = NumpyCompat.Fma32(freqs[b], mag[o + b], n);
                magSum[t] = s;
                num[t] = n;
                c[t] = s > Eps32 ? (float)(n / Math.Max(s, Eps32)) : 0f;
            }
            return c;
        }

        // ---- filterbanks ------------------------------------------------------------------

        private static double HzToMel(double f)
        {
            const double fSp = 200.0 / 3;
            double mels = (f - 0.0) / fSp;
            const double minLogHz = 1000.0;
            double minLogMel = (minLogHz - 0.0) / fSp;
            double logstep = Math.Log(6.4) / 27.0;
            if (f >= minLogHz) mels = minLogMel + Math.Log(f / minLogHz) / logstep;
            return mels;
        }

        private static double MelToHz(double m)
        {
            const double fSp = 200.0 / 3;
            double freqs = 0.0 + fSp * m;
            const double minLogHz = 1000.0;
            double minLogMel = (minLogHz - 0.0) / fSp;
            double logstep = Math.Log(6.4) / 27.0;
            if (m >= minLogMel) freqs = minLogHz * Math.Exp(logstep * (m - minLogMel));
            return freqs;
        }

        // librosa.mel_frequencies(n_mels, fmin=0, fmax, htk=False)
        public static double[] MelFrequencies(int nMels, double fmax)
        {
            var mels = NumpyCompat.Linspace(HzToMel(0.0), HzToMel(fmax), nMels, true);
            var hz = new double[nMels];
            for (int i = 0; i < nMels; i++) hz[i] = MelToHz(mels[i]);
            return hz;
        }

        // librosa.filters.mel(sr, n_fft, n_mels, fmax, norm="slaney") as float32, row-major (n_mels, bins).
        public static float[] MelBasis(int sr, int nMels, double fmax, out int[] first, out int[] last)
        {
            double d = 1.0 / sr;
            double val = 1.0 / (NFft * d);
            var fftfreqs = new double[Bins];
            for (int k = 0; k < Bins; k++) fftfreqs[k] = k * val;
            var melF = MelFrequencies(nMels + 2, fmax);
            var fdiff = new double[nMels + 1];
            for (int i = 0; i < nMels + 1; i++) fdiff[i] = melF[i + 1] - melF[i];
            var w = new float[nMels * Bins];
            first = new int[nMels];
            last = new int[nMels];
            for (int i = 0; i < nMels; i++)
            {
                double enorm = 2.0 / (melF[i + 2] - melF[i]);
                first[i] = -1;
                last[i] = -2;
                for (int k = 0; k < Bins; k++)
                {
                    double lower = -(melF[i] - fftfreqs[k]) / fdiff[i];
                    double upper = (melF[i + 2] - fftfreqs[k]) / fdiff[i + 1];
                    // weights[i] = np.maximum(0, np.minimum(lower, upper)) stored float32, then *= enorm (float32 * float64 -> float32)
                    float v = (float)Math.Max(0.0, Math.Min(lower, upper));
                    v = (float)(v * enorm);
                    w[i * Bins + k] = v;
                    if (v != 0f)
                    {
                        if (first[i] < 0) first[i] = k;
                        last[i] = k;
                    }
                }
            }
            return w;
        }

        // librosa.filters.chroma(sr, n_fft, tuning=0, n_chroma=12) as float32, row-major (12, bins).
        public static float[] ChromaBasis(int sr)
        {
            const int nChroma = 12;
            int nFft = NFft;
            var freqsAll = NumpyCompat.Linspace(0, sr, nFft, false);
            var frqbins = new double[nFft];
            double a440 = 440.0 * Math.Pow(2.0, 0.0 / nChroma);
            double refHz = a440 / 16;
            for (int i = 1; i < nFft; i++) frqbins[i] = nChroma * Log2Exact(freqsAll[i] / refHz);
            frqbins[0] = frqbins[1] - 1.5 * nChroma;
            var binwidth = new double[nFft];
            for (int i = 0; i < nFft - 1; i++) binwidth[i] = Math.Max(frqbins[i + 1] - frqbins[i], 1.0);
            binwidth[nFft - 1] = 1;
            double nChroma2 = Math.Round(nChroma / 2.0, MidpointRounding.ToEven);
            var wts = new double[nChroma * nFft];
            for (int c = 0; c < nChroma; c++)
            {
                for (int i = 0; i < nFft; i++)
                {
                    double D = frqbins[i] - c;
                    D = PyRemainder(D + nChroma2 + 10 * nChroma, nChroma) - nChroma2;
                    double z = 2 * D / binwidth[i];
                    wts[c * nFft + i] = Math.Exp(-0.5 * (z * z));
                }
            }
            // normalize(norm=2, axis=0): per column L2
            for (int i = 0; i < nFft; i++)
            {
                double ss = 0;
                // np.sum(mag**2, axis=0) over 12 rows: sequential (non-contiguous axis)
                ss = wts[i] * wts[i];
                for (int c = 1; c < nChroma; c++) ss += wts[c * nFft + i] * wts[c * nFft + i];
                double len = Math.Sqrt(ss); // ** (1.0 / norm) with norm=2 -> numpy fast path sqrt
                if (len < NumpyCompat.Float64Tiny) len = 1.0;
                for (int c = 0; c < nChroma; c++) wts[c * nFft + i] = wts[c * nFft + i] / len;
            }
            for (int i = 0; i < nFft; i++)
            {
                double t = (frqbins[i] / nChroma - 5.0) / 2;
                double g = Math.Exp(-0.5 * (t * t));
                for (int c = 0; c < nChroma; c++) wts[c * nFft + i] *= g;
            }
            // roll(-3) along chroma axis, keep the first 1 + n_fft/2 bins, float32
            var outv = new float[nChroma * Bins];
            for (int c = 0; c < nChroma; c++)
            {
                int src = (c + 3) % nChroma;
                for (int i = 0; i < Bins; i++) outv[c * Bins + i] = (float)wts[src * nFft + i];
            }
            return outv;
        }

        private static double Log2Exact(double x) => NumpyCompat.Log2(x);

        private static double PyRemainder(double a, double b)
        {
            double m = a % b;
            if (m != 0 && ((m < 0) != (b < 0))) m += b;
            return m;
        }

        // ---- log-mel + flux -------------------------------------------------------------

        // librosa.power_to_db(S, ref=max, amin=1e-10, top_db=80) for float32 S, in place.
        public static void PowerToDbInPlace(float[] S, double reference)
        {
            double refDb = 10.0 * Math.Log10(Math.Max(1e-10, Math.Abs(reference)));
            float maxv = float.NegativeInfinity;
            for (int i = 0; i < S.Length; i++)
            {
                float l = (float)(10f * MathF.Log10(Math.Max(1e-10f, S[i])));
                float v = (float)((double)l - refDb);
                S[i] = v;
                if (v > maxv) maxv = v;
            }
            float floor = (float)(maxv - 80f);
            for (int i = 0; i < S.Length; i++)
                if (S[i] < floor) S[i] = floor;
        }

        // analysis._spectral_flux over the rows listed in `rows` of a frame-major (rowsTotal, F) array.
        // pairwiseRows: the (rows, F) source array is Fortran-ordered in numpy (single-chunk chroma from
        // einsum), so diff.mean(axis=0) reduces along the contiguous axis with pairwise summation.
        public static float[] SpectralFlux(float[] S, int rowsTotal, int nFrames, int[] rows, int latency, bool pairwiseRows = false)
        {
            var flux = new float[nFrames];
            if (nFrames == 0) return flux;
            flux[0] = 0f;
            int nr = rows.Length;
            var dcol = new float[nr];
            for (int t = 1; t < nFrames; t++)
            {
                int o = t * rowsTotal, p = (t - 1) * rowsTotal;
                float acc = 0f;
                for (int i = 0; i < nr; i++)
                {
                    int r = rows[i];
                    float d = (float)(S[o + r] - S[p + r]);
                    if (!(d >= 0f)) d = d < 0f ? 0f : d; // np.maximum(diff, 0.0)
                    dcol[i] = d;
                    acc = i == 0 ? d : (float)(acc + d);
                }
                if (pairwiseRows) acc = NumpyCompat.PairwiseSum32(dcol, 0, nr, 1);
                flux[t] = (float)((double)acc / nr);
            }
            if (latency > 0)
            {
                for (int t = nFrames - 1; t >= latency; t--) flux[t] = flux[t - latency];
                for (int t = 0; t < Math.Min(latency, nFrames); t++) flux[t] = 0f;
            }
            return flux;
        }

        // analysis._robust_unit
        public static float[] RobustUnit(float[] x, double percentile)
        {
            if (x.Length == 0) return x;
            double scale = NumpyCompat.Percentile32(x, percentile);
            if (scale <= 1e-10)
            {
                float mx = x[0];
                for (int i = 1; i < x.Length; i++) if (x[i] > mx) mx = x[i];
                scale = mx;
            }
            var y = new float[x.Length];
            if (scale <= 1e-10) return y;
            float s32 = (float)scale;
            for (int i = 0; i < x.Length; i++)
            {
                float v = (float)(x[i] / s32);
                y[i] = v < 0f ? 0f : (v > 1f ? 1f : v);
            }
            return y;
        }
    }
}
