// mono float PCM 샘플레이트 변환. ffmpeg `-ar`(libswresample 기본 설정)을 C로 다시 구현했다.
// 순수 C# (UnityEngine 없음), 결정적, static 가변 상태 없음 → 백그라운드 스레드에서 동시에 호출해도 안전하다.
//
// libswresample 기본값: filter_size 32, phase_shift 10, linear_interp 1, exact_rational 1, cutoff 0.97,
// Kaiser window (beta 9). 처리 순서도 swr와 같다.
//   - 필터 뱅크: build_filter()와 같은 식을 double로 계산해 float로 저장
//   - 시작: 앞쪽 filter_length 샘플을 거울상으로 채움 (invert_initial_buffer, x[-n] = x[n])
//   - 끝: (min(남은 샘플, filter_length) + 1) / 2 샘플을 거울상으로 덧붙임 (resample_flush, x[N+j] = x[N-1-j])
//   - 누적: float 누산기 2개(짝/홀 탭)로 더하는 C 구현 순서 (resample_common)
//           비율이 정확한 유리수가 아니면 위상 1024개 + 선형 보간 (resample_linear)
// ffmpeg은 플랫폼별 SIMD(neon/avx)로 누적 순서가 달라 마지막 비트까지 같지는 않다. 측정 오차는
// Tests/LocalChart/ResamplerTests.cs 참고.
using System;
using System.Threading;

namespace SushiChain.LocalChart.Input
{
    public static class SwrResampler
    {
        public const int FilterSize = 32;
        public const int PhaseShift = 10;
        public const double Cutoff = 0.97;
        public const double KaiserBeta = 9.0;

        // swr 내부 상태. 호출마다 새로 만든다
        sealed class Plan
        {
            public int FilterLength;   // L (탭 수)
            public int Alloc;          // 위상당 저장 간격 (8의 배수)
            public int PhaseCount;
            public bool Linear;        // frac이 생기는 비율에서만 의미가 있다
            public float[] Bank;       // (PhaseCount + 1) * Alloc
            public long SrcIncr, DstIncr;
            public int DstIncrDiv, DstIncrMod;
            public int StartIndex;     // c->index 초기값
        }

        public static float[] Resample(float[] mono, int inRate, int outRate, CancellationToken ct = default)
        {
            if (mono == null) throw new ArgumentNullException(nameof(mono));
            if (inRate <= 0 || outRate <= 0) throw new ArgumentOutOfRangeException(nameof(inRate));
            if (inRate == outRate || mono.Length == 0) return (float[])mono.Clone();

            Plan p = BuildPlan(inRate, outRate);
            int L = p.FilterLength;
            int n = mono.Length;

            // 버퍼: [앞쪽 거울 L][입력 N][끝 거울 최대 (L+1)/2]
            int maxTail = (L + 1) / 2;
            var buf = new float[L + n + maxTail];
            for (int k = 1; k <= L; k++) buf[L - k] = mono[Reflect(k, n)];
            Array.Copy(mono, 0, buf, L, n);

            int avail = L + n;
            int sampleIndex = L;
            int index = p.StartIndex;
            while (index < 0) { sampleIndex--; index += p.PhaseCount; }
            long frac = 0;

            // 출력 개수를 먼저 센다 (끝 거울 길이는 그 시점에 남은 샘플 수로 정해진다)
            int count1 = CountOutputs(p, sampleIndex, index, frac, avail, out int s2, out int i2, out long f2);
            int remaining = avail - s2;
            int tail = (Math.Min(remaining, L) + 1) / 2;
            for (int j = 0; j < tail; j++) buf[avail + j] = buf[avail - 1 - j];
            int count2 = CountOutputs(p, s2, i2, f2, avail + tail, out _, out _, out _);

            var output = new float[count1 + count2];
            Run(p, buf, output, count1 + count2, sampleIndex, index, frac, ct);
            return output;
        }

