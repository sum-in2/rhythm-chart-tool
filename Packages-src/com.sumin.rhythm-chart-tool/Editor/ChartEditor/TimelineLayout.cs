using System;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>타임라인이 흐르는 방향.</summary>
    internal enum TimelineOrientation
    {
        /// <summary>시간이 왼쪽에서 오른쪽으로, 레인이 위에서 아래로 쌓인다.</summary>
        Horizontal = 0,

        /// <summary>시간이 위에서 아래로, 레인이 왼쪽에서 오른쪽으로 놓인다.</summary>
        Vertical = 1,
    }

    /// <summary>
    /// 타임라인의 화면 배치. (시간, 레인) ↔ 화면 좌표 변환을 이곳 한 군데에 모아서
    /// 가로/세로 방향을 같은 그리기·입력 코드로 처리하게 한다.
    /// 시간 축을 "주축(main)", 그에 수직인 방향(눈금자 → 파형 → 레인 0 → 1 → …)을 "교차축(cross)"이라 부른다.
    /// </summary>
    internal readonly struct TimelineLayout
    {
        const float HorizontalRulerThickness = 22f;
        const float VerticalRulerThickness = 48f;   // 세로에서는 시간 글자를 눈금자 안에 가로로 써야 해서 더 넓다
        public const float WaveformThickness = 72f;
        public const float MinLaneThickness = 24f;
        public const float MaxLaneThickness = 96f;
        public const float TapSize = 12f;

        public TimelineLayout(
            TimelineOrientation orientation, Rect area, float pixelsPerSecond, float scrollSeconds, int laneCount)
        {
            Orientation = orientation;
            Area = area;
            PixelsPerSecond = pixelsPerSecond;
            ScrollSeconds = scrollSeconds;
            LaneCount = Mathf.Max(1, laneCount);
        }

        public TimelineOrientation Orientation { get; }

        /// <summary>타임라인이 그려질 화면 영역(클립 좌표계).</summary>
        public Rect Area { get; }

        public float PixelsPerSecond { get; }

        /// <summary>주축 시작 지점에 해당하는 시각(초).</summary>
        public float ScrollSeconds { get; }

        public int LaneCount { get; }

        public bool IsHorizontal => Orientation == TimelineOrientation.Horizontal;

        public float MainExtent => IsHorizontal ? Area.width : Area.height;

        public float CrossExtent => IsHorizontal ? Area.height : Area.width;

        public double VisibleSeconds => MainExtent / PixelsPerSecond;

        public double VisibleEnd => ScrollSeconds + VisibleSeconds;

        public float RulerThickness => IsHorizontal ? HorizontalRulerThickness : VerticalRulerThickness;

        /// <summary>레인 한 줄의 두께. 남는 공간에 맞추되 너무 얇거나 두꺼워지지 않게 제한한다.</summary>
        public float LaneThickness =>
            Mathf.Clamp((CrossExtent - RulerThickness - WaveformThickness) / LaneCount, MinLaneThickness, MaxLaneThickness);

        public float LanesStart => RulerThickness + WaveformThickness;

        public float LanesEnd => LanesStart + LaneThickness * LaneCount;

        /// <summary>시각 → 주축 위치(영역 시작 기준 픽셀).</summary>
        public float TimeToMain(double time)
        {
            return (float)((time - ScrollSeconds) * PixelsPerSecond);
        }

        /// <summary>주축 위치 → 시각.</summary>
        public double MainToTime(float main)
        {
            return ScrollSeconds + main / PixelsPerSecond;
        }

        /// <summary>주축/교차축 범위로 화면 사각형을 만든다.</summary>
        public Rect Band(float mainMin, float mainMax, float crossMin, float crossMax)
        {
            return IsHorizontal
                ? Rect.MinMaxRect(Area.x + mainMin, Area.y + crossMin, Area.x + mainMax, Area.y + crossMax)
                : Rect.MinMaxRect(Area.x + crossMin, Area.y + mainMin, Area.x + crossMax, Area.y + mainMax);
        }

        public Rect RulerBand => Band(0f, MainExtent, 0f, RulerThickness);

        public Rect WaveformBand => Band(0f, MainExtent, RulerThickness, LanesStart);

        public Rect LanesBand => Band(0f, MainExtent, LanesStart, LanesEnd);

        public Rect LaneBand(int lane)
        {
            float start = LanesStart + LaneThickness * lane;
            return Band(0f, MainExtent, start, start + LaneThickness);
        }

        /// <summary>노트의 화면 사각형. 탭은 고정 크기, 롱노트는 길이만큼 늘어난다.</summary>
        public Rect NoteRect(double time, int lane, float holdLength)
        {
            const float Inset = 4f;
            // 탭은 시각이 노트의 가운데, 롱노트는 시각이 노트의 시작이다.
            float start = TimeToMain(time) - (holdLength > 0f ? 0f : TapSize * 0.5f);
            float length = Mathf.Max(TapSize, holdLength * PixelsPerSecond);
            float laneStart = LanesStart + LaneThickness * lane;
            return Band(start, start + length, laneStart + Inset, laneStart + LaneThickness - Inset);
        }

        public float MainOf(Vector2 position)
        {
            return IsHorizontal ? position.x - Area.x : position.y - Area.y;
        }

        public float CrossOf(Vector2 position)
        {
            return IsHorizontal ? position.y - Area.y : position.x - Area.x;
        }

        /// <summary>그 위치의 레인 번호. 레인 영역 밖이면 -1.</summary>
        public int LaneAt(Vector2 position)
        {
            float cross = CrossOf(position);
            if (cross < LanesStart || cross >= LanesEnd)
                return -1;
            return Mathf.Clamp((int)((cross - LanesStart) / LaneThickness), 0, LaneCount - 1);
        }

        /// <summary>레인 영역 밖이어도 가장 가까운 레인 번호를 돌려준다(드래그 중 이동량 계산용).</summary>
        public int LaneAtClamped(Vector2 position)
        {
            float cross = CrossOf(position);
            return Mathf.Clamp(Mathf.FloorToInt((cross - LanesStart) / LaneThickness), 0, LaneCount - 1);
        }

        public bool InRuler(Vector2 position) => CrossOf(position) >= 0f && CrossOf(position) < RulerThickness;

        public bool InWaveform(Vector2 position) =>
            CrossOf(position) >= RulerThickness && CrossOf(position) < LanesStart;

        /// <summary>스크롤 위치를 곡 길이 안으로 제한한다.</summary>
        public static float ClampScroll(float scroll, float duration, double visibleSeconds)
        {
            float max = Mathf.Max(0f, duration - (float)(visibleSeconds * 0.9));
            return Mathf.Clamp(scroll, 0f, max);
        }
    }
}
