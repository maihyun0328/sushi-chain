// ─────────────────────────────────────────────────────────────
// app/audio/analysis.py analyze_audio의 C# 포팅.
// 입력: app.audio.decode.decode_mono와 같은 mono float32 PCM, 22050 Hz.
// 출력: AudioFeatures (app/audio/features.py와 1:1).
// 순수 C#, 정적 가변 상태 없음 → 여러 스레드에서 동시에 호출해도 안전하다.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using SushiChain.LocalChart.Analysis;

namespace SushiChain.LocalChart
{
    public static class AudioAnalyzer
    {
        internal const int NMels = 96;
        internal const double MelFmax = 11025.0;
        internal const int OnsetLatencyFrames = 3;
        internal const double LowBandMaxHz = 200.0;
        internal const double HighBandMinHz = 4000.0;
        internal const int ChunkFrames = 4096;
        internal const double EnvNormPercentile = 99.5;
        internal const double RmsNormPercentile = 99.0;
        private const double Eps = 1e-10;

        public static AudioFeatures Analyze(float[] mono22050)
        {
            return Analyze(mono22050, ChunkFrames, null);
        }

        // chunkFrames: analysis.CHUNK_FRAMES (tests use a smaller value to exercise the multi-chunk path).
        internal static AudioFeatures Analyze(float[] y, int chunkFrames, AnalysisTrace trace)
        {
            if (y == null) throw new ArgumentNullException(nameof(y));
            int sr = LocalChartConstants.AnalysisSampleRate;
            int hop = LocalChartConstants.AnalysisHopLength;

            float[] x = Preprocess(y);
            if (trace != null) trace.Preprocessed = x;
            double duration = (double)x.Length / sr;
            if (x.Length < Spectral.NFft)
            {
                var p = new float[Spectral.NFft];
                Array.Copy(x, p, x.Length);
                x = p;
            }

            // --- STFT magnitude, RMS, centroid -------------------------------------------
            float[] mag = Spectral.StftMagnitude(x, hop, out int nFrames);
            float[] rmsRaw = Spectral.RmsFromMagnitude(mag, nFrames);
            float[] centroid = Spectral.Centroid(mag, nFrames, sr, out var magSum, out var centroidNum);
            if (trace != null)
            {
                trace.Mag = mag; trace.NFrames = nFrames; trace.RmsRaw = rmsRaw; trace.Centroid = centroid;
                trace.MagSum = magSum; trace.CentroidNum = centroidNum;
            }

            // --- HPSS ------------------------------------------------------------------------
            float[] chromaBasis = Spectral.ChromaBasis(sr);
            var hp = Hpss.Run(mag, nFrames, chunkFrames, chromaBasis, trace != null);
            mag = null;
            var chromaArgmax = new int[nFrames];
            for (int t = 0; t < nFrames; t++)
            {
                int o = t * 12, best = 0;
                for (int c = 1; c < 12; c++) if (hp.Chroma[o + c] > hp.Chroma[o + best]) best = c;
                chromaArgmax[t] = best;
            }
            var all12 = new int[12];
            for (int i = 0; i < 12; i++) all12[i] = i;
            // Single-chunk chroma comes out of einsum Fortran-ordered (pairwise row sum); the multi-chunk
            // path stitches into a C-ordered array (sequential row sum).
            float[] chromaFlux = Spectral.SpectralFlux(hp.Chroma, 12, nFrames, all12, OnsetLatencyFrames, nFrames <= chunkFrames);
            float[] harmonicChange = Spectral.RobustUnit(chromaFlux, EnvNormPercentile);
            if (trace != null)
            {
                trace.Hpss = hp; trace.ChromaFlux = chromaFlux; trace.HarmonicChange = harmonicChange;
            }

            // --- percussive log-mel + band onset envelopes ----------------------------------
            double fmax = Math.Min(MelFmax, sr / 2.0);
            float[] melBasis = Spectral.MelBasis(sr, NMels, fmax, out var mFirst, out var mLast);
            float[] perc = hp.Percussive;
            var mel = new float[(long)nFrames * NMels];
            float melMax = float.NegativeInfinity;
            for (int t = 0; t < nFrames; t++)
            {
                int po = t * Spectral.Bins;
                int mo = t * NMels;
                for (int m = 0; m < NMels; m++)
                {
                    float acc = 0f;
                    int bo = m * Spectral.Bins;
                    for (int k = mFirst[m]; k <= mLast[m]; k++)
                    {
                        float v = perc[po + k];
                        acc = NumpyCompat.Fma32(melBasis[bo + k], (float)(v * v), acc);
                    }
                    mel[mo + m] = acc;
                    if (acc > melMax) melMax = acc;
                }
            }
            if (trace != null) trace.Mel = (float[])mel.Clone();
            double reference = mel.Length > 0 ? melMax : 1.0;
            Spectral.PowerToDbInPlace(mel, reference);
            float[] melDb = mel;
            if (trace != null) { trace.MelDb = melDb; trace.MelRef = reference; }

            double[] centers = Spectral.MelFrequencies(NMels + 2, fmax);
            var lowRows = new List<int>();
            var midRows = new List<int>();
            var highRows = new List<int>();
            var fullRows = new int[NMels];
            for (int i = 0; i < NMels; i++)
            {
                fullRows[i] = i;
                double c = centers[i + 1];
                if (c < LowBandMaxHz) lowRows.Add(i);
                else if (c >= HighBandMinHz) highRows.Add(i);
                else midRows.Add(i);
            }
            float[] fluxFull = Spectral.SpectralFlux(melDb, NMels, nFrames, fullRows, OnsetLatencyFrames);
            float[] fluxLow = Spectral.SpectralFlux(melDb, NMels, nFrames, lowRows.ToArray(), OnsetLatencyFrames);
            float[] fluxMid = Spectral.SpectralFlux(melDb, NMels, nFrames, midRows.ToArray(), OnsetLatencyFrames);
            float[] fluxHigh = Spectral.SpectralFlux(melDb, NMels, nFrames, highRows.ToArray(), OnsetLatencyFrames);
            float[] onsetEnv = Spectral.RobustUnit(fluxFull, EnvNormPercentile);
            float[] onsetEnvLow = Spectral.RobustUnit(fluxLow, EnvNormPercentile);
            float[] onsetEnvMid = Spectral.RobustUnit(fluxMid, EnvNormPercentile);
            float[] onsetEnvHigh = Spectral.RobustUnit(fluxHigh, EnvNormPercentile);
            if (trace != null)
            {
                trace.FluxFull = fluxFull; trace.FluxLow = fluxLow; trace.FluxMid = fluxMid; trace.FluxHigh = fluxHigh;
            }

            // --- tempo + beats ---------------------------------------------------------------
            var rh = AnalyzeRhythm(onsetEnv, onsetEnvLow, onsetEnvMid, harmonicChange, sr, hop, chunkFrames, duration, trace);

            // --- energy normalization ---------------------------------------------------------
            double rmsScale = rmsRaw.Length > 0 ? NumpyCompat.Percentile32(rmsRaw, RmsNormPercentile) : 0.0;
            var rms = new float[nFrames];
            var harmonicRms = new float[nFrames];
            if (rmsScale > Eps)
            {
                float s = (float)rmsScale;
                for (int t = 0; t < nFrames; t++)
                {
                    rms[t] = Clip01((float)(rmsRaw[t] / s));
                    harmonicRms[t] = Clip01((float)(hp.HarmonicRmsRaw[t] / s));
                }
            }
            if (trace != null) trace.RmsScale = rmsScale;

            var frameTimes = new double[nFrames];
            for (int t = 0; t < nFrames; t++) frameTimes[t] = FramesToTime(t, sr, hop);

            return new AudioFeatures
            {
                SampleRate = sr,
                HopLength = hop,
                Duration = duration,
                Tempo = NumpyCompat.PyRound(rh.Tempo, 3),
                BeatTimes = rh.BeatTimes,
                DownbeatPhase = rh.DownbeatPhase,
                FrameTimes = frameTimes,
                OnsetEnv = onsetEnv,
                OnsetEnvLow = onsetEnvLow,
                OnsetEnvMid = onsetEnvMid,
                OnsetEnvHigh = onsetEnvHigh,
                OnsetTimes = rh.OnsetTimes,
                OnsetStrengths = rh.OnsetStrengths,
                Rms = rms,
                HarmonicRms = harmonicRms,
                SpectralCentroid = centroid,
                ChromaArgmax = chromaArgmax,
            };
        }

