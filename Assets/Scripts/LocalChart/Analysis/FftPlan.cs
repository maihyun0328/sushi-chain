// ─────────────────────────────────────────────────────────────
// 복소 FFT (double). 혼합 기수(4, 2, 3, 5, 일반 소수) 재귀 Cooley-Tukey.
// scipy.fft(pocketfft)와 비트 단위로 같지는 않다. 오차는 double ulp 수준이고,
// 서버가 결과를 float32로 내리는 지점(STFT)에서 대부분 사라진다. 측정값은 Tests/LocalChart 참고.
// 인스턴스는 스크래치 버퍼를 가지므로 스레드마다 따로 만든다 (정적 상태 없음).
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart.Analysis
{
    internal sealed class FftPlan
    {
        public readonly int N;
        private readonly int[] _factors;
        private readonly double[] _cos; // cos(2*pi*k/N)
        private readonly double[] _sin; // sin(2*pi*k/N)
        private readonly double[] _tr, _ti, _ur, _ui;

        public FftPlan(int n)
        {
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
            N = n;
            var f = new List<int>();
            int m = n;
            while (m % 4 == 0) { f.Add(4); m /= 4; }
            while (m % 2 == 0) { f.Add(2); m /= 2; }
            for (int d = 3; d * d <= m; d += 2)
                while (m % d == 0) { f.Add(d); m /= d; }
            if (m > 1) f.Add(m);
            if (f.Count == 0) f.Add(1);
            _factors = f.ToArray();
            _cos = new double[n];
            _sin = new double[n];
            // Accurate twiddles: reduce the angle to the first octant and use symmetry.
            for (int k = 0; k < n; k++)
            {
                TwiddleExact(k, n, out _cos[k], out _sin[k]);
            }
            int maxP = 1;
            foreach (int p in _factors) maxP = Math.Max(maxP, p);
            _tr = new double[maxP];
            _ti = new double[maxP];
            _ur = new double[maxP];
            _ui = new double[maxP];
        }

        private static void TwiddleExact(long k, long n, out double c, out double s)
        {
            // angle = 2*pi*k/n; use 8-fold symmetry so the argument passed to cos/sin is in [0, pi/4].
            long k8 = k * 8;
            long octant = k8 / n;           // 0..7
            long rem = k8 - octant * n;     // in [0, n)
            // Work with t = k/n in [0,1): map to x in [0, 1/8] (fraction of a full turn).
            double x;
            long o = octant;
            if ((o & 1) == 0) x = (double)rem / (8.0 * n);
            else x = (double)(n - rem) / (8.0 * n);
            double a = 2.0 * Math.PI * x;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            switch (o)
            {
                case 0: c = ca; s = sa; break;
                case 1: c = sa; s = ca; break;
                case 2: c = -sa; s = ca; break;
                case 3: c = -ca; s = sa; break;
                case 4: c = -ca; s = -sa; break;
                case 5: c = -sa; s = -ca; break;
                case 6: c = sa; s = -ca; break;
                default: c = ca; s = -sa; break;
            }
        }

        // Forward DFT: X[k] = sum_t x[t] * exp(-2*pi*i*k*t/N). inverse=true uses exp(+...), unscaled.
        public void Transform(double[] inRe, double[] inIm, double[] outRe, double[] outIm, bool inverse)
        {
            Rec(inRe, inIm, 0, 1, outRe, outIm, 0, N, 0, inverse);
        }

        private void Rec(double[] ir, double[] ii, int inOff, int stride, double[] or, double[] oi, int outOff, int n, int fi, bool inv)
        {
            int p = _factors[fi];
            int m = n / p;
            if (m == 1)
            {
                for (int j = 0; j < p; j++)
                {
                    _tr[j] = ir[inOff + j * stride];
                    _ti[j] = ii[inOff + j * stride];
                }
                SmallDft(p, or, oi, outOff, 1, inv);
                return;
            }
            for (int j = 0; j < p; j++)
                Rec(ir, ii, inOff + j * stride, stride * p, or, oi, outOff + j * m, m, fi + 1, inv);
            int tw = N / n; // twiddle stride for size n
            double sign = inv ? 1.0 : -1.0;
            for (int k = 0; k < m; k++)
            {
                for (int j = 0; j < p; j++)
                {
                    double xr = or[outOff + j * m + k], xi = oi[outOff + j * m + k];
                    if (j == 0 || k == 0)
                    {
                        _tr[j] = xr; _ti[j] = xi;
                    }
                    else
                    {
                        int e = (j * k * tw) % N;
                        double wr = _cos[e], wi = sign * _sin[e];
                        _tr[j] = xr * wr - xi * wi;
                        _ti[j] = xr * wi + xi * wr;
                    }
                }
                SmallDft(p, or, oi, outOff + k, m, inv);
            }
        }

        // DFT of size p over _tr/_ti, written to o[off + q*step].
        private void SmallDft(int p, double[] or, double[] oi, int off, int step, bool inv)
        {
            switch (p)
            {
                case 1:
                    or[off] = _tr[0]; oi[off] = _ti[0];
                    return;
                case 2:
                {
                    double ar = _tr[0], ai = _ti[0], br = _tr[1], bi = _ti[1];
                    or[off] = ar + br; oi[off] = ai + bi;
                    or[off + step] = ar - br; oi[off + step] = ai - bi;
                    return;
                }
                case 4:
                {
                    double ar = _tr[0], ai = _ti[0], br = _tr[1], bi = _ti[1];
                    double cr = _tr[2], ci = _ti[2], dr = _tr[3], di = _ti[3];
                    double t0r = ar + cr, t0i = ai + ci, t1r = ar - cr, t1i = ai - ci;
                    double t2r = br + dr, t2i = bi + di, t3r = br - dr, t3i = bi - di;
                    // forward: multiply t3 by -i; inverse: by +i
                    double r3r, r3i;
                    if (inv) { r3r = -t3i; r3i = t3r; } else { r3r = t3i; r3i = -t3r; }
                    or[off] = t0r + t2r; oi[off] = t0i + t2i;
                    or[off + step] = t1r + r3r; oi[off + step] = t1i + r3i;
                    or[off + 2 * step] = t0r - t2r; oi[off + 2 * step] = t0i - t2i;
                    or[off + 3 * step] = t1r - r3r; oi[off + 3 * step] = t1i - r3i;
                    return;
                }
                default:
                {
                    int tw = N / p;
                    double sign = inv ? 1.0 : -1.0;
                    for (int q = 0; q < p; q++)
                    {
                        double sr = 0, si = 0;
                        for (int j = 0; j < p; j++)
                        {
                            int e = ((j * q) % p) * tw;
                            double wr = _cos[e], wi = sign * _sin[e];
                            sr += _tr[j] * wr - _ti[j] * wi;
                            si += _tr[j] * wi + _ti[j] * wr;
                        }
                        _ur[q] = sr; _ui[q] = si;
                    }
                    for (int q = 0; q < p; q++)
                    {
                        or[off + q * step] = _ur[q];
                        oi[off + q * step] = _ui[q];
                    }
                    return;
                }
            }
        }
    }
}
