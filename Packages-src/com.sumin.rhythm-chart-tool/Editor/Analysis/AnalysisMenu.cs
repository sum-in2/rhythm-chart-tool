using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>오디오 분석 메뉴. 프로젝트 창에서 AudioClip 을 선택한 뒤 실행한다.</summary>
    internal static class AnalysisMenu
    {
        const string AnalyzeItem = "Analyze Selected Audio";

        [MenuItem(EditorConstants.MenuRoot + AnalyzeItem, false, 10)]
        [MenuItem(EditorConstants.AssetMenuRoot + AnalyzeItem, false, 2000)]
        static void Analyze()
        {
            AnalysisRunner.Run(Selection.activeObject as AudioClip);
        }

        [MenuItem(EditorConstants.MenuRoot + AnalyzeItem, true)]
        [MenuItem(EditorConstants.AssetMenuRoot + AnalyzeItem, true)]
        static bool ValidateAnalyze()
        {
            return Selection.activeObject is AudioClip && !AnalysisRunner.IsRunning;
        }
    }
}
