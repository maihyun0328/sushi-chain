// Analysis parity test: C# AudioAnalyzer vs the server's analyze_audio on golden inputs.
//
//   ROOT=<worktree> bash localchart-test.sh Tests/LocalChart/GoldenIO.cs Tests/LocalChart/AnalysisParityTest.cs \
//       -- Tests/LocalChart/golden [--dump <dir>] [--extra <file.lcg> ...]
//
// Per stage it prints: n, exact (bit-identical) count, max abs / max rel error, or exact equality for
// index outputs. `--dump <dir>` writes <dir>/<case>.features.lcg (C# AudioFeatures, exact bytes) for
// Tests/LocalChart/tools/compare_charts.py. Exit code 0 when every REQUIRED check passes:
//   constants (filterbanks, windows) bit-exact; rhythm stages fed with the server's envelopes exact;
//   final per-case: beat frames, onset frames, downbeat, tempo exact.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SushiChain.LocalChart.Analysis;

namespace SushiChain.LocalChart.Tests
{
    internal static class AnalysisParityTest
    {
        private static int _failures;
        private static readonly List<string> FailNames = new List<string>();

        private static void Fail(string what)
        {
            _failures++;
            FailNames.Add(what);
            Console.WriteLine("  FAIL " + what);
        }

        private static string Sha(float[] a)
        {
            using var h = SHA256.Create();
            return string.Concat(h.ComputeHash(GoldenFile.Bytes(a)).Select(b => b.ToString("x2")));
        }

        private static string Sha(byte[] b)
        {
            using var h = SHA256.Create();
            return string.Concat(h.ComputeHash(b).Select(x => x.ToString("x2")));
        }

        internal static (int exact, double maxAbs, double maxRel) Stats(IList<double> mine, IList<double> gold)
        {
            int exact = 0;
            double ma = 0, mr = 0;
            for (int i = 0; i < Math.Min(mine.Count, gold.Count); i++)
            {
                double a = mine[i], b = gold[i];
                if (BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b) || a == b) { exact++; continue; }
                if (double.IsInfinity(a) && a == b) { exact++; continue; }
                double d = Math.Abs(a - b);
                if (double.IsNaN(d)) d = double.PositiveInfinity;
                ma = Math.Max(ma, d);
                double den = Math.Max(Math.Abs(a), Math.Abs(b));
                if (den > 0) mr = Math.Max(mr, d / den);
            }
            return (exact, ma, mr);
        }

        private static bool Cmp(string name, float[] mine, float[] gold, bool required = false)
            => Cmp(name, mine.Select(v => (double)v).ToArray(), gold.Select(v => (double)v).ToArray(), required);

        private static bool Cmp(string name, double[] mine, double[] gold, bool required = false)
        {
            if (mine.Length != gold.Length)
            {
                Console.WriteLine($"  {name,-26} LENGTH {mine.Length} vs {gold.Length}");
                if (required) Fail(name);
                return false;
            }
            var (exact, ma, mr) = Stats(mine, gold);
            bool ok = exact == gold.Length;
            Console.WriteLine($"  {name,-26} n={gold.Length,-8} exact={exact,-8} {(ok ? "IDENTICAL" : $"maxAbs={ma:G4} maxRel={mr:G4}")}");
            if (required && !ok) Fail(name);
            return ok;
        }

        private static bool CmpInts(string name, int[] mine, int[] gold, bool required)
        {
            bool ok = mine.Length == gold.Length && mine.SequenceEqual(gold);
            int same = 0;
            for (int i = 0; i < Math.Min(mine.Length, gold.Length); i++) if (mine[i] == gold[i]) same++;
            Console.WriteLine($"  {name,-26} n={gold.Length,-8} mine_n={mine.Length,-6} equal={same,-6} {(ok ? "IDENTICAL" : "DIFFERENT")}");
            if (!ok && mine.Length == gold.Length)
            {
                var diffs = Enumerable.Range(0, gold.Length).Where(i => mine[i] != gold[i]).Take(8).Select(i => $"[{i}] {mine[i]} vs {gold[i]}");
                Console.WriteLine("    first diffs: " + string.Join(", ", diffs));
            }
            else if (!ok)
            {
                var a = new HashSet<int>(mine); var b = new HashSet<int>(gold);
                Console.WriteLine($"    only mine: {string.Join(",", a.Except(b).Take(10))}  only server: {string.Join(",", b.Except(a).Take(10))}");
            }
            if (required && !ok) Fail(name);
            return ok;
        }

