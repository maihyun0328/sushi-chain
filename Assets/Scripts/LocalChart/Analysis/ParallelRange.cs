// ─────────────────────────────────────────────────────────────
// 분석 루프 병렬화 헬퍼.
// 각 반복이 서로 다른 출력 위치에만 쓰는 루프(프레임별 STFT, 행/열별 median 등)만 병렬화한다.
// 반복 안의 연산 순서는 그대로이므로 결과는 순차 실행과 비트 단위로 같다 (서버 parity 유지).
// 스레드마다 FftPlan, 버퍼 등 가변 상태를 따로 갖도록 localInit을 쓴다.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SushiChain.LocalChart.Analysis
{
    internal static class ParallelRange
    {
        // 게임 메인 스레드와 오디오 스레드 몫으로 코어 2개를 남긴다
        internal static int MaxDegree => Math.Max(1, Environment.ProcessorCount - 2);

        // [from, to)를 블록으로 나눠 body(i, local)를 실행한다. local은 블록마다 init()으로 만든다.
        public static void For<TLocal>(int from, int to, Func<TLocal> init, Action<int, TLocal> body)
        {
            int n = to - from;
            if (n <= 0) return;
            int degree = MaxDegree;
            if (degree == 1 || n < 2)
            {
                TLocal local = init();
                for (int i = from; i < to; i++) body(i, local);
                return;
            }
            int blocks = Math.Min(n, degree * 4);
            int blockSize = (n + blocks - 1) / blocks;
            var options = new ParallelOptions { MaxDegreeOfParallelism = degree };
            Parallel.ForEach(Partitioner.Create(from, to, blockSize), options, range =>
            {
                TLocal local = init();
                for (int i = range.Item1; i < range.Item2; i++) body(i, local);
            });
        }

        public static void For(int from, int to, Action<int> body) =>
            For<object>(from, to, () => null, (i, _) => body(i));
    }
}
