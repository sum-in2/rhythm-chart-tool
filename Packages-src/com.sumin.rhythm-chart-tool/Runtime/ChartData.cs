using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuminTools.RhythmChart
{
    /// <summary>onset 이 가장 두드러진 주파수 대역.</summary>
    public enum OnsetBand
    {
        /// <summary>저역(킥 등).</summary>
        Low = 0,
        /// <summary>중역(스네어, 보컬 등).</summary>
        Mid = 1,
        /// <summary>고역(하이햇, 심벌 등).</summary>
        High = 2,
    }

    /// <summary>오디오 분석이 찾아낸 타격 시점 하나.</summary>
    [Serializable]
    public struct OnsetData
    {
        /// <summary>곡 시작 기준 시각(초).</summary>
        public float time;

        /// <summary>0~1 로 정규화한 세기. 곡 안에서의 상대적인 값이다.</summary>
        public float strength;

        /// <summary>가장 두드러진 주파수 대역.</summary>
        public OnsetBand band;
    }

    /// <summary>
    /// 한 곡의 차트 데이터. 분석 결과(BPM, 박자, onset)를 원본 그대로 보관해
    /// Python 을 다시 실행하지 않고도 노트를 다시 생성할 수 있게 한다.
    /// </summary>
    public class ChartData : ScriptableObject
    {
        [SerializeField] AudioClip audioClip;
        [SerializeField] int analysisSchemaVersion;
        [SerializeField] float bpm;
        [SerializeField] float offset;
        [SerializeField] float duration;
        [SerializeField] float[] beats = Array.Empty<float>();
        [SerializeField] List<OnsetData> onsets = new List<OnsetData>();

        /// <summary>차트가 만들어진 원본 오디오.</summary>
        public AudioClip AudioClip => audioClip;

        /// <summary>분석 JSON 의 schemaVersion.</summary>
        public int AnalysisSchemaVersion => analysisSchemaVersion;

        /// <summary>곡 전체에 적용되는 고정 BPM.</summary>
        public float Bpm => bpm;

        /// <summary>BPM 그리드의 첫 선 위치(초). 0 이상 한 박 길이 미만이다.</summary>
        public float Offset => offset;

        /// <summary>곡 길이(초).</summary>
        public float Duration => duration;

        /// <summary>박자 추적기가 찾은 박자 시각(초). 고정 그리드와 다를 수 있다.</summary>
        public IReadOnlyList<float> Beats => beats;

        /// <summary>분석이 찾은 모든 onset(시각 오름차순).</summary>
        public IReadOnlyList<OnsetData> Onsets => onsets;

        /// <summary>분석 결과를 이 차트에 덮어쓴다. 에셋의 GUID 와 참조는 유지된다.</summary>
        public void ApplyAnalysis(
            AudioClip clip,
            int schemaVersion,
            float bpm,
            float offset,
            float duration,
            IEnumerable<float> beats,
            IEnumerable<OnsetData> onsets)
        {
            audioClip = clip;
            analysisSchemaVersion = schemaVersion;
            this.bpm = bpm;
            this.offset = offset;
            this.duration = duration;
            this.beats = new List<float>(beats ?? Array.Empty<float>()).ToArray();
            this.onsets = new List<OnsetData>(onsets ?? Array.Empty<OnsetData>());
        }
    }
}
