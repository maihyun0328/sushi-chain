// app/chart/generator.py generate_chart 포팅: AudioFeatures -> NoteList.
// 같은 features + difficulty + seedKey 이면 서버와 같은 NoteList 를 만든다.
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    public static class ChartEngine
    {
        // generator.py FRAME_ARRAYS 순서
        static int Len(Array a) => a == null ? 0 : a.Length;

        static void CheckFeatures(AudioFeatures f)
        {
            double d = f.Duration;
            if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
                throw new ChartGenerationException($"invalid duration {d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
            int nFrames = Len(f.FrameTimes);
            if (nFrames == 0) throw new ChartGenerationException("features have no frames");
            var arrays = new (string name, Array value)[]
            {
                ("onset_env", f.OnsetEnv), ("onset_env_low", f.OnsetEnvLow), ("onset_env_mid", f.OnsetEnvMid),
                ("onset_env_high", f.OnsetEnvHigh), ("rms", f.Rms), ("harmonic_rms", f.HarmonicRms),
                ("spectral_centroid", f.SpectralCentroid), ("chroma_argmax", f.ChromaArgmax),
            };
            foreach (var (name, value) in arrays)
            {
                int size = Len(value);
                if (size != nFrames) throw new ChartGenerationException($"{name} has {size} frames, expected {nFrames}");
            }
            if (Len(f.OnsetTimes) != Len(f.OnsetStrengths))
                throw new ChartGenerationException("onset_times and onset_strengths differ in length");
        }

        // app/chart/generator.py seed_from_key
        public static ulong SeedFromKey(string seedKey) => Pcg64.SeedFromKey(seedKey);

        public static List<LocalNote> Generate(AudioFeatures features, string difficulty, string seedKey)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));
            DifficultyParams p = ChartParams.For(difficulty);
            CheckFeatures(features);

            double anchor = 0.0;
            bool any = false;
            if (features.OnsetTimes != null)
            {
                foreach (double o in features.OnsetTimes)
                {
                    if (double.IsNaN(o) || double.IsInfinity(o)) continue;
                    if (!any || o < anchor) { anchor = o; any = true; }
                }
            }
            BeatGrid grid = BeatGrid.Build(features.BeatTimes, features.Duration, features.Tempo, features.DownbeatPhase, p.UseTriplets, anchor);
            if (grid.Count == 0)
                throw new ChartGenerationException("no usable beat grid (beat tracking and tempo both implausible)");

            List<Candidate> candidates = Candidates.Find(features, grid, p);
            List<ChartEvent> events = Density.SelectEvents(candidates, features, grid, p);
            if (events.Count == 0)
                throw new ChartGenerationException("no onsets could be aligned to the beat grid");
            List<LongNote> longNotes = LongNotes.Plan(events, features, grid, p);
            events = LongNotes.DemoteHeldChords(events, longNotes);

            var rng = new Pcg64(SeedFromKey(seedKey));
            List<PlacedNote> placed;
            try
            {
                placed = Lanes.Assign(events, longNotes, features, p, rng);
            }
            catch (LaneAssignmentException exc)
            {
                throw new ChartGenerationException(exc.Message);
            }

            var notes = new List<LocalNote>(placed.Count);
            foreach (var pn in placed)
            {
                notes.Add(new LocalNote
                {
                    Dir = Lanes.DirectionFor(pn.Lane),
                    Type = pn.Lane,
                    StartTime = RoundTime(pn.Time),
                    EndTime = pn.End > 0 ? RoundTime(pn.End) : 0.0,
                });
            }
            notes = StableSort.By(notes, (a, b) =>
            {
                int c = a.StartTime.CompareTo(b.StartTime);
                return c != 0 ? c : a.Type.CompareTo(b.Type);
            });
            ChartValidator.Validate(notes, features.Duration, p.Difficulty);
            return notes;
        }

        // app/chart/serialize.py round_time: Python round(t, 3), -0.0 -> 0.0.
        public static double RoundTime(double t)
        {
            if (double.IsNaN(t) || double.IsInfinity(t))
                throw new ArgumentException($"non-finite time {t}");
            double r = NumpyCompat.PyRound3(t);
            return r == 0 ? 0.0 : r;
        }
    }
}
