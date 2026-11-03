// DELETE ON MERGE
// Feature/LocalChart-Input 브랜치에서만 컴파일용으로 쓰는 임시 stub.
// 실제 구현은 Feature/LocalChart-Analysis(AudioAnalyzer)와 Feature/LocalChart-Engine(ChartEngine)에 있다.
using System;
using System.Collections.Generic;

namespace SushiChain.LocalChart
{
    public static class AudioAnalyzer
    {
        public static AudioFeatures Analyze(float[] mono22050) =>
            throw new NotImplementedException("AudioAnalyzer stub (Feature/LocalChart-Analysis)");
    }

    public static class ChartEngine
    {
        public static List<LocalNote> Generate(AudioFeatures features, string difficulty, string seedKey) =>
            throw new NotImplementedException("ChartEngine stub (Feature/LocalChart-Engine)");
    }
}