        internal sealed class RhythmResult
        {
            public double Tempo;
            public double[] BeatTimes;
            public int DownbeatPhase;
            public double[] OnsetTimes;
            public float[] OnsetStrengths;
        }

        // Steps 5-7 of analyze_audio from the onset envelopes (separately testable on server envelopes).
        internal static RhythmResult AnalyzeRhythm(float[] onsetEnv, float[] low, float[] mid, float[] harmonicChange,
            int sr, int hop, int chunkFrames, double duration, AnalysisTrace trace)
        {
            double tempoRaw = Rhythm.EstimateTempo(onsetEnv, sr, hop, chunkFrames, out var tgMean, out var objective);
            double tempo0 = Rhythm.FoldTempo(tempoRaw);
            int[] beatFrames;
            Rhythm.BeatTrace bt = trace != null ? new Rhythm.BeatTrace() : null;
            if (tempo0 > 0)
                beatFrames = Rhythm.BeatTrack(onsetEnv, sr, hop, tempo0, Rhythm.BeatTightness, bt);
            else
                beatFrames = new int[0];
            var beatTimesAll = new double[beatFrames.Length];
            for (int i = 0; i < beatFrames.Length; i++) beatTimesAll[i] = FramesToTime(beatFrames[i], sr, hop);
            double tempo = Rhythm.RefineTempo(beatTimesAll, tempo0);
            int downbeat = Rhythm.DownbeatPhase(beatFrames, low, mid, harmonicChange, out var dbScores);

            int[] onsetFrames = Rhythm.Any(onsetEnv) ? Rhythm.OnsetDetect(onsetEnv, sr, hop) : new int[0];
            var onsetTimes = new List<double>();
            var onsetStrengths = new List<float>();
            for (int i = 0; i < onsetFrames.Length; i++)
            {
                double t = FramesToTime(onsetFrames[i], sr, hop);
                if (t <= duration)
                {
                    onsetTimes.Add(t);
                    onsetStrengths.Add(onsetEnv[onsetFrames[i]]);
                }
            }
            var beatTimes = new List<double>();
            foreach (double t in beatTimesAll) if (t <= duration) beatTimes.Add(t);

            if (trace != null)
            {
                trace.TempoRaw = tempoRaw; trace.Tempo0 = tempo0; trace.TempogramMean = tgMean; trace.TempoObjective = objective;
                trace.Beat = bt; trace.BeatFrames = beatFrames; trace.TempoRefined = tempo; trace.DownbeatScores = dbScores;
                trace.OnsetFrames = onsetFrames;
            }
            return new RhythmResult
            {
                Tempo = tempo,
                BeatTimes = beatTimes.ToArray(),
                DownbeatPhase = downbeat,
                OnsetTimes = onsetTimes.ToArray(),
                OnsetStrengths = onsetStrengths.ToArray(),
            };
        }

