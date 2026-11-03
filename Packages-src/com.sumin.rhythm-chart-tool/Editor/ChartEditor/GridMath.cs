using System;
using System.Collections.Generic;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>박자 그리드 선의 굵기 등급.</summary>
    internal enum GridLineKind
    {
        /// <summary>스냅 단위 선(박 사이).</summary>
        Subdivision,
        /// <summary>박(beat) 선.</summary>
        Beat,
        /// <summary>마디 선. 박자표 정보가 없어 4박마다로 가정한다.</summary>
        Bar,
    }

    internal struct GridLine
    {
        public double time;
        public GridLineKind kind;
    }

    /// <summary>BPM/offset 으로 정해지는 박자 그리드 계산. UI 와 무관한 순수 함수 모음.</summary>
    internal static class GridMath
    {
        public const int BeatsPerBar = 4;

        public static bool HasGrid(float bpm)
        {
            return bpm > 0f && !float.IsInfinity(bpm);
        }

        /// <summary>한 칸의 길이(초). 그리드가 없으면 0.</summary>
        public static double StepSeconds(float bpm, int divisions)
        {
            return HasGrid(bpm) ? 60.0 / bpm / Math.Max(1, divisions) : 0.0;
        }

        /// <summary>가장 가까운 그리드 칸으로 맞춘다. 그리드가 없으면 시각을 그대로 돌려준다.</summary>
        public static double Snap(double time, float bpm, float offset, int divisions)
        {
            double step = StepSeconds(bpm, divisions);
            return step > 0.0 ? offset + Math.Round((time - offset) / step) * step : time;
        }

        /// <summary>[from, to] 구간에 보이는 그리드 선을 <paramref name="buffer"/> 에 채운다(기존 내용은 지운다).</summary>
        public static void FillLines(List<GridLine> buffer, float bpm, float offset, int divisions, double from, double to)
        {
            buffer.Clear();
            double step = StepSeconds(bpm, divisions);
            if (step <= 0.0 || to < from)
                return;

            int perBeat = Math.Max(1, divisions);
            long first = (long)Math.Floor((from - offset) / step);
            long last = (long)Math.Ceiling((to - offset) / step);
            for (long slot = first; slot <= last; slot++)
            {
                GridLineKind kind = GridLineKind.Subdivision;
                if (Mod(slot, perBeat) == 0)
                    kind = Mod(slot / perBeat, BeatsPerBar) == 0 ? GridLineKind.Bar : GridLineKind.Beat;
                buffer.Add(new GridLine { time = offset + slot * step, kind = kind });
            }
        }

        static long Mod(long value, long modulus)
        {
            long remainder = value % modulus;
            return remainder < 0 ? remainder + modulus : remainder;
        }
    }
}