        // Resample()이 돌려줄 길이. 정확한 비율(44100, 48000, 32000, 16000 → 22050 등)에서는 ceil(N * out / in)
        public static int OutputLength(int inputLength, int inRate, int outRate)
        {
            if (inRate == outRate || inputLength == 0) return inputLength;
            Plan p = BuildPlan(inRate, outRate, buildBank: false);
            int L = p.FilterLength;
            int sampleIndex = L, index = p.StartIndex;
            while (index < 0) { sampleIndex--; index += p.PhaseCount; }
            int avail = L + inputLength;
            int c1 = CountOutputs(p, sampleIndex, index, 0, avail, out int s2, out int i2, out long f2);
            int tail = (Math.Min(avail - s2, L) + 1) / 2;
            return c1 + CountOutputs(p, s2, i2, f2, avail + tail, out _, out _, out _);
        }

        // swr 필터 탭 수 (L). 입력이 L 이하로 짧으면 ffmpeg과 길이/값이 다를 수 있다 (1 ms 미만 오디오에서만)
        public static int FilterLength(int inRate, int outRate) =>
            inRate == outRate ? 1 : BuildPlan(inRate, outRate, buildBank: false).FilterLength;

        static int Reflect(int k, int n)
        {
            // 입력이 filter_length보다 짧을 때만 쓰인다 (ffmpeg과 패리티 대상 아님)
            if (n == 1) return 0;
            int period = 2 * (n - 1);
            k %= period;
            return k < n ? k : period - k;
        }

        // sampleIndex + L <= avail 인 동안 출력이 나온다 (multiple_resample의 end_index 조건)
        static int CountOutputs(Plan p, int sampleIndex, int index, long frac, int avail,
            out int sOut, out int iOut, out long fOut)
        {
            int count = 0;
            int L = p.FilterLength;
            while (sampleIndex + L <= avail)
            {
                count++;
                Advance(p, ref sampleIndex, ref index, ref frac);
            }
            sOut = sampleIndex; iOut = index; fOut = frac;
            return count;
        }

        static void Advance(Plan p, ref int sampleIndex, ref int index, ref long frac)
        {
            frac += p.DstIncrMod;
            index += p.DstIncrDiv;
            if (frac >= p.SrcIncr)
            {
                frac -= p.SrcIncr;
                index++;
            }
            while (index >= p.PhaseCount)
            {
                sampleIndex++;
                index -= p.PhaseCount;
            }
        }

