// ─────────────────────────────────────────────────────────────
// analysis._hpss_features: 2x2 풀링 스펙트로그램에서 median filter(scipy.ndimage, mode="reflect") →
// softmask(power=2, split_zeros=False) → 2x2 업샘플 + edge 패딩 → harmonic/percussive 분리.
// harmonic RMS와 chroma(tuning=0)는 청크 안에서 바로 계산한다.
// _map_time_chunks와 같은 청크(CHUNK_FRAMES, context=27)로 처리한다. 청크 시작이 홀수면 풀링 짝이
// 달라지므로 청크 경계는 결과에 영향을 준다 (서버와 같은 경계를 써야 같은 값이 나온다).
// ─────────────────────────────────────────────────────────────
using System;

namespace SushiChain.LocalChart.Analysis
{
    internal static class Hpss
    {
        public const int KernelHarmonic = 23; // frames
        public const int KernelPercussive = 17; // bins
        public const int Context = KernelHarmonic + 4;
        private const int Bins = Spectral.Bins;

        public sealed class Result
        {
            public float[] Percussive;      // frame-major (F, 1025)
            public float[] HarmonicRmsRaw;  // (F,)
            public float[] Chroma;          // frame-major (F, 12)
            public float[] PooledFirstChunk; // single-chunk diagnostics: pooled (P, 512) frame-major
            public float[] MaskPooledFirstChunk;
            public int PooledFrames;
        }

        public static Result Run(float[] mag, int nFrames, int chunkFrames, float[] chromaBasis, bool keepDiagnostics)
        {
            var res = new Result
            {
                Percussive = new float[(long)nFrames * Bins],
                HarmonicRmsRaw = new float[nFrames],
                Chroma = new float[(long)nFrames * 12],
            };
            if (nFrames <= chunkFrames)
            {
                RunChunk(mag, 0, nFrames, 0, nFrames, chromaBasis, res, keepDiagnostics);
                return res;
            }
            for (int start = 0; start < nFrames; start += chunkFrames)
            {
                int stop = Math.Min(nFrames, start + chunkFrames);
                int lo = Math.Max(0, start - Context), hi = Math.Min(nFrames, stop + Context);
                RunChunk(mag, lo, hi, start, stop, chromaBasis, res, false);
            }
            return res;
        }

        // Processes chunk columns [lo, hi) and writes output columns [keepLo, keepHi).
        private static void RunChunk(float[] mag, int lo, int hi, int keepLo, int keepHi, float[] chromaBasis, Result res, bool diag)
        {
            int frames = hi - lo;
            int pb = Bins / 2 * 2, pf = frames / 2 * 2;
            int rows, cols, kh, kp, fy, fx;
            float[] low; // pooled, column-major by frame: low[c * rows + r]
            if (pf / 2 == 0 || pb / 2 == 0)
            {
                rows = Bins; cols = frames;
                low = new float[rows * cols];
                for (int c = 0; c < cols; c++)
                    Array.Copy(mag, (long)(lo + c) * Bins, low, (long)c * rows, rows);
                kh = KernelHarmonic; kp = KernelPercussive; fy = 1; fx = 1;
            }
            else
            {
                rows = pb / 2; cols = pf / 2;
                low = new float[rows * cols];
                for (int c = 0; c < cols; c++)
                {
                    int f0 = (lo + 2 * c) * Bins, f1 = f0 + Bins;
                    for (int r = 0; r < rows; r++)
                    {
                        float a = mag[f0 + 2 * r], b = mag[f1 + 2 * r];
                        float cc = mag[f0 + 2 * r + 1], d = mag[f1 + 2 * r + 1];
                        // reshape(pb/2, 2, pf/2, 2).mean(axis=(1, 3)): (a + b) + (c + d), / 4
                        float s = (float)((float)(a + b) + (float)(cc + d));
                        low[c * rows + r] = (float)((double)s / 4);
                    }
                }
                kh = Math.Max(3, (KernelHarmonic / 2) | 1);
                kp = Math.Max(3, (KernelPercussive / 2) | 1);
                fy = 2; fx = 2;
            }

            var hMed = MedianAlongCols(low, rows, cols, kh);
            var pMed = MedianAlongRows(low, rows, cols, kp);
            var mask = new float[rows * cols];
            for (int i = 0; i < mask.Length; i++) mask[i] = SoftMask(hMed[i], pMed[i]);
            if (diag)
            {
                res.PooledFirstChunk = low;
                res.MaskPooledFirstChunk = mask;
                res.PooledFrames = cols;
            }

            var harm = new float[Bins];
            var power = new float[Bins];
            for (int f = keepLo; f < keepHi; f++)
            {
                int cl = f - lo;
                int mc = Math.Min(cl / fx, cols - 1);
                int mo = (int)((long)f * Bins);
                for (int b = 0; b < Bins; b++)
                {
                    int mr = Math.Min(b / fy, rows - 1);
                    float m = mask[mc * rows + mr];
                    float v = mag[mo + b];
                    harm[b] = (float)(v * m);
                    res.Percussive[mo + b] = (float)(v * (float)(1f - m));
                }
                res.HarmonicRmsRaw[f] = Spectral.RmsFrame(harm, 0);
                for (int b = 0; b < Bins; b++) power[b] = (float)(harm[b] * harm[b]);
                // chroma_stft(S=harm**2, tuning=0): sgemm (sequential float32 FMA) + inf-norm per frame
                var raw = new float[12];
                double mx = 0.0;
                for (int c = 0; c < 12; c++)
                {
                    float acc = 0f;
                    int co = c * Bins;
                    for (int b = 0; b < Bins; b++) acc = NumpyCompat.Fma32(chromaBasis[co + b], power[b], acc);
                    raw[c] = acc;
                    double a = Math.Abs((double)acc);
                    if (c == 0 || a > mx) mx = a;
                }
                if (mx < NumpyCompat.Float32Tiny) mx = 1.0;
                int oc = f * 12;
                for (int c = 0; c < 12; c++) res.Chroma[oc + c] = (float)((double)raw[c] / mx);
            }
        }