        // librosa.frames_to_time: (frames * hop) / float(sr)
        internal static double FramesToTime(long frame, int sr, int hop) => (double)(frame * hop) / sr;

        private static float Clip01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // analysis.preprocess: float32, NaN/inf -> 0, DC removal (float64 mean), peak normalisation.
        internal static float[] Preprocess(float[] y)
        {
            var x = new float[y.Length];
            for (int i = 0; i < y.Length; i++)
            {
                float v = y[i];
                x[i] = (float.IsNaN(v) || float.IsInfinity(v)) ? 0f : v;
            }
            if (x.Length > 0)
            {
                float mean = (float)NumpyCompat.MeanAsFloat64(x);
                float peak = 0f;
                for (int i = 0; i < x.Length; i++)
                {
                    x[i] = (float)(x[i] - mean);
                    float a = Math.Abs(x[i]);
                    if (a > peak) peak = a;
                }
                if (peak > Eps)
                {
                    float g = (float)(1.0 / peak);
                    for (int i = 0; i < x.Length; i++) x[i] = (float)(x[i] * g);
                }
            }
            return x;
        }
    }

    // Intermediates for the parity tests (null in production).
    internal sealed class AnalysisTrace
    {
        public float[] Preprocessed;
        public float[] Mag;
        public int NFrames;
        public float[] RmsRaw, Centroid, MagSum, CentroidNum;
        public Hpss.Result Hpss;
        public float[] ChromaFlux, HarmonicChange;
        public float[] Mel, MelDb;
        public double MelRef;
        public float[] FluxFull, FluxLow, FluxMid, FluxHigh;
        public double TempoRaw, Tempo0, TempoRefined, RmsScale;
        public double[] TempogramMean, TempoObjective, DownbeatScores;
        public Rhythm.BeatTrace Beat;
        public int[] BeatFrames, OnsetFrames;
    }
}
