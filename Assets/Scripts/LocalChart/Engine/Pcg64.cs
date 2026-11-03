// numpy.random.default_rng(int) 의 비트 단위 재현: SeedSequence(pool 4) -> PCG64 (XSL-RR 128/64) -> Generator.random().
using System;
using System.Security.Cryptography;
using System.Text;

namespace SushiChain.LocalChart
{
    internal static class SeedSequence
    {
        const uint InitA = 0x43b0d7e5;
        const uint MultA = 0x931e8875;
        const uint InitB = 0x8b51f9dd;
        const uint MultB = 0x58f38ded;
        const uint MixMultL = 0xca01f9dd;
        const uint MixMultR = 0x4973f715;
        const int XShift = 16;
        const int PoolSize = 4;

        static uint HashMix(uint value, ref uint hashConst)
        {
            value ^= hashConst;
            unchecked { hashConst *= MultA; value *= hashConst; }
            value ^= value >> XShift;
            return value;
        }

        static uint Mix(uint x, uint y)
        {
            uint result = unchecked(MixMultL * x - MixMultR * y);
            result ^= result >> XShift;
            return result;
        }

        static uint[] EntropyWords(ulong seed)
        {
            if (seed == 0) return new uint[] { 0 };
            if (seed <= uint.MaxValue) return new uint[] { (uint)seed };
            return new uint[] { (uint)(seed & 0xFFFFFFFFUL), (uint)(seed >> 32) };
        }

        // SeedSequence(seed).generate_state(4, np.uint64)
        public static ulong[] GenerateState4x64(ulong seed)
        {
            uint[] entropy = EntropyWords(seed);
            var pool = new uint[PoolSize];
            uint hc = InitA;
            for (int i = 0; i < PoolSize; i++)
                pool[i] = HashMix(i < entropy.Length ? entropy[i] : 0u, ref hc);
            for (int src = 0; src < PoolSize; src++)
                for (int dst = 0; dst < PoolSize; dst++)
                    if (src != dst) pool[dst] = Mix(pool[dst], HashMix(pool[src], ref hc));
            for (int src = PoolSize; src < entropy.Length; src++)
                for (int dst = 0; dst < PoolSize; dst++)
                    pool[dst] = Mix(pool[dst], HashMix(entropy[src], ref hc));

            var words = new uint[8];
            uint hc2 = InitB;
            for (int i = 0; i < 8; i++)
            {
                uint v = pool[i % PoolSize];
                v ^= hc2;
                unchecked { hc2 *= MultB; v *= hc2; }
                v ^= v >> XShift;
                words[i] = v;
            }
            var state = new ulong[4];
            for (int k = 0; k < 4; k++) state[k] = words[2 * k] | ((ulong)words[2 * k + 1] << 32);
            return state;
        }
    }

    // PCG64 bit generator + Generator.random(). Not thread-safe per instance; each chart owns one.
    internal sealed class Pcg64
    {
        const ulong MultHi = 0x2360ED051FC65DA4UL;
        const ulong MultLo = 0x4385DF649FCCF645UL;

        ulong _stateHi, _stateLo;
        readonly ulong _incHi, _incLo;

        public Pcg64(ulong seed)
        {
            ulong[] s = SeedSequence.GenerateState4x64(seed);
            ulong initHi = s[0], initLo = s[1];
            ulong seqHi = s[2], seqLo = s[3];
            // inc = (initseq << 1) | 1
            _incHi = (seqHi << 1) | (seqLo >> 63);
            _incLo = (seqLo << 1) | 1UL;
            _stateHi = 0; _stateLo = 0;
            Step();
            Add(ref _stateHi, ref _stateLo, initHi, initLo);
            Step();
        }

        static void Add(ref ulong hi, ref ulong lo, ulong bHi, ulong bLo)
        {
            ulong l = unchecked(lo + bLo);
            ulong carry = l < lo ? 1UL : 0UL;
            hi = unchecked(hi + bHi + carry);
            lo = l;
        }

        static void Mul64(ulong a, ulong b, out ulong hi, out ulong lo)
        {
            ulong aL = a & 0xFFFFFFFFUL, aH = a >> 32;
            ulong bL = b & 0xFFFFFFFFUL, bH = b >> 32;
            ulong ll = aL * bL;
            ulong lh = aL * bH;
            ulong hl = aH * bL;
            ulong hh = aH * bH;
            ulong mid = (ll >> 32) + (lh & 0xFFFFFFFFUL) + (hl & 0xFFFFFFFFUL);
            lo = (ll & 0xFFFFFFFFUL) | (mid << 32);
            hi = hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
        }

        void Step()
        {
            // state = state * MULT + inc (mod 2^128)
            Mul64(_stateLo, MultLo, out ulong hi, out ulong lo);
            unchecked { hi += _stateHi * MultLo + _stateLo * MultHi; }
            _stateHi = hi; _stateLo = lo;
            Add(ref _stateHi, ref _stateLo, _incHi, _incLo);
        }

        public ulong NextUInt64()
        {
            Step();
            int rot = (int)(_stateHi >> 58);
            ulong x = _stateHi ^ _stateLo;
            return (x >> rot) | (x << ((-rot) & 63));
        }

        // Generator.random(): (next_uint64 >> 11) * 2^-53
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        // app/chart/generator.py seed_from_key: sha256(utf-8) -> first 8 bytes big-endian.
        public static ulong SeedFromKey(string seedKey)
        {
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes(seedKey ?? string.Empty));
            ulong v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | digest[i];
            return v;
        }
    }
}
