// 인터리브 PCM → mono. ffmpeg `-ac 1`(libswresample 기본 매트릭스)과 같은 계수를 쓴다.
// 순수 C# (UnityEngine 없음). 백그라운드 스레드에서 호출해도 안전하다.
//
// libswresample 기본값(center/surround mix level -3 dB, LFE 0, float 출력이라 정규화 없음)을 측정한 결과:
//   stereo → mono = (L + R) * sqrt(1/2)            (0.5 * (L + R)가 아니다. ffmpeg 9.0.1로 측정)
//   5.1    → mono = (FL + FR) * sqrt(1/2) + FC + (SL + SR) * 0.5   (LFE 제외)
// 채널 순서는 Unity(FMOD) 기준: FL FR FC LFE SL SR [BL BR].
using System;
using System.Threading;

namespace SushiChain.LocalChart.Input
{
    public static class AudioDownmix
    {
        const double Sqrt1_2 = 0.70710678118654752440; // M_SQRT1_2

        // 채널 수별 계수. 0이면 그 채널은 더하지 않는다 (swr의 matrix_ch는 0이 아닌 계수만 순서대로 더한다)
        public static float[] Coefficients(int channels)
        {
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
            float h = (float)Sqrt1_2;
            switch (channels)
            {
                case 1: return new[] { 1f };
                case 2: return new[] { h, h };
                case 3: return new[] { h, h, 1f };                                  // FL FR FC
                case 4: return new[] { h, h, 0.5f, 0.5f };                          // FL FR SL SR (quad)
                case 5: return new[] { h, h, 1f, 0.5f, 0.5f };                      // FL FR FC SL SR
                case 6: return new[] { h, h, 1f, 0f, 0.5f, 0.5f };                  // 5.1
                case 8: return new[] { h, h, 1f, 0f, 0.5f, 0.5f, 0.5f, 0.5f };      // 7.1
                default:
                {
                    // 알 수 없는 배치: 단순 평균
                    var c = new float[channels];
                    for (int k = 0; k < channels; k++) c[k] = 1f / channels;
                    return c;
                }
            }
        }

        // interleaved.Length는 channels의 배수여야 한다 (나머지는 버린다)
        public static float[] ToMono(float[] interleaved, int channels, CancellationToken ct = default)
        {
            if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));
            float[] coef = Coefficients(channels);
            int frames = interleaved.Length / channels;
            var mono = new float[frames];
            if (channels == 1)
            {
                Array.Copy(interleaved, mono, frames);
                return mono;
            }
            if (channels == 2)
            {
                float c0 = coef[0], c1 = coef[1];
                for (int n = 0; n < frames; n++)
                {
                    if ((n & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                    // 곱 두 개를 각각 float로 반올림한 뒤 더한다 (swr mix_2_1 C 구현과 같은 순서)
                    float a = interleaved[2 * n] * c0;
                    float b = interleaved[2 * n + 1] * c1;
                    mono[n] = a + b;
                }
                return mono;
            }
            for (int n = 0; n < frames; n++)
            {
                if ((n & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                int b = n * channels;
                float v = 0f;
                for (int k = 0; k < channels; k++)
                {
                    if (coef[k] == 0f) continue;
                    float p = interleaved[b + k] * coef[k];
                    v += p;
                }
                mono[n] = v;
            }
            return mono;
        }
    }
}
