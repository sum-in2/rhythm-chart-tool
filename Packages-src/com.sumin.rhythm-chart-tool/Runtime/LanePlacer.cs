using System;
using System.Collections.Generic;

namespace SuminTools.RhythmChart
{
    /// <summary>
    /// 노트를 시간 순서대로 받아 레인을 정한다. 대역(저/중/고)에 따라 레인 위치를 기울이고,
    /// 같은 레인 연속 금지와 인접 레인 점프 제한을 지키며, 설정에 따라 계단/트릴 패턴을 섞는다.
    /// 같은 시드와 같은 호출 순서에서는 항상 같은 결과를 낸다.
    /// </summary>
    internal sealed class LanePlacer
    {
        const double BandPositionLow = 0.0;
        const double BandPositionMid = 0.5;
        const double BandPositionHigh = 1.0;
        const int PartnerMinDistance = 2;
        const int PartnerPreferredMaxDistance = 3;
        const int TrillMaxDistance = 3;
        const double PreferredPartnerWeight = 2.0;
        const double OtherPartnerWeight = 1.0;

        readonly int laneCount;
        readonly DifficultySettings settings;
        readonly DeterministicRandom rng;
        readonly bool[] previousGroup;   // 직전 노트(동시치기면 두 레인)가 쓴 레인
        readonly bool[] priorGroup;      // 그 앞 노트가 쓴 레인. 동시치기 짝 레인이 직전 노트의 레인을 반복하지 않게 하는 데 쓴다.
        int lastPrimary = -1;
        LanePattern pattern;

        public LanePlacer(int laneCount, DifficultySettings settings, DeterministicRandom rng)
        {
            this.laneCount = laneCount;
            this.settings = settings;
            this.rng = rng;
            previousGroup = new bool[laneCount];
            priorGroup = new bool[laneCount];
        }

        /// <summary>
        /// 다음 노트의 레인을 정한다.
        /// 아직 배치하지 않은 노트 수(<paramref name="notesRemaining"/>)는 패턴 길이를 정하는 데 쓴다.
        /// </summary>
        public int NextLane(OnsetBand band, int notesRemaining)
        {
            MaybeStartPattern(notesRemaining);

            if (pattern != null && !IsAllowed(pattern.Current))
                pattern = null;

            int lane;
            if (pattern != null)
            {
                lane = pattern.Current;
                pattern.Advance();
                if (pattern.Remaining <= 0)
                    pattern = null;
            }
            else
            {
                lane = PickByBand(band);
            }

            Array.Copy(previousGroup, priorGroup, previousGroup.Length);
            Array.Clear(previousGroup, 0, previousGroup.Length);
            previousGroup[lane] = true;
            lastPrimary = lane;
            return lane;
        }

        /// <summary>
        /// 동시치기의 짝이 될 레인을 정한다. 이어질 패턴 레인과 겹치지 않고, 가능하면 두 칸 이상 떨어진 레인을 쓴다
        /// (바로 옆 레인이면 다음 노트가 갈 곳이 막힐 수 있다).
        /// </summary>
        public int PartnerLane(int primary)
        {
            int avoid = pattern != null ? pattern.Current : -1;
            // 가능한 한 엄격한 조건부터 차례로 완화한다. 직전 노트의 레인을 반복하지 않는 것이 가장 우선이라
            // 마지막 단계 전까지는 그 조건을 유지하고, 먼저 '이어질 패턴 레인 피하기'와 '두 칸 이상 떨어지기'를 포기한다.
            int lane = PickPartner(primary, avoid, PartnerMinDistance, true);
            if (lane < 0)
                lane = PickPartner(primary, avoid, 1, true);
            if (lane < 0)
                lane = PickPartner(primary, -1, 1, true);
            if (lane < 0)
                lane = PickPartner(primary, -1, 1, false);
            previousGroup[lane] = true;
            return lane;
        }

