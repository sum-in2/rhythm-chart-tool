using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>분석용 venv 의 상태.</summary>
    internal enum EnvStatus
    {
        /// <summary>venv 가 없거나 설치가 끝나지 않았다.</summary>
        Missing,

        /// <summary>venv 는 있지만 requirements.txt 가 설치 당시와 달라졌다(패키지 업데이트 등).</summary>
        Outdated,

        /// <summary>바로 분석할 수 있다.</summary>
        Ready,
    }

    /// <summary>패키지 안의 Python 파일 위치와 venv 상태를 다룬다.</summary>
    internal static class PythonEnvironment
    {
        [Serializable]
        class Marker
        {
            public string requirementsHash;
        }

        /// <summary>패키지의 Python~ 폴더. 설치 방식(file:, Git URL)과 무관하게 resolvedPath 로 찾는다.</summary>
        public static string PythonDir
        {
            get
            {
                PackageInfo package = PackageInfo.FindForAssembly(typeof(PythonEnvironment).Assembly);
                if (package == null)
                    throw new InvalidOperationException("Rhythm Chart Tool 패키지 위치를 찾지 못했습니다.");
                return Path.Combine(package.resolvedPath, EditorConstants.PythonDirName);
            }
        }

        public static string ScriptPath => Path.Combine(PythonDir, EditorConstants.ScriptFileName);

        public static string RequirementsPath => Path.Combine(PythonDir, EditorConstants.RequirementsFileName);

        /// <summary>venv 안의 python 실행 파일 경로.</summary>
        public static string VenvPython(string venvDir)
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                ? Path.Combine(venvDir, "Scripts", "python.exe")
                : Path.Combine(venvDir, "bin", "python");
        }

        public static string VenvPython() => VenvPython(RhythmChartSettings.VenvPath);

        static string MarkerPath => Path.Combine(RhythmChartSettings.VenvPath, EditorConstants.EnvMarkerFileName);

        public static EnvStatus GetStatus()
        {
            if (!File.Exists(VenvPython()) || !File.Exists(MarkerPath))
                return EnvStatus.Missing;

            Marker marker = JsonUtility.FromJson<Marker>(File.ReadAllText(MarkerPath, Encoding.UTF8));
            return marker != null && marker.requirementsHash == ComputeRequirementsHash()
                ? EnvStatus.Ready
                : EnvStatus.Outdated;
        }

        /// <summary>설치가 끝났음을 기록한다. 이 파일이 있어야 Ready 로 본다(설치 중 취소된 venv 를 걸러내기 위해).</summary>
        public static void WriteMarker()
        {
            var marker = new Marker { requirementsHash = ComputeRequirementsHash() };
            File.WriteAllText(MarkerPath, JsonUtility.ToJson(marker), new UTF8Encoding(false));
        }

        // 줄바꿈 차이(git autocrlf)로 해시가 달라지지 않게 CR 을 제거하고 계산한다.
        static string ComputeRequirementsHash()
        {
            string text = File.ReadAllText(RequirementsPath, Encoding.UTF8).Replace("\r", string.Empty);
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
        }
    }
}
