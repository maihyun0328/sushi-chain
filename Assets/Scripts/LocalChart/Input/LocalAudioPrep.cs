// 디코딩된 PCM(인터리브, 임의 샘플레이트) → 분석 입력(mono float32 @ 22050 Hz).
// 서버의 ffmpeg `-ac 1 -ar 22050`과 같은 순서: 다운믹스 → 리샘플. 순수 C#, 스레드 안전.
using System;
using System.Threading;

namespace SushiChain.LocalChart.Input
{
    public static class LocalAudioPrep
    {
        public static float[] ToMono22050(float[] interleaved, int channels, int sampleRate, CancellationToken ct = default)
        {
            if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            float[] mono = AudioDownmix.ToMono(interleaved, channels, ct);
            ct.ThrowIfCancellationRequested();
            float[] y = SwrResampler.Resample(mono, sampleRate, LocalChartConstants.AnalysisSampleRate, ct);
            // decode_mono와 같이 비유한값은 0으로
            for (int i = 0; i < y.Length; i++)
                if (float.IsNaN(y[i]) || float.IsInfinity(y[i])) y[i] = 0f;
            return y;
        }
    }
}