        void MaybeStartPattern(int notesRemaining)
        {
            if (pattern != null || settings.patternChance <= 0f || notesRemaining < settings.patternMinLength)
                return;
            if (rng.NextDouble() >= settings.patternChance)
                return;

            double stairs = settings.stairsWeight, trill = settings.trillWeight;
            if (stairs + trill <= 0.0)
                return;

            bool useStairs = rng.NextDouble() * (stairs + trill) < stairs;
            int maxLength = Math.Max(settings.patternMinLength, settings.patternMaxLength);
            int length = Math.Min(notesRemaining, rng.NextRange(settings.patternMinLength, maxLength));

            var starts = new List<int>();
            for (int lane = 0; lane < laneCount; lane++)
                if (IsAllowed(lane))
                    starts.Add(lane);
            if (starts.Count == 0)
                return;
            int start = starts[rng.NextInt(starts.Count)];

            if (useStairs)
            {
                int direction = rng.NextInt(2) == 0 ? 1 : -1;
                if (start == 0) direction = 1;
                else if (start == laneCount - 1) direction = -1;
                pattern = new StairsPattern(start, direction, laneCount, length);
                return;
            }

            var seconds = new List<int>();
            int reach = Math.Min(settings.maxLaneJump, TrillMaxDistance);
            for (int lane = 0; lane < laneCount; lane++)
                if (lane != start && Math.Abs(lane - start) <= reach)
                    seconds.Add(lane);
            if (seconds.Count == 0)
                return;
            pattern = new TrillPattern(start, seconds[rng.NextInt(seconds.Count)], length);
        }

        // 직전 노트가 쓴 레인이 아니고, 직전 주 레인에서 점프 제한 안에 있는가
        bool IsAllowed(int lane)
        {
            if (lane < 0 || lane >= laneCount || previousGroup[lane])
                return false;
            return lastPrimary < 0 || Math.Abs(lane - lastPrimary) <= settings.maxLaneJump;
        }

        int PickByBand(OnsetBand band)
        {
            double preferred = (laneCount - 1) * (band == OnsetBand.Low ? BandPositionLow
                : band == OnsetBand.High ? BandPositionHigh : BandPositionMid);

            var candidates = new List<int>();
            for (int lane = 0; lane < laneCount; lane++)
                if (IsAllowed(lane))
                    candidates.Add(lane);

            if (candidates.Count == 0)
                return NearestFreeLane();

            double total = 0.0;
            var weights = new double[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                double distance = Math.Abs(candidates[i] - preferred);
                weights[i] = 1.0 / (1.0 + distance * distance);
                total += weights[i];
            }

            double roll = rng.NextDouble() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0.0)
                    return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        // 점프 제한 안에 쓸 수 있는 레인이 없을 때(동시치기 직후 등): 직전 레인에 가장 가까운 빈 레인으로 완화한다.
        int NearestFreeLane()
        {
            int best = -1, bestDistance = int.MaxValue;
            for (int lane = 0; lane < laneCount; lane++)
            {
                if (previousGroup[lane])
                    continue;
                int distance = lastPrimary < 0 ? 0 : Math.Abs(lane - lastPrimary);
                if (distance < bestDistance)
                {
                    best = lane;
                    bestDistance = distance;
                }
            }
            return best >= 0 ? best : (lastPrimary + 1) % laneCount;
        }

        int PickPartner(int primary, int avoid, int minDistance, bool respectPrior)
        {
            var candidates = new List<int>();
            var weights = new List<double>();
            double total = 0.0;
            for (int lane = 0; lane < laneCount; lane++)
            {
                int distance = Math.Abs(lane - primary);
                if (lane == primary || lane == avoid || distance < minDistance || (respectPrior && priorGroup[lane]))
                    continue;
                double weight = distance <= PartnerPreferredMaxDistance ? PreferredPartnerWeight : OtherPartnerWeight;
                candidates.Add(lane);
                weights.Add(weight);
                total += weight;
            }
            if (candidates.Count == 0)
                return -1;

            double roll = rng.NextDouble() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0.0)
                    return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }
    }
}
