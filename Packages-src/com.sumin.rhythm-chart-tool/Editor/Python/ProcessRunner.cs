using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 외부 프로세스를 비동기로 실행한다. 출력 줄과 종료 이벤트는 모두 에디터 메인 스레드에서 호출되므로
    /// 핸들러에서 Unity API 를 써도 된다. 도메인 리로드나 에디터 종료 시 실행 중인 프로세스를 정리한다.
    /// </summary>
    internal sealed class ProcessRunner
    {
        static readonly List<ProcessRunner> Active = new List<ProcessRunner>();
        static bool hooked;

        readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        Process process;
        bool finished;

        /// <summary>stdout 한 줄(메인 스레드).</summary>
        public event Action<string> OutputLine;

        /// <summary>stderr 한 줄(메인 스레드).</summary>
        public event Action<string> ErrorLine;

        /// <summary>프로세스 종료(메인 스레드). 인자는 종료 코드이며 <see cref="WasKilled"/> 일 때는 의미가 없다.</summary>
        public event Action<int> Completed;

        public bool IsRunning => process != null && !finished;

        /// <summary><see cref="Kill"/> 로 중단됐는지 여부.</summary>
        public bool WasKilled { get; private set; }

        /// <summary>프로세스를 시작한다. 실행 파일이 없거나 시작에 실패하면 예외를 던진다.</summary>
        public static ProcessRunner Start(string executable, IEnumerable<string> arguments, string workingDirectory)
        {
            var runner = new ProcessRunner();
            runner.Begin(executable, arguments, workingDirectory);
            return runner;
        }

        /// <summary>UTF-8 출력과 인자 목록(공백/한글 경로 안전)을 쓰는 공통 시작 정보.</summary>
        public static ProcessStartInfo CreateStartInfo(
            string executable, IEnumerable<string> arguments, string workingDirectory)
        {
            var utf8 = new UTF8Encoding(false);
            var info = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
            };
            if (!string.IsNullOrEmpty(workingDirectory))
                info.WorkingDirectory = workingDirectory;
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);

            // Windows 기본 코드페이지(cp949 등)로 한글 경로/메시지가 깨지지 않게 Python 입출력을 UTF-8 로 고정한다.
            info.Environment["PYTHONUTF8"] = "1";
            info.Environment["PYTHONIOENCODING"] = "utf-8";
            info.Environment["PYTHONUNBUFFERED"] = "1";
            info.Environment["PIP_NO_INPUT"] = "1";
            return info;
        }

        /// <summary>실행 중인 프로세스를 자식 프로세스까지 종료한다.</summary>
        public void Kill()
        {
            if (!IsRunning)
                return;
            WasKilled = true;
            KillProcessTree(process);
        }

        void Begin(string executable, IEnumerable<string> arguments, string workingDirectory)
        {
            process = new Process { StartInfo = CreateStartInfo(executable, arguments, workingDirectory) };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) queue.Enqueue(() => OutputLine?.Invoke(e.Data));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) queue.Enqueue(() => ErrorLine?.Invoke(e.Data));
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            EnsureHooks();
            Active.Add(this);
            EditorApplication.update += Pump;

            Process started = process;
            Task.Run(() =>
            {
                // 인자 없는 WaitForExit 는 비동기 출력 읽기가 끝날 때까지 기다린다. 그래서 종료 이벤트가 항상 마지막 줄 뒤에 온다.
                started.WaitForExit();
                int code = started.ExitCode;
                queue.Enqueue(() => Finish(code));
            });
        }

        void Pump()
        {
            while (queue.TryDequeue(out Action action))
                action();
        }

        void Finish(int exitCode)
        {
            finished = true;
            Active.Remove(this);
            EditorApplication.update -= Pump;
            process.Dispose();
            Completed?.Invoke(exitCode);
        }

        static void EnsureHooks()
        {
            if (hooked)
                return;
            hooked = true;
            AssemblyReloadEvents.beforeAssemblyReload += KillAll;
            EditorApplication.quitting += KillAll;
        }

        static void KillAll()
        {
            foreach (ProcessRunner runner in Active.ToArray())
                runner.Kill();
        }

        static void KillProcessTree(Process target)
        {
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Windows 의 venv python.exe 는 실제 인터프리터를 자식으로 띄우는 런처라서 부모만 죽이면 분석이 계속된다.
                    var info = new ProcessStartInfo("taskkill")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    info.ArgumentList.Add("/PID");
                    info.ArgumentList.Add(target.Id.ToString());
                    info.ArgumentList.Add("/T");
                    info.ArgumentList.Add("/F");
                    using (Process killer = Process.Start(info))
                        killer?.WaitForExit(EditorConstants.ProcessKillWaitMs);
                }
                if (!target.HasExited)
                    target.Kill();
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                // 이미 종료된 프로세스는 무시한다.
            }
        }
    }
}
