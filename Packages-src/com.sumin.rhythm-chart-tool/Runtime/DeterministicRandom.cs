namespace SuminTools.RhythmChart
{
    /// <summary>
    /// 같은 시드에서 항상 같은 수열을 내는 난수 생성기(SplitMix64).
    /// System.Random 의 구현은 런타임/플랫폼에 따라 달라질 수 있어서 직접 구현했다. 정수 연산만 쓴다.
    /// </summary>
    internal sealed class DeterministicRandom
    {
        const ulong Increment = 0x9E3779B97F4A7C15UL;
        const ulong MixA = 0xBF58476D1CE4E5B9UL;
        const ulong MixB = 0x94D049BB133111EBUL;
        const double UnitScale = 1.0 / (1UL << 53);

        ulong state;

        public DeterministicRandom(int seed)
        {
            state = unchecked((ulong)(uint)seed);
        }

        /// <summary>[0, 1) 범위의 값.</summary>
        public double NextDouble()
        {
            return (NextUInt64() >> 11) * UnitScale;
        }

        /// <summary>[0, exclusiveMax) 범위의 정수. exclusiveMax 가 1 이하이면 0.</summary>
        public int NextInt(int exclusiveMax)
        {
            return exclusiveMax <= 1 ? 0 : (int)(NextDouble() * exclusiveMax);
        }

        /// <summary>[min, max] 범위(양끝 포함)의 정수.</summary>
        public int NextRange(int min, int max)
        {
            return max <= min ? min : min + NextInt(max - min + 1);
        }

        ulong NextUInt64()
        {
            unchecked
            {
                state += Increment;
                ulong z = state;
                z = (z ^ (z >> 30)) * MixA;
                z = (z ^ (z >> 27)) * MixB;
                return z ^ (z >> 31);
            }
        }
    }
}
