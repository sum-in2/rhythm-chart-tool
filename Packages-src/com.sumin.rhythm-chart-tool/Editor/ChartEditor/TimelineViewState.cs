using System;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>편집 도구.</summary>
    internal enum EditTool
    {
        /// <summary>선택/이동(Q).</summary>
        Select = 0,

        /// <summary>노트 그리기(W): 클릭=탭, 드래그=롱노트.</summary>
        Draw = 1,

        /// <summary>지우기(E).</summary>
        Erase = 2,
    }

    /// <summary>창이 기억하는 보기 설정. 창 레이아웃과 함께 저장되어 도메인 리로드 후에도 유지된다.</summary>
    [Serializable]
    internal sealed class TimelineViewState
    {
        public const float MinPixelsPerSecond = 10f;
        public const float MaxPixelsPerSecond = 2000f;
        public const float DefaultPixelsPerSecond = 120f;

        public TimelineOrientation orientation = TimelineOrientation.Horizontal;
        public float pixelsPerSecond = DefaultPixelsPerSecond;
        public float scrollSeconds;
        public EditTool tool = EditTool.Select;
        public bool snapEnabled = true;
        public int snapDivisions = 4;
        public bool followPlayhead = true;
    }
}
