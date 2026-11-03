using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 프로젝트 쪽에 venv 를 만들고 requirements.txt 를 설치한다. 비동기로 실행되며 진행 로그를 <see cref="Log"/> 에 쌓는다.
    /// </summary>
    internal static class PythonEnvSetup
    {
        static readonly List<string> log = new List<string>();
        static ProcessRunner runner;
        static int progressId = -1;
        static Action<bool> onFinished;
        static bool cancelRequested;

        public static bool IsRunning { get; private set; }

        public static IReadOnlyList<string> Log => log;

        /// <summary>로그가 바뀔 때마다 호출된다(메인 스레드).</summary>
        public static event Action LogChanged;

        /// <summary>환경 설정을 시작한다. 이미 실행 중이면 무시한다. 끝나면 성공 여부와 함께 콜백을 호출한다.</summary>
        public static void Start(Action<bool> finished = null)
        {
            if (IsRunning)
                return;

            log.Clear();
            IsRunning = true;
            cancelRequested = false;
            onFinished = finished;
            Append("== Python 환경 설정 ==");

            if (!PythonLocator.TryResolve(out PythonInfo python, out string error))
            {
                Fail(error);
                return;
            }
            if (!python.MeetsMinimum)
            {
                Fail($"Python {EditorConstants.MinPythonMajor}.{EditorConstants.MinPythonMinor} 이상이 필요합니다. " +
                     $"선택된 인터프리터: {python}");
                return;
            }
            Append($"사용할 Python: {python}");

            progressId = Progress.Start("Rhythm Chart: Python 환경 설정", null, Progress.Options.Indefinite);
            Progress.RegisterCancelCallback(progressId, () =>
            {
                Cancel();
                return true;
            });
            AssemblyReloadEvents.beforeAssemblyReload += RemoveProgress;

            string venvDir = RhythmChartSettings.VenvPath;
            if (File.Exists(PythonEnvironment.VenvPython(venvDir)))
            {
                Append($"기존 venv 를 사용합니다: {venvDir}");
                InstallRequirements();
            }
            else
            {
                Append($"venv 생성: {venvDir}");
                Run(python.Executable, new[] { "-m", "venv", venvDir }, "venv 생성", InstallRequirements);
            }
        }

        /// <summary>진행 중인 설정을 취소한다.</summary>
        public static void Cancel()
        {
            if (!IsRunning)
                return;
            cancelRequested = true;
            runner?.Kill();
        }

        static void InstallRequirements()
        {
            Append("패키지 설치(pip install -r requirements.txt) ...");
            string[] args =
            {
                "-m", "pip", "install", "--disable-pip-version-check",
                "-r", PythonEnvironment.RequirementsPath,
            };
            Run(PythonEnvironment.VenvPython(), args, "패키지 설치", () =>
            {
                PythonEnvironment.WriteMarker();
                Finish(true, "Python 환경 설정을 마쳤습니다.");
            });
        }

        static void Run(string executable, IEnumerable<string> args, string stepName, Action onSuccess)
        {
            try
            {
                runner = ProcessRunner.Start(executable, args, PythonEnvironment.PythonDir);
            }
            catch (Exception e)
            {
                Fail($"{stepName} 시작 실패: {e.Message}");
                return;
            }

            runner.OutputLine += Append;
            runner.ErrorLine += Append;
            runner.Completed += code =>
            {
                if (cancelRequested || runner.WasKilled)
                    Finish(false, "사용자가 취소했습니다.");
                else if (code != 0)
                    Fail($"{stepName} 실패(종료 코드 {code}). 위 로그를 확인하세요.");
                else
                    onSuccess();
            };
        }

        static void Append(string line)
        {
            log.Add(line);
            if (log.Count > EditorConstants.SetupLogMaxLines)
                log.RemoveRange(0, log.Count - EditorConstants.SetupLogMaxLines);
            if (progressId >= 0)
                Progress.SetDescription(progressId, line);
            LogChanged?.Invoke();
        }

        static void Fail(string message)
        {
            Debug.LogError(EditorConstants.LogPrefix + message);
            Finish(false, message);
        }

        static void Finish(bool success, string message)
        {
            Append(message);
            if (success)
                Debug.Log(EditorConstants.LogPrefix + message);

            RemoveProgress();
            AssemblyReloadEvents.beforeAssemblyReload -= RemoveProgress;
            IsRunning = false;
            runner = null;

            Action<bool> callback = onFinished;
            onFinished = null;
            callback?.Invoke(success);
        }

        static void RemoveProgress()
        {
            if (progressId < 0)
                return;
            Progress.Remove(progressId);
            progressId = -1;
        }
    }
}
