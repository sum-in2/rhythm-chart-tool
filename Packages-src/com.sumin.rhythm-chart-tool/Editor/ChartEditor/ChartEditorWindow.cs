using System;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 차트 에디터. 파형과 비트 그리드 위에서 노트를 추가/삭제/이동하고, 재생하며 확인하고, 난이도별 통계를 본다.
    /// 노트 편집은 <see cref="ChartEditModel"/> 이 맡고, 이 창은 도구 모음·재생·창 수명주기를 맡는다.
    /// </summary>
    internal sealed class ChartEditorWindow : EditorWindow
    {
        const float ScrollbarThickness = 15f;
        const float PlayheadFollowMargin = 0.15f;
        const double PlaybackStartGraceSeconds = 0.5;
        const float StatsPanelHeight = 112f;
        const float StatsRowHeight = 15f;

        static readonly string[] ToolNames = { "선택 (Q)", "그리기 (W)", "지우기 (E)" };
        static readonly string[] SnapNames = { "1/4", "1/8", "1/16" };
        static readonly int[] SnapDivisions = { 1, 2, 4 };
        static readonly string[] OrientationNames = { "가로", "세로" };

        [SerializeField] ChartData chart;
        [SerializeField] Difficulty difficulty = Difficulty.Normal;
        [SerializeField] TimelineViewState view = new TimelineViewState();

        ChartEditModel model;
        TimelineCanvas canvas;
        WaveformData waveform;
        string waveformMessage;
        double playhead;
        bool playing;
        double playbackStartedAt;
        Rect lastCanvasRect;

        [MenuItem(EditorConstants.MenuRoot + "Chart Editor", false, 20)]
        static void OpenFromMenu()
        {
            Open(Selection.activeObject as ChartData);
        }

        [OnOpenAsset(1)]
        static bool OnOpenAsset(int instanceId, int line)
        {
            // Unity 6.3 에서 int 인스턴스 ID 가 deprecated 되었지만, 2022.3 호환을 위해 이 API 를 유지한다.
#pragma warning disable CS0618
            var asset = EditorUtility.InstanceIDToObject(instanceId) as ChartData;
#pragma warning restore CS0618
            if (asset == null)
                return false;
            Open(asset);
            return true;
        }

        /// <summary>차트 에디터를 열고, <paramref name="target"/> 이 있으면 그 차트를 편집한다.</summary>
        public static ChartEditorWindow Open(ChartData target)
        {
            var window = GetWindow<ChartEditorWindow>("Chart Editor");
            window.minSize = new Vector2(720f, 460f);
            if (target != null)
                window.SetChart(target);
            window.Show();
            return window;
        }

        void OnEnable()
        {
            canvas = new TimelineCanvas { Seek = Seek, RequestRepaint = Repaint };
            Undo.undoRedoPerformed += OnUndoRedo;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += StopPlayback;
            Bind();
        }

        void OnDisable()
        {
            StopPlayback();
            Undo.undoRedoPerformed -= OnUndoRedo;
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            EditorApplication.update -= OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPlayback;
        }

        /// <summary>편집할 차트를 바꾼다. 재생 위치와 스크롤은 처음으로 돌아간다.</summary>
        public void SetChart(ChartData target)
        {
            StopPlayback();
            chart = target;
            playhead = 0.0;
            view.scrollSeconds = 0f;
            Bind();
            Repaint();
        }

        void Bind()
        {
            model = chart != null ? new ChartEditModel(chart, difficulty) : null;
            waveform = chart != null ? WaveformCache.Get(chart.AudioClip, out waveformMessage) : null;
            if (chart == null)
                waveformMessage = null;
        }

        // ---------------------------------------------------------------- 외부 변경 감지

        void OnUndoRedo()
        {
            model?.Reload();
            Repaint();
        }

        // 인스펙터의 '생성' 버튼 등 창 밖에서 차트가 바뀌면 다시 불러온다. 내가 방금 반영한 변경은 이미 같으므로 무시된다.
        void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (model == null || canvas.IsDragging || model.MatchesChart())
                return;
            model.Reload();
            Repaint();
        }

        // ---------------------------------------------------------------- 재생

        void OnEditorUpdate()
        {
            if (!playing)
                return;

            bool started = EditorApplication.timeSinceStartup - playbackStartedAt > PlaybackStartGraceSeconds;
            if (started && !AudioPreviewPlayer.IsPlaying)
            {
                playing = false;     // 곡 끝까지 재생됨
                Repaint();
                return;
            }

            playhead = Math.Min(AudioPreviewPlayer.PositionSeconds, chart.Duration);
            if (view.followPlayhead)
                FollowPlayhead();
            Repaint();
        }

        void FollowPlayhead()
        {
            double visible = VisibleSeconds();
            if (playhead < view.scrollSeconds || playhead > view.scrollSeconds + visible)
                view.scrollSeconds = TimelineLayout.ClampScroll(
                    (float)(playhead - visible * PlayheadFollowMargin), chart.Duration, visible);
        }

        double VisibleSeconds()
        {
            bool horizontal = view.orientation == TimelineOrientation.Horizontal;
            float extent = horizontal ? lastCanvasRect.width : lastCanvasRect.height - ScrollbarThickness;
            return Math.Max(1.0, extent) / view.pixelsPerSecond;
        }

        bool CanPlay => chart != null && chart.AudioClip != null && AudioPreviewPlayer.IsAvailable;

        void TogglePlayback()
        {
            if (playing)
            {
                StopPlayback();
                return;
            }
            if (!CanPlay)
                return;

            if (playhead >= chart.Duration - 0.05)
                playhead = 0.0;
            AudioPreviewPlayer.PlayFrom(chart.AudioClip, playhead);
            playbackStartedAt = EditorApplication.timeSinceStartup;
            playing = true;
        }

        void StopPlayback()
        {
            if (playing)
                AudioPreviewPlayer.StopAll();
            playing = false;
        }

        void Seek(double seconds)
        {
            playhead = seconds;
            if (playing)
            {
                AudioPreviewPlayer.PlayFrom(chart.AudioClip, seconds);
                playbackStartedAt = EditorApplication.timeSinceStartup;
            }
            Repaint();
        }

        // ---------------------------------------------------------------- 그리기

        void OnGUI()
        {
            DrawPrimaryToolbar();
            if (model == null)
            {
                EditorGUILayout.HelpBox("편집할 차트(ChartData)를 위 칸에 지정하거나, 프로젝트 창에서 차트를 더블클릭하세요.", MessageType.Info);
                return;
            }

            DrawSecondaryToolbar();
            HandleKeys();

            lastCanvasRect = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            canvas.OnGUI(lastCanvasRect, view, model, waveform, waveformMessage, playhead);

            DrawStats();
        }

        void DrawPrimaryToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                var picked = (ChartData)EditorGUILayout.ObjectField(chart, typeof(ChartData), false, GUILayout.Width(240f));
                if (EditorGUI.EndChangeCheck() && picked != chart)
                    SetChart(picked);

                if (chart != null)
                {
                    string label = EditorUtility.IsDirty(chart) ? "저장*" : "저장";
                    if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(48f)))
                        AssetDatabase.SaveAssets();

                    EditorGUI.BeginChangeCheck();
                    int selected = GUILayout.Toolbar(
                        (int)difficulty, Enum.GetNames(typeof(Difficulty)), EditorStyles.toolbarButton);
                    if (EditorGUI.EndChangeCheck() && selected != (int)difficulty)
                    {
                        difficulty = (Difficulty)selected;
                        model.SetDifficulty(difficulty);
                    }
                }

                GUILayout.FlexibleSpace();
                if (chart != null)
                    GUILayout.Label($"{TimelineCanvas.FormatTime(playhead)} / {TimelineCanvas.FormatTime(chart.Duration)}", EditorStyles.miniLabel);
            }
        }

        void DrawSecondaryToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                view.tool = (EditTool)GUILayout.Toolbar((int)view.tool, ToolNames, EditorStyles.toolbarButton);
                GUILayout.Space(8f);

                view.snapEnabled = GUILayout.Toggle(view.snapEnabled, "스냅", EditorStyles.toolbarButton, GUILayout.Width(44f));
                int snapIndex = Mathf.Max(0, Array.IndexOf(SnapDivisions, view.snapDivisions));
                snapIndex = EditorGUILayout.Popup(snapIndex, SnapNames, EditorStyles.toolbarPopup, GUILayout.Width(48f));
                view.snapDivisions = SnapDivisions[snapIndex];
                GUILayout.Space(8f);

                view.orientation = (TimelineOrientation)EditorGUILayout.Popup(
                    (int)view.orientation, OrientationNames, EditorStyles.toolbarPopup, GUILayout.Width(52f));

                GUILayout.Label("확대", EditorStyles.miniLabel);
                float log = Mathf.Log10(view.pixelsPerSecond);
                float newLog = GUILayout.HorizontalSlider(
                    log, Mathf.Log10(TimelineViewState.MinPixelsPerSecond), Mathf.Log10(TimelineViewState.MaxPixelsPerSecond),
                    GUILayout.Width(110f));
                if (!Mathf.Approximately(newLog, log))
                    view.pixelsPerSecond = Mathf.Pow(10f, newLog);

                view.followPlayhead = GUILayout.Toggle(view.followPlayhead, "따라가기", EditorStyles.toolbarButton, GUILayout.Width(62f));
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!CanPlay))
                {
                    string tooltip = CanPlay ? "스페이스바" : AudioPreviewPlayer.IsAvailable ? "차트에 오디오가 없습니다" : "이 Unity 버전에서는 미리듣기를 쓸 수 없습니다";
                    if (GUILayout.Button(new GUIContent(playing ? "■ 정지" : "▶ 재생", tooltip), EditorStyles.toolbarButton, GUILayout.Width(64f)))
                        TogglePlayback();
                }
            }
        }

        void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField)
                return;

            bool used = true;
            switch (e.keyCode)
            {
                case KeyCode.Q: view.tool = EditTool.Select; break;
                case KeyCode.W: view.tool = EditTool.Draw; break;
                case KeyCode.E: view.tool = EditTool.Erase; break;
                case KeyCode.Space: TogglePlayback(); break;
                case KeyCode.Home: Seek(0.0); break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (model.DeleteSelected() > 0)
                        model.Commit("노트 삭제");
                    break;
                case KeyCode.A when EditorGUI.actionKey:
                    model.SelectAll();
                    break;
                case KeyCode.Escape:
                    canvas.CancelDrag(model);
                    model.ClearSelection();
                    break;
                default:
                    used = false;
                    break;
            }

            if (used)
            {
                e.Use();
                Repaint();
            }
        }

        void DrawStats()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(StatsPanelHeight)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("난이도별 통계", EditorStyles.boldLabel, GUILayout.Width(120f));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(
                        $"레인 {chart.LaneCount}개  ·  BPM {chart.Bpm:0.##}  ·  선택 {model.SelectedCount}개",
                        EditorStyles.miniLabel);
                }

                GUIStyle normal = EditorStyles.miniLabel;
                var bold = new GUIStyle(normal) { fontStyle = FontStyle.Bold };
                DrawStatsRow("난이도", "노트 수", "초당 밀도", "롱노트 비율", bold);
                for (int i = 0; i < ChartConstants.DifficultyCount; i++)
                {
                    var d = (Difficulty)i;
                    ChartStats stats = d == difficulty
                        ? StatsOf(model)
                        : ChartStats.Compute(chart.GetNotes(d), chart.Duration);
                    DrawStatsRow(
                        d == difficulty ? $"▶ {d}" : d.ToString(),
                        stats.NoteCount.ToString(),
                        $"{stats.NotesPerSecond:0.00} /s",
                        $"{stats.HoldRatio * 100f:0.#}%",
                        d == difficulty ? bold : normal);
                }
            }
        }

        // 편집 중인 난이도는 아직 반영하지 않은 드래그 결과까지 포함해 센다.
        static ChartStats StatsOf(ChartEditModel model)
        {
            var notes = new NoteData[model.Entries.Count];
            for (int i = 0; i < notes.Length; i++)
                notes[i] = model.Entries[i].note;
            return ChartStats.Compute(notes, model.Chart.Duration);
        }

        static void DrawStatsRow(string a, string b, string c, string d, GUIStyle style)
        {
            GUILayoutOption height = GUILayout.Height(StatsRowHeight);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(a, style, GUILayout.Width(110f), height);
                GUILayout.Label(b, style, GUILayout.Width(80f), height);
                GUILayout.Label(c, style, GUILayout.Width(100f), height);
                GUILayout.Label(d, style, GUILayout.Width(100f), height);
            }
        }
    }
}