        static void Run(Plan p, float[] src, float[] dst, int count, int sampleIndex, int index, long frac,
            CancellationToken ct)
        {
            int L = p.FilterLength;
            int alloc = p.Alloc;
            float[] bank = p.Bank;
            bool linear = p.Linear;
            float srcIncr = p.SrcIncr; // (FELEM_FLOAT) frac / c->src_incr: float / int → float
            for (int d = 0; d < count; d++)
            {
                if ((d & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                int f = alloc * index;
                if (linear)
                {
                    float val = 0f, v2 = 0f;
                    for (int i = 0; i < L; i++)
                    {
                        float s = src[sampleIndex + i];
                        float m1 = s * bank[f + i];
                        float m2 = s * bank[f + i + alloc];
                        val += m1;
                        v2 += m2;
                    }
                    float diff = v2 - val;
                    float fr = (float)frac;
                    float t = diff * fr;
                    t /= srcIncr;
                    val += t;
                    dst[d] = val;
                }
                else
                {
                    float val = 0f, val2 = 0f;
                    int i = 0;
                    for (; i + 1 < L; i += 2)
                    {
                        float m1 = src[sampleIndex + i] * bank[f + i];
                        float m2 = src[sampleIndex + i + 1] * bank[f + i + 1];
                        val += m1;
                        val2 += m2;
                    }
                    if (i < L)
                    {
                        float m1 = src[sampleIndex + i] * bank[f + i];
                        val += m1;
                    }
                    dst[d] = val + val2;
                }
                Advance(p, ref sampleIndex, ref index, ref frac);
            }
        }

        // ───────── swri_resample_init / build_filter ─────────

        static Plan BuildPlan(int inRate, int outRate, bool buildBank = true)
        {
            double factor = Math.Min(outRate * Cutoff / inRate, 1.0);
            int phaseCount = 1 << PhaseShift;
            int filterLength = Math.Max((int)Math.Ceiling(FilterSize / factor), 1);
            if (filterLength > 1) filterLength = (filterLength + 1) & ~1; // FFALIGN(, 2)

            // exact_rational
            Reduce(outRate, inRate, out long exactNum, out _);
            if (exactNum <= phaseCount) phaseCount = (int)exactNum;

            var p = new Plan
            {
                FilterLength = filterLength,
                Alloc = (filterLength + 7) & ~7,
                PhaseCount = phaseCount,
                Linear = true,
            };
            Reduce(outRate, (long)inRate * phaseCount, out long srcIncr, out long dstIncr);
            while (dstIncr < (1 << 20) && srcIncr < (1 << 20)) { dstIncr *= 2; srcIncr *= 2; }
            p.SrcIncr = srcIncr;
            p.DstIncr = dstIncr;
            p.DstIncrDiv = (int)(dstIncr / srcIncr);
            p.DstIncrMod = (int)(dstIncr % srcIncr);
            p.StartIndex = -phaseCount * ((filterLength - 1) / 2);
            // 정확한 비율이면 frac이 항상 0이다. 이때 swr의 linear 경로는 (v2 - val) * 0을 더하지만
            // 누적 순서가 common과 다르다. 측정상 ffmpeg 출력에 더 가까운 쪽을 쓴다 (테스트 참고).
            p.Linear = p.DstIncrMod != 0;
            if (buildBank) p.Bank = BuildBank(factor, filterLength, p.Alloc, phaseCount);
            return p;
        }

        static float[] BuildBank(double factor, int tapCount, int alloc, int phaseCount)
        {
            var bank = new float[alloc * (phaseCount + 1)];
            int phNb = phaseCount % 2 != 0 ? phaseCount : phaseCount / 2 + 1;
            int center = (tapCount - 1) / 2;
            var tab = new double[tapCount];
            var sinLut = new double[phNb];
            double norm = 0;
            if (factor > 1.0) factor = 1.0;
            if (factor == 1.0)
            {
                for (int ph = 0; ph < phNb; ph++)
                    sinLut[ph] = Math.Sin(Math.PI * ph / phaseCount) * ((center & 1) != 0 ? 1 : -1);
            }
            for (int ph = 0; ph < phNb; ph++)
            {
                double s = sinLut[ph];
                for (int i = 0; i < tapCount; i++)
                {
                    double x = Math.PI * ((double)(i - center) - (double)ph / phaseCount) * factor;
                    double y;
                    if (x == 0) y = 1.0;
                    else if (factor == 1.0) y = s / x;
                    else y = Math.Sin(x) / x;
                    double w = 2.0 * x / (factor * tapCount * Math.PI);
                    y *= BesselI0(KaiserBeta * Math.Sqrt(Math.Max(1 - w * w, 0)));
                    tab[i] = y;
                    s = -s;
                    if (ph == 0) norm += y;
                }
                for (int i = 0; i < tapCount; i++)
                    bank[ph * alloc + i] = (float)(tab[i] * 1 / norm);
                if (phaseCount % 2 != 0) continue;
                for (int i = 0; i < tapCount; i++)
                    bank[(phaseCount - ph) * alloc + tapCount - 1 - i] = bank[ph * alloc + i];
            }
            // memcpy(filter_bank + (alloc*pc + 1), filter_bank, alloc - 1); filter_bank[alloc*pc] = filter_bank[alloc - 1]
            Array.Copy(bank, 0, bank, alloc * phaseCount + 1, alloc - 1);
            bank[alloc * phaseCount] = bank[alloc - 1];
            return bank;
        }

        // 0차 수정 베셀 함수 (급수). ffmpeg av_bessel_i0와 double 기준 수 ulp 이내
        static double BesselI0(double x)
        {
            double half = x * 0.5;
            double q = half * half;
            double term = 1.0, sum = 1.0;
            for (int k = 1; k < 500; k++)
            {
                term *= q / ((double)k * k);
                sum += term;
                if (term < sum * 1e-17) break;
            }
            return sum;
        }

        static void Reduce(long num, long den, out long rn, out long rd)
        {
            long a = num, b = den;
            while (b != 0) { long t = a % b; a = b; b = t; }
            rn = num / a;
            rd = den / a;
        }
    }
}
