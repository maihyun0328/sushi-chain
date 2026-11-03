// ─────────────────────────────────────────────────────────────
// AudioSettings.dspTime을 연속 시계로 보간한다. UnityEngine을 쓰지 않는 순수 C#이라 Unity 없이 테스트할 수 있다.
//
// dspTime은 오디오 스레드가 DSP 버퍼 하나를 처리할 때마다 한 칸씩 갱신된다.
//   1024 샘플 @ 48kHz 기준으로 약 21ms 계단이 생긴다.
// AudioSource.time도 같은 방식으로 계단이 진다 (버퍼에 보낸 샘플 수로 계산).
// 그래서 프레임마다 읽으면 노트 위치와 판정 시각에 계단 크기만큼 지터가 섞인다.
//
// 방법
//   1. 프레임마다 (dspTime, realtime) 쌍을 샘플링하고, dspTime이 바뀐 순간의 offset = dsp - realtime을 기록한다.
//   2. dsp는 실제 오디오 시각의 하한값이라 offset 샘플은 항상 참값 이하다.
//      따라서 최근 window 안의 최대값이 참 offset에 가장 가깝다.
//   3. 오디오 시각 ≈ realtime + offset. realtime은 연속 시계라 계단이 없다.
//   - 두 시계 사이의 drift(ppm 단위)는 window를 계속 밀어서 따라간다.
//   - dsp가 멈췄다 재개되거나 리셋되면 (AudioSettings.Reset, 백그라운드 복귀) 샘플을 버리고 다시 잡는다.
//
// 참고
//   - https://itch.io/blog/744936/the-right-way-to-interpolate-the-audio-playback-time-unity
//   - https://discussions.unity.com/t/audiosource-time-innaccurate-in-android/665249
// ─────────────────────────────────────────────────────────────
using System;

public sealed class DspClockEstimator
{
    const int Capacity = 1024; // 256 샘플 버퍼 @48kHz면 초당 약 188틱. 2초 window를 담는다
    const double DiscontinuitySec = 0.05; // offset이 이만큼 줄어들면 시계가 끊긴 것으로 본다

    readonly double windowSec;
    readonly double[] sampleRt = new double[Capacity];
    readonly double[] sampleOffset = new double[Capacity];
    int head;   // 가장 오래된 샘플 위치
    int count;
    double lastDsp = double.NaN;
    double offset;

    public bool HasEstimate { get; private set; }
    public double Offset => offset;

    public DspClockEstimator(double windowSec = 2.0)
    {
        this.windowSec = windowSec;
    }

    public void Reset()
    {
        head = 0;
        count = 0;
        lastDsp = double.NaN;
        HasEstimate = false;
    }

    // 프레임마다 호출한다
    public void Sample(double dsp, double realtime)
    {
        if (dsp == lastDsp) return;

        double sample = dsp - realtime;
        bool discontinuous = !double.IsNaN(lastDsp) &&
            (dsp < lastDsp || (HasEstimate && sample < offset - DiscontinuitySec));
        if (discontinuous)
        {
            head = 0;
            count = 0;
        }
        lastDsp = dsp;

        if (count == Capacity)
        {
            head = (head + 1) % Capacity;
            count--;
        }
        int tail = (head + count) % Capacity;
        sampleRt[tail] = realtime;
        sampleOffset[tail] = sample;
        count++;

        // window 밖 샘플 제거 (최소 1개는 남긴다)
        while (count > 1 && realtime - sampleRt[head] > windowSec)
        {
            head = (head + 1) % Capacity;
            count--;
        }

        double max = double.NegativeInfinity;
        for (int k = 0; k < count; k++)
        {
            double o = sampleOffset[(head + k) % Capacity];
            if (o > max) max = o;
        }
        offset = max;
        HasEstimate = true;
    }

    // realtime(Time.realtimeSinceStartupAsDouble과 같은 시간축) → 보간된 dsp 시각
    public double ToDsp(double realtime) => realtime + offset;
}