        private static bool CmpScalar(string name, double mine, double gold, bool required)
        {
            bool ok = mine == gold || BitConverter.DoubleToInt64Bits(mine) == BitConverter.DoubleToInt64Bits(gold);
            Console.WriteLine($"  {name,-26} mine={mine:R} server={gold:R} {(ok ? "IDENTICAL" : "DIFFERENT")}");
            if (required && !ok) Fail(name);
            return ok;
        }

        private static void CmpSha(string name, string mine, string gold)
        {
            Console.WriteLine($"  {name,-26} sha256 {(mine == gold ? "IDENTICAL" : "different")}");
        }

        // (rows, W) row-major slice of a frame-major (F, rows) array
        private static float[] SliceRowsMajor(float[] frameMajor, int rows, int s0, int s1)
        {
            int w = s1 - s0;
            var o = new float[rows * w];
            for (int r = 0; r < rows; r++)
                for (int j = 0; j < w; j++) o[r * w + j] = frameMajor[(s0 + j) * rows + r];
            return o;
        }

        private static float[] FrameMajorToRowMajor(float[] fm, int rows, int frames) => SliceRowsMajor(fm, rows, 0, frames);

        private static double[] ColSum64(float[] fm, int rows, int frames)
        {
            var o = new double[frames];
            for (int t = 0; t < frames; t++)
            {
                double s = 0; // numpy float64 sum over axis 0: sequential
                for (int r = 0; r < rows; r++) s += fm[t * rows + r];
                o[t] = s;
            }
            return o;
        }

        private static int Main(string[] args)
        {
            if (args.Length < 1) { Console.WriteLine("usage: <golden dir> [--dump dir] [--extra file.lcg]"); return 2; }
            string dir = args[0];
            string dump = null;
            var extra = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--dump") dump = args[++i];
                else if (args[i] == "--extra") extra.Add(args[++i]);
            }
            if (dump != null) Directory.CreateDirectory(dump);

