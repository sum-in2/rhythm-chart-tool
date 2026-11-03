using System.Collections.Generic;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>한 난이도의 통계.</summary>
    internal readonly struct ChartStats
    {
        public ChartStats(int noteCount, float notesPerSecond, float holdRatio)
        {
            NoteCount = noteCount;
            NotesPerSecond = notesPerSecond;
            HoldRatio = holdRatio;
        }

        public int NoteCount { get; }

        /// <summary>곡 전체 길이 기준 초당 노트 수.</summary>
        public float NotesPerSecond { get; }

        /// <summary>노트 중 롱노트의 비율(0~1).</summary>
        public float HoldRatio { get; }

        public static ChartStats Compute(IReadOnlyList<NoteData> notes, float duration)
        {
            int holds = 0;
            foreach (NoteData note in notes)
                if (note.type == NoteType.Hold)
                    holds++;
            int count = notes.Count;
            return new ChartStats(
                count,
                duration > 0f ? count / duration : 0f,
                count > 0 ? holds / (float)count : 0f);
        }
    }
}
