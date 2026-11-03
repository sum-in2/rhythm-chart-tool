using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SuminTools.RhythmChart
{
    /// <summary>
    /// <see cref="ChartPlayer"/> 를 키보드(Input System)로 플레이하고 IMGUI 로 그리는 최소 화면. 샘플 씬과 에디터의 플레이 테스트가 쓴다.
    /// 노트가 위에서 떨어지는 세로 화면이고, 렌더러/UI 패키지에 의존하지 않아 어느 렌더 파이프라인에서나 그려진다.
    /// 실제 게임에서는 이 컴포넌트 대신 자체 화면과 입력을 만들어 <see cref="ChartPlayer"/> 의 Press/Release 를 연결하면 된다.
    /// </summary>
    [RequireComponent(typeof(ChartPlayer))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rhythm Chart/Chart Player View")]
    public class ChartPlayerView : MonoBehaviour
    {
        const float MaxLaneWidth = 96f;
        const float JudgeLineHeightRatio = 0.82f;
        const float NoteHeight = 16f;
        const float JudgementFlashSeconds = 0.5f;
        const float MissedNoteFadeSeconds = 0.4f;

        static readonly Key[][] KeyLayouts =
        {
            null, null, null, null,
            new[] { Key.D, Key.F, Key.J, Key.K },                      // 4 레인
            new[] { Key.D, Key.F, Key.Space, Key.J, Key.K },           // 5 레인
            new[] { Key.S, Key.D, Key.F, Key.J, Key.K, Key.L },        // 6 레인
        };

        static readonly Color[] LaneColors =
        {
            new Color(0.95f, 0.45f, 0.45f), new Color(0.95f, 0.75f, 0.35f), new Color(0.55f, 0.85f, 0.45f),
            new Color(0.4f, 0.8f, 0.9f), new Color(0.55f, 0.55f, 0.95f), new Color(0.85f, 0.5f, 0.9f),
        };

        [Tooltip("Start 에서 곧바로 재생을 시작한다. 끄면 Begin() 으로 시작한다.")]
        [SerializeField] bool playOnStart = true;

        [Tooltip("처음 재생을 시작할 곡 위치(초). 그보다 앞선 노트는 판정에서 빠진다.")]
        [SerializeField, Min(0f)] float startSeconds;

        [Tooltip("노트가 화면 위에서 판정선까지 내려오는 데 걸리는 시간(초).")]
        [SerializeField, Range(0.5f, 4f)] float approachSeconds = 1.5f;

        ChartPlayer player;
        bool[] laneDown = Array.Empty<bool>();
        GUIStyle bigStyle, smallStyle;
        Judgement flashJudgement;
        double flashError;
        float flashStartedAt = float.NegativeInfinity;

        /// <summary>Esc 로 종료를 요청했을 때. 에디터 플레이 테스트가 플레이 모드를 끄는 데 쓴다.</summary>
        public event Action QuitRequested;

        public ChartPlayer Player => player != null ? player : (player = GetComponent<ChartPlayer>());

        /// <summary>노트가 떨어지는 시간(초).</summary>
        public float ApproachSeconds
        {
            get => approachSeconds;
            set => approachSeconds = Mathf.Clamp(value, 0.5f, 4f);
        }

        /// <summary>자동 시작을 끈다/켠다. 에디터 플레이 테스트처럼 시작 위치를 코드로 정할 때 쓴다.</summary>
        public bool PlayOnStart
        {
            get => playOnStart;
            set => playOnStart = value;
        }

        /// <summary><paramref name="songStart"/> 초 위치부터 플레이를 시작한다.</summary>
        public void Begin(double songStart)
        {
            Player.Play(songStart);
        }

        void OnEnable()
        {
            Player.Judged += OnJudged;
        }

        void OnDisable()
        {
            Player.Judged -= OnJudged;
        }

        void Start()
        {
            if (playOnStart && Player.Chart != null)
                Begin(startSeconds);
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[Key.Escape].wasPressedThisFrame)
                QuitRequested?.Invoke();

            if (!Player.IsPlaying)
                return;

            Key[] keys = KeysFor(Player.Run.LaneCount);
            if (laneDown.Length != keys.Length)
                laneDown = new bool[keys.Length];

            // 이벤트 시각(장치가 알려 주는 실제 입력 시각)으로 판정해서 프레임 간격에 흔들리지 않게 한다.
            double eventTime = keyboard.lastUpdateTime;
            for (int lane = 0; lane < keys.Length; lane++)
            {
                var control = keyboard[keys[lane]];
                if (control.wasPressedThisFrame)
                    Player.Press(lane, eventTime);
                if (control.wasReleasedThisFrame)
                    Player.Release(lane, eventTime);
                laneDown[lane] = control.isPressed;
            }
        }

        void OnJudged(NoteJudgement result)
        {
            flashJudgement = result.Result;
            flashError = result.TimingError;
            flashStartedAt = Time.unscaledTime;
        }

        static Key[] KeysFor(int laneCount)
        {
            return KeyLayouts[Mathf.Clamp(laneCount, ChartConstants.MinLaneCount, ChartConstants.MaxLaneCount)];
        }

        // ---------------------------------------------------------------- 그리기

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            EnsureStyles();

            ChartRun run = Player.Run;
            if (run == null)
            {
                DrawText(new Rect(0, 0, Screen.width, Screen.height), "Chart not started", bigStyle, Color.white);
                return;
            }

            int lanes = run.LaneCount;
            float laneWidth = Mathf.Min(MaxLaneWidth, Screen.width / (lanes + 4f));
            float fieldWidth = laneWidth * lanes;
            float left = (Screen.width - fieldWidth) * 0.5f;
            float judgeY = Screen.height * JudgeLineHeightRatio;

            Fill(new Rect(left, 0f, fieldWidth, Screen.height), new Color(0.08f, 0.08f, 0.1f, 0.92f));
            DrawLanes(run, left, laneWidth, judgeY);
            DrawNotes(run, left, laneWidth, judgeY);
            DrawHud(run, left, fieldWidth, judgeY);
            if (Player.IsFinished)
                DrawResult(run);
        }

        void DrawLanes(ChartRun run, float left, float laneWidth, float judgeY)
        {
            Key[] keys = KeysFor(run.LaneCount);
            for (int lane = 0; lane < run.LaneCount; lane++)
            {
                float x = left + laneWidth * lane;
                bool pressed = lane < laneDown.Length && laneDown[lane];
                Color color = LaneColors[lane % LaneColors.Length];
                if (pressed)
                    Fill(new Rect(x, 0f, laneWidth, judgeY), new Color(color.r, color.g, color.b, 0.18f));
                Fill(new Rect(x, 0f, 1f, Screen.height), new Color(1f, 1f, 1f, 0.12f));
                DrawText(new Rect(x, judgeY + 22f, laneWidth, 28f), keys[lane].ToString(), smallStyle,
                    pressed ? color : new Color(1f, 1f, 1f, 0.6f));
            }
            Fill(new Rect(left + laneWidth * run.LaneCount, 0f, 1f, Screen.height), new Color(1f, 1f, 1f, 0.12f));
            Fill(new Rect(left, judgeY - 2f, laneWidth * run.LaneCount, 4f), new Color(1f, 1f, 1f, 0.85f));
        }

        void DrawNotes(ChartRun run, float left, float laneWidth, float judgeY)
        {
            double songTime = Player.SongTime;
            float pixelsPerSecond = judgeY / approachSeconds;

            for (int i = 0; i < run.NoteCount; i++)
            {
                NoteState state = run.GetState(i);
                if (state == NoteState.Skipped)
                    continue;

                NoteData note = run.GetNote(i);
                bool missed = state == NoteState.Done && run.GetResult(i) == Judgement.Miss;
                if (state == NoteState.Done && !missed)
                    continue;                                   // 친 노트는 사라진다
                if (missed && songTime - note.time > MissedNoteFadeSeconds + approachSeconds)
                    continue;

                float headY = judgeY - (float)(note.time - songTime) * pixelsPerSecond;
                float tailY = headY - note.holdLength * pixelsPerSecond;
                if (state == NoteState.Holding)
                    headY = Mathf.Min(headY, judgeY);           // 누르는 동안 머리는 판정선에 붙어 있다
                if (tailY > Screen.height || headY < -NoteHeight)
                    continue;

                Color color = LaneColors[note.lane % LaneColors.Length];
                if (missed)
                    color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                float x = left + laneWidth * note.lane + 4f;
                float width = laneWidth - 8f;

                if (note.holdLength > 0f)
                    Fill(new Rect(x + 6f, tailY, width - 12f, Mathf.Max(1f, headY - tailY)),
                        new Color(color.r, color.g, color.b, state == NoteState.Holding ? 0.9f : 0.5f));
                Fill(new Rect(x, headY - NoteHeight * 0.5f, width, NoteHeight), color);
            }
        }

        void DrawHud(ChartRun run, float left, float fieldWidth, float judgeY)
        {
            float centerX = left + fieldWidth * 0.5f;

            if (run.Combo > 1)
                DrawText(new Rect(centerX - 120f, judgeY * 0.35f, 240f, 60f), run.Combo.ToString(), bigStyle, Color.white);

            float flashAge = Time.unscaledTime - flashStartedAt;
            if (flashAge >= 0f && flashAge < JudgementFlashSeconds)
            {
                Color color = flashJudgement == Judgement.Perfect ? new Color(1f, 0.9f, 0.3f)
                    : flashJudgement == Judgement.Good ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f);
                color.a = 1f - flashAge / JudgementFlashSeconds;
                DrawText(new Rect(centerX - 120f, judgeY * 0.5f, 240f, 40f), flashJudgement.ToString().ToUpperInvariant(), bigStyle, color);
                if (flashJudgement != Judgement.Miss)
                    DrawText(new Rect(centerX - 120f, judgeY * 0.5f + 40f, 240f, 24f),
                        $"{(flashError >= 0 ? "LATE" : "EARLY")} {Math.Abs(flashError) * 1000.0:0} ms", smallStyle, color);
            }

            float panelX = Mathf.Max(8f, left - 190f);
            DrawText(new Rect(panelX, 12f, 180f, 24f),
                $"Accuracy {run.Accuracy * 100f:0.0}%", smallStyle, Color.white, TextAnchor.UpperLeft);
            DrawText(new Rect(panelX, 36f, 180f, 24f),
                $"Perfect {run.PerfectCount}  Good {run.GoodCount}  Miss {run.MissCount}", smallStyle, Color.white, TextAnchor.UpperLeft);
            DrawText(new Rect(panelX, 60f, 180f, 24f),
                $"Max combo {run.MaxCombo}   {run.Resolved}/{run.Total}", smallStyle, Color.white, TextAnchor.UpperLeft);
            DrawText(new Rect(Screen.width - 200f, 12f, 188f, 24f),
                "Esc: quit", smallStyle, new Color(1f, 1f, 1f, 0.7f), TextAnchor.UpperRight);

            if (Player.IsPlaying && Player.SongTime < Player.StartSeconds)
                DrawText(new Rect(centerX - 150f, judgeY * 0.25f, 300f, 50f), "Ready...", bigStyle, new Color(1f, 1f, 1f, 0.8f));
        }

        void DrawResult(ChartRun run)
        {
            Rect panel = new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.25f, 360f, 220f);
            Fill(panel, new Color(0f, 0f, 0f, 0.85f));
            DrawText(new Rect(panel.x, panel.y + 12f, panel.width, 50f), "RESULT", bigStyle, Color.white);
            DrawText(new Rect(panel.x, panel.y + 72f, panel.width, 30f), $"Accuracy {run.Accuracy * 100f:0.0}%", smallStyle, Color.white);
            DrawText(new Rect(panel.x, panel.y + 102f, panel.width, 30f),
                $"Perfect {run.PerfectCount}   Good {run.GoodCount}   Miss {run.MissCount}", smallStyle, Color.white);
            DrawText(new Rect(panel.x, panel.y + 132f, panel.width, 30f), $"Max combo {run.MaxCombo}", smallStyle, Color.white);
            DrawText(new Rect(panel.x, panel.y + 176f, panel.width, 30f), "Esc: quit", smallStyle, new Color(1f, 1f, 1f, 0.7f));
        }

        void EnsureStyles()
        {
            if (bigStyle != null)
                return;
            bigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 34, fontStyle = FontStyle.Bold };
            smallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
        }

        static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void DrawText(Rect rect, string text, GUIStyle style, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            style.alignment = anchor;
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
