using System.IO;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>패키지에 들어 있는 난이도별 기본 프리셋 에셋을 찾고 만든다.</summary>
    internal static class DefaultPresets
    {
        const string PresetDirName = "Presets";

        static string PackageAssetPath
        {
            get
            {
                PackageInfo package = PackageInfo.FindForAssembly(typeof(DefaultPresets).Assembly);
                return package != null ? package.assetPath : null;
            }
        }

        /// <summary>그 난이도의 기본 프리셋 에셋 경로(예: Packages/com.sumin.rhythm-chart-tool/Runtime/Presets/Easy.asset).</summary>
        public static string AssetPath(Difficulty difficulty)
        {
            return $"{PackageAssetPath}/Runtime/{PresetDirName}/{difficulty}.asset";
        }

        public static DifficultyPreset Load(Difficulty difficulty)
        {
            return AssetDatabase.LoadAssetAtPath<DifficultyPreset>(AssetPath(difficulty));
        }

        /// <summary>프리셋이 지정되지 않은 난이도에 패키지 기본 프리셋을 지정한다. 이미 지정된 것은 건드리지 않는다.</summary>
        public static void AssignMissing(ChartData chart)
        {
            for (int i = 0; i < ChartConstants.DifficultyCount; i++)
            {
                var difficulty = (Difficulty)i;
                if (chart.GetPreset(difficulty) == null)
                    chart.SetPreset(difficulty, Load(difficulty));
            }
        }

        /// <summary>코드의 기본값으로 패키지 프리셋 에셋을 만들거나 덮어쓴다(개발용, 로컬 패키지에서만 쓸 수 있다).</summary>
        public static void Rebuild()
        {
            string directory = $"{PackageAssetPath}/Runtime/{PresetDirName}";
            if (!AssetDatabase.IsValidFolder(directory))
                AssetDatabase.CreateFolder($"{PackageAssetPath}/Runtime", PresetDirName);

            for (int i = 0; i < ChartConstants.DifficultyCount; i++)
            {
                var difficulty = (Difficulty)i;
                DifficultyPreset preset = Load(difficulty);
                bool isNew = preset == null;
                if (isNew)
                    preset = UnityEngine.ScriptableObject.CreateInstance<DifficultyPreset>();

                preset.Set(difficulty, DifficultySettings.CreateDefault(difficulty));
                if (isNew)
                    AssetDatabase.CreateAsset(preset, AssetPath(difficulty));
                else
                    EditorUtility.SetDirty(preset);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>프리셋을 <paramref name="folder"/> 안에 복사해 프로젝트에서 편집할 수 있게 한다. 복사본을 반환한다.</summary>
        public static DifficultyPreset CopyToProject(DifficultyPreset source, string folder, string baseName)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName}_{source.Difficulty}.preset.asset");
            var copy = UnityEngine.ScriptableObject.CreateInstance<DifficultyPreset>();
            copy.Set(source.Difficulty, source.Settings);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            return copy;
        }

        public static string ChartFolder(ChartData chart)
        {
            string path = AssetDatabase.GetAssetPath(chart);
            return string.IsNullOrEmpty(path) ? "Assets" : Path.GetDirectoryName(path).Replace('\\', '/');
        }
    }
}
