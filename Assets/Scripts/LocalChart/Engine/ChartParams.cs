// app/chart/params.py 포팅. 값을 바꾸면 서버와 결과가 달라진다 (CHART_ALGO_VERSION 1.0.0 기준).
using System;

namespace SushiChain.LocalChart
{
    internal enum MetricLevel
    {
        Downbeat = 0,
        Beat = 1,
        Eighth = 2,
        Sixteenth = 3,
        Triplet = 4,
    }

    internal sealed class DifficultyParams
    {
        public string Difficulty;
        public MetricLevel BaseLevel;
        public MetricLevel? StrongLevel;
        public double StrongThreshold;
        public bool UseTriplets;
        public double TripletThreshold;
        public double BaseRate;
        public double MinGap;
        public int WindowBeats;
        public double EnergyExponent;
        public double EnergyFactorMin;
        public double EnergyFactorMax;
        public double MinStrength;
        public int MaxChord;
        public double ChordMinStrength;
        public MetricLevel[] ChordLevels;
        public double ChordMaxRatio;
        public double ChordMinSpacing;
        public double JackMin;
        public double LongMinLen;
        public double LongMaxLen;
        public double LongMaxRatio;
        public bool LongSameSideFree;

        public bool IsChordLevel(MetricLevel level) => Array.IndexOf(ChordLevels, level) >= 0;
    }

    internal static class ChartParams
    {
        public const double LeadInSeconds = 2.0;
        public const double TailMarginSeconds = 0.05;
        public const double TimeEpsilon = 0.0015;
        public const double SnapFraction = 0.35;
        public const double SnapMaxSeconds = 0.060;

        public static double MetricWeight(MetricLevel level)
        {
            switch (level)
            {
                case MetricLevel.Downbeat: return 0.35;
                case MetricLevel.Beat: return 0.25;
                case MetricLevel.Eighth: return 0.12;
                case MetricLevel.Sixteenth: return 0.0;
                default: return -0.05;
            }
        }

        // 호출마다 새 인스턴스 (정적 가변 상태 없음).
        public static DifficultyParams For(string difficulty)
        {
            switch (difficulty)
            {
                case "easy":
                    return new DifficultyParams
                    {
                        Difficulty = "easy", BaseLevel = MetricLevel.Beat, StrongLevel = MetricLevel.Eighth,
                        StrongThreshold = 0.85, UseTriplets = false, TripletThreshold = 1.1,
                        BaseRate = 1.2, MinGap = 0.33, WindowBeats = 8, EnergyExponent = 0.8,
                        EnergyFactorMin = 0.4, EnergyFactorMax = 1.25, MinStrength = 0.15,
                        MaxChord = 1, ChordMinStrength = 1.1, ChordLevels = new MetricLevel[0],
                        ChordMaxRatio = 0.0, ChordMinSpacing = double.PositiveInfinity,
                        JackMin = 0.5, LongMinLen = 0.8, LongMaxLen = 3.0, LongMaxRatio = 0.12, LongSameSideFree = true,
                    };
                case "normal":
                    return new DifficultyParams
                    {
                        Difficulty = "normal", BaseLevel = MetricLevel.Eighth, StrongLevel = MetricLevel.Sixteenth,
                        StrongThreshold = 0.8, UseTriplets = false, TripletThreshold = 1.1,
                        BaseRate = 2.3, MinGap = 0.18, WindowBeats = 8, EnergyExponent = 0.8,
                        EnergyFactorMin = 0.4, EnergyFactorMax = 1.25, MinStrength = 0.1,
                        MaxChord = 2, ChordMinStrength = 0.85, ChordLevels = new[] { MetricLevel.Downbeat },
                        ChordMaxRatio = 0.03, ChordMinSpacing = 4.0,
                        JackMin = 0.3, LongMinLen = 0.6, LongMaxLen = 3.0, LongMaxRatio = 0.12, LongSameSideFree = true,
                    };
                case "hard":
                    return new DifficultyParams
                    {
                        Difficulty = "hard", BaseLevel = MetricLevel.Sixteenth, StrongLevel = null,
                        StrongThreshold = 1.1, UseTriplets = true, TripletThreshold = 0.5,
                        BaseRate = 3.4, MinGap = 0.09, WindowBeats = 8, EnergyExponent = 0.7,
                        EnergyFactorMin = 0.45, EnergyFactorMax = 1.2, MinStrength = 0.05,
                        MaxChord = 2, ChordMinStrength = 0.7, ChordLevels = new[] { MetricLevel.Downbeat, MetricLevel.Beat },
                        ChordMaxRatio = 0.06, ChordMinSpacing = 1.0,
                        JackMin = 0.18, LongMinLen = 0.45, LongMaxLen = 3.0, LongMaxRatio = 0.12, LongSameSideFree = false,
                    };
                default:
                    throw new ArgumentException($"'{difficulty}' is not a valid Difficulty", nameof(difficulty));
            }
        }
    }
}
