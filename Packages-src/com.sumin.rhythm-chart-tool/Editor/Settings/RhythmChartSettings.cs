using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 에디터 설정. Python 실행 파일은 컴퓨터 전체에서 공유하고,
    /// venv 경로는 프로젝트마다 따로 저장한다(EditorPrefs 는 컴퓨터 전체 저장소이기 때문).
    /// </summary>
    internal static class RhythmChartSettings
    {
        const string PythonPathKey = EditorConstants.PrefsPrefix + "PythonPath";
        const string VenvPathKeyPrefix = EditorConstants.PrefsPrefix + "VenvPath.";
        const int ProjectKeyLength = 12;

        /// <summary>venv 를 만들 때 쓰는 시스템 Python 실행 파일 경로. 비어 있으면 자동 검색한다.</summary>
        public static string PythonPath
        {
            get => EditorPrefs.GetString(PythonPathKey, string.Empty);
            set => EditorPrefs.SetString(PythonPathKey, value ?? string.Empty);
        }

        /// <summary>이 프로젝트의 venv 경로. 지정하지 않았으면 <see cref="DefaultVenvPath"/>.</summary>
        public static string VenvPath
        {
            get
            {
                string custom = EditorPrefs.GetString(VenvPathKey, string.Empty);
                return string.IsNullOrWhiteSpace(custom) ? DefaultVenvPath : custom;
            }
            set => EditorPrefs.SetString(VenvPathKey, value ?? string.Empty);
        }

        public static bool IsVenvPathCustom =>
            !string.IsNullOrWhiteSpace(EditorPrefs.GetString(VenvPathKey, string.Empty));

        public static string DefaultVenvPath => Path.Combine(CacheRoot, EditorConstants.VenvDirName);

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>Library/RhythmChartTool. Library 는 버전 관리에서 제외되고 프로젝트별로 분리된다.</summary>
        public static string CacheRoot => Path.Combine(ProjectRoot, "Library", EditorConstants.CacheDirName);

        public static string AnalysisDir => Path.Combine(CacheRoot, EditorConstants.AnalysisDirName);

        static string VenvPathKey => VenvPathKeyPrefix + ProjectKey;

        static string ProjectKey
        {
            get
            {
                using (var sha = SHA1.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(ProjectRoot.ToLowerInvariant()));
                    return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, ProjectKeyLength);
                }
            }
        }
    }
}
