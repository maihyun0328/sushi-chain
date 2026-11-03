// app/chart/longnotes.py 포팅.
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    internal struct LongNote
    {
        public int EventIndex;
        public double Start;
        public double End;
    }

    internal static class LongNotes
    {
        const double SustainRatio = 0.6;
        const double HarmonicMin = 0.15;
        const double AttackStrength = 0.5;
        const double AttackIgnoreSeconds = 0.06;
        const double AttackMarginSeconds = 0.05;
        const double OnsetEnvLowMean = 0.3;
        const int OnsetFrames = 3;

        // Raw (unsnapped) end of the sustain starting at the event, or null.
        public static double? SustainEnd(AudioFeatures f, double[] harm, double[] env, ChartEvent ev, DifficultyParams p)
        {
            Candidate c = ev.Candidate;
            double[] frameTimes = f.FrameTimes;
            int f0 = c.Frame;
            if (f0 >= harm.Length - 1) return null;
            int hEnd = Math.Min(harm.Length, f0 + OnsetFrames);
            double h0 = harm[f0];
            for (int i = f0 + 1; i < hEnd; i++) if (harm[i] > h0) h0 = harm[i];
            if (h0 < HarmonicMin) return null;

            double horizon = c.Time + p.LongMaxLen;
            int fLast = NumpyCompat.SearchSortedRight(frameTimes, horizon);
            double threshold = SustainRatio * h0;
            int fEnd = -1;
            int spanEnd = Math.Min(fLast, harm.Length);
            for (int i = f0 + 1; i < spanEnd; i++)
            {
                if (harm[i] < threshold) { fEnd = i; break; }
            }
            if (fEnd < 0) fEnd = Math.Min(fLast, harm.Length - 1);
            double end = NumpyCompat.PyMin(frameTimes[Math.Min(fEnd, frameTimes.Length - 1)], horizon);

            double[] onsets = f.OnsetTimes ?? new double[0];
            float[] strengths = f.OnsetStrengths ?? new float[0];
            double attackFrom = c.OnsetTime + AttackIgnoreSeconds;
            for (int i = 0; i < onsets.Length; i++)
            {
                if (onsets[i] > attackFrom && onsets[i] < end && (double)strengths[i] >= AttackStrength)
                {
                    end = onsets[i] - AttackMarginSeconds;
                    break;
                }
            }

            var body = new double[frameTimes.Length];
            int n = 0;
            for (int i = 0; i < frameTimes.Length; i++)
                if (frameTimes[i] >= attackFrom && frameTimes[i] < end) body[n++] = env[i];
            if (n > 0 && NumpyCompat.PairwiseSum(body, 0, n) / n > OnsetEnvLowMean) return null;
            return end > c.Time ? end : (double?)null;
        }

        public static List<LongNote> Plan(List<ChartEvent> events, AudioFeatures f, BeatGrid grid, DifficultyParams p)
        {
            var result = new List<LongNote>();
            int cap = (int)Math.Floor(p.LongMaxRatio * events.Count);
            if (cap <= 0) return result;
            double latest = f.Duration - ChartParams.TailMarginSeconds;
            var chordTimes = new List<double>();
            foreach (var e in events) if (e.IsChord) chordTimes.Add(e.Time);

            var harm = new double[f.HarmonicRms.Length];
            for (int i = 0; i < harm.Length; i++) harm[i] = NumpyCompat.NanToNum(f.HarmonicRms[i]);
            var env = new double[f.OnsetEnv.Length];
            for (int i = 0; i < env.Length; i++) env[i] = NumpyCompat.NanToNum(f.OnsetEnv[i]);

            var proposals = new List<LongNote>();
            for (int i = 0; i < events.Count; i++)
            {
                ChartEvent ev = events[i];
                double? rawEnd = SustainEnd(f, harm, env, ev, p);
                if (rawEnd == null) continue;
                double limit = NumpyCompat.PyMin(rawEnd.Value, latest);
                if (p.LongSameSideFree)
                {
                    foreach (double tc in chordTimes)
                    {
                        if (tc > ev.Time)
                        {
                            limit = NumpyCompat.PyMin(limit, tc - p.MinGap);
                            break;
                        }
                    }
                }
                double? end = grid.FloorTime(limit, p.BaseLevel);
                if (end == null || end.Value - ev.Time < p.LongMinLen) continue;
                proposals.Add(new LongNote { EventIndex = i, Start = ev.Time, End = end.Value });
            }

            var order = StableSort.By(proposals, (a, b) =>
            {
                int c = (-(a.End - a.Start)).CompareTo(-(b.End - b.Start));
                return c != 0 ? c : a.Start.CompareTo(b.Start);
            });
            foreach (var prop in order)
            {
                if (result.Count >= cap) break;
                bool overlaps = false;
                foreach (var q in result)
                {
                    if (prop.Start <= q.End + p.MinGap && q.Start <= prop.End + p.MinGap) { overlaps = true; break; }
                }
                if (overlaps) continue;
                result.Add(prop);
            }
            return StableSort.By(result, (a, b) => a.Start.CompareTo(b.Start));
        }

        // Events that start a long note are always single notes.
        public static List<ChartEvent> DemoteHeldChords(List<ChartEvent> events, List<LongNote> longNotes)
        {
            var held = new HashSet<int>();
            foreach (var ln in longNotes) held.Add(ln.EventIndex);
            var output = new List<ChartEvent>(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                ChartEvent e = events[i];
                output.Add(held.Contains(i) && e.IsChord ? new ChartEvent(e.Candidate, false) : e);
            }
            return output;
        }
    }
}
