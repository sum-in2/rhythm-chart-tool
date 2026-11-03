using System;
using System.Collections.Generic;

namespace SuminTools.RhythmChart
{
    /// <summary>
    /// 분석된 onset 으로 한 난이도의 노트를 만든다. 에디터와 무관한 순수 C# 이라 Python 을 다시 실행하지 않고 즉시 재생성할 수 있다.
    /// 같은 입력과 같은 시드에서는 항상 같은 결과가 나온다.
    /// </summary>
    /// <remarks>
    /// 처리 순서: ① strength 상위 N% 선별 → ② 박자 그리드에 스냅(같은 칸은 가장 강한 것만) →
    /// ③ 최소 간격 적용 → ④ 동시치기/롱노트 선택 → ⑤ 레인 배치.
    /// </remarks>
    public static class ChartGenerator
    {
        const double Epsilon = 1e-6;

        struct Candidate
        {
            public float time;
            public float strength;
            public OnsetBand band;
        }

        /// <summary>기본 시드와 난이도로 그 난이도 전용 시드를 만든다(난이도마다 다른 수열을 쓰기 위해).</summary>
        public static int DeriveSeed(int baseSeed, Difficulty difficulty)
        {
            unchecked
            {
                return baseSeed * 397 ^ ((int)difficulty + 1) * 7919;
            }
        }

        /// <summary>노트를 생성한다. 반환 목록은 시간, 레인 순으로 정렬된다.</summary>
        /// <param name="onsets">분석된 onset.</param>
        /// <param name="bpm">고정 BPM. 0 이하이면 그리드 스냅 없이 원래 시각을 쓴다.</param>
        /// <param name="offset">BPM 그리드의 첫 선 위치(초).</param>
        /// <param name="duration">곡 길이(초). 이 범위 밖의 onset 은 버린다.</param>
        /// <param name="laneCount">레인 수. 허용 범위로 보정된다.</param>
        /// <param name="settings">난이도 규칙.</param>
        /// <param name="seed">난수 시드.</param>
        public static List<NoteData> Generate(
            IReadOnlyList<OnsetData> onsets,
            float bpm,
            float offset,
            float duration,
            int laneCount,
            DifficultySettings settings,
            int seed)
        {
            if (onsets == null) throw new ArgumentNullException(nameof(onsets));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            laneCount = Clamp(laneCount, ChartConstants.MinLaneCount, ChartConstants.MaxLaneCount);
            bool hasGrid = bpm > 0f && !float.IsInfinity(bpm);
            double beat = hasGrid ? 60.0 / bpm : ChartConstants.FallbackBeatSeconds;
            int subdivisions = Math.Max(1, (int)settings.gridSnap);
            double step = beat / subdivisions;

            List<Candidate> candidates = SelectStrongest(onsets, settings.keepTopPercent, duration);
            if (hasGrid)
                candidates = SnapToGrid(candidates, offset, step, duration);
            candidates = EnforceMinInterval(candidates, settings.minNoteInterval);

            var notes = new List<NoteData>();
            int count = candidates.Count;
            if (count == 0)
                return notes;

            bool[] isChord = PickStrongest(candidates, RoundCount(count, settings.chordRatio));
            float[] holdLengths = AssignHolds(candidates, isChord, settings, beat, hasGrid ? step : 0.0, duration);

            var placer = new LanePlacer(laneCount, settings, new DeterministicRandom(seed));
            for (int i = 0; i < count; i++)
            {
                Candidate c = candidates[i];
                int lane = placer.NextLane(c.band, count - i);
                bool isHold = holdLengths[i] > 0f;
                notes.Add(new NoteData
                {
                    time = c.time,
                    lane = lane,
                    holdLength = isHold ? holdLengths[i] : 0f,
                    type = isHold ? NoteType.Hold : NoteType.Tap,
                });

                if (isChord[i])
                {
                    notes.Add(new NoteData
                    {
                        time = c.time,
                        lane = placer.PartnerLane(lane),
                        holdLength = 0f,
                        type = NoteType.Tap,
                    });
                }
            }

            notes.Sort((a, b) =>
            {
                int byTime = a.time.CompareTo(b.time);
                return byTime != 0 ? byTime : a.lane.CompareTo(b.lane);
            });
            return notes;
        }

