using System;

namespace SuminTools.RhythmChart
{
    /// <summary>난이도. 값은 배열 인덱스로도 쓰인다.</summary>
    public enum Difficulty
    {
        /// <summary>쉬움.</summary>
        Easy = 0,
        /// <summary>보통.</summary>
        Normal = 1,
        /// <summary>어려움.</summary>
        Hard = 2,
        /// <summary>최고 난이도.</summary>
        Expert = 3,
    }

    /// <summary>노트 종류.</summary>
    public enum NoteType
    {
        /// <summary>한 번 누르는 노트.</summary>
        Tap = 0,
        /// <summary>누르고 있는 노트. <see cref="NoteData.holdLength"/> 동안 유지한다.</summary>
        Hold = 1,
    }

    /// <summary>차트의 노트 하나.</summary>
    [Serializable]
    public struct NoteData
    {
        /// <summary>곡 시작 기준 노트 시각(초).</summary>
        public float time;

        /// <summary>레인 번호(0 부터). 차트의 레인 수 미만이어야 한다.</summary>
        public int lane;

        /// <summary>롱노트 유지 시간(초). <see cref="NoteType.Tap"/> 이면 0.</summary>
        public float holdLength;

        /// <summary>노트 종류.</summary>
        public NoteType type;
    }
}
