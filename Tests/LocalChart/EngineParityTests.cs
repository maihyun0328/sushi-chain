// Engine 트랙 패리티 테스트 (Unity 없이 .NET 6 에서 실행).
// 골든: Tests/LocalChart/golden/engine/*.json (tools/make_engine_golden.py 로 생성)
// 실행: ROOT=<worktree> bash localchart-test.sh Tests/LocalChart/EngineParityTests.cs -- <golden dir>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SushiChain.LocalChart;

public static class EngineParityTests
{
    static int _failures;
    static int _checks;

    static void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) { _failures++; Console.WriteLine("  FAIL " + what); }
    }

    static double HexF64(string hex)
    {
        var b = new byte[8];
        for (int i = 0; i < 8; i++) b[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber);
        return BitConverter.ToDouble(b, 0);
    }

    static string Bits(double x) => BitConverter.DoubleToInt64Bits(x).ToString("X16");

    static byte[] B64(JsonElement e) => Convert.FromBase64String(e.GetProperty("b64").GetString());

    static double[] F8(byte[] b) { var a = new double[b.Length / 8]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }
    static float[] F4(byte[] b) { var a = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }
    static int[] I4(byte[] b) { var a = new int[b.Length / 4]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }
    static ulong[] U8(byte[] b) { var a = new ulong[b.Length / 8]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }

    static AudioFeatures LoadFeatures(JsonElement f)
    {
        var a = f.GetProperty("arrays");
        return new AudioFeatures
        {
            SampleRate = f.GetProperty("sample_rate").GetInt32(),
            HopLength = f.GetProperty("hop_length").GetInt32(),
            Duration = HexF64(f.GetProperty("duration").GetString()),
            Tempo = HexF64(f.GetProperty("tempo").GetString()),
            DownbeatPhase = f.GetProperty("downbeat_phase").GetInt32(),
            BeatTimes = F8(B64(a.GetProperty("beat_times"))),
            FrameTimes = F8(B64(a.GetProperty("frame_times"))),
            OnsetEnv = F4(B64(a.GetProperty("onset_env"))),
            OnsetEnvLow = F4(B64(a.GetProperty("onset_env_low"))),
            OnsetEnvMid = F4(B64(a.GetProperty("onset_env_mid"))),
            OnsetEnvHigh = F4(B64(a.GetProperty("onset_env_high"))),
            OnsetTimes = F8(B64(a.GetProperty("onset_times"))),
            OnsetStrengths = F4(B64(a.GetProperty("onset_strengths"))),
            Rms = F4(B64(a.GetProperty("rms"))),
            HarmonicRms = F4(B64(a.GetProperty("harmonic_rms"))),
            SpectralCentroid = F4(B64(a.GetProperty("spectral_centroid"))),
            ChromaArgmax = I4(B64(a.GetProperty("chroma_argmax"))),
        };
    }

    static void TestRng(string dir)
    {
        Console.WriteLine("[rng]");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "rng.json")));
        int seeds = 0, draws = 0;
        foreach (var e in doc.RootElement.GetProperty("seeds").EnumerateArray())
        {
            ulong seed = ulong.Parse(e.GetProperty("seed").GetString());
            var key = e.GetProperty("seed_key");
            if (key.ValueKind == JsonValueKind.String)
                Check(ChartEngine.SeedFromKey(key.GetString()) == seed, $"seed_from_key('{key.GetString()}')");
            ulong[] state = SeedSequence.GenerateState4x64(seed);
            var want = e.GetProperty("state_u64").EnumerateArray().Select(x => ulong.Parse(x.GetString())).ToArray();
            Check(state.SequenceEqual(want), $"seedseq state seed={seed}");
            var raw = U8(Convert.FromBase64String(e.GetProperty("raw_u64_b64").GetString()));
            var g = new Pcg64(seed);
            for (int i = 0; i < raw.Length; i++) { ulong v = g.NextUInt64(); if (v != raw[i]) { Check(false, $"raw u64 seed={seed} i={i}"); break; } }
            var dbl = F8(Convert.FromBase64String(e.GetProperty("random_f64_b64").GetString()));
            var g2 = new Pcg64(seed);
            int bad = 0;
            for (int i = 0; i < dbl.Length; i++)
                if (BitConverter.DoubleToInt64Bits(g2.NextDouble()) != BitConverter.DoubleToInt64Bits(dbl[i])) bad++;
            Check(bad == 0, $"random() seed={seed}: {bad}/{dbl.Length} mismatched");
            seeds++; draws += dbl.Length;
        }
        Console.WriteLine($"  {seeds} seeds, {draws} draws compared bit-exact");
    }

    static void TestRound(string dir)
    {
        Console.WriteLine("[round_time]");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "round_time.json")));
        int n = 0, bad = 0;
        foreach (var pr in doc.RootElement.GetProperty("pairs").EnumerateArray())
        {
            double x = HexF64(pr[0].GetString()), want = HexF64(pr[1].GetString());
            double got = ChartEngine.RoundTime(x);
            n++;
            if (BitConverter.DoubleToInt64Bits(got) != BitConverter.DoubleToInt64Bits(want))
            {
                bad++;
                if (bad <= 5) Console.WriteLine($"  round({x:R}) = {got:R}, python {want:R}");
            }
        }
        Check(bad == 0, $"round_time {bad}/{n} mismatched");
        Console.WriteLine($"  {n} vectors, {bad} mismatched");
    }

    // Numbers are formatted differently (Python repr vs .NET "R"), so messages are compared with numbers masked.
    static string Mask(string m) => System.Text.RegularExpressions.Regex.Replace(m, @"-?\d+(\.\d+)?([eE][-+]?\d+)?", "#");

    static void TestValidate(string dir)
    {
        Console.WriteLine("[validate]");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "validate.json")));
        int n = 0, bad = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var notes = c.GetProperty("notes").EnumerateArray().Select(x => new LocalNote
            {
                Dir = x[0].GetInt32(), Type = x[1].GetInt32(), StartTime = HexF64(x[2].GetString()), EndTime = HexF64(x[3].GetString()),
            }).ToList();
            var d = c.GetProperty("difficulty");
            string diff = d.ValueKind == JsonValueKind.Null ? null : d.GetString();
            var want = c.GetProperty("problems").EnumerateArray().Select(x => Mask(x.GetString())).ToList();
            var got = ChartValidator.Problems(notes, c.GetProperty("duration").GetDouble(), diff).Select(Mask).ToList();
            n++;
            if (!got.SequenceEqual(want))
            {
                bad++;
                Console.WriteLine($"  {c.GetProperty("name").GetString()}/{diff ?? "none"}: c# [{string.Join(" | ", got)}] py [{string.Join(" | ", want)}]");
            }
        }
        Check(bad == 0, $"validate {bad}/{n} mismatched");
        Console.WriteLine($"  {n} cases, {bad} mismatched");
    }

    static void TestCase(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        string name = root.GetProperty("case").GetString();
        var features = LoadFeatures(root.GetProperty("features"));
        foreach (var ch in root.GetProperty("charts").EnumerateArray())
        {
            string diff = ch.GetProperty("difficulty").GetString();
            string seedKey = ch.GetProperty("seed_key").GetString();
            string label = $"{name}/{diff}{(seedKey.StartsWith("alt-seed:") ? "/alt" : "")}";
            List<LocalNote> got = null;
            string gotError = null;
            try { got = ChartEngine.Generate(features, diff, seedKey); }
            catch (ChartGenerationException ex) { gotError = ex.Message; }

            if (ch.TryGetProperty("error", out var err))
            {
                string want = err.GetProperty("message").GetString();
                bool ok = gotError != null;
                Check(ok, $"{label}: expected {err.GetProperty("type").GetString()} '{want}', got {(got == null ? "?" : got.Count + " notes")}");
                if (ok && err.GetProperty("type").GetString() == "ChartGenerationError")
                    Check(gotError == want, $"{label}: message '{gotError}' != '{want}'");
                Console.WriteLine($"  {label,-24} error  {(ok ? "OK" : "MISMATCH")}  ({want})");
                continue;
            }

            var wantNotes = ch.GetProperty("notes").EnumerateArray().Select(n => new LocalNote
            {
                Dir = n[0].GetInt32(), Type = n[1].GetInt32(),
                StartTime = HexF64(n[2].GetString()), EndTime = HexF64(n[3].GetString()),
            }).ToList();
            if (got == null)
            {
                Check(false, $"{label}: threw '{gotError}', expected {wantNotes.Count} notes");
                continue;
            }
            int mism = 0;
            int m = Math.Max(got.Count, wantNotes.Count);
            for (int i = 0; i < m; i++)
            {
                bool same = i < got.Count && i < wantNotes.Count
                    && got[i].Dir == wantNotes[i].Dir && got[i].Type == wantNotes[i].Type
                    && Bits(got[i].StartTime) == Bits(wantNotes[i].StartTime)
                    && Bits(got[i].EndTime) == Bits(wantNotes[i].EndTime);
                if (!same)
                {
                    mism++;
                    if (mism <= 8)
                    {
                        string g = i < got.Count ? $"({got[i].Dir},{got[i].Type},{got[i].StartTime:R},{got[i].EndTime:R})" : "-";
                        string w = i < wantNotes.Count ? $"({wantNotes[i].Dir},{wantNotes[i].Type},{wantNotes[i].StartTime:R},{wantNotes[i].EndTime:R})" : "-";
                        Console.WriteLine($"    #{i}: c# {g}  py {w}");
                    }
                }
            }
            Check(mism == 0 && got.Count == wantNotes.Count, $"{label}: {mism} differing notes (c# {got.Count}, py {wantNotes.Count})");
            Console.WriteLine($"  {label,-24} {wantNotes.Count,4} notes  {(mism == 0 ? "IDENTICAL" : mism + " DIFF")}");
        }
    }

    public static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : Path.Combine("Tests", "LocalChart", "golden", "engine");
        if (!Directory.Exists(dir)) { Console.WriteLine("golden dir not found: " + dir); return 99; }
        TestRng(dir);
        TestRound(dir);
        TestValidate(dir);
        Console.WriteLine("[charts]");
        foreach (string path in Directory.GetFiles(dir, "case_*.json").OrderBy(p => p, StringComparer.Ordinal)) TestCase(path);
        Console.WriteLine($"{_checks} checks, {_failures} failures");
        return _failures;
    }
}