            Constants(Path.Combine(dir, "analysis_constants.lcg"));
            var cases = Directory.GetFiles(dir, "analysis_*.lcg").Where(p => !p.EndsWith("analysis_constants.lcg")).OrderBy(p => p).ToList();
            cases.AddRange(extra);
            foreach (var path in cases) RunCase(path, dump);
            ConcurrencyCheck(cases[0]);

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "ALL REQUIRED CHECKS PASSED" : $"{_failures} REQUIRED CHECK(S) FAILED: {string.Join("; ", FailNames)}");
            return _failures == 0 ? 0 : 1;
        }

        // Analyze has no shared state: four concurrent runs must equal a sequential run bit for bit.
        private static void ConcurrencyCheck(string path)
        {
            var input = GoldenFile.Read(path)["input"].F4();
            var reference = AudioAnalyzer.Analyze(input);
            var results = new AudioFeatures[4];
            System.Threading.Tasks.Parallel.For(0, results.Length, i => results[i] = AudioAnalyzer.Analyze(input));
            bool ok = results.All(r => Sha(r.OnsetEnv) == Sha(reference.OnsetEnv) && Sha(r.SpectralCentroid) == Sha(reference.SpectralCentroid)
                                       && Sha(r.HarmonicRms) == Sha(reference.HarmonicRms) && r.BeatTimes.SequenceEqual(reference.BeatTimes)
                                       && r.OnsetTimes.SequenceEqual(reference.OnsetTimes) && r.ChromaArgmax.SequenceEqual(reference.ChromaArgmax)
                                       && r.Tempo == reference.Tempo && r.DownbeatPhase == reference.DownbeatPhase);
            Console.WriteLine();
            Console.WriteLine($"== concurrency: 4 parallel Analyze runs {(ok ? "IDENTICAL" : "DIFFERENT")} to a sequential run");
            if (!ok) Fail("concurrency");
        }

        private static void Constants(string path)
        {
            Console.WriteLine("== constants");
            var g = GoldenFile.Read(path);
            int sr = LocalChartConstants.AnalysisSampleRate;
            var mel = Spectral.MelBasis(sr, AudioAnalyzer.NMels, 11025.0, out _, out _);
            Cmp("mel_basis", mel, g["mel_basis"].F4(), true);
            Cmp("chroma_basis", Spectral.ChromaBasis(sr), g["chroma_basis"].F4(), true);
            Cmp("stft_window", NumpyCompat.HannPeriodic(2048), g["stft_window"].F8(), true);
            Cmp("tempogram_window", NumpyCompat.HannPeriodic(689), g["tempogram_window"].F8(), true);
            Cmp("mel_centers", Spectral.MelFrequencies(98, 11025.0), g["mel_centers"].F8(), true);
            Cmp("fft_freqs", Spectral.FftFrequencies32(sr), g["fft_freqs"].F4(), true);
            if (g.Has("fft2048_in"))
            {
                FftCheck("fft2048 (rfft, float64)", g["fft2048_in"].F8(), g["fft2048_re"].F8(), g["fft2048_im"].F8(), 2048, false);
                FftCheck("fft1440 (rfft, float64)", g["fft1440_in"].F8(), g["fft1440_re"].F8(), g["fft1440_im"].F8(), 1440, false);
                FftCheck("ifft1440 (irfft, float64)", g["ifft1440_in"].F8(), g["ifft1440_out"].F8(), null, 1440, true);
            }
        }

        // Compares FftPlan to scipy.fft on the same frames: max abs error relative to the frame's max |X|.
        private static void FftCheck(string name, double[] input, double[] goldRe, double[] goldIm, int n, bool inverse)
        {
            var plan = new FftPlan(n);
            int bins = n / 2 + 1;
            int frames = inverse ? input.Length / bins : input.Length / n;
            double worstAbs = 0, worstRelToMax = 0, worstRel = 0;
            int exact = 0, total = 0, exact32 = 0;
            var re = new double[n]; var im = new double[n]; var oRe = new double[n]; var oIm = new double[n];
            for (int f = 0; f < frames; f++)
            {
                if (!inverse)
                {
                    for (int i = 0; i < n; i++) { re[i] = input[f * n + i]; im[i] = 0; }
                    plan.Transform(re, im, oRe, oIm, false);
                    double mx = 0;
                    for (int k = 0; k < bins; k++) mx = Math.Max(mx, Math.Sqrt(goldRe[f * bins + k] * goldRe[f * bins + k] + goldIm[f * bins + k] * goldIm[f * bins + k]));
                    for (int k = 0; k < bins; k++)
                    {
                        foreach (var (a, b) in new[] { (oRe[k], goldRe[f * bins + k]), (oIm[k], goldIm[f * bins + k]) })
                        {
                            total++;
                            if (a == b) exact++;
                            if ((float)a == (float)b) exact32++;
                            double d = Math.Abs(a - b);
                            worstAbs = Math.Max(worstAbs, d);
                            worstRelToMax = Math.Max(worstRelToMax, d / mx);
                            if (Math.Abs(b) > 1e-3 * mx) worstRel = Math.Max(worstRel, d / Math.Abs(b));
                        }
                    }
                }
                else
                {
                    for (int k = 0; k < bins; k++) { re[k] = input[f * bins + k]; im[k] = 0; }
                    for (int k = bins; k < n; k++) { re[k] = re[n - k]; im[k] = 0; }
                    plan.Transform(re, im, oRe, oIm, true);
                    double mx = 0;
                    for (int t = 0; t < n; t++) mx = Math.Max(mx, Math.Abs(goldRe[f * n + t]));
                    for (int t = 0; t < n; t++)
                    {
                        double a = oRe[t] * (1.0 / n), b = goldRe[f * n + t];
                        total++;
                        if (a == b) exact++;
                        if ((float)a == (float)b) exact32++;
                        double d = Math.Abs(a - b);
                        worstAbs = Math.Max(worstAbs, d);
                        worstRelToMax = Math.Max(worstRelToMax, d / mx);
                        if (Math.Abs(b) > 1e-3 * mx) worstRel = Math.Max(worstRel, d / Math.Abs(b));
                    }
                }
            }
            Console.WriteLine($"  {name,-26} values={total} exact64={exact} same_after_float32_cast={exact32} maxAbs={worstAbs:G4} maxAbs/max|X|={worstRelToMax:G4} maxRel(|X|>1e-3max)={worstRel:G4}");
        }

        private static void RunCase(string path, string dump)
        {
            var g = GoldenFile.Read(path);
            string name = g.Meta.GetProperty("case").GetString();
            int chunk = g.Meta.GetProperty("chunk_frames").GetInt32();
            var input = g["input"].F4();
            Console.WriteLine();
            Console.WriteLine($"== {name}: {input.Length} samples, CHUNK_FRAMES={chunk}");
            var trace = new AnalysisTrace();
            var sw = Stopwatch.StartNew();
            var f = AudioAnalyzer.Analyze(input, chunk, trace);
            sw.Stop();
            var sw2 = Stopwatch.StartNew();
            var fPlain = AudioAnalyzer.Analyze(input, chunk, null);
            sw2.Stop();
            Console.WriteLine($"  analyze time: {sw2.ElapsedMilliseconds} ms (traced run {sw.ElapsedMilliseconds} ms)");
            int F = trace.NFrames;
            var sl = g.Meta.GetProperty("slice");
            int s0 = sl[0].GetInt32(), s1 = sl[1].GetInt32();
            int bins = Spectral.Bins;

            Console.WriteLine("-- stages (chained: each stage uses the C# output of the previous one)");
            CmpSha("preprocessed", Sha(trace.Preprocessed), g.Meta.GetProperty("sha_preprocessed").GetString());
            Cmp("preprocessed[:4096]", trace.Preprocessed.Take(4096).ToArray(), g["preprocessed_head"].F4());
            CmpSha("stft mag", Sha(FrameMajorToRowMajor(trace.Mag, bins, F)), g.Meta.GetProperty("sha_mag").GetString());
            Cmp("stft mag slice", SliceRowsMajor(trace.Mag, bins, s0, s1), g["mag_slice"].F4());
            Cmp("stft mag colsum(f64)", ColSum64(trace.Mag, bins, F), g["mag_colsum"].F8());
            Cmp("rms_raw", trace.RmsRaw, g["rms_raw"].F4());
            Cmp("mag_sum", trace.MagSum, g["mag_sum"].F4());
            Cmp("centroid numerator", trace.CentroidNum, g["centroid_num"].F4());
            Cmp("spectral_centroid", trace.Centroid, g["centroid"].F4());
            if (g.Has("pooled_slice"))
            {
                int rows = 512, W = (s1 - s0) / 2;
                var mineP = new float[rows * W]; var mineM = new float[rows * W];
                for (int r = 0; r < rows; r++)
                    for (int j = 0; j < W; j++)
                    {
                        mineP[r * W + j] = trace.Hpss.PooledFirstChunk[(s0 / 2 + j) * rows + r];
                        mineM[r * W + j] = trace.Hpss.MaskPooledFirstChunk[(s0 / 2 + j) * rows + r];
                    }
                Cmp("hpss pooled slice", mineP, g["pooled_slice"].F4());
                Cmp("hpss mask slice", mineM, g["mask_pooled_slice"].F4());
            }
            CmpSha("percussive", Sha(FrameMajorToRowMajor(trace.Hpss.Percussive, bins, F)), g.Meta.GetProperty("sha_perc").GetString());
            Cmp("percussive slice", SliceRowsMajor(trace.Hpss.Percussive, bins, s0, s1), g["perc_slice"].F4());
            Cmp("percussive colsum(f64)", ColSum64(trace.Hpss.Percussive, bins, F), g["perc_colsum"].F8());
            Cmp("harmonic_rms_raw", trace.Hpss.HarmonicRmsRaw, g["harmonic_rms_raw"].F4());
            Cmp("chroma", FrameMajorToRowMajor(trace.Hpss.Chroma, 12, F), g["chroma"].F4());
            Cmp("chroma_flux_raw", trace.ChromaFlux, g["chroma_flux_raw"].F4());
            Cmp("harmonic_change", trace.HarmonicChange, g["harmonic_change"].F4());
            CmpSha("mel power", Sha(FrameMajorToRowMajor(trace.Mel, 96, F)), g.Meta.GetProperty("sha_mel").GetString());
            Cmp("mel power slice", SliceRowsMajor(trace.Mel, 96, s0, s1), g["mel_slice"].F4());
            CmpScalar("mel ref (max)", trace.MelRef, g.Meta.GetProperty("mel_ref").GetDouble(), false);
            CmpSha("mel_db", Sha(FrameMajorToRowMajor(trace.MelDb, 96, F)), g.Meta.GetProperty("sha_mel_db").GetString());
            Cmp("mel_db slice", SliceRowsMajor(trace.MelDb, 96, s0, s1), g["mel_db_slice"].F4());
            Cmp("flux full", trace.FluxFull, g["flux_full"].F4());
            Cmp("flux low", trace.FluxLow, g["flux_low"].F4());
            Cmp("flux mid", trace.FluxMid, g["flux_mid"].F4());
            Cmp("flux high", trace.FluxHigh, g["flux_high"].F4());
            Cmp("onset_env", f.OnsetEnv, g["env_full"].F4());
            Cmp("onset_env_low", f.OnsetEnvLow, g["env_low"].F4());
            Cmp("onset_env_mid", f.OnsetEnvMid, g["env_mid"].F4());
            Cmp("onset_env_high", f.OnsetEnvHigh, g["env_high"].F4());
            if (g.Has("tempogram_mean"))
            {
                Cmp("tempogram mean", trace.TempogramMean, g["tempogram_mean"].F8());
                Cmp("tempo objective", trace.TempoObjective, g["tempo_objective"].F8());
            }
            CmpScalar("tempo raw", trace.TempoRaw, g.Meta.GetProperty("tempo_raw").GetDouble(), false);
            if (trace.Beat != null && g.Has("beat_localscore"))
            {
                Cmp("beat norm env", trace.Beat.NormEnv, g["beat_norm_env"].F4());
                Cmp("beat localscore", trace.Beat.LocalScore, g["beat_localscore"].F4());
                Cmp("beat cumscore", trace.Beat.CumScore, g["beat_cumscore"].F8());
                CmpInts("beat backlink", trace.Beat.Backlink, g["beat_backlink"].Ints(), false);
                CmpInts("beat frames untrimmed", trace.Beat.UntrimmedFrames, g["beat_frames_untrimmed"].Ints(), false);
            }
            CmpInts("beat frames", trace.BeatFrames, g["beat_frames"].Ints(), false);
            CmpInts("onset frames", trace.OnsetFrames, g["onset_frames"].Ints(), false);
            CmpInts("chroma argmax", f.ChromaArgmax, g["feat_chroma_argmax"].Ints(), false);

            Console.WriteLine("-- isolated stages (fed with the server's own inputs)");
            var envFull = g["env_full"].F4();
            Cmp("robust_unit(flux full)", Spectral.RobustUnit(g["flux_full"].F4(), 99.5), envFull, true);
            Cmp("robust_unit(chroma flux)", Spectral.RobustUnit(g["chroma_flux_raw"].F4(), 99.5), g["harmonic_change"].F4(), true);
            var chromaG = g["chroma"].F4();
            var argG = new int[F];
            for (int t = 0; t < F; t++) { int b = 0; for (int c = 1; c < 12; c++) if (chromaG[c * F + t] > chromaG[b * F + t]) b = c; argG[t] = b; }
            CmpInts("argmax(server chroma)", argG, g["feat_chroma_argmax"].Ints(), true);
            var chromaFm = new float[F * 12];
            for (int t = 0; t < F; t++) for (int c = 0; c < 12; c++) chromaFm[t * 12 + c] = chromaG[c * F + t];
            var all12 = Enumerable.Range(0, 12).ToArray();
            Cmp("flux(server chroma)", Spectral.SpectralFlux(chromaFm, 12, F, all12, 3, F <= chunk), g["chroma_flux_raw"].F4(), true);
            Cmp("rms pct99 scale", new[] { (double)NumpyCompat.Percentile32(g["rms_raw"].F4(), 99.0) }, new[] { g.Meta.GetProperty("rms_scale").GetDouble() }, true);
            var rt = new AnalysisTrace();
            double duration = g.Meta.GetProperty("duration").GetDouble();
            var rr = AudioAnalyzer.AnalyzeRhythm(envFull, g["env_low"].F4(), g["env_mid"].F4(), g["harmonic_change"].F4(),
                22050, 256, chunk, duration, rt);
            if (g.Has("tempogram_mean"))
            {
                Cmp("tempogram mean (iso)", rt.TempogramMean, g["tempogram_mean"].F8());
                Cmp("tempo objective (iso)", rt.TempoObjective, g["tempo_objective"].F8());
            }
            CmpScalar("tempo raw (iso)", rt.TempoRaw, g.Meta.GetProperty("tempo_raw").GetDouble(), true);
            if (rt.Beat != null && g.Has("beat_localscore"))
            {
                Cmp("beat norm env (iso)", rt.Beat.NormEnv, g["beat_norm_env"].F4(), true);
                Cmp("beat localscore (iso)", rt.Beat.LocalScore, g["beat_localscore"].F4(), true);
                Cmp("beat cumscore (iso)", rt.Beat.CumScore, g["beat_cumscore"].F8(), true);
                CmpInts("beat backlink (iso)", rt.Beat.Backlink, g["beat_backlink"].Ints(), true);
                CmpInts("beat untrimmed (iso)", rt.Beat.UntrimmedFrames, g["beat_frames_untrimmed"].Ints(), true);
            }
            CmpInts("beat frames (iso)", rt.BeatFrames, g["beat_frames"].Ints(), true);
            CmpInts("onset frames (iso)", rt.OnsetFrames, g["onset_frames"].Ints(), true);
            CmpScalar("tempo refined (iso)", rt.TempoRefined, g.Meta.GetProperty("tempo_refined").GetDouble(), true);
            CmpInts("downbeat (iso)", new[] { rr.DownbeatPhase }, new[] { g.Meta.GetProperty("downbeat_phase").GetInt32() }, true);

            Console.WriteLine("-- final AudioFeatures (full pipeline from PCM)");
            CmpScalar("duration", f.Duration, g.Meta.GetProperty("feat_duration").GetDouble(), true);
            CmpScalar("tempo", f.Tempo, g.Meta.GetProperty("feat_tempo").GetDouble(), true);
            CmpInts("downbeat_phase", new[] { f.DownbeatPhase }, new[] { g.Meta.GetProperty("feat_downbeat_phase").GetInt32() }, true);
            Cmp("beat_times", f.BeatTimes, g["feat_beat_times"].F8(), true);
            Cmp("frame_times", f.FrameTimes, g["feat_frame_times"].F8(), true);
            Cmp("onset_times", f.OnsetTimes, g["feat_onset_times"].F8(), true);
            Cmp("onset_strengths", f.OnsetStrengths, g["feat_onset_strengths"].F4());
            Cmp("onset_env", f.OnsetEnv, g["feat_onset_env"].F4());
            Cmp("onset_env_low", f.OnsetEnvLow, g["feat_onset_env_low"].F4());
            Cmp("onset_env_mid", f.OnsetEnvMid, g["feat_onset_env_mid"].F4());
            Cmp("onset_env_high", f.OnsetEnvHigh, g["feat_onset_env_high"].F4());
            Cmp("rms", f.Rms, g["feat_rms"].F4());
            Cmp("harmonic_rms", f.HarmonicRms, g["feat_harmonic_rms"].F4());
            Cmp("spectral_centroid", f.SpectralCentroid, g["feat_spectral_centroid"].F4());
            CmpInts("chroma_argmax", f.ChromaArgmax, g["feat_chroma_argmax"].Ints(), false);

            bool sameRuns = Sha(f.OnsetEnv) == Sha(fPlain.OnsetEnv) && f.BeatTimes.SequenceEqual(fPlain.BeatTimes)
                            && Sha(f.SpectralCentroid) == Sha(fPlain.SpectralCentroid);
            Console.WriteLine($"  traced vs untraced run    {(sameRuns ? "IDENTICAL" : "DIFFERENT")}");
            if (!sameRuns) Fail(name + " determinism");

            if (dump != null) DumpFeatures(Path.Combine(dump, name + ".features.lcg"), f, name);
        }

        internal static void DumpFeatures(string path, AudioFeatures f, string name)
        {
            var arrays = new List<(string, string, int[], byte[])>
            {
                ("beat_times", "f8", new[] { f.BeatTimes.Length }, GoldenFile.Bytes(f.BeatTimes)),
                ("frame_times", "f8", new[] { f.FrameTimes.Length }, GoldenFile.Bytes(f.FrameTimes)),
                ("onset_env", "f4", new[] { f.OnsetEnv.Length }, GoldenFile.Bytes(f.OnsetEnv)),
                ("onset_env_low", "f4", new[] { f.OnsetEnvLow.Length }, GoldenFile.Bytes(f.OnsetEnvLow)),
                ("onset_env_mid", "f4", new[] { f.OnsetEnvMid.Length }, GoldenFile.Bytes(f.OnsetEnvMid)),
                ("onset_env_high", "f4", new[] { f.OnsetEnvHigh.Length }, GoldenFile.Bytes(f.OnsetEnvHigh)),
                ("onset_times", "f8", new[] { f.OnsetTimes.Length }, GoldenFile.Bytes(f.OnsetTimes)),
                ("onset_strengths", "f4", new[] { f.OnsetStrengths.Length }, GoldenFile.Bytes(f.OnsetStrengths)),
                ("rms", "f4", new[] { f.Rms.Length }, GoldenFile.Bytes(f.Rms)),
                ("harmonic_rms", "f4", new[] { f.HarmonicRms.Length }, GoldenFile.Bytes(f.HarmonicRms)),
                ("spectral_centroid", "f4", new[] { f.SpectralCentroid.Length }, GoldenFile.Bytes(f.SpectralCentroid)),
                ("chroma_argmax", "i8", new[] { f.ChromaArgmax.Length }, GoldenFile.BytesI8(f.ChromaArgmax)),
            };
            string meta = "{" + $"\"case\":\"{name}\",\"sample_rate\":{f.SampleRate},\"hop_length\":{f.HopLength},"
                          + $"\"duration\":{f.Duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},"
                          + $"\"tempo\":{f.Tempo.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},"
                          + $"\"downbeat_phase\":{f.DownbeatPhase}" + "}";
            GoldenFile.Write(path, arrays, meta);
        }
    }
}