        // librosa.util.softmask(X, X_ref, power=2, split_zeros=False) for one float32 element.
        public static float SoftMask(float x, float xRef)
        {
            float z = Math.Max(x, xRef);
            if (z < NumpyCompat.Float32Tiny) return 0f;
            float a = (float)(x / z);
            a = (float)(a * a);
            float r = (float)(xRef / z);
            r = (float)(r * r);
            return (float)(a / (float)(a + r));
        }

        // scipy 'reflect' extension (d c b a | a b c d | d c b a), periodic with period 2n.
        private static int Reflect(int i, int n)
        {
            if (i >= 0 && i < n) return i;
            int period = 2 * n;
            int m = i % period;
            if (m < 0) m += period;
            return m < n ? m : period - 1 - m;
        }

        // median_filter(size=(1, k)) on a (rows, cols) array stored column-major (x[c * rows + r]).
        private static float[] MedianAlongCols(float[] x, int rows, int cols, int k)
        {
            var y = new float[x.Length];
            var buf = new float[k];
            int h = k / 2;
            var idx = new int[cols + 2 * h];
            for (int j = 0; j < idx.Length; j++) idx[j] = Reflect(j - h, cols);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    for (int j = 0; j < k; j++) buf[j] = x[idx[c + j] * rows + r];
                    y[c * rows + r] = SelectMedian(buf, k);
                }
            }
            return y;
        }

        // median_filter(size=(k, 1)).
        private static float[] MedianAlongRows(float[] x, int rows, int cols, int k)
        {
            var y = new float[x.Length];
            var buf = new float[k];
            int h = k / 2;
            var idx = new int[rows + 2 * h];
            for (int j = 0; j < idx.Length; j++) idx[j] = Reflect(j - h, rows);
            for (int c = 0; c < cols; c++)
            {
                int o = c * rows;
                for (int r = 0; r < rows; r++)
                {
                    for (int j = 0; j < k; j++) buf[j] = x[o + idx[r + j]];
                    y[o + r] = SelectMedian(buf, k);
                }
            }
            return y;
        }

        private static float SelectMedian(float[] buf, int k)
        {
            // insertion sort (k <= 23); odd k so the median is an element (exact)
            for (int i = 1; i < k; i++)
            {
                float v = buf[i];
                int j = i - 1;
                while (j >= 0 && buf[j] > v) { buf[j + 1] = buf[j]; j--; }
                buf[j + 1] = v;
            }
            return buf[k / 2];
        }
    }
}
