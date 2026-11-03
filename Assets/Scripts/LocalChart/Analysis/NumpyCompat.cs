// ─────────────────────────────────────────────────────────────
// numpy / numba 연산 순서와 반올림을 그대로 재현하는 헬퍼.
// float32 연산은 모두 (float) 캐스트로 매 단계 반올림을 강제한다 (Mono가 float을 double로 계산해도 같은 결과).
// 각 함수 주석에 대응하는 numpy 동작을 적었다. numpy 2.5 / numba 0.68 기준.
// ─────────────────────────────────────────────────────────────
using System;

namespace SushiChain.LocalChart.Analysis
{
    internal static class NumpyCompat
    {
        public const float Float32Tiny = 1.17549435e-38f;  // np.finfo(np.float32).tiny
        public const double Float64Tiny = 2.2250738585072014e-308;

        // ---- reductions ------------------------------------------------------------------

        // np.add.reduce on a contiguous (or strided) 1-D float32 array: numpy's pairwise_sum
        // (8 accumulators per block of <= 128, recursive halves rounded down to a multiple of 8).
        public static float PairwiseSum32(float[] a, int offset, int n, int stride)
        {
            if (n < 8)
            {
                float res = -0.0f;
                for (int i = 0; i < n; i++) res = (float)(res + a[offset + i * stride]);
                return res;
            }
            if (n <= 128)
            {
                float r0 = a[offset], r1 = a[offset + stride], r2 = a[offset + 2 * stride], r3 = a[offset + 3 * stride];
                float r4 = a[offset + 4 * stride], r5 = a[offset + 5 * stride], r6 = a[offset + 6 * stride], r7 = a[offset + 7 * stride];
                int i;
                int lim = n - (n % 8);
                for (i = 8; i < lim; i += 8)
                {
                    int o = offset + i * stride;
                    r0 = (float)(r0 + a[o]);
                    r1 = (float)(r1 + a[o + stride]);
                    r2 = (float)(r2 + a[o + 2 * stride]);
                    r3 = (float)(r3 + a[o + 3 * stride]);
                    r4 = (float)(r4 + a[o + 4 * stride]);
                    r5 = (float)(r5 + a[o + 5 * stride]);
                    r6 = (float)(r6 + a[o + 6 * stride]);
                    r7 = (float)(r7 + a[o + 7 * stride]);
                }
                float res = (float)((float)((float)(r0 + r1) + (float)(r2 + r3)) + (float)((float)(r4 + r5) + (float)(r6 + r7)));
                for (; i < n; i++) res = (float)(res + a[offset + i * stride]);
                return res;
            }
            int n2 = n / 2;
            n2 -= n2 % 8;
            return (float)(PairwiseSum32(a, offset, n2, stride) + PairwiseSum32(a, offset + n2 * stride, n - n2, stride));
        }

        public static double PairwiseSum64(double[] a, int offset, int n, int stride)
        {
            if (n < 8)
            {
                double res = -0.0;
                for (int i = 0; i < n; i++) res += a[offset + i * stride];
                return res;
            }
            if (n <= 128)
            {
                double r0 = a[offset], r1 = a[offset + stride], r2 = a[offset + 2 * stride], r3 = a[offset + 3 * stride];
                double r4 = a[offset + 4 * stride], r5 = a[offset + 5 * stride], r6 = a[offset + 6 * stride], r7 = a[offset + 7 * stride];
                int i;
                int lim = n - (n % 8);
                for (i = 8; i < lim; i += 8)
                {
                    int o = offset + i * stride;
                    r0 += a[o];
                    r1 += a[o + stride];
                    r2 += a[o + 2 * stride];
                    r3 += a[o + 3 * stride];
                    r4 += a[o + 4 * stride];
                    r5 += a[o + 5 * stride];
                    r6 += a[o + 6 * stride];
                    r7 += a[o + 7 * stride];
                }
                double res = ((r0 + r1) + (r2 + r3)) + ((r4 + r5) + (r6 + r7));
                for (; i < n; i++) res += a[offset + i * stride];
                return res;
            }
            int n2 = n / 2;
            n2 -= n2 % 8;
            return PairwiseSum64(a, offset, n2, stride) + PairwiseSum64(a, offset + n2 * stride, n - n2, stride);
        }

        // x.mean(dtype=np.float64) of a float32 array: buffered cast in blocks of 8192, pairwise per block.
        public static double MeanAsFloat64(float[] x)
        {
            if (x.Length == 0) return double.NaN;
            const int Buf = 8192;
            var tmp = new double[Math.Min(Buf, x.Length)];
            double total = 0.0;
            bool first = true;
            for (int s = 0; s < x.Length; s += Buf)
            {
                int len = Math.Min(Buf, x.Length - s);
                for (int i = 0; i < len; i++) tmp[i] = x[s + i];
                double part = PairwiseSum64(tmp, 0, len, 1);
                total = first ? part : total + part;
                first = false;
            }
            return total / x.Length;
        }

