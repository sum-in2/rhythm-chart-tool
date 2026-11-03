using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 차트 에디터의 "현재 위치부터 플레이 테스트". 플레이 모드로 들어간 뒤 현재 열린 씬 위에 플레이어 오브젝트를 만들어
    /// 선택한 위치부터 차트를 재생한다. 오브젝트는 플레이 모드 동안에만 존재하므로 씬은 바뀌지 않는다.
    /// 도메인 리로드로 정적 변수가 사라지므로 요청은 SessionState 로 넘긴다.
    /// </summary>
    internal static class ChartPlaytest
    {
        const string PrefsPrefix = EditorConstants.PrefsPrefix + "Playtest.";
        const string PendingKey = PrefsPrefix + "Pending";
        const string ChartGuidKey = PrefsPrefix + "ChartGuid";
        const string DifficultyKey = PrefsPrefix + "Difficulty";
        const string StartKey = PrefsPrefix + "StartSeconds";

        public static bool CanStart => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>플레이 모드로 들어가 <paramref name="startSeconds"/> 부터 플레이 테스트를 시작한다.</summary>
        public static void Start(ChartData chart, Difficulty difficulty, double startSeconds)
        {
            string path = AssetDatabase.GetAssetPath(chart);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError(EditorConstants.LogPrefix + "저장된 차트 에셋만 플레이 테스트할 수 있습니다.", chart);
                return;
            }
            if (!CanStart)
                return;

            // 플레이 모드는 디스크의 에셋을 다시 읽을 수 있으므로 편집 내용을 먼저 저장한다.
            AssetDatabase.SaveAssetIfDirty(chart);

            SessionState.SetString(ChartGuidKey, AssetDatabase.AssetPathToGUID(path));
            SessionState.SetInt(DifficultyKey, (int)difficulty);
            SessionState.SetFloat(StartKey, (float)startSeconds);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Register()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                Launch();
            }
        }

        static void Launch()
        {
            string guid = SessionState.GetString(ChartGuidKey, string.Empty);
            var chart = AssetDatabase.LoadAssetAtPath<ChartData>(AssetDatabase.GUIDToAssetPath(guid));
            if (chart == null)
            {
                Debug.LogError(EditorConstants.LogPrefix + "플레이 테스트할 차트를 찾지 못했습니다.");
                return;
            }

            var host = new GameObject("Chart Playtest");
            if (Camera.allCamerasCount == 0)
            {
                // 카메라가 없으면 Game 뷰에 "No cameras rendering" 이 뜨므로 임시 카메라를 둔다(플레이 모드 동안만).
                var camera = host.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
            }
            if (Object.FindFirstObjectByType<AudioListener>() == null)
                host.AddComponent<AudioListener>();

            var player = host.AddComponent<ChartPlayer>();
            player.Chart = chart;
            player.Difficulty = (Difficulty)SessionState.GetInt(DifficultyKey, (int)Difficulty.Normal);

            var view = host.AddComponent<ChartPlayerView>();
            view.PlayOnStart = false;
            view.QuitRequested += () => EditorApplication.isPlaying = false;
            view.Begin(SessionState.GetFloat(StartKey, 0f));
        }
    }
}
