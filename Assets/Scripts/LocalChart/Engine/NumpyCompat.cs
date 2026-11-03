// numpy / Python 수치 의미를 비트 단위로 재현하는 헬퍼 (app/chart/* 가 의존하는 부분만).
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    internal static class NumpyCompat
    {
        // numpy pairwise_sum_DOUBLE (8개 언롤, 블록 128). np.add.reduce / ndarray.mean 의 float64 경로.
        public static double PairwiseSum(double[] a, int offset, int n)
        {
            if (n < 8)
            {
                double res = 0.0;
                for (int i = 0; i < n; i++) res += a[offset + i];
                return res;
            }
            if (n <= 128)
            {
                double r0 = a[offset], r1 = a[offset + 1], r2 = a[offset + 2], r3 = a[offset + 3];
                double r4 = a[offset + 4], r5 = a[offset + 5], r6 = a[offset + 6], r7 = a[offset + 7];
                int i = 8;
                int limit = n - (n % 8);
                for (; i < limit; i += 8)
                {
                    int p = offset + i;
                    r0 += a[p]; r1 += a[p + 1]; r2 += a[p + 2]; r3 += a[p + 3];
                    r4 += a[p + 4]; r5 += a[p + 5]; r6 += a[p + 6]; r7 += a[p + 7];
                }
                double res = ((r0 + r1) + (r2 + r3)) + ((r4 + r5) + (r6 + r7));
                for (; i < n; i++) res += a[offset + i];
                return res;
            }
            int n2 = n / 2;
            n2 -= n2 % 8;
            return PairwiseSum(a, offset, n2) + PairwiseSum(a, offset + n2, n - n2);
        }

        // np.mean of a contiguous float64 array (n > 0).
        public static double Mean(double[] a) => PairwiseSum(a, 0, a.Length) / a.Length;

        // np.median of a float64 array without NaN (n > 0).
        public static double Median(IList<double> values)
        {
            var s = new double[values.Count];
            values.CopyTo(s, 0);
            Array.Sort(s);
            int n = s.Length;
            if ((n & 1) == 1) return s[n / 2];
            // mean([a, b]) == (0.0 + a + b) / 2
            return (0.0 + s[n / 2 - 1] + s[n / 2]) / 2.0;
        }

        // np.searchsorted(a, v, side="left") on an ascending array.
        public static int SearchSortedLeft(double[] a, double v)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (a[mid] < v) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        // np.searchsorted(a, v, side="right") / bisect.bisect_right.
        public static int SearchSortedRight(double[] a, double v)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (v < a[mid]) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        public static int BisectLeft(List<double> a, double v)
        {
            int lo = 0, hi = a.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (a[mid] < v) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        public static int BisectRight(List<double> a, double v)
        {
            int lo = 0, hi = a.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (v < a[mid]) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        // np.nan_to_num for float64: NaN -> 0, +inf -> max, -inf -> -max.
        public static double NanToNum(double x)
        {
            if (double.IsNaN(x)) return 0.0;
            if (double.IsPositiveInfinity(x)) return double.MaxValue;
            if (double.IsNegativeInfinity(x)) return -double.MaxValue;
            return x;
        }

        // np.clip(x, lo, hi) (NaN propagates).
        public static double Clip(double x, double lo, double hi)
        {
            if (double.IsNaN(x)) return x;
            if (x < lo) return lo;
            if (x > hi) return hi;
            return x;
        }

        // Python builtin min(a, b) / max(a, b): the first argument wins unless the second is strictly better.
        public static double PyMin(double a, double b) => b < a ? b : a;
        public static double PyMax(double a, double b) => b > a ? b : a;

        // Python a % m for m > 0 (result in 0..m-1).
        public static int PyMod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }

        // Python round(x, 3): rounds the exact binary value half-to-even, then the nearest double to q/1000.
        public static double PyRound3(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            if (x == 0.0) return x;
            bool neg = x < 0;
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(x));
            int exp = (int)((bits >> 52) & 0x7FF);
            ulong mant = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (exp == 0) exp = 1; else mant |= 1UL << 52;
            int e2 = exp - 1075; // |x| = mant * 2^e2
            if (e2 >= 0) return x; // already an integer
            int k = -e2;
            ulong num = mant * 1000UL; // < 2^63
            ulong q;
            if (k >= 64)
            {
                q = 0; // num < 2^63 < 2^(k-1): strictly below one half
            }
            else
            {
                q = num >> k;
                ulong r = num & ((1UL << k) - 1UL);
                ulong half = 1UL << (k - 1);
                if (r > half || (r == half && (q & 1UL) == 1UL)) q++;
            }
            double res = q / 1000.0;
            return neg ? -res : res;
        }
    }
}
