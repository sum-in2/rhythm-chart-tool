using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 파형, 눈금자, 비트 그리드, 레인, 노트, 재생 헤드를 그리고 마우스 입력(추가/삭제/롱노트/선택/이동/스크럽)을 처리한다.
    /// 좌표 변환은 모두 <see cref="TimelineLayout"/> 을 거치므로 가로/세로가 같은 코드로 동작한다.
    /// </summary>
    internal sealed class TimelineCanvas
    {
        const float ScrollbarThickness = 15f;
        const float WheelScrollFraction = 0.04f;
        const float WheelZoomSpeed = 0.08f;
        const float MinColumnPixels = 6f;       // 이보다 촘촘한 선은 그리지 않는다
        const int MaxGridLines = 800;
        const float ClickDragThreshold = 4f;
        const float HitInflate = 2f;

        static readonly Color BackgroundColor = new Color(0.14f, 0.14f, 0.15f);
        static readonly Color RulerColor = new Color(0.2f, 0.2f, 0.22f);
        static readonly Color WaveformBackground = new Color(0.1f, 0.1f, 0.11f);
        static readonly Color WaveformColor = new Color(0.35f, 0.65f, 0.85f, 0.95f);
        static readonly Color LaneColorA = new Color(0.18f, 0.18f, 0.2f);
        static readonly Color LaneColorB = new Color(0.16f, 0.16f, 0.18f);
        static readonly Color OutsideColor = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color BarLineColor = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color BeatLineColor = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color SubdivisionLineColor = new Color(1f, 1f, 1f, 0.07f);
        static readonly Color PlayheadColor = new Color(1f, 0.3f, 0.3f, 1f);
        static readonly Color SelectionColor = Color.white;
        static readonly Color BoxFill = new Color(0.3f, 0.6f, 1f, 0.2f);
        static readonly Color BoxLine = new Color(0.4f, 0.7f, 1f, 0.9f);
        static readonly Color TextColor = new Color(0.8f, 0.8f, 0.8f);

        static readonly Color[] LaneColors =
        {
            new Color(0.95f, 0.45f, 0.45f), new Color(0.95f, 0.75f, 0.35f), new Color(0.55f, 0.85f, 0.45f),
            new Color(0.4f, 0.8f, 0.9f), new Color(0.55f, 0.55f, 0.95f), new Color(0.85f, 0.5f, 0.9f),
        };

        static readonly float[] RulerSteps = { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f };

        enum Drag { None, Scrub, Move, Create, Box, Erase }

        readonly List<GridLine> gridLines = new List<GridLine>();
        float[] columnMin = new float[0];
        float[] columnMax = new float[0];
        GUIStyle labelStyle;

        Drag drag;
        Vector2 mouseDownPosition;
        Vector2 boxEnd;
        bool boxAdditive;
        NoteEntry moveAnchor;
        double moveAnchorTime;
        float moveAnchorNoteTime;
        int moveAnchorLane;
        int createLane;
        double createStart;
        double createEnd;
        bool eraseChanged;

        /// <summary>눈금자/파형을 눌러 재생 위치를 옮겼을 때(초).</summary>
        public Action<double> Seek;

        /// <summary>화면을 다시 그려야 할 때.</summary>
        public Action RequestRepaint;

        /// <summary>드래그 중인가. 드래그 중에는 모델이 차트 에셋과 달라도 정상이다.</summary>
        public bool IsDragging => drag != Drag.None;

        /// <summary>진행 중인 드래그를 취소한다(Esc).</summary>
        public void CancelDrag(ChartEditModel model)
        {
            if (drag == Drag.Move)
                model.CancelMove();
            if (drag == Drag.Erase && eraseChanged)
                model.Reload();
            drag = Drag.None;
            eraseChanged = false;
            GUIUtility.hotControl = 0;
        }

        public void OnGUI(
            Rect rect, TimelineViewState view, ChartEditModel model,
            WaveformData waveform, string waveformMessage, double playhead)
        {
            bool horizontal = view.orientation == TimelineOrientation.Horizontal;
            Rect area = horizontal
                ? new Rect(rect.x, rect.y, rect.width, rect.height - ScrollbarThickness)
                : new Rect(rect.x, rect.y, rect.width - ScrollbarThickness, rect.height);
            Rect scrollRect = horizontal
                ? new Rect(rect.x, area.yMax, area.width, ScrollbarThickness)
                : new Rect(area.xMax, rect.y, ScrollbarThickness, area.height);

            GUI.BeginClip(area);
            var layout = new TimelineLayout(
                view.orientation, new Rect(0f, 0f, area.width, area.height),
                view.pixelsPerSecond, view.scrollSeconds, model.Chart.LaneCount);

            HandleInput(layout, view, model);
            layout = new TimelineLayout(
                view.orientation, layout.Area, view.pixelsPerSecond, view.scrollSeconds, model.Chart.LaneCount);

            if (Event.current.type == EventType.Repaint)
                Draw(layout, view, model, waveform, waveformMessage, playhead);
            GUI.EndClip();

            DrawScrollbar(scrollRect, layout, view, model.Chart.Duration);
        }

        // ---------------------------------------------------------------- 입력

        void HandleInput(TimelineLayout layout, TimelineViewState view, ChartEditModel model)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Vector2 mouse = e.mousePosition;

            switch (e.GetTypeForControl(id))
            {
                case EventType.ScrollWheel:
                    if (layout.Area.Contains(mouse))
                    {
                        if (EditorGUI.actionKey)
                            ZoomAt(layout, view, layout.MainOf(mouse), Mathf.Exp(-e.delta.y * WheelZoomSpeed), model.Chart.Duration);
                        else
                            view.scrollSeconds = TimelineLayout.ClampScroll(
                                view.scrollSeconds + (float)(e.delta.y + e.delta.x) * (float)layout.VisibleSeconds * WheelScrollFraction,
                                model.Chart.Duration, layout.VisibleSeconds);
                        e.Use();
                        RequestRepaint?.Invoke();
                    }
                    break;

                case EventType.MouseDown:
                    if (layout.Area.Contains(mouse) && (e.button == 0 || e.button == 1))
                    {
                        GUIUtility.hotControl = id;
                        mouseDownPosition = mouse;
                        OnMouseDown(layout, view, model, e);
                        e.Use();
                        RequestRepaint?.Invoke();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        OnMouseDrag(layout, view, model, mouse);
                        e.Use();
                        RequestRepaint?.Invoke();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        OnMouseUp(layout, view, model, mouse, e);
                        e.Use();
                        RequestRepaint?.Invoke();
                    }
                    break;
            }
        }

        void OnMouseDown(TimelineLayout layout, TimelineViewState view, ChartEditModel model, Event e)
        {
            Vector2 mouse = e.mousePosition;

            if (e.button == 0 && (layout.InRuler(mouse) || layout.InWaveform(mouse)))
            {
                drag = Drag.Scrub;
                SeekTo(layout, mouse, model.Chart.Duration);
                return;
            }

            NoteEntry hit = HitTest(layout, model, mouse);
            bool additive = e.shift || EditorGUI.actionKey;

            if (e.button == 1)
            {
                drag = Drag.None;
                if (hit != null)
                {
                    if (hit.selected) model.DeleteSelected(); else model.Delete(hit);
                    model.Commit("노트 삭제");
                }
                return;
            }

            if (view.tool == EditTool.Erase)
            {
                drag = Drag.Erase;
                eraseChanged = false;
                EraseAt(layout, model, mouse);
                return;
            }

            if (hit != null)
            {
                if (EditorGUI.actionKey)
                    hit.selected = !hit.selected;
                else if (e.shift)
                    hit.selected = true;
                else if (!hit.selected)
                    model.SelectOnly(hit);

                if (hit.selected)
                {
                    drag = Drag.Move;
                    moveAnchor = hit;
                    moveAnchorTime = layout.MainToTime(layout.MainOf(mouse));
                    moveAnchorNoteTime = hit.note.time;
                    moveAnchorLane = layout.LaneAtClamped(mouse);
                    model.BeginMove();
                }
                else
                {
                    drag = Drag.None;
                }
                return;
            }

            int lane = layout.LaneAt(mouse);
            if (view.tool == EditTool.Draw && lane >= 0)
            {
                drag = Drag.Create;
                createLane = lane;
                createStart = createEnd = SnapTime(layout, view, model, layout.MainToTime(layout.MainOf(mouse)));
                return;
            }

            // 선택 도구(또는 레인 밖): 빈 곳을 누르면 범위 선택을 시작한다.
            drag = Drag.Box;
            boxEnd = mouse;
            boxAdditive = additive;
        }

        void OnMouseDrag(TimelineLayout layout, TimelineViewState view, ChartEditModel model, Vector2 mouse)
        {
            switch (drag)
            {
                case Drag.Scrub:
                    SeekTo(layout, mouse, model.Chart.Duration);
                    break;

                case Drag.Move:
                    double delta = layout.MainToTime(layout.MainOf(mouse)) - moveAnchorTime;
                    double target = SnapTime(layout, view, model, moveAnchorNoteTime + delta);
                    model.PreviewMove((float)(target - moveAnchorNoteTime), layout.LaneAtClamped(mouse) - moveAnchorLane);
                    break;

                case Drag.Create:
                    createEnd = SnapTime(layout, view, model, layout.MainToTime(layout.MainOf(mouse)));
                    break;

                case Drag.Box:
                    boxEnd = mouse;
                    break;

                case Drag.Erase:
                    EraseAt(layout, model, mouse);
                    break;
            }
        }

        void OnMouseUp(TimelineLayout layout, TimelineViewState view, ChartEditModel model, Vector2 mouse, Event e)
        {
            switch (drag)
            {
                case Drag.Move:
                    model.EndMove();
                    break;

                case Drag.Create:
                {
                    float start = (float)Math.Min(createStart, createEnd);
                    float length = (float)Math.Abs(createEnd - createStart);
                    NoteEntry added = model.TryAdd(Mathf.Max(0f, start), createLane, length);
                    if (added != null)
                    {
                        model.SelectOnly(added);
                        model.Commit(added.note.type == NoteType.Hold ? "롱노트 추가" : "노트 추가");
                    }
                    break;
                }

                case Drag.Box:
                {
                    bool isClick = (mouse - mouseDownPosition).magnitude < ClickDragThreshold;
                    if (isClick)
                    {
                        if (!boxAdditive)
                            model.ClearSelection();
                    }
                    else
                    {
                        Rect box = BoxRect();
                        model.SelectWhere(
                            entry => layout.NoteRect(entry.note.time, entry.note.lane, entry.note.holdLength).Overlaps(box),
                            boxAdditive);
                    }
                    break;
                }

                case Drag.Erase:
                    if (eraseChanged)
                        model.Commit("노트 삭제");
                    break;
            }

            drag = Drag.None;
            eraseChanged = false;
        }

        void EraseAt(TimelineLayout layout, ChartEditModel model, Vector2 mouse)
        {
            NoteEntry hit = HitTest(layout, model, mouse);
            if (hit != null && model.Delete(hit))
                eraseChanged = true;
        }

        void SeekTo(TimelineLayout layout, Vector2 mouse, float duration)
        {
            double time = Math.Max(0.0, Math.Min(duration, layout.MainToTime(layout.MainOf(mouse))));
            Seek?.Invoke(time);
        }

        static NoteEntry HitTest(TimelineLayout layout, ChartEditModel model, Vector2 mouse)
        {
            if (layout.LaneAt(mouse) < 0)
                return null;
            IReadOnlyList<NoteEntry> entries = model.Entries;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                NoteData note = entries[i].note;
                Rect rect = layout.NoteRect(note.time, note.lane, note.holdLength);
                rect.xMin -= HitInflate; rect.xMax += HitInflate; rect.yMin -= HitInflate; rect.yMax += HitInflate;
                if (rect.Contains(mouse))
                    return entries[i];
            }
            return null;
        }

        static double SnapTime(TimelineLayout layout, TimelineViewState view, ChartEditModel model, double time)
        {
            ChartData chart = model.Chart;
            double snapped = view.snapEnabled ? GridMath.Snap(time, chart.Bpm, chart.Offset, view.snapDivisions) : time;
            return Math.Max(0.0, Math.Min(chart.Duration, snapped));
        }

        Rect BoxRect()
        {
            return Rect.MinMaxRect(
                Mathf.Min(mouseDownPosition.x, boxEnd.x), Mathf.Min(mouseDownPosition.y, boxEnd.y),
                Mathf.Max(mouseDownPosition.x, boxEnd.x), Mathf.Max(mouseDownPosition.y, boxEnd.y));
        }

        static void ZoomAt(TimelineLayout layout, TimelineViewState view, float mainPosition, float factor, float duration)
        {
            double anchorTime = layout.MainToTime(mainPosition);
            view.pixelsPerSecond = Mathf.Clamp(
                view.pixelsPerSecond * factor, TimelineViewState.MinPixelsPerSecond, TimelineViewState.MaxPixelsPerSecond);
            var zoomed = new TimelineLayout(layout.Orientation, layout.Area, view.pixelsPerSecond, 0f, layout.LaneCount);
            view.scrollSeconds = TimelineLayout.ClampScroll(
                (float)(anchorTime - mainPosition / view.pixelsPerSecond), duration, zoomed.VisibleSeconds);
        }

        void DrawScrollbar(Rect rect, TimelineLayout layout, TimelineViewState view, float duration)
        {
            float visible = (float)layout.VisibleSeconds;
            float max = Mathf.Max(duration, visible);
            float value = layout.IsHorizontal
                ? GUI.HorizontalScrollbar(rect, view.scrollSeconds, visible, 0f, max)
                : GUI.VerticalScrollbar(rect, view.scrollSeconds, visible, 0f, max);
            if (!Mathf.Approximately(value, view.scrollSeconds))
                view.scrollSeconds = TimelineLayout.ClampScroll(value, duration, visible);
        }

        // ---------------------------------------------------------------- 그리기

        void Draw(
            TimelineLayout layout, TimelineViewState view, ChartEditModel model,
            WaveformData waveform, string waveformMessage, double playhead)
        {
            ChartData chart = model.Chart;
            Fill(layout.Area, BackgroundColor);
            Fill(layout.RulerBand, RulerColor);
            Fill(layout.WaveformBand, WaveformBackground);
            for (int lane = 0; lane < layout.LaneCount; lane++)
                Fill(layout.LaneBand(lane), lane % 2 == 0 ? LaneColorA : LaneColorB);

            DrawWaveform(layout, waveform, waveformMessage);
            DrawGrid(layout, view, chart);
            DrawRuler(layout);
            DrawLaneLabels(layout);
            DrawOutsideShade(layout, chart.Duration);
            DrawNotes(layout, model);
            DrawDragPreview(layout, view, model);
            DrawPlayhead(layout, playhead);
        }

        void DrawWaveform(TimelineLayout layout, WaveformData waveform, string message)
        {
            Rect band = layout.WaveformBand;
            if (waveform == null)
            {
                GUI.Label(band, string.IsNullOrEmpty(message) ? "파형 없음" : message, Label(TextAnchor.MiddleCenter));
                return;
            }

            int columns = Mathf.CeilToInt(layout.MainExtent);
            if (columnMin.Length < columns)
            {
                columnMin = new float[columns];
                columnMax = new float[columns];
            }
            waveform.GetColumns(layout.ScrollSeconds, 1.0 / layout.PixelsPerSecond, columns, columnMin, columnMax);

            float center = layout.RulerThickness + TimelineLayout.WaveformThickness * 0.5f;
            float half = TimelineLayout.WaveformThickness * 0.5f - 2f;
            for (int i = 0; i < columns; i++)
            {
                if (columnMax[i] <= columnMin[i] && columnMax[i] == 0f)
                    continue;
                float top = center - Mathf.Clamp01(columnMax[i]) * half;
                float bottom = center - Mathf.Clamp(columnMin[i], -1f, 0f) * half;
                Fill(layout.Band(i, i + 1, top, Mathf.Max(bottom, top + 1f)), WaveformColor);
            }
        }

        void DrawGrid(TimelineLayout layout, TimelineViewState view, ChartData chart)
        {
            if (!GridMath.HasGrid(chart.Bpm))
                return;

            // 촘촘해서 구분이 안 되면 박 단위로, 그것도 촘촘하면 마디 선만 그린다.
            int divisions = view.snapDivisions;
            if (GridMath.StepSeconds(chart.Bpm, divisions) * layout.PixelsPerSecond < MinColumnPixels)
                divisions = 1;
            bool barsOnly = GridMath.StepSeconds(chart.Bpm, 1) * layout.PixelsPerSecond < MinColumnPixels;

            GridMath.FillLines(gridLines, chart.Bpm, chart.Offset, divisions, layout.ScrollSeconds, layout.VisibleEnd);
            if (gridLines.Count > MaxGridLines && !barsOnly)
                barsOnly = true;

            foreach (GridLine line in gridLines)
            {
                if (barsOnly && line.kind != GridLineKind.Bar)
                    continue;
                float main = layout.TimeToMain(line.time);
                Color color = line.kind == GridLineKind.Bar ? BarLineColor
                    : line.kind == GridLineKind.Beat ? BeatLineColor : SubdivisionLineColor;
                Fill(layout.Band(main, main + 1f, layout.RulerThickness, layout.LanesEnd), color);
            }
        }

        void DrawRuler(TimelineLayout layout)
        {
            float step = RulerSteps[RulerSteps.Length - 1];
            foreach (float candidate in RulerSteps)
            {
                if (candidate * layout.PixelsPerSecond >= 70f)
                {
                    step = candidate;
                    break;
                }
            }

            double first = Math.Ceiling(layout.ScrollSeconds / step) * step;
            for (double t = first; t <= layout.VisibleEnd; t += step)
            {
                float main = layout.TimeToMain(t);
                Fill(layout.Band(main, main + 1f, layout.RulerThickness - 8f, layout.RulerThickness), TextColor);

                Rect label = layout.IsHorizontal
                    ? new Rect(layout.Area.x + main + 3f, layout.Area.y, 60f, layout.RulerThickness - 4f)
                    : new Rect(layout.Area.x + 2f, layout.Area.y + main + 1f, layout.RulerThickness - 6f, 14f);
                GUI.Label(label, FormatTime(t), Label(TextAnchor.UpperLeft));
            }
        }

        void DrawLaneLabels(TimelineLayout layout)
        {
            for (int lane = 0; lane < layout.LaneCount; lane++)
            {
                Rect band = layout.LaneBand(lane);
                GUI.Label(new Rect(band.x + 3f, band.y + 1f, 22f, 16f), (lane + 1).ToString(), Label(TextAnchor.UpperLeft));
            }
        }

        void DrawOutsideShade(TimelineLayout layout, float duration)
        {
            float start = layout.TimeToMain(0.0);
            if (start > 0f)
                Fill(layout.Band(0f, start, layout.RulerThickness, layout.LanesEnd), OutsideColor);
            float end = layout.TimeToMain(duration);
            if (end < layout.MainExtent)
                Fill(layout.Band(Mathf.Max(0f, end), layout.MainExtent, layout.RulerThickness, layout.LanesEnd), OutsideColor);
        }

        void DrawNotes(TimelineLayout layout, ChartEditModel model)
        {
            double visibleStart = layout.ScrollSeconds, visibleEnd = layout.VisibleEnd;
            foreach (NoteEntry entry in model.Entries)
            {
                NoteData note = entry.note;
                if (note.time > visibleEnd || note.time + note.holdLength < visibleStart)
                    continue;

                Rect rect = layout.NoteRect(note.time, note.lane, note.holdLength);
                Color color = LaneColors[note.lane % LaneColors.Length];
                if (note.type == NoteType.Hold)
                {
                    Fill(rect, new Color(color.r, color.g, color.b, 0.55f));
                    Fill(layout.HoldHeadRect(note.time, note.lane), color);
                }
                else
                {
                    Fill(rect, color);
                }

                if (entry.selected)
                    Outline(rect, SelectionColor, 2f);
                else
                    Outline(rect, new Color(0f, 0f, 0f, 0.6f), 1f);
            }
        }

        void DrawDragPreview(TimelineLayout layout, TimelineViewState view, ChartEditModel model)
        {
            if (drag == Drag.Create)
            {
                float start = (float)Math.Min(createStart, createEnd);
                float length = (float)Math.Abs(createEnd - createStart);
                Color color = LaneColors[createLane % LaneColors.Length];
                Rect rect = layout.NoteRect(start, createLane, length >= ChartEditModel.MinHoldLength ? length : 0f);
                Fill(rect, new Color(color.r, color.g, color.b, 0.5f));
                Outline(rect, color, 1f);
            }
            else if (drag == Drag.Box)
            {
                Rect box = BoxRect();
                Fill(box, BoxFill);
                Outline(box, BoxLine, 1f);
            }
        }

        void DrawPlayhead(TimelineLayout layout, double playhead)
        {
            float main = layout.TimeToMain(playhead);
            if (main < -2f || main > layout.MainExtent + 2f)
                return;
            Fill(layout.Band(main - 1f, main + 1f, 0f, layout.LanesEnd), PlayheadColor);
        }

        // ---------------------------------------------------------------- 도우미

        GUIStyle Label(TextAnchor anchor)
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, clipping = TextClipping.Overflow };
                labelStyle.normal.textColor = TextColor;
            }
            labelStyle.alignment = anchor;
            return labelStyle;
        }

        static void Fill(Rect rect, Color color)
        {
            if (rect.width > 0f && rect.height > 0f)
                EditorGUI.DrawRect(rect, color);
        }

        static void Outline(Rect rect, Color color, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        /// <summary>시각을 눈금자용 글자로. 60초 이상이면 분:초.</summary>
        internal static string FormatTime(double seconds)
        {
            int minutes = (int)(seconds / 60.0);
            double rest = seconds - minutes * 60.0;
            return minutes > 0 ? $"{minutes}:{rest:00.0}" : $"{rest:0.0}s";
        }
    }
}
