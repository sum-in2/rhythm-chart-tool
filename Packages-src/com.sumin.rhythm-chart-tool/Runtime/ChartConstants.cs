namespace SuminTools.RhythmChart
{
    /// <summary>차트 전반에서 쓰는 상수.</summary>
    public static class ChartConstants
    {
        /// <summary>설정 가능한 최소 레인 수.</summary>
        public const int MinLaneCount = 4;

        /// <summary>설정 가능한 최대 레인 수.</summary>
        public const int MaxLaneCount = 6;

        /// <summary>새 차트의 기본 레인 수.</summary>
        public const int DefaultLaneCount = 4;

        /// <summary>난이도 개수(<see cref="Difficulty"/> 의 값 수).</summary>
        public const int DifficultyCount = 4;

        /// <summary>새 차트의 기본 생성 시드.</summary>
        public const int DefaultSeed = 12345;

        /// <summary>BPM 을 알 수 없을 때 박자 길이 대신 쓰는 값(초). 120BPM 에 해당한다.</summary>
        public const float FallbackBeatSeconds = 0.5f;
    }
}
