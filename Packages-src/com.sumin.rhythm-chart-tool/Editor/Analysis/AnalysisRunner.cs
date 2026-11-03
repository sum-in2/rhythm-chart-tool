using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 선택한 오디오를 analyze.py 로 분석해 <see cref="ChartData"/> 로 저장한다.
    /// 프로세스는 비동기로 실행되어 에디터를 막지 않고, 진행률 표시줄에서 취소할 수 있다.
    /// </summary>
    internal static class AnalysisRunner
    {
        const string ProgressTitle = "Rhythm Chart 분석";

        static ProcessRunner runner;
        static AudioClip clip;
        static string chartAssetPath;
        static string jsonPath;
        static float progress;
        static bool canceled;
        static readonly List<string> stderrLines = new List<string>();
        static readonly List<string> stdoutLines = new List<string>();

        public static bool IsRunning => runner != null;

        /// <summary>분석을 시작한다. Python 환경이 없으면 설정 여부를 물은 뒤 이어서 실행한다.</summary>
        public static void Run(AudioClip target)
        {
            Run(target, false);
        }

        static void Run(AudioClip target, bool afterSetup)
        {
            if (target == null)
                return;
            if (IsRunning || PythonEnvSetup.IsRunning)
            {
                EditorUtility.DisplayDialog(ProgressTitle, "이미 분석 또는 Python 환경 설정이 진행 중입니다.", "확인");
                return;
            }

            string audioAssetPath = AssetDatabase.GetAssetPath(target);
            string audioFullPath = ResolvePhysicalPath(audioAssetPath);
            if (string.IsNullOrEmpty(audioFullPath) || !File.Exists(audioFullPath))
            {
                Debug.LogError($"{EditorConstants.LogPrefix}오디오 파일을 찾을 수 없습니다: '{audioAssetPath}'", target);
                return;
            }

            if (PythonEnvironment.GetStatus() != EnvStatus.Ready)
            {
                if (afterSetup)
                {
                    Debug.LogError(EditorConstants.LogPrefix + "Python 환경을 설정했지만 사용할 수 없는 상태입니다. 설정 창을 확인하세요.");
                    return;
                }
                if (!EditorUtility.DisplayDialog(
                        ProgressTitle,
                        "분석에 필요한 Python 환경이 없거나 오래되었습니다.\n지금 설정할까요? (패키지를 내려받아 몇 분 걸릴 수 있습니다)",
                        "지금 설정", "취소"))
                    return;
                PythonEnvSetup.Start(success =>
                {
                    if (success)
                        Run(target, true);
                });
                return;
            }

            string chartPath = ChartImporter.GetChartAssetPath(audioAssetPath);
            if (AssetDatabase.LoadAssetAtPath<ChartData>(chartPath) != null &&
                !EditorUtility.DisplayDialog(
                    ProgressTitle,
                    $"이미 차트가 있습니다.\n{chartPath}\n분석 결과로 덮어쓸까요? (에셋 GUID 와 참조는 유지됩니다)",
                    "덮어쓰기", "취소"))
                return;

            Start(target, audioAssetPath, audioFullPath, chartPath);
        }

        static void Start(AudioClip target, string audioAssetPath, string audioFullPath, string chartPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(audioAssetPath);
            Directory.CreateDirectory(RhythmChartSettings.AnalysisDir);
            string outputJson = Path.Combine(RhythmChartSettings.AnalysisDir, guid + ".json");
            if (File.Exists(outputJson))
                File.Delete(outputJson);

            clip = target;
            chartAssetPath = chartPath;
            jsonPath = outputJson;
            progress = 0f;
            canceled = false;
            stderrLines.Clear();
            stdoutLines.Clear();

            string[] args = { PythonEnvironment.ScriptPath, audioFullPath, outputJson };
            try
            {
                runner = ProcessRunner.Start(PythonEnvironment.VenvPython(), args, PythonEnvironment.PythonDir);
            }
            catch (Exception e)
            {
                Debug.LogError($"{EditorConstants.LogPrefix}분석을 시작하지 못했습니다: {e.Message}", target);
                Reset();
                return;
            }

            runner.OutputLine += OnOutputLine;
            runner.ErrorLine += stderrLines.Add;
            runner.Completed += OnCompleted;
            EditorApplication.update += UpdateProgressBar;
            AssemblyReloadEvents.beforeAssemblyReload += ClearProgressBar;
        }

        static void OnOutputLine(string line)
        {
            if (line.StartsWith(EditorConstants.ProgressLinePrefix, StringComparison.Ordinal) &&
                float.TryParse(
                    line.Substring(EditorConstants.ProgressLinePrefix.Length),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                progress = Mathf.Clamp01(value);
            }
            else
            {
                stdoutLines.Add(line);
            }
        }

        static void UpdateProgressBar()
        {
            // 같은 update 틱에서 종료 처리(OnCompleted)가 먼저 돌았다면 구독 해제 후에도 이번 틱에는 한 번 더 호출된다.
            if (runner == null)
                return;

            string info = $"{clip.name}  ({Mathf.RoundToInt(progress * 100f)}%)";
            if (EditorUtility.DisplayCancelableProgressBar(ProgressTitle, info, progress) && runner != null)
            {
                canceled = true;
                runner.Kill();
            }
        }

        static void OnCompleted(int exitCode)
        {
            ClearProgressBar();
            AudioClip finishedClip = clip;
            string json = jsonPath;
            string chartPath = chartAssetPath;
            bool wasCanceled = canceled || runner.WasKilled;
            string stderr = string.Join("\n", stderrLines);
            string stdoutTail = string.Join("\n", stdoutLines.GetRange(
                Math.Max(0, stdoutLines.Count - EditorConstants.FailureLogTailLines),
                Math.Min(stdoutLines.Count, EditorConstants.FailureLogTailLines)));
            Reset();

            if (wasCanceled)
            {
                Debug.Log(EditorConstants.LogPrefix + "분석을 취소했습니다.");
            }
            else if (exitCode != 0)
            {
                var message = new StringBuilder($"{EditorConstants.LogPrefix}분석에 실패했습니다(종료 코드 {exitCode}).");
                message.Append('\n').Append(stderr.Length > 0 ? stderr : stdoutTail);
                Debug.LogError(message.ToString(), finishedClip);
            }
            else
            {
                ImportResult(finishedClip, json, chartPath, stderr);
            }

            if (File.Exists(json))
                File.Delete(json);
        }

        static void ImportResult(AudioClip finishedClip, string json, string chartPath, string stderr)
        {
            try
            {
                ChartData chart = ChartImporter.Import(json, finishedClip, chartPath);
                // 성공했는데 stderr 가 있으면 라이브러리 경고다. 실패가 아니므로 에러가 아닌 일반 로그로 남긴다.
                if (stderr.Length > 0)
                    Debug.Log($"{EditorConstants.LogPrefix}분석 중 경고 출력:\n{stderr}");
                Debug.Log(
                    $"{EditorConstants.LogPrefix}분석 완료: {chartPath}\n" +
                    $"BPM {chart.Bpm:0.##}, offset {chart.Offset:0.###}s, 길이 {chart.Duration:0.#}s, " +
                    $"beats {chart.Beats.Count}, onsets {chart.Onsets.Count}\n" +
                    $"노트: Easy {chart.GetNotes(Difficulty.Easy).Count} / Normal {chart.GetNotes(Difficulty.Normal).Count} / " +
                    $"Hard {chart.GetNotes(Difficulty.Hard).Count} / Expert {chart.GetNotes(Difficulty.Expert).Count}",
                    chart);
                EditorGUIUtility.PingObject(chart);
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException)
            {
                Debug.LogError($"{EditorConstants.LogPrefix}분석 결과를 가져오지 못했습니다: {e.Message}", finishedClip);
            }
        }

        static void ClearProgressBar()
        {
            EditorApplication.update -= UpdateProgressBar;
            AssemblyReloadEvents.beforeAssemblyReload -= ClearProgressBar;
            EditorUtility.ClearProgressBar();
        }

        static void Reset()
        {
            runner = null;
            clip = null;
            chartAssetPath = null;
            jsonPath = null;
        }

        // Assets/ 는 프로젝트 루트 기준, Packages/ 는 패키지 설치 위치(resolvedPath) 기준으로 실제 경로를 얻는다.
        static string ResolvePhysicalPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;
            PackageInfo package = PackageInfo.FindForAssetPath(assetPath);
            if (package != null)
                return Path.GetFullPath(Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length + 1)));
            return Path.GetFullPath(Path.Combine(RhythmChartSettings.ProjectRoot, assetPath));
        }
    }
}
