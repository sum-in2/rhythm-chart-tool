namespace SuminTools.RhythmChart
{
    /// <summary>여러 노트에 걸쳐 레인을 정해 주는 패턴 템플릿.</summary>
    internal abstract class LanePattern
    {
        /// <summary>아직 배치하지 않은 노트 수.</summary>
        public int Remaining { get; protected set; }

        /// <summary>다음 노트가 놓일 레인.</summary>
        public abstract int Current { get; }

        /// <summary>그다음 노트가 놓일 레인. 마지막 노트면 -1.</summary>
        public abstract int Upcoming { get; }

        /// <summary>한 노트를 배치한 것으로 보고 다음으로 넘어간다.</summary>
        public abstract void Advance();
    }

    /// <summary>계단: 한 칸씩 같은 방향으로 이동하다 끝에서 되돌아온다.</summary>
    internal sealed class StairsPattern : LanePattern
    {
        readonly int laneCount;
        int lane;
        int direction;

        public StairsPattern(int startLane, int direction, int laneCount, int length)
        {
            this.laneCount = laneCount;
            lane = startLane;
            this.direction = direction;
            Remaining = length;
        }

        public override int Current => lane;

        public override int Upcoming => Remaining > 1 ? Step(lane, direction).lane : -1;

        public override void Advance()
        {
            (lane, direction) = Step(lane, direction);
            Remaining--;
        }

        (int lane, int direction) Step(int from, int dir)
        {
            int next = from + dir;
            if (next < 0 || next >= laneCount)
            {
                dir = -dir;
                next = from + dir;
            }
            return (next, dir);
        }
    }

    /// <summary>트릴: 두 레인을 번갈아 친다.</summary>
    internal sealed class TrillPattern : LanePattern
    {
        readonly int first;
        readonly int second;
        bool onFirst = true;

        public TrillPattern(int first, int second, int length)
        {
            this.first = first;
            this.second = second;
            Remaining = length;
        }

        public override int Current => onFirst ? first : second;

        public override int Upcoming => Remaining > 1 ? (onFirst ? second : first) : -1;

        public override void Advance()
        {
            onFirst = !onFirst;
            Remaining--;
        }
    }
}
