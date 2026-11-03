// app/chart/density.py 포팅.
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    internal sealed class ChartEvent
    {
        public readonly Candidate Candidate;
        public readonly bool IsChord;

        public ChartEvent(Candidate candidate, bool isChord)
        {
            Candidate = candidate;
            IsChord = isChord;
        }

        public double Time => Candidate.Time;
        public int Size => IsChord ? 2 : 1;
    }

    internal struct DensityWindow
    {
        public double Start, End, Energy, Factor;
        public int Budget;
    }

    internal static class Density
    {
        const double SilenceRms = 0.02;
        const double ChordBandMin = 0.5;

        public static List<DensityWindow> BuildWindows(AudioFeatures f, BeatGrid grid, DifficultyParams p)
        {
            var windows = new List<DensityWindow>();
            double first = ChartParams.LeadInSeconds;
            double last = f.Duration - ChartParams.TailMarginSeconds;
            if (last <= first) return windows;

            var downbeats = new List<double>();
            for (int i = 0; i < grid.Count; i++)
                if (grid.Levels[i] == MetricLevel.Downbeat) downbeats.Add(grid.Times[i]);
            int barsPerWindow = Math.Max(1, p.WindowBeats / 4);
            var edges = new List<double> { first };
            for (int i = 0; i < downbeats.Count; i += barsPerWindow)
            {
                double t = downbeats[i];
                if (first < t && t < last) edges.Add(t);
            }
            edges.Add(last);

            double[] frameTimes = f.FrameTimes;
            var rms = new double[f.Rms.Length];
            for (int i = 0; i < rms.Length; i++) rms[i] = NumpyCompat.Clip(NumpyCompat.NanToNum(f.Rms[i]), 0.0, 1.0);

            var energies = new double[edges.Count - 1];
            var buf = new double[rms.Length];
            for (int w = 0; w < energies.Length; w++)
            {
                double a = edges[w], b = edges[w + 1];
                int n = 0;
                for (int i = 0; i < frameTimes.Length; i++)
                    if (frameTimes[i] >= a && frameTimes[i] < b) buf[n++] = rms[i];
                energies[w] = n > 0 ? NumpyCompat.PairwiseSum(buf, 0, n) / n : 0.0;
            }

            var audible = new List<double>();
            foreach (double e in energies) if (e >= SilenceRms) audible.Add(e);
            double reference = audible.Count > 0 ? NumpyCompat.Median(audible) : 0.0;

            for (int w = 0; w < energies.Length; w++)
            {
                double a = edges[w], b = edges[w + 1], energy = energies[w];
                double factor;
                if (reference <= 0.0 || energy < SilenceRms)
                    factor = 0.0;
                else
                    factor = NumpyCompat.Clip(Math.Pow(energy / reference, p.EnergyExponent), p.EnergyFactorMin, p.EnergyFactorMax);
                double raw = p.BaseRate * (b - a) * factor;
                int budget = factor > 0.0 ? Math.Max(1, (int)Math.Floor(raw + 0.5)) : 0;
                windows.Add(new DensityWindow { Start = a, End = b, Energy = energy, Factor = factor, Budget = budget });
            }
            return windows;
        }

        static bool ChordEligible(Candidate c, DifficultyParams p) =>
            p.MaxChord >= 2
            && p.IsChordLevel(c.Level)
            && c.Strength >= p.ChordMinStrength
            && c.Low >= ChordBandMin
            && NumpyCompat.PyMax(c.Mid, c.High) >= ChordBandMin;

        public static List<ChartEvent> SelectEvents(List<Candidate> candidates, AudioFeatures f, BeatGrid grid, DifficultyParams p)
        {
            var windows = BuildWindows(f, grid, p);
            var chosen = new List<ChartEvent>();
            if (windows.Count == 0 || candidates.Count == 0) return chosen;
            var starts = new List<double>(windows.Count);
            int expectedEvents = 0;
            foreach (var w in windows) { starts.Add(w.Start); expectedEvents += w.Budget; }
            var used = new int[windows.Count];
            int chordCap = (int)Math.Floor(p.ChordMaxRatio * expectedEvents);

            var acceptedTimes = new List<double>();
            var chordTimes = new List<double>();
            var order = StableSort.By(candidates, (a, b) =>
            {
                int c = (-a.Score).CompareTo(-b.Score);
                return c != 0 ? c : a.Time.CompareTo(b.Time);
            });
            foreach (var c in order)
            {
                int w = Math.Max(0, NumpyCompat.BisectRight(starts, c.Time) - 1);
                if (used[w] >= windows[w].Budget) continue;
                int pos = NumpyCompat.BisectLeft(acceptedTimes, c.Time);
                bool tooClose = (pos > 0 && c.Time - acceptedTimes[pos - 1] < p.MinGap)
                    || (pos < acceptedTimes.Count && acceptedTimes[pos] - c.Time < p.MinGap);
                if (tooClose) continue;

                bool isChord = false;
                if (ChordEligible(c, p) && chordTimes.Count < chordCap && used[w] + 2 <= windows[w].Budget)
                {
                    int cpos = NumpyCompat.BisectLeft(chordTimes, c.Time);
                    bool spaced = (cpos == 0 || c.Time - chordTimes[cpos - 1] >= p.ChordMinSpacing)
                        && (cpos == chordTimes.Count || chordTimes[cpos] - c.Time >= p.ChordMinSpacing);
                    if (spaced)
                    {
                        isChord = true;
                        chordTimes.Insert(cpos, c.Time);
                    }
                }

                var ev = new ChartEvent(c, isChord);
                used[w] += ev.Size;
                acceptedTimes.Insert(pos, c.Time);
                chosen.Add(ev);
            }
            return StableSort.By(chosen, (a, b) => a.Time.CompareTo(b.Time));
        }
    }
}
