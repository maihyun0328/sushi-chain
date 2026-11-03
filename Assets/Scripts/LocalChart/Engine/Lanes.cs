// app/chart/lanes.py 포팅. 레인 순서 L1, L2, R1, R2 (= SUSHI_TYPE 0..3).
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    internal struct PlacedNote
    {
        public double Time;
        public int Lane;
        public double End; // 0.0 = short note
    }

    internal sealed class LaneAssignmentException : Exception
    {
        public LaneAssignmentException(string message) : base(message) { }
    }

    internal static class Lanes
    {
        public const int LaneCount = 4;
        static readonly double[] LanePos = { 0.0, 1.0 / 3.0, 2.0 / 3.0, 1.0 };
        static readonly int[,] ChordPairs = { { 0, 2 }, { 0, 3 }, { 1, 2 }, { 1, 3 } };

        const double BrightnessWindowSeconds = 4.0;
        const double CentroidWeight = 0.6;
        const double RunGap = 0.25;
        const int MaxSameSideRun = 3;
        const double WPitch = 1.0;
        const double WJack = 10.0;
        const double WRun = 1.5;
        const double WBalance = 3.0;
        const double WRepeat = 0.25;
        const double WJitter = 1e-3;
        const int BalanceWarmup = 4;

        // dir: L1/L2 -> LEFT(0), R1/R2 -> RIGHT(1)
        public static int DirectionFor(int lane) => lane <= 1 ? 0 : 1;

        static double RawBrightness(double[] frameTimes, double[] centroid, ChartEvent ev)
        {
            Candidate c = ev.Candidate;
            int lo = NumpyCompat.SearchSortedLeft(frameTimes, c.Time - BrightnessWindowSeconds);
            int hi = NumpyCompat.SearchSortedLeft(frameTimes, c.Time + BrightnessWindowSeconds);
            int size = Math.Max(0, Math.Min(hi, centroid.Length) - lo);
            double value = centroid[Math.Min(c.Frame, centroid.Length - 1)];
            double rank;
            if (size > 0)
            {
                int less = 0, equal = 0;
                for (int i = lo; i < lo + size; i++)
                {
                    if (centroid[i] < value) less++;
                    if (centroid[i] == value) equal++;
                }
                rank = (double)less / size + 0.5 * ((double)equal / size);
            }
            else
            {
                rank = 0.5;
            }
            double total = c.Low + c.Mid + c.High;
            double band = total > 1e-9 ? (0.5 * c.Mid + c.High) / total : 0.5;
            return CentroidWeight * rank + (1.0 - CentroidWeight) * band;
        }

        public static double[] LanePositions(AudioFeatures f, List<ChartEvent> events)
        {
            double[] frameTimes = f.FrameTimes;
            var centroid = new double[f.SpectralCentroid.Length];
            for (int i = 0; i < centroid.Length; i++) centroid[i] = NumpyCompat.NanToNum(f.SpectralCentroid[i]);
            var raw = new double[events.Count];
            var times = new double[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                raw[i] = RawBrightness(frameTimes, centroid, events[i]);
                times[i] = events[i].Time;
            }
            var output = new double[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                int lo = NumpyCompat.SearchSortedLeft(times, times[i] - BrightnessWindowSeconds);
                int hi = NumpyCompat.SearchSortedRight(times, times[i] + BrightnessWindowSeconds);
                int size = Math.Max(0, hi - lo);
                if (size <= 1) { output[i] = 0.5; continue; }
                int below = 0, equal = 0;
                for (int j = lo; j < hi; j++)
                {
                    if (raw[j] < raw[i]) below++;
                    if (raw[j] == raw[i]) equal++;
                }
                output[i] = ((double)below + 0.5 * (double)(equal - 1)) / (size - 1);
            }
            return output;
        }

        sealed class LaneState
        {
            readonly DifficultyParams _p;
            readonly Pcg64 _rng;
            public readonly double[] LastTime = new double[LaneCount];
            readonly double[] _busyUntil = new double[LaneCount];
            readonly double[] _sideBusyUntil = new double[2];
            readonly int[] _counts = new int[LaneCount];
            // side: 0 LEFT, 1 RIGHT, -1 chord (None)
            public readonly List<KeyValuePair<double, int>> SideHistory = new List<KeyValuePair<double, int>>();
            int _prevLane = -1;
            readonly double[] _jitter = new double[LaneCount];

            public LaneState(DifficultyParams p, Pcg64 rng)
            {
                _p = p;
                _rng = rng;
                for (int i = 0; i < LaneCount; i++) { LastTime[i] = double.NegativeInfinity; _busyUntil[i] = double.NegativeInfinity; }
                _sideBusyUntil[0] = _sideBusyUntil[1] = double.NegativeInfinity;
            }

            bool Blocked(int lane, double t) => t <= _busyUntil[lane] || t <= _sideBusyUntil[DirectionFor(lane)];

            double RunPenalty(int side, double t)
            {
                int count = SideHistory.Count;
                if (count < MaxSameSideRun) return 0.0;
                for (int i = count - MaxSameSideRun; i < count; i++)
                    if (SideHistory[i].Value != side) return 0.0;
                var times = new double[MaxSameSideRun + 1];
                for (int i = 0; i < MaxSameSideRun; i++) times[i] = SideHistory[count - MaxSameSideRun + i].Key;
                times[MaxSameSideRun] = t;
                for (int i = 0; i < MaxSameSideRun; i++)
                    if (!(times[i + 1] - times[i] < RunGap)) return 0.0;
                return WRun;
            }

            public double[] LaneCosts(double t, double pos)
            {
                for (int k = 0; k < LaneCount; k++) _jitter[k] = _rng.NextDouble() * WJitter; // always drawn
                int n = 0;
                for (int k = 0; k < LaneCount; k++) n += _counts[k];
                var costs = new double[LaneCount];
                for (int k = 0; k < LaneCount; k++)
                {
                    if (Blocked(k, t)) { costs[k] = double.PositiveInfinity; continue; }
                    double cost = WPitch * Math.Abs(pos - LanePos[k]) + _jitter[k];
                    if (t - LastTime[k] < _p.JackMin) cost += WJack;
                    if (n >= BalanceWarmup) cost += WBalance * ((double)_counts[k] / n - 0.25);
                    if (k == _prevLane) cost += WRepeat;
                    cost += RunPenalty(DirectionFor(k), t);
                    costs[k] = cost;
                }
                return costs;
            }

            public void Place(int lane, double t, double end)
            {
                double release = end > 0.0 ? end : t;
                LastTime[lane] = release;
                _counts[lane]++;
                _prevLane = lane;
                if (end > 0.0)
                {
                    _busyUntil[lane] = end + _p.MinGap;
                    if (_p.LongSameSideFree) _sideBusyUntil[DirectionFor(lane)] = end + _p.MinGap;
                }
            }
        }

        public static List<PlacedNote> Assign(List<ChartEvent> events, List<LongNote> longNotes, AudioFeatures f, DifficultyParams p, Pcg64 rng)
        {
            var longEnd = new Dictionary<int, double>();
            foreach (var ln in longNotes) longEnd[ln.EventIndex] = ln.End;
            var state = new LaneState(p, rng);
            var placed = new List<PlacedNote>();
            double[] positions = LanePositions(f, events);
            for (int i = 0; i < events.Count; i++)
            {
                ChartEvent ev = events[i];
                double t = ev.Time;
                double[] costs = state.LaneCosts(t, positions[i]);
                if (ev.IsChord)
                {
                    int bestLeft = -1, bestRight = -1;
                    double bestCost = double.PositiveInfinity;
                    for (int k = 0; k < 4; k++)
                    {
                        int left = ChordPairs[k, 0], right = ChordPairs[k, 1];
                        double total = costs[left] + costs[right];
                        if (total < bestCost) { bestLeft = left; bestRight = right; bestCost = total; }
                    }
                    if (bestLeft < 0)
                        throw new LaneAssignmentException($"no free LEFT+RIGHT lane pair for chord at {Fmt3(t)}s");
                    state.Place(bestLeft, t, 0.0);
                    placed.Add(new PlacedNote { Time = t, Lane = bestLeft, End = 0.0 });
                    state.Place(bestRight, t, 0.0);
                    placed.Add(new PlacedNote { Time = t, Lane = bestRight, End = 0.0 });
                    state.SideHistory.Add(new KeyValuePair<double, int>(t, -1));
                    continue;
                }

                int lane = 0;
                double cost = costs[0];
                for (int k = 1; k < LaneCount; k++)
                    if (costs[k] < cost) { cost = costs[k]; lane = k; }
                if (double.IsNaN(cost) || double.IsInfinity(cost))
                    throw new LaneAssignmentException($"no free lane for note at {Fmt3(t)}s");
                double end = longEnd.TryGetValue(i, out double e) ? e : 0.0;
                if (end > 0.0 && t - state.LastTime[lane] < p.JackMin) end = 0.0; // never hold a forced jack
                state.Place(lane, t, end);
                state.SideHistory.Add(new KeyValuePair<double, int>(t, DirectionFor(lane)));
                placed.Add(new PlacedNote { Time = t, Lane = lane, End = end });
            }
            return placed;
        }

        internal static string Fmt3(double t) => t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
    }
}
