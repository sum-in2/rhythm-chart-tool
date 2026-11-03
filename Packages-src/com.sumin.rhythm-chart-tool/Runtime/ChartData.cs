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
        [Serializable]
        class DifficultyNotes
        {
            public List<NoteData> notes = new List<NoteData>();
        }

        [SerializeField] AudioClip audioClip;

        // 분석 결과
        [SerializeField] int analysisSchemaVersion;
        [SerializeField] float bpm;
        [SerializeField] float offset;
        [SerializeField] float duration;
        [SerializeField] float[] beats = Array.Empty<float>();
        [SerializeField] List<OnsetData> onsets = new List<OnsetData>();

        // 생성 설정
        [SerializeField] int laneCount = ChartConstants.DefaultLaneCount;
        [SerializeField] int generationSeed = ChartConstants.DefaultSeed;
        [SerializeField] DifficultyPreset[] presets = new DifficultyPreset[ChartConstants.DifficultyCount];

        // 난이도별 노트
        [SerializeField] DifficultyNotes[] difficultyNotes = new DifficultyNotes[ChartConstants.DifficultyCount];

        /// <summary>차트가 만들어진 원본 오디오.</summary>
        public AudioClip AudioClip => audioClip;

        /// <summary>분석 JSON 의 schemaVersion.</summary>
        public int AnalysisSchemaVersion => analysisSchemaVersion;

        /// <summary>곡 전체에 적용되는 고정 BPM. 박자를 찾지 못했으면 0.</summary>
        public float Bpm => bpm;

        /// <summary>BPM 그리드의 첫 선 위치(초). 0 이상 한 박 길이 미만이다.</summary>
        public float Offset => offset;

        /// <summary>곡 길이(초).</summary>
        public float Duration => duration;

        /// <summary>박자 추적기가 찾은 박자 시각(초). 고정 그리드와 다를 수 있다.</summary>
        public IReadOnlyList<float> Beats => beats;

        /// <summary>분석이 찾은 모든 onset(시각 오름차순).</summary>
        public IReadOnlyList<OnsetData> Onsets => onsets;

        /// <summary>레인 수(4~6).</summary>
        public int LaneCount => laneCount;

        /// <summary>노트 생성에 쓰는 기본 시드. 난이도마다 여기서 다른 시드가 파생된다.</summary>
        public int GenerationSeed => generationSeed;

        /// <summary>어느 난이도에든 노트가 하나라도 있는가.</summary>
        public bool HasAnyNotes
        {
            get
            {
                for (int i = 0; i < ChartConstants.DifficultyCount; i++)
                    if (GetNotes((Difficulty)i).Count > 0)
                        return true;
                return false;
            }
        }

        /// <summary>분석 결과를 이 차트에 덮어쓴다. 에셋의 GUID, 참조, 이미 있는 노트는 건드리지 않는다.</summary>
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

        /// <summary>레인 수를 지정한다(4~6 으로 보정). 이미 있는 노트는 바뀌지 않으므로 다시 생성해야 맞는다.</summary>
        public void SetLaneCount(int count)
        {
            laneCount = Mathf.Clamp(count, ChartConstants.MinLaneCount, ChartConstants.MaxLaneCount);
        }

        /// <summary>노트 생성 시드를 지정한다.</summary>
        public void SetGenerationSeed(int seed)
        {
            generationSeed = seed;
        }

        /// <summary>그 난이도에 지정된 프리셋. 지정하지 않았으면 null.</summary>
        public DifficultyPreset GetPreset(Difficulty difficulty)
        {
            EnsureInitialized();
            return presets[(int)difficulty];
        }

        /// <summary>그 난이도의 프리셋을 지정한다. null 이면 코드의 기본값을 쓴다.</summary>
        public void SetPreset(Difficulty difficulty, DifficultyPreset preset)
        {
            EnsureInitialized();
            presets[(int)difficulty] = preset;
        }

        /// <summary>생성에 쓸 규칙. 프리셋이 없으면 난이도 기본값.</summary>
        public DifficultySettings GetSettings(Difficulty difficulty)
        {
            DifficultyPreset preset = GetPreset(difficulty);
            return preset != null ? preset.Settings : DifficultySettings.CreateDefault(difficulty);
        }

        /// <summary>그 난이도의 노트(시간, 레인 순).</summary>
        public IReadOnlyList<NoteData> GetNotes(Difficulty difficulty)
        {
            EnsureInitialized();
            return difficultyNotes[(int)difficulty].notes;
        }

        /// <summary>그 난이도의 노트를 통째로 바꾼다(에디터에서 수정한 결과를 저장할 때 등).</summary>
        public void SetNotes(Difficulty difficulty, IEnumerable<NoteData> notes)
        {
            EnsureInitialized();
            difficultyNotes[(int)difficulty].notes = new List<NoteData>(notes ?? Array.Empty<NoteData>());
        }

        /// <summary>분석 데이터와 현재 규칙으로 그 난이도의 노트를 새로 만든다. 기존 노트는 사라진다.</summary>
        public void Generate(Difficulty difficulty)
        {
            SetNotes(difficulty, ChartGenerator.Generate(
                onsets, bpm, offset, duration, laneCount,
                GetSettings(difficulty), ChartGenerator.DeriveSeed(generationSeed, difficulty)));
        }

        /// <summary>모든 난이도의 노트를 새로 만든다.</summary>
        public void GenerateAll()
        {
            for (int i = 0; i < ChartConstants.DifficultyCount; i++)
                Generate((Difficulty)i);
        }

        void OnEnable()
        {
            EnsureInitialized();
        }

        // 직렬화 데이터가 오래됐거나 새로 만든 직후(배열이 비었거나 null 요소)에도 항상 난이도 4개를 갖춘다.
        void EnsureInitialized()
        {
            if (presets == null || presets.Length != ChartConstants.DifficultyCount)
                Array.Resize(ref presets, ChartConstants.DifficultyCount);

            if (difficultyNotes == null || difficultyNotes.Length != ChartConstants.DifficultyCount)
                Array.Resize(ref difficultyNotes, ChartConstants.DifficultyCount);
            for (int i = 0; i < difficultyNotes.Length; i++)
            {
                if (difficultyNotes[i] == null)
                    difficultyNotes[i] = new DifficultyNotes();
                if (difficultyNotes[i].notes == null)
                    difficultyNotes[i].notes = new List<NoteData>();
            }
        }
    }
}
