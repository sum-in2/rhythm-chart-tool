using System.Collections.Generic;
using NUnit.Framework;
using SuminTools.RhythmChart.Editor;
using UnityEngine;

namespace SuminTools.RhythmChart.Tests
{
    public class TimelineLayoutAndGridTests
    {
        const float Tolerance = 1e-3f;

        static TimelineLayout Layout(TimelineOrientation orientation, float pps = 100f, float scroll = 0f, int lanes = 4)
        {
            return new TimelineLayout(orientation, new Rect(0f, 0f, 600f, 500f), pps, scroll, lanes);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void TimeAndMainPosition_AreInverse(int orientationValue)
        {
            var orientation = (TimelineOrientation)orientationValue;
            TimelineLayout layout = Layout(orientation, 80f, 12.5f);
            foreach (double time in new[] { 12.5, 13.0, 20.25, 100.0 })
                Assert.AreEqual(time, layout.MainToTime(layout.TimeToMain(time)), Tolerance);
            Assert.AreEqual(0f, layout.TimeToMain(12.5), Tolerance, "스크롤 위치가 주축의 시작이다");
        }

        [Test]
        public void Horizontal_LaneIsDecidedByY_AndTimeByX()
        {
            TimelineLayout layout = Layout(TimelineOrientation.Horizontal);
            float laneThickness = layout.LaneThickness;

            for (int lane = 0; lane < 4; lane++)
            {
                var inside = new Vector2(250f, layout.LanesStart + laneThickness * (lane + 0.5f));
                Assert.AreEqual(lane, layout.LaneAt(inside), $"lane {lane}");
            }
            Assert.AreEqual(-1, layout.LaneAt(new Vector2(250f, 5f)), "눈금자 위");
            Assert.AreEqual(-1, layout.LaneAt(new Vector2(250f, layout.RulerThickness + 10f)), "파형 위");
            Assert.AreEqual(-1, layout.LaneAt(new Vector2(250f, layout.LanesEnd + 1f)), "레인 아래");
        }

        [Test]
        public void Vertical_LaneIsDecidedByX_AndTimeByY()
        {
            TimelineLayout layout = Layout(TimelineOrientation.Vertical);
            float laneThickness = layout.LaneThickness;

            for (int lane = 0; lane < 4; lane++)
            {
                var inside = new Vector2(layout.LanesStart + laneThickness * (lane + 0.5f), 250f);
                Assert.AreEqual(lane, layout.LaneAt(inside), $"lane {lane}");
            }
            Assert.AreEqual(-1, layout.LaneAt(new Vector2(5f, 250f)), "눈금자 위");
            Assert.AreEqual(250f, layout.MainOf(new Vector2(10f, 250f)), Tolerance);
            Assert.AreEqual(10f, layout.CrossOf(new Vector2(10f, 250f)), Tolerance);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void LaneAtClamped_StaysInsideTheLanes(int orientationValue)
        {
            var orientation = (TimelineOrientation)orientationValue;
            TimelineLayout layout = Layout(orientation, lanes: 5);
            Vector2 before = orientation == TimelineOrientation.Horizontal ? new Vector2(100f, -50f) : new Vector2(-50f, 100f);
            Vector2 after = orientation == TimelineOrientation.Horizontal ? new Vector2(100f, 9999f) : new Vector2(9999f, 100f);
            Assert.AreEqual(0, layout.LaneAtClamped(before));
            Assert.AreEqual(4, layout.LaneAtClamped(after));
        }

        [Test]
        public void Horizontal_NoteRect_TapIsCenteredAndHoldStartsAtItsTime()
        {
            TimelineLayout layout = Layout(TimelineOrientation.Horizontal, 100f, 0f);

            Rect tap = layout.NoteRect(2.0, 1, 0f);
            Assert.AreEqual(TimelineLayout.TapSize, tap.width, Tolerance);
            Assert.AreEqual(200f, tap.center.x, Tolerance);

            Rect hold = layout.NoteRect(2.0, 1, 0.5f);
            Assert.AreEqual(200f, hold.xMin, Tolerance);
            Assert.AreEqual(50f, hold.width, Tolerance);
            Assert.AreEqual(layout.LaneBand(1).center.y, hold.center.y, Tolerance);
        }

        [Test]
        public void Vertical_NoteRect_UsesTheSameMathOnTheOtherAxis()
        {
            TimelineLayout layout = Layout(TimelineOrientation.Vertical, 100f, 0f);

            Rect tap = layout.NoteRect(2.0, 1, 0f);
            Assert.AreEqual(TimelineLayout.TapSize, tap.height, Tolerance);
            Assert.AreEqual(200f, tap.center.y, Tolerance);

            Rect hold = layout.NoteRect(2.0, 1, 0.5f);
            Assert.AreEqual(200f, hold.yMin, Tolerance);
            Assert.AreEqual(50f, hold.height, Tolerance);
            Assert.AreEqual(layout.LaneBand(1).center.x, hold.center.x, Tolerance);
        }

        [Test]
        public void HoldHead_StartsAtTheHoldsStart_InBothOrientations()
        {
            TimelineLayout horizontal = Layout(TimelineOrientation.Horizontal, 100f, 0f);
            Rect hold = horizontal.NoteRect(2.0, 1, 0.5f);
            Rect head = horizontal.HoldHeadRect(2.0, 1);
            Assert.AreEqual(hold.xMin, head.xMin, Tolerance, "머리가 몸통보다 앞서 나가지 않는다");
            Assert.AreEqual(TimelineLayout.TapSize, head.width, Tolerance);
            Assert.AreEqual(hold.yMin, head.yMin, Tolerance);
            Assert.AreEqual(hold.yMax, head.yMax, Tolerance);

            TimelineLayout vertical = Layout(TimelineOrientation.Vertical, 100f, 0f);
            hold = vertical.NoteRect(2.0, 1, 0.5f);
            head = vertical.HoldHeadRect(2.0, 1);
            Assert.AreEqual(hold.yMin, head.yMin, Tolerance);
            Assert.AreEqual(TimelineLayout.TapSize, head.height, Tolerance);
            Assert.AreEqual(hold.xMin, head.xMin, Tolerance);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void LaneBands_AreAdjacentAndOrdered(int orientationValue)
        {
            var orientation = (TimelineOrientation)orientationValue;
            TimelineLayout layout = Layout(orientation);
            for (int lane = 1; lane < 4; lane++)
            {
                Rect a = layout.LaneBand(lane - 1), b = layout.LaneBand(lane);
                float gap = orientation == TimelineOrientation.Horizontal ? b.yMin - a.yMax : b.xMin - a.xMax;
                Assert.AreEqual(0f, gap, Tolerance);
            }
        }

        [Test]
        public void LaneThickness_IsLimitedToItsRange()
        {
            var tiny = new TimelineLayout(TimelineOrientation.Horizontal, new Rect(0, 0, 600, 120), 100f, 0f, 6);
            var huge = new TimelineLayout(TimelineOrientation.Horizontal, new Rect(0, 0, 600, 5000), 100f, 0f, 4);
            Assert.AreEqual(TimelineLayout.MinLaneThickness, tiny.LaneThickness);
            Assert.AreEqual(TimelineLayout.MaxLaneThickness, huge.LaneThickness);
        }

        [Test]
        public void ClampScroll_KeepsTheViewInsideTheSong()
        {
            Assert.AreEqual(0f, TimelineLayout.ClampScroll(-5f, 100f, 10.0));
            Assert.AreEqual(100f - 9f, TimelineLayout.ClampScroll(500f, 100f, 10.0), Tolerance);
            Assert.AreEqual(0f, TimelineLayout.ClampScroll(3f, 5f, 10.0), "곡보다 화면이 크면 0");
        }

        // ---------------------------------------------------------------- GridMath

        [Test]
        public void Snap_MovesToTheNearestGridLine()
        {
            // 120BPM: 한 박 0.5초, 16분음표(4분할) 0.125초, offset 0.1
            Assert.AreEqual(0.1, GridMath.Snap(0.12, 120f, 0.1f, 4), Tolerance);
            Assert.AreEqual(0.225, GridMath.Snap(0.2, 120f, 0.1f, 4), Tolerance);
            Assert.AreEqual(0.6, GridMath.Snap(0.55, 120f, 0.1f, 1), Tolerance);
            Assert.AreEqual(0.1, GridMath.Snap(0.0, 120f, 0.1f, 2), Tolerance);
        }

        [Test]
        public void Snap_WithoutBpm_ReturnsTheTimeUnchanged()
        {
            Assert.AreEqual(1.2345, GridMath.Snap(1.2345, 0f, 0.1f, 4));
            Assert.IsFalse(GridMath.HasGrid(0f));
            Assert.AreEqual(0.0, GridMath.StepSeconds(0f, 4));
        }

        [Test]
        public void FillLines_MarksBeatsAndBars()
        {
            var lines = new List<GridLine>();
            GridMath.FillLines(lines, 120f, 0f, 2, 0.0, 4.0);   // 8분음표(0.25초) 간격, 한 박 0.5초

            Assert.AreEqual(17, lines.Count);   // 0, 0.25, ..., 4.0
            Assert.AreEqual(GridLineKind.Bar, lines[0].kind);            // 0.0
            Assert.AreEqual(GridLineKind.Subdivision, lines[1].kind);    // 0.25
            Assert.AreEqual(GridLineKind.Beat, lines[2].kind);           // 0.5
            Assert.AreEqual(GridLineKind.Bar, lines[8].kind);            // 2.0 = 4박
            Assert.AreEqual(GridLineKind.Bar, lines[16].kind);           // 4.0
        }

        [Test]
        public void FillLines_HandlesRangesBeforeTheOffset()
        {
            var lines = new List<GridLine>();
            GridMath.FillLines(lines, 120f, 1.0f, 1, -1.0, 1.0);   // offset 1.0 앞쪽(음수 칸)도 그린다

            Assert.IsTrue(lines.Count >= 4);
            foreach (GridLine line in lines)
            {
                double beats = (line.time - 1.0) / 0.5;
                Assert.AreEqual(System.Math.Round(beats), beats, Tolerance);
                bool isBar = ((long)System.Math.Round(beats) % 4 + 4) % 4 == 0;
                Assert.AreEqual(isBar ? GridLineKind.Bar : GridLineKind.Beat, line.kind, $"t={line.time}");
            }
        }

        [Test]
        public void FillLines_WithoutBpm_IsEmpty()
        {
            var lines = new List<GridLine> { new GridLine() };
            GridMath.FillLines(lines, 0f, 0f, 4, 0.0, 10.0);
            Assert.AreEqual(0, lines.Count);
        }

        [Test]
        public void FormatTime_ShowsMinutesOnlyWhenNeeded()
        {
            Assert.AreEqual("5.0s", TimelineCanvas.FormatTime(5.0));
            Assert.AreEqual("1:05.5", TimelineCanvas.FormatTime(65.5));
        }
    }
}