        // np.mean of a 1-D float32 array: float32 pairwise sum, then float64 division by the count.
        public static float Mean32(float[] a, int offset, int n, int stride)
        {
            float s = PairwiseSum32(a, offset, n, stride);
            return (float)((double)s / n);
        }

        // np.percentile(x, q) (method="linear", scalar q) of a float32 array, as numpy 2.x computes it:
        // gamma is a Python float (weak), so the lerp runs in float32.
        public static float Percentile32(float[] x, double q)
        {
            int n = x.Length;
            var sorted = (float[])x.Clone();
            Array.Sort(sorted);
            double quant = q / 100.0;
            double vi = (n - 1) * quant;
            if (vi >= n - 1) return sorted[n - 1];
            if (vi < 0) return sorted[0];
            double prevD = Math.Floor(vi);
            int prev = (int)prevD;
            int next = prev + 1;
            double gamma = vi - prevD;
            float a = sorted[prev], b = sorted[next];
            float diff = (float)(b - a);
            if (gamma >= 0.5)
                return (float)(b - (float)(diff * (float)(1.0 - gamma)));
            return (float)(a + (float)(diff * (float)gamma));
        }

        // np.median of a float64 array.
        public static double Median64(double[] x)
        {
            int n = x.Length;
            var s = (double[])x.Clone();
            Array.Sort(s);
            if (n % 2 == 1) return s[n / 2];
            return (s[n / 2 - 1] + s[n / 2]) / 2.0;
        }

        // ---- float32 fused multiply-add (Accelerate sgemm accumulates with FMA) ----------
        // Exact fma(a, b, c) rounded once to float32, using only double arithmetic.
        public static float Fma32(float a, float b, float c)
        {
            double p = (double)a * b;          // exact (24 + 24 bits)
            double s = p + c;
            double bb = s - p;
            double e = (p - (s - bb)) + (c - bb); // s + e == p + c exactly
            if (e == 0.0) return (float)s;
            long bits = BitConverter.DoubleToInt64Bits(s);
            // s sits exactly on a float32 rounding midpoint: move it one double ulp toward the true value.
            if ((bits & 0x1FFFFFFFL) == 0x10000000L)
            {
                bool towardLarger = (e > 0) == (s > 0);
                s = BitConverter.Int64BitsToDouble(bits + (towardLarger ? 1 : -1));
            }
            return (float)s;
        }

        // ---- scalar math -----------------------------------------------------------------

