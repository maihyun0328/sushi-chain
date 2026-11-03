// app/chart/grid.py 포팅.
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    internal sealed class BeatGrid
    {
        public const int BeatsPerBar = 4;
        public const double MinBeatPeriod = 60.0 / 260.0;
        public const double MaxBeatPeriod = 60.0 / 40.0;

        public readonly double[] Times;
        public readonly MetricLevel[] Levels;
        public readonly double[] Steps;
        public readonly double[] Beats;
        public readonly double BeatPeriod;

        BeatGrid(double[] times, MetricLevel[] levels, double[] steps, double[] beats, double period)
        {
            Times = times; Levels = levels; Steps = steps; Beats = beats; BeatPeriod = period;
        }

        public int Count => Times.Length;

        public static double SnapTolerance(double step) => NumpyCompat.PyMin(ChartParams.SnapFraction * step, ChartParams.SnapMaxSeconds);

        // Index of the nearest grid point for each time, or -1 if none is within tolerance.
        public int[] Snap(double[] t)
        {
            var output = new int[t.Length];
            for (int i = 0; i < output.Length; i++) output[i] = -1;
            int g = Times.Length;
            if (g == 0 || t.Length == 0) return output;
            for (int i = 0; i < t.Length; i++)
            {
                int right = Math.Min(Math.Max(NumpyCompat.SearchSortedLeft(Times, t[i]), 0), g - 1);
                int left = Math.Min(Math.Max(right - 1, 0), g - 1);
                int pick = Math.Abs(Times[left] - t[i]) <= Math.Abs(Times[right] - t[i]) ? left : right;
                double dist = Math.Abs(Times[pick] - t[i]);
                double tol = Math.Min(ChartParams.SnapFraction * Steps[pick], ChartParams.SnapMaxSeconds);
                if (dist <= tol) output[i] = pick;
            }
            return output;
        }

        // Latest grid time <= t whose level is at most maxLevel, or null.
        public double? FloorTime(double t, MetricLevel maxLevel)
        {
            for (int i = Times.Length - 1; i >= 0; i--)
                if (Times[i] <= t && (int)Levels[i] <= (int)maxLevel) return Times[i];
            return null;
        }

        static double[] CleanBeats(double[] beatTimes)
        {
            var finite = new List<double>();
            if (beatTimes != null)
                foreach (double b in beatTimes)
                    if (!double.IsNaN(b) && !double.IsInfinity(b)) finite.Add(b);
            finite.Sort();
            var uniq = new List<double>(finite.Count);
            foreach (double b in finite)
                if (uniq.Count == 0 || uniq[uniq.Count - 1] != b) uniq.Add(b);
            if (uniq.Count < 2) return uniq.ToArray();
            var keep = new List<double> { uniq[0] };
            for (int i = 1; i < uniq.Count; i++)
                if (uniq[i] - keep[keep.Count - 1] >= MinBeatPeriod * 0.5) keep.Add(uniq[i]);
            return keep.ToArray();
        }

        static double? BasePeriod(double[] beats, double tempo)
        {
            if (beats.Length >= 2)
            {
                var diff = new double[beats.Length - 1];
                for (int i = 0; i < diff.Length; i++) diff[i] = beats[i + 1] - beats[i];
                double period = NumpyCompat.Median(diff);
                if (MinBeatPeriod <= period && period <= MaxBeatPeriod) return period;
            }
            if (!double.IsNaN(tempo) && !double.IsInfinity(tempo) && tempo > 0)
            {
                double period = 60.0 / tempo;
                if (MinBeatPeriod <= period && period <= MaxBeatPeriod) return period;
            }
            return null;
        }

        public static BeatGrid Build(double[] beatTimes, double duration, double tempo, int downbeatPhase, bool useTriplets, double anchor)
        {
            double[] beats = CleanBeats(beatTimes);
            double? maybePeriod = BasePeriod(beats, tempo);
            if (maybePeriod == null || double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0)
                return new BeatGrid(new double[0], new MetricLevel[0], new double[0], new double[0], maybePeriod ?? 0.0);
            double period = maybePeriod.Value;
            if (beats.Length == 0) beats = new[] { anchor };

            int nBefore = beats[0] > 0 ? (int)Math.Ceiling((beats[0] - 0.0) / period) + 1 : 1;
            double last = beats[beats.Length - 1];
            int nAfter = last < duration ? (int)Math.Ceiling((duration - last) / period) + 1 : 1;
            var ext = new double[nBefore + beats.Length + nAfter];
            int w = 0;
            for (int k = nBefore; k >= 1; k--) ext[w++] = beats[0] - period * (double)k;
            for (int i = 0; i < beats.Length; i++) ext[w++] = beats[i];
            for (int k = 1; k <= nAfter; k++) ext[w++] = last + period * (double)k;

            int downbeatIndex = nBefore + NumpyCompat.PyMod(downbeatPhase, BeatsPerBar);

            int per = useTriplets ? 6 : 4;
            int n = (ext.Length - 1) * per;
            var times = new double[n];
            var levels = new MetricLevel[n];
            var steps = new double[n];
            int p = 0;
            var pattern = new MetricLevel[4];
            for (int k = 0; k < ext.Length - 1; k++)
            {
                double start = ext[k];
                double local = ext[k + 1] - ext[k];
                double step = local / 4.0;
                bool isDown = NumpyCompat.PyMod(k - downbeatIndex, BeatsPerBar) == 0;
                pattern[0] = isDown ? MetricLevel.Downbeat : MetricLevel.Beat;
                pattern[1] = MetricLevel.Sixteenth;
                pattern[2] = MetricLevel.Eighth;
                pattern[3] = MetricLevel.Sixteenth;
                for (int j = 0; j < 4; j++)
                {
                    times[p] = start + (double)j * step;
                    levels[p] = pattern[j];
                    steps[p] = step;
                    p++;
                }
                if (useTriplets)
                {
                    for (int j = 1; j <= 2; j++)
                    {
                        times[p] = start + (double)j * local / 3.0;
                        levels[p] = MetricLevel.Triplet;
                        steps[p] = step;
                        p++;
                    }
                }
            }

            // np.argsort(kind="stable")
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = times[a].CompareTo(times[b]);
                return c != 0 ? c : a.CompareTo(b);
            });

            var outT = new List<double>(n);
            var outL = new List<MetricLevel>(n);
            var outS = new List<double>(n);
            foreach (int i in order)
            {
                double t = times[i];
                if (t >= 0.0 && t <= duration)
                {
                    outT.Add(t); outL.Add(levels[i]); outS.Add(steps[i]);
                }
            }
            return new BeatGrid(outT.ToArray(), outL.ToArray(), outS.ToArray(), ext, period);
        }
    }
}
