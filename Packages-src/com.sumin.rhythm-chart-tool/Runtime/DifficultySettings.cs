using System;
using UnityEngine;

namespace SuminTools.RhythmChart
{
    /// <summary>노트를 놓을 박자 그리드의 촘촘함. 값은 한 박을 나누는 개수다.</summary>
    public enum GridSnap
    {
        /// <summary>4분음표.</summary>
        Quarter = 1,
        /// <summary>8분음표.</summary>
        Eighth = 2,
        /// <summary>16분음표.</summary>
        Sixteenth = 4,
    }

    /// <summary>한 난이도의 노트 생성 규칙. <see cref="ChartGenerator"/> 의 입력이다.</summary>
    [Serializable]
    public sealed class DifficultySettings
    {
        [Header("노트 선별")]
        [Tooltip("onset 을 strength 순으로 줄 세워 곡 안에서 강한 쪽 상위 몇 %만 쓸지.")]
        [Range(1f, 100f)] public float keepTopPercent = 40f;

        [Tooltip("노트를 이 그리드에 맞춘다. 같은 칸에 몰린 onset 은 가장 강한 것만 남는다.")]
        public GridSnap gridSnap = GridSnap.Eighth;

        [Tooltip("노트 사이 최소 간격(초). 이보다 가까운 노트는 약한 쪽을 버린다.")]
        [Range(0.05f, 1f)] public float minNoteInterval = 0.25f;

        [Header("롱노트")]
        [Tooltip("노트 중 롱노트로 바꿀 비율. 뒤에 빈 구간이 긴 노트부터 고른다.")]
        [Range(0f, 1f)] public float holdRatio = 0.1f;

        [Tooltip("롱노트 최소 길이(박).")]
        [Range(0.5f, 8f)] public float holdMinBeats = 1f;

        [Tooltip("롱노트 최대 길이(박).")]
        [Range(0.5f, 16f)] public float holdMaxBeats = 4f;

        [Header("동시치기")]
        [Tooltip("노트 중 두 레인을 동시에 치는 노트의 비율. 강한 노트부터 고른다.")]
        [Range(0f, 1f)] public float chordRatio = 0f;

        [Header("레인 배치")]
        [Tooltip("연속한 노트 사이에 허용하는 최대 레인 거리(동시치기 직후에는 완화될 수 있다).")]
        [Range(1, 5)] public int maxLaneJump = 1;

        [Tooltip("노트마다 계단/트릴 같은 패턴을 시작할 확률.")]
        [Range(0f, 1f)] public float patternChance = 0.2f;

        [Tooltip("계단(한 칸씩 이동) 패턴의 비중.")]
        [Range(0f, 1f)] public float stairsWeight = 1f;

        [Tooltip("트릴(두 레인 번갈아) 패턴의 비중.")]
        [Range(0f, 1f)] public float trillWeight = 0f;

        [Tooltip("패턴 최소 노트 수.")]
        [Range(3, 12)] public int patternMinLength = 3;

        [Tooltip("패턴 최대 노트 수.")]
        [Range(3, 12)] public int patternMaxLength = 6;

        /// <summary>난이도별 기본값. 24 Sorrows(150BPM) 한 곡에서 노트 밀도를 보고 정한 값이다.</summary>
        public static DifficultySettings CreateDefault(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy:
                    return new DifficultySettings
                    {
                        keepTopPercent = 25f, gridSnap = GridSnap.Quarter, minNoteInterval = 0.35f,
                        holdRatio = 0.12f, holdMinBeats = 1f, holdMaxBeats = 3f,
                        chordRatio = 0f, maxLaneJump = 1,
                        patternChance = 0.1f, stairsWeight = 1f, trillWeight = 0f,
                        patternMinLength = 3, patternMaxLength = 4,
                    };
                case Difficulty.Normal:
                    return new DifficultySettings
                    {
                        keepTopPercent = 45f, gridSnap = GridSnap.Eighth, minNoteInterval = 0.2f,
                        holdRatio = 0.12f, holdMinBeats = 1f, holdMaxBeats = 4f,
                        chordRatio = 0.05f, maxLaneJump = 2,
                        patternChance = 0.2f, stairsWeight = 0.8f, trillWeight = 0.2f,
                        patternMinLength = 3, patternMaxLength = 6,
                    };
                case Difficulty.Hard:
                    return new DifficultySettings
                    {
                        keepTopPercent = 70f, gridSnap = GridSnap.Sixteenth, minNoteInterval = 0.14f,
                        holdRatio = 0.1f, holdMinBeats = 1f, holdMaxBeats = 4f,
                        chordRatio = 0.12f, maxLaneJump = 3,
                        patternChance = 0.3f, stairsWeight = 0.6f, trillWeight = 0.4f,
                        patternMinLength = 4, patternMaxLength = 8,
                    };
                case Difficulty.Expert:
                    return new DifficultySettings
                    {
                        keepTopPercent = 95f, gridSnap = GridSnap.Sixteenth, minNoteInterval = 0.1f,
                        holdRatio = 0.08f, holdMinBeats = 0.75f, holdMaxBeats = 4f,
                        chordRatio = 0.2f, maxLaneJump = 4,
                        patternChance = 0.4f, stairsWeight = 0.4f, trillWeight = 0.6f,
                        patternMinLength = 4, patternMaxLength = 10,
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null);
            }
        }

        /// <summary>복사본을 만든다.</summary>
        public DifficultySettings Clone()
        {
            return (DifficultySettings)MemberwiseClone();
        }

        /// <summary>모든 필드가 같은지 비교한다.</summary>
        public bool ValuesEqual(DifficultySettings other)
        {
            return other != null
                && keepTopPercent == other.keepTopPercent && gridSnap == other.gridSnap
                && minNoteInterval == other.minNoteInterval
                && holdRatio == other.holdRatio && holdMinBeats == other.holdMinBeats
                && holdMaxBeats == other.holdMaxBeats && chordRatio == other.chordRatio
                && maxLaneJump == other.maxLaneJump && patternChance == other.patternChance
                && stairsWeight == other.stairsWeight && trillWeight == other.trillWeight
                && patternMinLength == other.patternMinLength && patternMaxLength == other.patternMaxLength;
        }
    }
}
