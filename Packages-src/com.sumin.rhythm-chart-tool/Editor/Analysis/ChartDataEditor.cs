using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// ChartData 인스펙터. 분석 요약, 레인 수/시드/프리셋 지정, 난이도별 노트 생성, 차트 JSON 입출력을 제공한다.
    /// 수천 개짜리 onset 목록을 기본 인스펙터로 그리면 느려서 요약만 보여준다.
    /// </summary>
    [CustomEditor(typeof(ChartData))]
    internal sealed class ChartDataEditor : UnityEditor.Editor
    {
        const string JsonExtension = "json";

        public override void OnInspectorGUI()
        {
            var chart = (ChartData)target;

            if (GUILayout.Button("차트 에디터 열기", GUILayout.Height(24f)))
                ChartEditorWindow.Open(chart);
            EditorGUILayout.Space();
            DrawSummary(chart);
            EditorGUILayout.Space();
            DrawGenerationSettings(chart);
            EditorGUILayout.Space();
            DrawDifficulties(chart);
            EditorGUILayout.Space();
            DrawJson(chart);
        }

        static void DrawSummary(ChartData chart)
        {
            EditorGUILayout.LabelField("분석 결과", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("오디오", chart.AudioClip, typeof(AudioClip), false);
                EditorGUILayout.LabelField("BPM / offset", $"{chart.Bpm:0.##} / {chart.Offset:0.###}s");
                EditorGUILayout.LabelField("길이", $"{chart.Duration:0.#}s");
                EditorGUILayout.LabelField("beats / onsets", $"{chart.Beats.Count} / {chart.Onsets.Count}");
            }
        }

        static void DrawGenerationSettings(ChartData chart)
        {
            EditorGUILayout.LabelField("생성 설정", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            int lanes = EditorGUILayout.IntSlider("레인 수", chart.LaneCount, ChartConstants.MinLaneCount, ChartConstants.MaxLaneCount);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(chart, "레인 수 변경");
                chart.SetLaneCount(lanes);
                EditorUtility.SetDirty(chart);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                int seed = EditorGUILayout.IntField("시드", chart.GenerationSeed);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(chart, "시드 변경");
                    chart.SetGenerationSeed(seed);
                    EditorUtility.SetDirty(chart);
                }

                if (GUILayout.Button("랜덤", GUILayout.Width(50f)))
                {
                    Undo.RecordObject(chart, "시드 변경");
                    chart.SetGenerationSeed(Environment.TickCount);
                    EditorUtility.SetDirty(chart);
                }
            }

            if (HasNoteOutsideLanes(chart))
                EditorGUILayout.HelpBox("현재 레인 수를 벗어난 노트가 있습니다. 난이도를 다시 생성하세요.", MessageType.Warning);
        }

        static void DrawDifficulties(ChartData chart)
        {
            EditorGUILayout.LabelField("난이도", EditorStyles.boldLabel);
            for (int i = 0; i < ChartConstants.DifficultyCount; i++)
            {
                var difficulty = (Difficulty)i;
                DrawDifficulty(chart, difficulty);
            }

            if (GUILayout.Button("4개 난이도 모두 생성"))
                Generate(chart, null);
        }

        static void DrawDifficulty(ChartData chart, Difficulty difficulty)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(difficulty.ToString(), EditorStyles.boldLabel, GUILayout.Width(70f));
                    EditorGUILayout.LabelField(DescribeNotes(chart, difficulty));
                    if (GUILayout.Button("생성", GUILayout.Width(50f)))
                        Generate(chart, difficulty);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var preset = (DifficultyPreset)EditorGUILayout.ObjectField(
                        "프리셋", chart.GetPreset(difficulty), typeof(DifficultyPreset), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(chart, "프리셋 변경");
                        chart.SetPreset(difficulty, preset);
                        EditorUtility.SetDirty(chart);
                    }

                    // 패키지 안의 프리셋은 읽기 전용일 수 있으므로 프로젝트로 복사해서 고치게 한다.
                    DifficultyPreset current = chart.GetPreset(difficulty);
                    bool inPackage = current != null && AssetDatabase.GetAssetPath(current).StartsWith("Packages/", StringComparison.Ordinal);
                    using (new EditorGUI.DisabledScope(!inPackage))
                    {
                        if (GUILayout.Button("프로젝트로 복사", GUILayout.Width(100f)))
                            CopyPresetToProject(chart, difficulty, current);
                    }
                }

                DifficultyPreset editable = chart.GetPreset(difficulty);
                if (editable != null)
                    DrawPresetSliders(editable);
            }
        }

        // 프리셋의 슬라이더(Range 특성이 붙은 필드)를 인스펙터 안에서 바로 조정한다.
        static void DrawPresetSliders(DifficultyPreset preset)
        {
            bool readOnly = AssetDatabase.GetAssetPath(preset).StartsWith("Packages/", StringComparison.Ordinal);
            var serialized = new SerializedObject(preset);
            SerializedProperty settings = serialized.FindProperty("settings");

            using (new EditorGUI.DisabledScope(readOnly))
            {
                EditorGUI.indentLevel++;
                serialized.Update();
                SerializedProperty iterator = settings.Copy();
                SerializedProperty end = settings.GetEndProperty();
                bool enter = true;
                while (iterator.NextVisible(enter) && !SerializedProperty.EqualContents(iterator, end))
                {
                    EditorGUILayout.PropertyField(iterator, true);
                    enter = false;
                }
                serialized.ApplyModifiedProperties();
                EditorGUI.indentLevel--;
            }

            if (readOnly)
                EditorGUILayout.HelpBox("패키지 기본 프리셋입니다. 값을 바꾸려면 '프로젝트로 복사'를 누르세요.", MessageType.None);
        }

        static void DrawJson(ChartData chart)
        {
            EditorGUILayout.LabelField("차트 JSON", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("내보내기..."))
                    ExportJson(chart);
                if (GUILayout.Button("가져오기..."))
                    ImportJson(chart);
            }
        }

        static void Generate(ChartData chart, Difficulty? difficulty)
        {
            string label = difficulty.HasValue ? $"{difficulty.Value} 난이도" : "모든 난이도";
            if (chart.HasAnyNotes && !EditorUtility.DisplayDialog(
                    "노트 생성", $"{label}의 기존 노트가 사라집니다. 다시 생성할까요?", "생성", "취소"))
                return;

            Undo.RecordObject(chart, "노트 생성");
            if (difficulty.HasValue)
                chart.Generate(difficulty.Value);
            else
                chart.GenerateAll();
            EditorUtility.SetDirty(chart);
        }

        static void CopyPresetToProject(ChartData chart, Difficulty difficulty, DifficultyPreset source)
        {
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(chart)).Replace(".chart", string.Empty);
            DifficultyPreset copy = DefaultPresets.CopyToProject(source, DefaultPresets.ChartFolder(chart), name);
            Undo.RecordObject(chart, "프리셋 복사");
            chart.SetPreset(difficulty, copy);
            EditorUtility.SetDirty(chart);
        }

        static void ExportJson(ChartData chart)
        {
            string defaultName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(chart)).Replace(".chart", string.Empty);
            string path = EditorUtility.SaveFilePanel("차트 JSON 내보내기", string.Empty, defaultName + ".chart", JsonExtension);
            if (string.IsNullOrEmpty(path))
                return;
            File.WriteAllText(path, ChartJson.ToJson(chart), new UTF8Encoding(false));
            Debug.Log($"{EditorConstants.LogPrefix}차트 JSON 을 저장했습니다: {path}", chart);
        }

        static void ImportJson(ChartData chart)
        {
            string path = EditorUtility.OpenFilePanel("차트 JSON 가져오기", string.Empty, JsonExtension);
            if (string.IsNullOrEmpty(path))
                return;
            if (!EditorUtility.DisplayDialog(
                    "차트 JSON 가져오기", "분석 데이터와 모든 난이도의 노트가 파일 내용으로 바뀝니다. 계속할까요?", "가져오기", "취소"))
                return;

            try
            {
                Undo.RecordObject(chart, "차트 JSON 가져오기");
                ChartJson.FromJson(File.ReadAllText(path, Encoding.UTF8), chart);
                EditorUtility.SetDirty(chart);
            }
            catch (Exception e) when (e is InvalidDataException || e is NotSupportedException || e is IOException)
            {
                Debug.LogError($"{EditorConstants.LogPrefix}차트 JSON 을 가져오지 못했습니다: {e.Message}", chart);
            }
        }

        static string DescribeNotes(ChartData chart, Difficulty difficulty)
        {
            var notes = chart.GetNotes(difficulty);
            if (notes.Count == 0)
                return "노트 없음";

            int holds = 0;
            foreach (NoteData note in notes)
                if (note.type == NoteType.Hold)
                    holds++;
            float perSecond = chart.Duration > 0f ? notes.Count / chart.Duration : 0f;
            return $"{notes.Count}개  ·  초당 {perSecond:0.0}  ·  롱노트 {100f * holds / notes.Count:0}%";
        }

        static bool HasNoteOutsideLanes(ChartData chart)
        {
            for (int i = 0; i < ChartConstants.DifficultyCount; i++)
                foreach (NoteData note in chart.GetNotes((Difficulty)i))
                    if (note.lane >= chart.LaneCount)
                        return true;
            return false;
        }
    }
}
