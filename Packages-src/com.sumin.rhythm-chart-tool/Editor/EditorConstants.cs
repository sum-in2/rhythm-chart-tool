namespace SuminTools.RhythmChart.Editor
{
    /// <summary>에디터 코드 전반에서 쓰는 상수 모음.</summary>
    internal static class EditorConstants
    {
        public const string LogPrefix = "[Rhythm Chart] ";
        public const string MenuRoot = "Tools/Rhythm Chart/";
        public const string AssetMenuRoot = "Assets/Rhythm Chart/";
        public const string PrefsPrefix = "SuminTools.RhythmChart.";

        // 패키지 안의 Python 관련 파일
        public const string PythonDirName = "Python~";
        public const string ScriptFileName = "analyze.py";
        public const string RequirementsFileName = "requirements.txt";

        // Library 아래 캐시 구조: Library/RhythmChartTool/{venv, analysis}
        public const string CacheDirName = "RhythmChartTool";
        public const string VenvDirName = "venv";
        public const string AnalysisDirName = "analysis";
        public const string EnvMarkerFileName = "rhythmchart-env.json";

        public const string ChartAssetSuffix = ".chart.asset";

        // librosa 1.0.0 / numpy / scipy 의 Requires-Python 이 >=3.12 이다(requirements.txt 의 고정 버전 기준).
        public const int MinPythonMajor = 3;
        public const int MinPythonMinor = 12;

        public const int ProcessProbeTimeoutMs = 10000;
        public const int ProcessKillWaitMs = 5000;
        public const int SetupLogMaxLines = 1000;

        // analyze.py 가 stdout 으로 내보내는 진행률 줄: "PROGRESS 0.40"
        public const string ProgressLinePrefix = "PROGRESS ";
        public const int SupportedAnalysisSchemaVersion = 1;
        public const int FailureLogTailLines = 20;
    }
}
