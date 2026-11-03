// app/chart/candidates.py 포팅.
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    internal sealed class Candidate
    {
        public double Time;
        public double OnsetTime;
        public int GridIndex;
        public MetricLevel Level;
        public double Strength;
        public double Score;
        public int Frame;
        public double Low, Mid, High;
    }

    internal static class Candidates
    {
        const double BandSalienceWeight = 0.2;
        const double EnergySalienceWeight = 0.15;
        const int BandLookbackFrames = 1;
        const int BandLookaheadFrames = 2;

        public static bool LevelAllowed(MetricLevel level, double strength, DifficultyParams p)
        {
            if (level == MetricLevel.Triplet) return p.UseTriplets && strength >= p.TripletThreshold;
            if ((int)level <= (int)p.BaseLevel) return true;
            return p.StrongLevel.HasValue && level == p.StrongLevel.Value && strength >= p.StrongThreshold;
        }

        public static int FrameIndex(double[] frameTimes, double t)
        {
            int i = NumpyCompat.SearchSortedLeft(frameTimes, t);
            if (i <= 0) return 0;
            if (i >= frameTimes.Length) return frameTimes.Length - 1;
            return (t - frameTimes[i - 1]) <= (frameTimes[i] - t) ? i - 1 : i;
        }

        static double BandPeak(float[] env, int frame)
        {
            int lo = Math.Max(0, frame - BandLookbackFrames);
            int hi = Math.Min(env.Length, frame + BandLookaheadFrames + 1);
            if (hi <= lo) return 0.0;
            // np.max (NaN propagates) then np.clip in float32.
            float m = env[lo];
            for (int i = lo + 1; i < hi; i++)
            {
                float v = env[i];
                if (float.IsNaN(m)) break;
                if (float.IsNaN(v) || v > m) m = v;
            }
            return NumpyCompat.Clip((double)m, 0.0, 1.0);
        }

        public static List<Candidate> Find(AudioFeatures f, BeatGrid grid, DifficultyParams p)
        {
            double[] onsetsAll = f.OnsetTimes ?? new double[0];
            float[] strengthsAll = f.OnsetStrengths ?? new float[0];
            var onsetList = new List<double>();
            var strengthList = new List<double>();
            for (int i = 0; i < onsetsAll.Length; i++)
            {
                double o = onsetsAll[i];
                if (double.IsNaN(o) || double.IsInfinity(o)) continue;
                onsetList.Add(o);
                strengthList.Add(NumpyCompat.Clip(NumpyCompat.NanToNum(strengthsAll[i]), 0.0, 1.0));
            }
            double[] onsets = onsetList.ToArray();
            int[] snapped = grid.Snap(onsets);

            double latest = f.Duration - ChartParams.TailMarginSeconds;
            // dict[int, Candidate] 의 삽입 순서를 그대로 유지한다.
            var best = new Dictionary<int, int>();
            var ordered = new List<Candidate>();
            for (int i = 0; i < onsets.Length; i++)
            {
                double onsetT = onsets[i];
                double strength = strengthList[i];
                int gi = snapped[i];
                if (gi < 0 || strength < p.MinStrength) continue;
                double t = grid.Times[gi];
                if (t < ChartParams.LeadInSeconds || t > latest) continue;
                MetricLevel level = grid.Levels[gi];
                if (!LevelAllowed(level, strength, p)) continue;
                int frame = FrameIndex(f.FrameTimes, onsetT);
                double low = BandPeak(f.OnsetEnvLow, frame);
                double mid = BandPeak(f.OnsetEnvMid, frame);
                double high = BandPeak(f.OnsetEnvHigh, frame);
                double energy = NumpyCompat.Clip((double)f.Rms[frame], 0.0, 1.0);
                double band = NumpyCompat.PyMax(NumpyCompat.PyMax(low, mid), high);
                double score = strength + ChartParams.MetricWeight(level) + BandSalienceWeight * band + EnergySalienceWeight * energy;
                var cand = new Candidate
                {
                    Time = t, OnsetTime = onsetT, GridIndex = gi, Level = level, Strength = strength, Score = score,
                    Frame = frame, Low = low, Mid = mid, High = high,
                };
                if (best.TryGetValue(gi, out int slot))
                {
                    if (cand.Strength > ordered[slot].Strength) ordered[slot] = cand;
                }
                else
                {
                    best[gi] = ordered.Count;
                    ordered.Add(cand);
                }
            }
            return StableSort.By(ordered, (a, b) => a.Time.CompareTo(b.Time));
        }
    }

    internal static class StableSort
    {
        // Python sorted(): stable, ties keep input order.
        public static List<T> By<T>(List<T> items, Comparison<T> cmp)
        {
            var idx = new int[items.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, (a, b) =>
            {
                int c = cmp(items[a], items[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            var result = new List<T>(items.Count);
            foreach (int i in idx) result.Add(items[i]);
            return result;
        }
    }
}
