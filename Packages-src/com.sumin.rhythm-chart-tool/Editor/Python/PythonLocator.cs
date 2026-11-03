using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>시스템에 설치된 Python 인터프리터 정보.</summary>
    internal readonly struct PythonInfo
    {
        public PythonInfo(string executable, int major, int minor)
        {
            Executable = executable;
            Major = major;
            Minor = minor;
        }

        /// <summary>인터프리터가 스스로 보고한 실제 실행 파일 경로(sys.executable).</summary>
        public string Executable { get; }
        public int Major { get; }
        public int Minor { get; }

        public bool MeetsMinimum =>
            Major > EditorConstants.MinPythonMajor ||
            (Major == EditorConstants.MinPythonMajor && Minor >= EditorConstants.MinPythonMinor);

        public override string ToString() => $"Python {Major}.{Minor} ({Executable})";
    }

    /// <summary>Python 인터프리터를 찾고 검증한다.</summary>
    internal static class PythonLocator
    {
        const string ProbeScript =
            "import sys;print(sys.executable);print(sys.version_info[0]);print(sys.version_info[1])";

        static readonly string[][] AutoCandidates =
        {
            new[] { "python3" },
            new[] { "python" },
            new[] { "py", "-3" },
        };

        /// <summary>
        /// 설정에 지정된 경로를 쓰고, 비어 있으면 PATH 에서 자동 검색한다.
        /// 자동 검색에 성공하면 그 경로를 설정에 저장한다. 최소 버전 검사는 호출자가 한다.
        /// </summary>
        public static bool TryResolve(out PythonInfo info, out string error)
        {
            string configured = RhythmChartSettings.PythonPath;
            if (!string.IsNullOrWhiteSpace(configured))
                return TryProbe(new[] { configured }, out info, out error);

            if (TryFindOnPath(out info))
            {
                RhythmChartSettings.PythonPath = info.Executable;
                error = null;
                return true;
            }

            error = "PATH 에서 Python 을 찾지 못했습니다. 설정 창에서 Python 실행 파일 경로를 지정하세요.";
            return false;
        }

        /// <summary>PATH 의 후보를 차례로 검사해 최소 버전을 만족하는 첫 인터프리터를 반환한다.</summary>
        public static bool TryFindOnPath(out PythonInfo found)
        {
            PythonInfo? fallback = null;
            foreach (string[] candidate in AutoCandidates)
            {
                if (!TryProbe(candidate, out PythonInfo info, out _))
                    continue;
                if (info.MeetsMinimum)
                {
                    found = info;
                    return true;
                }
                fallback = fallback ?? info;
            }

            // 최소 버전 미만만 있으면 그 사실을 알릴 수 있게 하나를 돌려준다.
            found = fallback ?? default;
            return false;
        }

        /// <summary>
        /// 실행해서 실제 경로와 버전을 얻는다. 존재하지 않는 경로나 Windows 스토어 스텁처럼
        /// 실행은 되지만 Python 이 아닌 경우는 실패로 처리한다.
        /// </summary>
        public static bool TryProbe(IReadOnlyList<string> commandWithPrefixArgs, out PythonInfo info, out string error)
        {
            info = default;
            var arguments = new List<string>();
            for (int i = 1; i < commandWithPrefixArgs.Count; i++)
                arguments.Add(commandWithPrefixArgs[i]);
            arguments.Add("-c");
            arguments.Add(ProbeScript);

            try
            {
                ProcessStartInfo start = ProcessRunner.CreateStartInfo(commandWithPrefixArgs[0], arguments, null);
                using (Process process = Process.Start(start))
                {
                    // 동기 ReadToEnd 는 멈춘 프로세스에서 타임아웃이 걸리지 않으므로 비동기로 읽는다.
                    Task<string> reading = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(EditorConstants.ProcessProbeTimeoutMs))
                    {
                        process.Kill();
                        error = $"응답이 없습니다: {commandWithPrefixArgs[0]}";
                        return false;
                    }

                    string stdout = reading.Result;
                    string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (process.ExitCode != 0 || lines.Length < 3 ||
                        !int.TryParse(lines[1], out int major) || !int.TryParse(lines[2], out int minor))
                    {
                        error = $"Python 이 아니거나 실행에 실패했습니다: {commandWithPrefixArgs[0]}";
                        return false;
                    }

                    info = new PythonInfo(lines[0], major, minor);
                    error = null;
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                error = $"실행할 수 없습니다: {commandWithPrefixArgs[0]} ({e.Message})";
                return false;
            }
        }
    }
}
