using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SuminTools.RhythmChart
{
    /// <summary>
    /// 차트 JSON 입출력. 에디터 밖(런타임)에서도 쓸 수 있다.
    /// 파일에는 <c>schemaVersion</c> 이 들어가고, 이전 버전 파일은 <see cref="ChartJsonMigrations"/> 에 등록된 단계를 거쳐 올려서 읽는다.
    /// </summary>
    public static class ChartJson
    {
        /// <summary>이 코드가 읽고 쓰는 차트 JSON 의 schemaVersion.</summary>
        public const int CurrentSchemaVersion = 1;

        [Serializable]
        class ChartDto
        {
            public int schemaVersion;
            public float bpm;
            public float offset;
            public float duration;
            public int laneCount;
            public int generationSeed;
            public float[] beats;
            public OnsetDto[] onsets;
            public DifficultyDto[] difficulties;
        }

        [Serializable]
        class OnsetDto
        {
            public float time;
            public float strength;
            public string band;
        }

        [Serializable]
        class DifficultyDto
        {
            public string difficulty;
            public NoteDto[] notes;
        }

        [Serializable]
        class NoteDto
        {
            public float time;
            public int lane;
            public float holdLength;
            public string type;
        }

        /// <summary>차트(분석 데이터, 생성 설정 일부, 노트)를 JSON 문자열로 만든다. 오디오 클립과 프리셋 참조는 포함하지 않는다.</summary>
        public static string ToJson(ChartData chart, bool prettyPrint = true)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));

            var dto = new ChartDto
            {
                schemaVersion = CurrentSchemaVersion,
                bpm = chart.Bpm,
                offset = chart.Offset,
                duration = chart.Duration,
                laneCount = chart.LaneCount,
                generationSeed = chart.GenerationSeed,
                beats = ToArray(chart.Beats),
                onsets = new OnsetDto[chart.Onsets.Count],
                difficulties = new DifficultyDto[ChartConstants.DifficultyCount],
            };

            for (int i = 0; i < dto.onsets.Length; i++)
            {
                OnsetData onset = chart.Onsets[i];
                dto.onsets[i] = new OnsetDto { time = onset.time, strength = onset.strength, band = onset.band.ToString() };
            }

            for (int d = 0; d < ChartConstants.DifficultyCount; d++)
            {
                var difficulty = (Difficulty)d;
                IReadOnlyList<NoteData> notes = chart.GetNotes(difficulty);
                var noteDtos = new NoteDto[notes.Count];
                for (int i = 0; i < noteDtos.Length; i++)
                {
                    NoteData note = notes[i];
                    noteDtos[i] = new NoteDto
                    {
                        time = note.time, lane = note.lane, holdLength = note.holdLength, type = note.type.ToString(),
                    };
                }
                dto.difficulties[d] = new DifficultyDto { difficulty = difficulty.ToString(), notes = noteDtos };
            }

            return JsonUtility.ToJson(dto, prettyPrint);
        }

        /// <summary>
        /// JSON 을 읽어 <paramref name="target"/> 의 분석 데이터, 레인 수, 시드, 노트를 바꾼다.
        /// 오디오 클립과 프리셋 참조는 유지한다. 내용이 잘못됐으면 <see cref="InvalidDataException"/>,
        /// 현재보다 새 버전이거나 마이그레이션이 없으면 <see cref="NotSupportedException"/> 을 던진다.
        /// 예외가 나면 <paramref name="target"/> 은 바뀌지 않는다.
        /// </summary>
        public static void FromJson(string json, ChartData target)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (target == null) throw new ArgumentNullException(nameof(target));

            ChartDto dto = Parse(ChartJsonMigrations.MigrateToCurrent(json));
            Validate(dto);

            var onsets = new List<OnsetData>(dto.onsets.Length);
            foreach (OnsetDto onset in dto.onsets)
                onsets.Add(new OnsetData { time = onset.time, strength = onset.strength, band = ParseEnum<OnsetBand>(onset.band) });

            var notesByDifficulty = new List<NoteData>[ChartConstants.DifficultyCount];
            for (int d = 0; d < notesByDifficulty.Length; d++)
                notesByDifficulty[d] = new List<NoteData>();
            foreach (DifficultyDto difficultyDto in dto.difficulties)
            {
                var difficulty = ParseEnum<Difficulty>(difficultyDto.difficulty);
                foreach (NoteDto note in difficultyDto.notes ?? Array.Empty<NoteDto>())
                {
                    notesByDifficulty[(int)difficulty].Add(new NoteData
                    {
                        time = note.time, lane = note.lane, holdLength = note.holdLength, type = ParseEnum<NoteType>(note.type),
                    });
                }
            }

            // 여기까지 예외 없이 왔을 때만 target 을 바꾼다.
            target.ApplyAnalysis(target.AudioClip, target.AnalysisSchemaVersion, dto.bpm, dto.offset, dto.duration, dto.beats, onsets);
            target.SetLaneCount(dto.laneCount);
            target.SetGenerationSeed(dto.generationSeed);
            for (int d = 0; d < notesByDifficulty.Length; d++)
                target.SetNotes((Difficulty)d, notesByDifficulty[d]);
        }

        /// <summary>JSON 으로 새 <see cref="ChartData"/> 를 만든다(에셋으로 저장되지 않은 인스턴스).</summary>
        public static ChartData CreateFromJson(string json)
        {
            var chart = ScriptableObject.CreateInstance<ChartData>();
            try
            {
                FromJson(json, chart);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(chart);
                throw;
            }
            return chart;
        }

        static ChartDto Parse(string json)
        {
            try
            {
                return JsonUtility.FromJson<ChartDto>(json) ?? throw new InvalidDataException("차트 JSON 이 비어 있습니다.");
            }
            catch (ArgumentException e)
            {
                throw new InvalidDataException($"차트 JSON 을 읽지 못했습니다: {e.Message}");
            }
        }

        static void Validate(ChartDto dto)
        {
            if (dto.beats == null || dto.onsets == null || dto.difficulties == null)
                throw new InvalidDataException("차트 JSON 에 beats, onsets, difficulties 가 모두 있어야 합니다.");
            if (!(dto.duration > 0f) || float.IsInfinity(dto.duration))
                throw new InvalidDataException($"곡 길이가 올바르지 않습니다: {dto.duration}");
            if (dto.bpm < 0f || float.IsNaN(dto.bpm) || float.IsInfinity(dto.bpm))
                throw new InvalidDataException($"BPM 이 올바르지 않습니다: {dto.bpm}");
            if (dto.laneCount < ChartConstants.MinLaneCount || dto.laneCount > ChartConstants.MaxLaneCount)
                throw new InvalidDataException(
                    $"레인 수는 {ChartConstants.MinLaneCount}~{ChartConstants.MaxLaneCount} 여야 합니다: {dto.laneCount}");

            foreach (DifficultyDto difficulty in dto.difficulties)
            {
                foreach (NoteDto note in difficulty.notes ?? Array.Empty<NoteDto>())
                {
                    if (note.lane < 0 || note.lane >= dto.laneCount)
                        throw new InvalidDataException(
                            $"노트의 레인이 범위를 벗어났습니다(lane={note.lane}, 레인 수={dto.laneCount}, 시각={note.time}).");
                    if (float.IsNaN(note.time) || note.time < 0f || note.holdLength < 0f)
                        throw new InvalidDataException($"노트 값이 올바르지 않습니다(시각={note.time}, 길이={note.holdLength}).");
                }
            }
        }

        static T ParseEnum<T>(string name) where T : struct
        {
            if (Enum.TryParse(name, true, out T value) && Enum.IsDefined(typeof(T), value))
                return value;
            throw new InvalidDataException($"알 수 없는 {typeof(T).Name} 값입니다: '{name}'");
        }

        static float[] ToArray(IReadOnlyList<float> source)
        {
            var array = new float[source.Count];
            for (int i = 0; i < array.Length; i++)
                array[i] = source[i];
            return array;
        }
    }

    /// <summary>
    /// 차트 JSON 의 이전 버전을 현재 버전으로 올리는 단계를 등록하는 곳(마이그레이션 훅).
    /// <c>Register(n, f)</c> 의 <c>f</c> 는 schemaVersion n 인 JSON 을 받아 n+1 인 JSON 문자열을 돌려줘야 한다.
    /// </summary>
    public static class ChartJsonMigrations
    {
        static readonly Dictionary<int, Func<string, string>> Steps = new Dictionary<int, Func<string, string>>();

        [Serializable]
        class Header
        {
            public int schemaVersion;
        }

        /// <summary>schemaVersion <paramref name="fromVersion"/> → <paramref name="fromVersion"/>+1 변환을 등록한다. 이미 있으면 바꾼다.</summary>
        public static void Register(int fromVersion, Func<string, string> migrate)
        {
            if (migrate == null) throw new ArgumentNullException(nameof(migrate));
            Steps[fromVersion] = migrate;
        }

        /// <summary>등록한 변환을 지운다. 있었으면 true.</summary>
        public static bool Unregister(int fromVersion)
        {
            return Steps.Remove(fromVersion);
        }

        internal static string MigrateToCurrent(string json)
        {
            Header header;
            try
            {
                header = JsonUtility.FromJson<Header>(json);
            }
            catch (ArgumentException e)
            {
                throw new InvalidDataException($"차트 JSON 을 읽지 못했습니다: {e.Message}");
            }
            if (header == null)
                throw new InvalidDataException("차트 JSON 이 비어 있습니다.");

            int version = header.schemaVersion;
            if (version > ChartJson.CurrentSchemaVersion)
                throw new NotSupportedException(
                    $"이 패키지보다 새로운 차트 JSON 입니다(schemaVersion={version}, 지원={ChartJson.CurrentSchemaVersion}). 패키지를 업데이트하세요.");

            while (version < ChartJson.CurrentSchemaVersion)
            {
                if (!Steps.TryGetValue(version, out Func<string, string> migrate))
                    throw new NotSupportedException(
                        $"차트 JSON schemaVersion {version} 을(를) {version + 1} 로 올리는 마이그레이션이 등록되어 있지 않습니다.");

                json = migrate(json);
                int next = JsonUtility.FromJson<Header>(json)?.schemaVersion ?? -1;
                if (next != version + 1)
                    throw new InvalidDataException(
                        $"마이그레이션({version}→{version + 1}) 결과의 schemaVersion 이 {next} 입니다. {version + 1} 이어야 합니다.");
                version = next;
            }
            return json;
        }
    }
}
