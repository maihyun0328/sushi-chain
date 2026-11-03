// End-to-end parity: PCM → C# AudioAnalyzer.Analyze → C# ChartEngine.Generate, for easy/normal/hard.
//
//   ROOT=<repo> bash localchart-test.sh Tests/LocalChart/GoldenIO.cs Tests/LocalChart/E2EParityTest.cs \
//       -- <out dir> <input> [<input> ...] [--repeat N]
//
// <input> is either an analysis golden (*.lcg, PCM taken from its "input" array) or a raw mono
// 22050 Hz little-endian float32 file (*.f32). For every input it writes <out dir>/<case>.csharp.json:
//   {"case", "n_samples", "sha", "analyze_ms": [..], "charts": {"easy": {"seed_key", "generate_ms",
//    "notes": [[dir, type, start, end], ...]} | {"error": "..."}}}
// with times printed as round-trip doubles. Tests/LocalChart/tools/e2e_parity.py then runs the server
// pipeline (analyze_audio → generate_chart, same PCM and seed key) and compares.
// --repeat N runs Analyze N times (timing only; the first run includes JIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SushiChain.LocalChart.Tests
{
    internal static class E2EParityTest
    {
        private static readonly string[] Difficulties = { "easy", "normal", "hard" };

        private static float[] LoadPcm(string path, out string caseName)
        {
            if (path.EndsWith(".lcg", StringComparison.OrdinalIgnoreCase))
            {
                var g = GoldenFile.Read(path);
                caseName = g.Meta.TryGetProperty("case", out var c) ? c.GetString() ?? Path.GetFileNameWithoutExtension(path)
                                                                     : Path.GetFileNameWithoutExtension(path);
                return g["input"].F4();
            }
            var bytes = File.ReadAllBytes(path);
            var pcm = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, pcm, 0, pcm.Length * 4);
            caseName = Path.GetFileNameWithoutExtension(path);
            return pcm;
        }

        private static string Sha(float[] a)
        {
            using var h = SHA256.Create();
            return string.Concat(h.ComputeHash(GoldenFile.Bytes(a)).Select(b => b.ToString("x2")));
        }

        private static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static string Js(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        public static int Main(string[] args)
        {
            if (args.Length < 2) { Console.WriteLine("usage: <out dir> <input...> [--repeat N]"); return 2; }
            string outDir = args[0];
            Directory.CreateDirectory(outDir);
            int repeat = 1;
            var inputs = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--repeat") repeat = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else inputs.Add(args[i]);
            }

            foreach (var path in inputs)
            {
                var pcm = LoadPcm(path, out var name);
                string sha = Sha(pcm);
                var sb = new StringBuilder();
                sb.Append("{\"case\":").Append(Js(name)).Append(",\"n_samples\":").Append(pcm.Length)
                  .Append(",\"sha\":").Append(Js(sha)).Append(",\"analyze_ms\":[");

                AudioFeatures f = null;
                string analyzeError = null;
                var times = new List<double>();
                for (int r = 0; r < repeat; r++)
                {
                    var sw = Stopwatch.StartNew();
                    try { f = AudioAnalyzer.Analyze(pcm); }
                    catch (ChartGenerationException e) { analyzeError = e.Message; }
                    sw.Stop();
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    if (analyzeError != null) break;
                }
                sb.Append(string.Join(",", times.Select(t => D(Math.Round(t, 2))))).Append("],\"charts\":{");

                var line = new StringBuilder($"{name,-16} {pcm.Length / 22050.0,7:F1}s  analyze {string.Join("/", times.Select(t => $"{t:F0}"))} ms");
                for (int di = 0; di < Difficulties.Length; di++)
                {
                    string diff = Difficulties[di];
                    string key = SeedKeys.ForLocalFile(sha, diff);
                    if (di > 0) sb.Append(',');
                    sb.Append(Js(diff)).Append(":{\"seed_key\":").Append(Js(key));
                    if (f == null) { sb.Append(",\"error\":").Append(Js("analyze: " + analyzeError)).Append('}'); continue; }
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var notes = ChartEngine.Generate(f, diff, key);
                        sw.Stop();
                        sb.Append(",\"generate_ms\":").Append(D(Math.Round(sw.Elapsed.TotalMilliseconds, 2))).Append(",\"notes\":[");
                        sb.Append(string.Join(",", notes.Select(n => $"[{n.Dir},{n.Type},{D(n.StartTime)},{D(n.EndTime)}]")));
                        sb.Append("]}");
                        line.Append($"  {diff} {notes.Count} ({sw.Elapsed.TotalMilliseconds:F0} ms)");
                    }
                    catch (ChartGenerationException e)
                    {
                        sb.Append(",\"error\":").Append(Js(e.Message)).Append('}');
                        line.Append($"  {diff} ERROR");
                    }
                }
                sb.Append("}}");
                File.WriteAllText(Path.Combine(outDir, name + ".csharp.json"), sb.ToString());
                Console.WriteLine(line);
            }
            return 0;
        }
    }
}
