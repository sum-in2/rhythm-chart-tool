using System;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>analyze.py 가 출력하는 JSON 의 구조(JsonUtility 로 읽는다). 필드 이름은 JSON 키와 같아야 한다.</summary>
    [Serializable]
    internal sealed class AnalysisResult
    {
        public int schemaVersion;
        public float bpm;
        public float offset;
        public float duration;
        public float[] beats;
        public AnalysisOnset[] onsets;
    }

    [Serializable]
    internal sealed class AnalysisOnset
    {
        public float time;
        public float strength;
        public string band;
    }
}
