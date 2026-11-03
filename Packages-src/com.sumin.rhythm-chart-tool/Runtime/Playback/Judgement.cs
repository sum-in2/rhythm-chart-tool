using System;
using UnityEngine;

namespace SuminTools.RhythmChart
{
    /// <summary>노트 하나의 판정 결과.</summary>
    public enum Judgement
    {
        /// <summary>정확하게 쳤다.</summary>
        Perfect = 0,
        /// <summary>조금 어긋나게 쳤다.</summary>
        Good = 1,
        /// <summary>놓쳤거나 롱노트를 일찍 놓았다.</summary>
        Miss = 2,
    }

    /// <summary>노트가 어떤 경위로 판정되었는가.</summary>
    public enum JudgementKind
    {
        /// <summary>탭 노트를 쳤거나 롱노트를 끝까지 눌렀다.</summary>
        Hit = 0,
        /// <summary>시간 안에 치지 못했다.</summary>
        Missed = 1,
        /// <summary>롱노트를 끝나기 전에 놓았다.</summary>
        Broken = 2,
    }

    /// <summary>한 노트의 최종 판정(노트마다 정확히 한 번 발생한다).</summary>
    public readonly struct NoteJudgement
    {
        public NoteJudgement(int noteIndex, NoteData note, Judgement result, JudgementKind kind, double timingError)
        {
            NoteIndex = noteIndex;
            Note = note;
            Result = result;
            Kind = kind;
            TimingError = timingError;
        }

        /// <summary>정렬된 노트 목록에서의 번호.</summary>
        public int NoteIndex { get; }

        public NoteData Note { get; }

        public Judgement Result { get; }

        public JudgementKind Kind { get; }

        /// <summary>친 시각 − 노트 시각(초). 양수면 늦게 친 것. 놓친 노트는 0.</summary>
        public double TimingError { get; }
    }

    /// <summary>판정 창과 보정값.</summary>
    [Serializable]
    public sealed class JudgementSettings
    {
        [Tooltip("노트 시각 ± 이 값(초) 안에서 치면 Perfect.")]
        [Range(0.01f, 0.2f)] public float perfectWindow = 0.05f;

        [Tooltip("노트 시각 ± 이 값(초) 안에서 치면 Good. 이보다 늦으면 Miss, 이보다 이르면 입력을 무시한다.")]
        [Range(0.02f, 0.4f)] public float goodWindow = 0.12f;

        [Tooltip("롱노트 끝 시각보다 이 값(초)만큼 일찍 놓는 것까지는 끝까지 누른 것으로 본다.")]
        [Range(0f, 0.4f)] public float holdTailTolerance = 0.12f;

        [Tooltip("입력 장치/오디오 지연 보정(초). 항상 늦게 쳐지면 양수로 올린다.")]
        [Range(-0.2f, 0.2f)] public float inputLatency = 0f;
    }
}