        // Python round(x, ndigits) for a double: exact decimal rounding (half-even on exact ties).
        public static double PyRound(double x, int ndigits)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || x == 0.0) return x;
            long bits = BitConverter.DoubleToInt64Bits(x);
            bool neg = bits < 0;
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            int e2 = exp - 1075; // x = mant * 2^e2
            var pow10 = System.Numerics.BigInteger.Pow(10, ndigits);
            System.Numerics.BigInteger num = new System.Numerics.BigInteger(mant) * pow10;
            System.Numerics.BigInteger q;
            if (e2 >= 0)
            {
                q = num << e2;
            }
            else
            {
                var den = System.Numerics.BigInteger.One << (-e2);
                q = System.Numerics.BigInteger.DivRem(num, den, out var rem);
                var twice = rem * 2;
                int cmp = twice.CompareTo(den);
                if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
            }
            // q / 10^ndigits correctly rounded to double: q and 10^ndigits are exact doubles here.
            double r = (double)q / Math.Pow(10, ndigits);
            if (q > new System.Numerics.BigInteger(9007199254740992L))
                r = double.Parse(q.ToString() + "e-" + ndigits, System.Globalization.CultureInfo.InvariantCulture);
            return neg ? -r : r;
        }

        // log1p (fdlibm s_log1p.c). numpy calls the platform log1p; used only inside the tempo prior argmax.
        public static double Log1p(double x)
        {
            const double ln2_hi = 6.93147180369123816490e-01, ln2_lo = 1.90821492927058770002e-10;
            const double Lp1 = 6.666666666666735130e-01, Lp2 = 3.999999999940941908e-01, Lp3 = 2.857142874366239149e-01,
                Lp4 = 2.222219843214978396e-01, Lp5 = 1.818357216161805012e-01, Lp6 = 1.531383769920937332e-01,
                Lp7 = 1.479819860511658591e-01;
            if (double.IsNaN(x)) return x;
            if (x == -1.0) return double.NegativeInfinity;
            if (x < -1.0) return double.NaN;
            if (double.IsPositiveInfinity(x)) return x;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int hx = (int)(bits >> 32);
            int ax = hx & 0x7fffffff;
            int k = 1;
            double f = 0, c = 0;
            int hu = 0;
            if (hx < 0x3FDA827A)
            {
                if (ax < 0x3e200000)
                {
                    if (ax < 0x3c900000) return x;
                    return x - x * x * 0.5;
                }
                if (hx > 0 || hx <= unchecked((int)0xbfd2bec4)) { k = 0; f = x; hu = 1; }
            }
            if (k != 0)
            {
                double u;
                if (hx < 0x43400000)
                {
                    u = 1.0 + x;
                    hu = (int)(BitConverter.DoubleToInt64Bits(u) >> 32);
                    k = (hu >> 20) - 1023;
                    c = (k > 0) ? 1.0 - (u - x) : x - (u - 1.0);
                    c /= u;
                }
                else
                {
                    u = x;
                    hu = (int)(BitConverter.DoubleToInt64Bits(u) >> 32);
                    k = (hu >> 20) - 1023;
                    c = 0;
                }
                hu &= 0x000fffff;
                long ub;
                if (hu < 0x6a09e)
                {
                    ub = ((long)(hu | 0x3ff00000) << 32) | (BitConverter.DoubleToInt64Bits(u) & 0xffffffffL);
                }
                else
                {
                    k += 1;
                    ub = ((long)(hu | 0x3fe00000) << 32) | (BitConverter.DoubleToInt64Bits(u) & 0xffffffffL);
                    hu = (0x00100000 - hu) >> 2;
                }
                u = BitConverter.Int64BitsToDouble(ub);
                f = u - 1.0;
            }
            double hfsq = 0.5 * f * f;
            if (hu == 0)
            {
                if (f == 0.0)
                {
                    if (k == 0) return 0.0;
                    c += k * ln2_lo;
                    return k * ln2_hi + c;
                }
                double R0 = hfsq * (1.0 - 0.66666666666666666 * f);
                if (k == 0) return f - R0;
                return k * ln2_hi - ((R0 - (k * ln2_lo + c)) - f);
            }
            double s = f / (2.0 + f);
            double z = s * s;
            double R = z * (Lp1 + z * (Lp2 + z * (Lp3 + z * (Lp4 + z * (Lp5 + z * (Lp6 + z * Lp7))))));
            if (k == 0) return f - (hfsq - s * (hfsq + R));
            return k * ln2_hi - ((hfsq - (s * (hfsq + R) + (k * ln2_lo + c))) - f);
        }

        // log2 (netstandard2.1 has no Math.Log2). Exact for powers of two; used only in the tempo prior.
        public static double Log2(double x)
        {
            if (double.IsPositiveInfinity(x)) return x;
            return Math.Log(x) * 1.4426950408889634;
        }

        // np.linspace(start, stop, num, endpoint) in float64.
        public static double[] Linspace(double start, double stop, int num, bool endpoint)
        {
            var y = new double[num];
            int div = endpoint ? num - 1 : num;
            double delta = stop - start;
            if (div > 0)
            {
                double step = delta / div;
                if (step == 0)
                    for (int i = 0; i < num; i++) y[i] = (i / (double)div) * delta + start;
                else
                    for (int i = 0; i < num; i++) y[i] = i * step + start;
            }
            else
            {
                for (int i = 0; i < num; i++) y[i] = double.NaN * delta + start;
            }
            if (endpoint && num > 1) y[num - 1] = stop;
            return y;
        }

        // scipy.signal.get_window("hann", n, fftbins=True): periodic Hann, general_cosine with a = [0.5, 0.5].
        public static double[] HannPeriodic(int n)
        {
            if (n <= 1)
            {
                var one = new double[Math.Max(n, 0)];
                for (int i = 0; i < one.Length; i++) one[i] = 1.0;
                return one;
            }
            var fac = Linspace(-Math.PI, Math.PI, n + 1, true);
            var w = new double[n];
            for (int i = 0; i < n; i++)
            {
                double v = 0.0;
                v += 0.5 * Math.Cos(0 * fac[i]);
                v += 0.5 * Math.Cos(1 * fac[i]);
                w[i] = v;
            }
            return w;
        }

        // Python float floor division a // b for positive operands.
        public static double FloorDiv(double a, double b)
        {
            double mod = a % b;
            double div = (a - mod) / b;
            if (mod != 0 && ((b < 0) != (mod < 0))) div -= 1.0;
            double floordiv = Math.Floor(div);
            if (div - floordiv > 0.5) floordiv += 1.0;
            return floordiv;
        }
    }
}
