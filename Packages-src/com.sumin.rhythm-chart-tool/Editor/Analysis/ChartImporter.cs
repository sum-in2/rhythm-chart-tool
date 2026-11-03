using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>분석 JSON 을 읽어 <see cref="ChartData"/> 에셋으로 저장한다.</summary>
    internal static class ChartImporter
    {
        /// <summary>오디오 에셋 옆에 만들 차트 에셋 경로. 예: Assets/Music/song.mp3 → Assets/Music/song.chart.asset</summary>
        public static string GetChartAssetPath(string audioAssetPath)
        {
            string directory = Path.GetDirectoryName(audioAssetPath).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(audioAssetPath);
            return $"{directory}/{name}{EditorConstants.ChartAssetSuffix}";
        }

        /// <summary>
        /// JSON 을 검증하고 차트 에셋에 반영한다. 이미 에셋이 있으면 GUID 와 참조를 유지한 채 분석 데이터만 덮어쓴다.
        /// JSON 이 잘못됐으면 예외를 던진다.
        /// </summary>
        public static ChartData Import(string jsonPath, AudioClip clip, string chartAssetPath)
        {
            AnalysisResult result = Parse(File.ReadAllText(jsonPath, Encoding.UTF8));

            var onsets = new List<OnsetData>(result.onsets.Length);
            foreach (AnalysisOnset onset in result.onsets)
            {
                onsets.Add(new OnsetData
                {
                    time = onset.time,
                    strength = onset.strength,
                    band = ParseBand(onset.band),
                });
            }

            ChartData chart = AssetDatabase.LoadAssetAtPath<ChartData>(chartAssetPath);
            bool isNew = chart == null;
            if (isNew)
                chart = ScriptableObject.CreateInstance<ChartData>();

            chart.ApplyAnalysis(
                clip, result.schemaVersion, result.bpm, result.offset, result.duration, result.beats, onsets);

            if (isNew)
                AssetDatabase.CreateAsset(chart, chartAssetPath);
            else
                EditorUtility.SetDirty(chart);
            AssetDatabase.SaveAssets();
            return chart;
        }

        /// <summary>분석 JSON 문자열을 파싱하고 값을 검증한다. 문제가 있으면 사용자에게 보여줄 메시지의 예외를 던진다.</summary>
        internal static AnalysisResult Parse(string json)
        {
            AnalysisResult result;
            try
            {
                result = JsonUtility.FromJson<AnalysisResult>(json);
            }
            catch (ArgumentException e)
            {
                throw new InvalidDataException($"분석 JSON 을 읽지 못했습니다: {e.Message}");
            }

            if (result == null)
                throw new InvalidDataException("분석 JSON 이 비어 있습니다.");
            if (result.schemaVersion != EditorConstants.SupportedAnalysisSchemaVersion)
                throw new InvalidDataException(
                    $"지원하지 않는 분석 JSON 버전입니다(schemaVersion={result.schemaVersion}, " +
                    $"지원={EditorConstants.SupportedAnalysisSchemaVersion}). 패키지와 Python 스크립트 버전이 다를 수 있습니다.");
            if (result.beats == null || result.onsets == null)
                throw new InvalidDataException("분석 JSON 에 beats 또는 onsets 가 없습니다.");
            if (!(result.duration > 0f) || float.IsInfinity(result.duration))
                throw new InvalidDataException($"곡 길이가 올바르지 않습니다: {result.duration}");
            // 박자를 못 찾은 곡은 analyze.py 가 bpm 0 을 낸다. 차트는 만들 수 있으므로 거부하지 않는다.
            if (result.bpm < 0f || float.IsNaN(result.bpm) || float.IsInfinity(result.bpm))
                throw new InvalidDataException($"BPM 이 올바르지 않습니다: {result.bpm}");
            return result;
        }

        static OnsetBand ParseBand(string band)
        {
            if (Enum.TryParse(band, true, out OnsetBand parsed) && Enum.IsDefined(typeof(OnsetBand), parsed))
                return parsed;
            throw new InvalidDataException($"알 수 없는 onset 대역입니다: '{band}'");
        }
    }
}
