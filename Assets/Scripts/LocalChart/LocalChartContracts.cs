// ─────────────────────────────────────────────────────────────
// 온디바이스 채보 생성 — 공유 계약.
// ai-chart-server(app/audio/analysis.py, app/chart/*)를 C#으로 포팅한다. 목표는 서버와 동일한 결과다.
// 이 폴더(Assets/Scripts/LocalChart)는 순수 C#이다.
//   - UnityEngine을 참조하지 않는다 (asmdef noEngineReferences).
//   - netstandard2.1 API만 쓴다.
//   - 백그라운드 스레드에서 실행해도 안전하다.
// Unity 연동(파일 로드, 디코딩, UI)은 Assets/Scripts/CustomSong/Local/에 둔다 (Assembly-CSharp).
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    public static class LocalChartConstants
    {
        public const string ChartAlgoVersion = "1.0.0";   // app/core/constants.py CHART_ALGO_VERSION
        public const string FeatureVersion = "3";         // app/core/constants.py FEATURE_VERSION
        public const int AnalysisSampleRate = 22050;      // ANALYSIS_SAMPLE_RATE
        public const int AnalysisHopLength = 256;         // ANALYSIS_HOP_LENGTH
    }

    // app/audio/features.py AudioFeatures와 1:1로 대응한다. 배열 dtype은 서버 쪽 numpy와 같다.
    public sealed class AudioFeatures
    {
        public int SampleRate;
        public int HopLength;
        public double Duration;          // 초
        public double Tempo;             // BPM
        public double[] BeatTimes;       // (B,) 초
        public int DownbeatPhase;        // 0..3
        public double[] FrameTimes;      // (F,) 초
        public float[] OnsetEnv;         // (F,) [0,1]
        public float[] OnsetEnvLow;      // (F,)
        public float[] OnsetEnvMid;      // (F,)
        public float[] OnsetEnvHigh;     // (F,)
        public double[] OnsetTimes;      // (N,) 초, 오름차순
        public float[] OnsetStrengths;   // (N,)
        public float[] Rms;              // (F,) [0,1]
        public float[] HarmonicRms;      // (F,)
        public float[] SpectralCentroid; // (F,) Hz
        public int[] ChromaArgmax;       // (F,) 0..11
    }

    // Unity NoteData와 같은 의미. dir 0=LEFT 1=RIGHT, type L1=0 L2=1 R1=2 R2=3, EndTime 0 = 숏노트
    public struct LocalNote
    {
        public int Dir;
        public int Type;
        public double StartTime;
        public double EndTime;
    }

    public sealed class ChartGenerationException : Exception
    {
        public ChartGenerationException(string message) : base(message) { }
    }

    // ===== 트랙별 구현 진입점 =====
    // Analysis 트랙: AudioAnalyzer.Analyze(float[] mono22050) → AudioFeatures
    //                (app/audio/analysis.py analyze_audio와 동일)
    // Engine 트랙:   ChartEngine.Generate(AudioFeatures, string difficulty, string seedKey) → List<LocalNote>
    //                (app/chart/generator.py generate_chart와 동일)
    //                difficulty는 "easy" | "normal" | "hard"
    //                seedKey는 서버와 같은 형식: chart_cache_key(source_key, difficulty)
    //                  → "upload:<sha256 hex>|<difficulty>|1.0.0" (로컬 파일은 업로드와 같은 source_key 규칙)
    // Input 트랙:    Unity 쪽 파일 선택, 디코딩, 리샘플, 저장, UI (Assets/Scripts/CustomSong/Local/)

    public static class SeedKeys
    {
        public static string ForLocalFile(string sha256Hex, string difficulty) =>
            $"upload:{sha256Hex}|{difficulty}|{LocalChartConstants.ChartAlgoVersion}";
    }
}
