using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>Python 경로, venv 경로를 설정하고 분석 환경을 만드는 창.</summary>
    internal sealed class RhythmChartSettingsWindow : EditorWindow
    {
        const float LogHeight = 220f;

        Vector2 logScroll;
        string logText = string.Empty;
        EnvStatus envStatus;
        string pythonStatus = string.Empty;
        MessageType pythonStatusType = MessageType.None;

        [MenuItem(EditorConstants.MenuRoot + "Settings...", false, 0)]
        static void Open()
        {
            var window = GetWindow<RhythmChartSettingsWindow>("Rhythm Chart");
            window.minSize = new Vector2(420f, 420f);
            window.Show();
        }

        void OnEnable()
        {
            PythonEnvSetup.LogChanged += OnLogChanged;
            OnLogChanged();
            RefreshPythonStatus();
            envStatus = PythonEnvironment.GetStatus();
        }

        // OnGUI 는 마우스 이동마다 호출되므로 파일을 읽는 상태 확인은 필요한 때만 한다.
        void OnFocus()
        {
            envStatus = PythonEnvironment.GetStatus();
        }

        void OnDisable()
        {
            PythonEnvSetup.LogChanged -= OnLogChanged;
        }

        void OnLogChanged()
        {
            logText = string.Join("\n", PythonEnvSetup.Log);
            logScroll.y = float.MaxValue;
            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUI.DisabledScope(PythonEnvSetup.IsRunning))
            {
                DrawPythonSection();
                EditorGUILayout.Space();
                DrawVenvSection();
            }

            EditorGUILayout.Space();
            DrawEnvSection();
            DrawLog();
        }

        void DrawPythonSection()
        {
            EditorGUILayout.LabelField("Python (venv 생성에 사용)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string path = EditorGUILayout.DelayedTextField("실행 파일", RhythmChartSettings.PythonPath);
                if (EditorGUI.EndChangeCheck())
                {
                    RhythmChartSettings.PythonPath = path;
                    RefreshPythonStatus();
                }

                if (GUILayout.Button("찾아보기", GUILayout.Width(70f)))
                {
                    string picked = EditorUtility.OpenFilePanel("Python 실행 파일 선택", string.Empty, string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        RhythmChartSettings.PythonPath = picked;
                        RefreshPythonStatus();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("자동 검색"))
                {
                    RhythmChartSettings.PythonPath = string.Empty;
                    RefreshPythonStatus();
                }
            }

            if (!string.IsNullOrEmpty(pythonStatus))
                EditorGUILayout.HelpBox(pythonStatus, pythonStatusType);
        }

        void DrawVenvSection()
        {
            EditorGUILayout.LabelField("가상환경(venv)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string path = EditorGUILayout.DelayedTextField("경로", RhythmChartSettings.VenvPath);
                if (EditorGUI.EndChangeCheck())
                    RhythmChartSettings.VenvPath = path == RhythmChartSettings.DefaultVenvPath ? string.Empty : path;

                if (GUILayout.Button("찾아보기", GUILayout.Width(70f)))
                {
                    string picked = EditorUtility.OpenFolderPanel("venv 폴더 선택", RhythmChartSettings.ProjectRoot, "venv");
                    if (!string.IsNullOrEmpty(picked))
                        RhythmChartSettings.VenvPath = picked;
                }
            }

            using (new EditorGUI.DisabledScope(!RhythmChartSettings.IsVenvPathCustom))
            {
                if (GUILayout.Button("기본 경로로 되돌리기 (Library/RhythmChartTool/venv)"))
                    RhythmChartSettings.VenvPath = string.Empty;
            }
        }

        void DrawEnvSection()
        {
            EnvStatus status = envStatus;
            switch (status)
            {
                case EnvStatus.Ready:
                    EditorGUILayout.HelpBox("Python 환경이 준비되어 있습니다.", MessageType.Info);
                    break;
                case EnvStatus.Outdated:
                    EditorGUILayout.HelpBox(
                        "requirements.txt 가 설치 당시와 달라졌습니다. 환경을 다시 설정하세요.", MessageType.Warning);
                    break;
                default:
                    EditorGUILayout.HelpBox(
                        "Python 환경이 없습니다. 아래 버튼으로 venv 를 만들고 패키지를 설치하세요.", MessageType.Warning);
                    break;
            }

            if (PythonEnvSetup.IsRunning)
            {
                if (GUILayout.Button("취소", GUILayout.Height(28f)))
                    PythonEnvSetup.Cancel();
            }
            else if (GUILayout.Button(status == EnvStatus.Ready ? "Python 환경 다시 설정" : "Python 환경 설정", GUILayout.Height(28f)))
            {
                PythonEnvSetup.Start(_ =>
                {
                    envStatus = PythonEnvironment.GetStatus();
                    Repaint();
                });
            }
        }

        void DrawLog()
        {
            EditorGUILayout.LabelField("로그", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(logScroll, GUILayout.Height(LogHeight)))
            {
                logScroll = scroll.scrollPosition;
                EditorGUILayout.SelectableLabel(
                    logText, EditorStyles.textArea, GUILayout.ExpandHeight(true), GUILayout.MinHeight(LogHeight - 8f));
            }
        }

        void RefreshPythonStatus()
        {
            string configured = RhythmChartSettings.PythonPath;
            bool found = string.IsNullOrWhiteSpace(configured)
                ? PythonLocator.TryFindOnPath(out PythonInfo info)
                : PythonLocator.TryProbe(new[] { configured }, out info, out _);

            if (info.Executable == null)
            {
                pythonStatus = found ? string.Empty : "Python 을 찾지 못했습니다. 실행 파일 경로를 지정하세요.";
                pythonStatusType = MessageType.Error;
            }
            else if (!info.MeetsMinimum)
            {
                pythonStatus = $"{info} — {EditorConstants.MinPythonMajor}.{EditorConstants.MinPythonMinor} 이상이 필요합니다.";
                pythonStatusType = MessageType.Error;
            }
            else
            {
                pythonStatus = info.ToString();
                pythonStatusType = MessageType.Info;
            }
        }
    }
}
