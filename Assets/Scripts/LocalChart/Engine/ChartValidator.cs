// app/chart/validate.py 포팅. 문제를 모두 모은 뒤 한 번에 ChartGenerationException 을 던진다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SushiChain.LocalChart
{
    internal static class ChartValidator
    {
        const int MaxReportedProblems = 50;

        static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        static string R(double x) => x.ToString("R", CultureInfo.InvariantCulture);

        // Returns every problem (empty == valid).
        public static List<string> Problems(IList<LocalNote> notes, double duration, string difficulty)
        {
            var problems = new List<string>();
            if (!Finite(duration) || duration <= 0)
            {
                problems.Add($"invalid duration {R(duration)}");
                return problems;
            }
            Structural(notes, duration, problems);
            LaneOverlaps(notes, problems);
            if (difficulty != null) DifficultyRules(notes, ChartParams.For(difficulty), problems);
            return problems;
        }

        public static void Validate(IList<LocalNote> notes, double duration, string difficulty)
        {
            List<string> problems = Problems(notes, duration, difficulty);
            if (problems.Count == 0) return;
            int shown = Math.Min(problems.Count, MaxReportedProblems);
            var sb = new StringBuilder();
            sb.Append("invalid chart (").Append(problems.Count).Append(" problems): ");
            sb.Append(string.Join("; ", problems.GetRange(0, shown)));
            if (problems.Count > shown) sb.Append("; ... and ").Append(problems.Count - shown).Append(" more");
            throw new ChartGenerationException(sb.ToString());
        }

        static void Structural(IList<LocalNote> notes, double duration, List<string> problems)
        {
            double prevStart = double.NegativeInfinity;
            for (int i = 0; i < notes.Count; i++)
            {
                LocalNote n = notes[i];
                bool laneOk = n.Type >= 0 && n.Type <= 3;
                bool dirOk = n.Dir == 0 || n.Dir == 1;
                if (!laneOk || !dirOk)
                    problems.Add($"note {i}: unknown dir/type ({n.Dir}, {n.Type})");
                else if (Lanes.DirectionFor(n.Type) != n.Dir)
                    problems.Add($"note {i}: dir {n.Dir} does not match type {n.Type}");

                double start = n.StartTime, end = n.EndTime;
                if (!(Finite(start) && Finite(end)))
                {
                    problems.Add($"note {i}: non-finite time");
                    continue;
                }
                if (start < 0 || end < 0) problems.Add($"note {i}: negative time");
                if (start > duration || end > duration)
                    problems.Add($"note {i}: time beyond duration {Lanes.Fmt3(duration)}s");
                if (end != 0.0 && end <= start)
                    problems.Add($"note {i}: long note EndTime {R(end)} <= StartTime {R(start)}");
                if (start < prevStart)
                    problems.Add($"note {i}: not sorted by StartTime ({R(start)} < {R(prevStart)})");
                prevStart = NumpyCompat.PyMax(prevStart, start);
            }
        }

        static void LaneOverlaps(IList<LocalNote> notes, List<string> problems)
        {
            // dict 삽입 순서 = 레인이 처음 나온 순서
            var laneOrder = new List<int>();
            var byLane = new Dictionary<int, List<(double start, double end, int i)>>();
            for (int i = 0; i < notes.Count; i++)
            {
                LocalNote n = notes[i];
                if (!(Finite(n.StartTime) && Finite(n.EndTime))) continue;
                if (!byLane.TryGetValue(n.Type, out var list))
                {
                    list = new List<(double, double, int)>();
                    byLane[n.Type] = list;
                    laneOrder.Add(n.Type);
                }
                list.Add((n.StartTime, n.EndTime > 0 ? n.EndTime : n.StartTime, i));
            }
            foreach (int lane in laneOrder)
            {
                var spans = byLane[lane];
                spans.Sort((a, b) =>
                {
                    int c = a.start.CompareTo(b.start);
                    if (c != 0) return c;
                    c = a.end.CompareTo(b.end);
                    return c != 0 ? c : a.i.CompareTo(b.i);
                });
                double busyUntil = double.NegativeInfinity;
                int busyIdx = -1;
                foreach (var s in spans)
                {
                    if (s.start <= busyUntil + ChartParams.TimeEpsilon * 0.5)
                        problems.Add($"note {s.i}: overlaps note {busyIdx} in lane {lane}");
                    if (s.end >= busyUntil) { busyUntil = s.end; busyIdx = s.i; }
                }
            }
        }

        static void DifficultyRules(IList<LocalNote> notes, DifficultyParams p, List<string> problems)
        {
            var finite = new List<int>();
            for (int i = 0; i < notes.Count; i++)
                if (Finite(notes[i].StartTime) && Finite(notes[i].EndTime)) finite.Add(i);

            var groups = new Dictionary<double, List<int>>();
            foreach (int i in finite)
            {
                LocalNote n = notes[i];
                if (n.StartTime < ChartParams.LeadInSeconds - ChartParams.TimeEpsilon)
                    problems.Add($"note {i}: StartTime {R(n.StartTime)} inside the {R(ChartParams.LeadInSeconds)}s lead-in");
                double key = NumpyCompat.PyRound3(n.StartTime);
                if (key == 0.0) key = 0.0; // -0.0 and 0.0 are one dict key
                if (!groups.TryGetValue(key, out var members)) { members = new List<int>(); groups[key] = members; }
                members.Add(i);
            }

            var times = new List<double>(groups.Keys);
            times.Sort();
            foreach (double t in times)
            {
                var members = groups[t];
                if (members.Count > p.MaxChord)
                    problems.Add($"{members.Count} simultaneous notes at {R(t)}s (max {p.MaxChord} for {p.Difficulty})");
                else if (members.Count == 2)
                {
                    bool hasLeft = false, hasRight = false;
                    foreach (int m in members)
                    {
                        if (notes[m].Dir == 0) hasLeft = true;
                        else if (notes[m].Dir == 1) hasRight = true;
                    }
                    // Python: set of dirs == {LEFT, RIGHT}
                    var dirs = new HashSet<int>();
                    foreach (int m in members) dirs.Add(notes[m].Dir);
                    if (!(dirs.Count == 2 && hasLeft && hasRight))
                        problems.Add($"chord at {R(t)}s is not one LEFT + one RIGHT note");
                }
            }

            for (int k = 0; k + 1 < times.Count; k++)
            {
                double a = times[k], b = times[k + 1];
                if (b - a < p.MinGap - ChartParams.TimeEpsilon)
                    problems.Add($"gap {Lanes.Fmt3(b - a)}s between {R(a)}s and {R(b)}s below min {R(p.MinGap)}s for {p.Difficulty}");
            }

            if (p.LongSameSideFree)
            {
                foreach (int i in finite)
                {
                    LocalNote held = notes[i];
                    if (held.EndTime <= 0) continue;
                    foreach (int j in finite)
                    {
                        LocalNote other = notes[j];
                        if (j != i && other.Dir == held.Dir && held.StartTime < other.StartTime && other.StartTime <= held.EndTime)
                            problems.Add($"note {j}: on the same side as long note {i} while it is held");
                    }
                }
            }
        }
    }
}