        // ① 유효한 onset 중 strength 가 큰 상위 percent% 만 남긴다(시간 순서 유지).
        static List<Candidate> SelectStrongest(IReadOnlyList<OnsetData> onsets, float keepTopPercent, float duration)
        {
            var valid = new List<Candidate>(onsets.Count);
            foreach (OnsetData onset in onsets)
            {
                bool usable = !float.IsNaN(onset.time) && !float.IsNaN(onset.strength)
                    && onset.time >= 0f && onset.time <= duration;
                if (usable)
                    valid.Add(new Candidate { time = onset.time, strength = onset.strength, band = onset.band });
            }
            valid.Sort((a, b) => a.time.CompareTo(b.time));

            int keep = RoundCount(valid.Count, keepTopPercent / 100f);
            if (valid.Count > 0 && keepTopPercent > 0f)
                keep = Math.Max(keep, 1);

            bool[] kept = PickStrongest(valid, keep);
            var result = new List<Candidate>(keep);
            for (int i = 0; i < valid.Count; i++)
                if (kept[i])
                    result.Add(valid[i]);
            return result;
        }

        // ② 가장 가까운 그리드 칸으로 옮기고, 같은 칸에 몰린 것은 가장 강한 하나만 남긴다.
        static List<Candidate> SnapToGrid(List<Candidate> candidates, float offset, double step, float duration)
        {
            var result = new List<Candidate>(candidates.Count);
            long firstSlot = (long)Math.Ceiling(-offset / step - Epsilon);
            long lastSlotSeen = long.MinValue;

            foreach (Candidate c in candidates)
            {
                long slot = Math.Max((long)Math.Round((c.time - offset) / step), firstSlot);
                double snapped = offset + slot * step;
                if (snapped > duration)
                    continue;

                Candidate moved = c;
                moved.time = (float)snapped;
                if (slot == lastSlotSeen)
                {
                    int last = result.Count - 1;
                    if (moved.strength > result[last].strength)
                        result[last] = moved;
                }
                else
                {
                    result.Add(moved);
                    lastSlotSeen = slot;
                }
            }
            return result;
        }

        // ③ 최소 간격보다 가까운 노트는 약한 쪽을 버린다. 같은 세기면 먼저 나온 쪽을 남긴다.
        static List<Candidate> EnforceMinInterval(List<Candidate> candidates, float minInterval)
        {
            var kept = new List<Candidate>(candidates.Count);
            foreach (Candidate c in candidates)
            {
                bool dropped = false;
                while (kept.Count > 0 && c.time - kept[kept.Count - 1].time < minInterval - Epsilon)
                {
                    if (c.strength > kept[kept.Count - 1].strength)
                        kept.RemoveAt(kept.Count - 1);
                    else
                    {
                        dropped = true;
                        break;
                    }
                }
                if (!dropped)
                    kept.Add(c);
            }
            return kept;
        }

        // ④ 롱노트: 동시치기가 아닌 노트 중 뒤 공백이 충분한 것만 후보로 삼고, 공백이 긴 것부터 비율만큼 고른다.
        static float[] AssignHolds(
            List<Candidate> notes, bool[] isChord, DifficultySettings settings, double beat, double step, float duration)
        {
            int count = notes.Count;
            var holds = new float[count];
            int wanted = RoundCount(count, settings.holdRatio);
            if (wanted == 0)
                return holds;

            double minLength = settings.holdMinBeats * beat;
            double maxLength = Math.Max(settings.holdMaxBeats, settings.holdMinBeats) * beat;

            var eligible = new List<KeyValuePair<int, double>>();   // (노트 인덱스, 뒤 공백)
            var lengths = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (isChord[i])
                    continue;
                double nextTime = i + 1 < count ? notes[i + 1].time : duration;
                double gap = nextTime - notes[i].time;
                double length = Math.Min(gap - settings.minNoteInterval, maxLength);
                if (step > 0.0)
                    length = Math.Floor(length / step + Epsilon) * step;
                if (length >= minLength - Epsilon)
                {
                    eligible.Add(new KeyValuePair<int, double>(i, gap));
                    lengths[i] = length;
                }
            }

            eligible.Sort((a, b) =>
            {
                int byGap = b.Value.CompareTo(a.Value);
                return byGap != 0 ? byGap : a.Key.CompareTo(b.Key);
            });
            for (int k = 0; k < Math.Min(wanted, eligible.Count); k++)
                holds[eligible[k].Key] = (float)lengths[eligible[k].Key];
            return holds;
        }

        // strength 가 큰 순서로 count 개를 골라 표시한다(같은 세기면 앞선 인덱스 우선).
        static bool[] PickStrongest(List<Candidate> candidates, int count)
        {
            var picked = new bool[candidates.Count];
            if (count <= 0)
                return picked;

            var order = new List<int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                order.Add(i);
            order.Sort((a, b) =>
            {
                int byStrength = candidates[b].strength.CompareTo(candidates[a].strength);
                return byStrength != 0 ? byStrength : a.CompareTo(b);
            });

            for (int k = 0; k < Math.Min(count, order.Count); k++)
                picked[order[k]] = true;
            return picked;
        }

        static int RoundCount(int total, float ratio)
        {
            return Clamp((int)Math.Round(total * (double)ratio, MidpointRounding.AwayFromZero), 0, total);
        }

        static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
