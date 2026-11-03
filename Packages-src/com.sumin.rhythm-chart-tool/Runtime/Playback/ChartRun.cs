using System;
using System.Collections.Generic;

namespace SuminTools.RhythmChart
{
    /// <summary>노트의 진행 상태.</summary>
    public enum NoteState
    {
        /// <summary>아직 판정 전.</summary>
        Pending = 0,
        /// <summary>롱노트를 누르고 있는 중.</summary>
        Holding = 1,
        /// <summary>판정이 끝났다. 결과는 <see cref="ChartRun.GetResult"/>.</summary>
        Done = 2,
        /// <summary>시작 위치보다 앞이거나 레인 범위 밖이라 이번 플레이에서 제외됐다.</summary>
        Skipped = 3,
    }

    /// <summary>
    /// 한 번의 플레이에서 노트 판정을 계산한다. Unity 오디오/입력과 무관한 순수 C# 이라
    /// 곡 시각(초)만 넘겨 주면 어디서든 쓸 수 있고 테스트할 수 있다.
    /// </summary>
    /// <remarks>
    /// 규칙: 레인마다 아직 판정되지 않은 가장 이른 노트만 대상이다. 그 노트의 판정 창(goodWindow)보다 이르게 치면 입력을 무시하고,
    /// 창을 지나도록 치지 않으면 Miss. 롱노트는 머리를 친 뒤 끝까지(끝 시각 − 허용 오차) 눌러야 하며 일찍 놓으면 Miss.
    /// 롱노트의 판정은 머리를 친 정확도를 따른다.
    /// </remarks>
    public sealed class ChartRun
    {
        // 판정 창은 float 로 저장되므로(0.12f ≈ 0.11999999) 경계값에서 결과가 뒤집히지 않게 아주 작은 여유를 둔다.
        const double BoundaryEpsilon = 1e-6;

        readonly NoteData[] notes;
        readonly NoteState[] states;
        readonly Judgement[] results;
        readonly Judgement[] headResults;
        readonly double[] headErrors;
        readonly int[][] laneNotes;
        readonly int[] laneCursor;
        readonly JudgementSettings settings;

        /// <param name="source">차트의 노트(정렬되어 있지 않아도 된다).</param>
        /// <param name="laneCount">레인 수. 이 범위를 벗어난 노트는 제외한다.</param>
        /// <param name="settings">판정 창.</param>
        /// <param name="startTime">이 시각보다 앞선 노트는 제외한다(중간부터 플레이할 때).</param>
        public ChartRun(IReadOnlyList<NoteData> source, int laneCount, JudgementSettings settings, double startTime)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            LaneCount = laneCount;

            notes = new NoteData[source.Count];
            for (int i = 0; i < notes.Length; i++)
                notes[i] = source[i];
            Array.Sort(notes, (a, b) =>
            {
                int byTime = a.time.CompareTo(b.time);
                return byTime != 0 ? byTime : a.lane.CompareTo(b.lane);
            });

            states = new NoteState[notes.Length];
            results = new Judgement[notes.Length];
            headResults = new Judgement[notes.Length];
            headErrors = new double[notes.Length];

            var perLane = new List<int>[Math.Max(0, laneCount)];
            for (int lane = 0; lane < perLane.Length; lane++)
                perLane[lane] = new List<int>();
            for (int i = 0; i < notes.Length; i++)
            {
                bool playable = notes[i].lane >= 0 && notes[i].lane < laneCount && notes[i].time >= startTime;
                states[i] = playable ? NoteState.Pending : NoteState.Skipped;
                if (playable)
                {
                    perLane[notes[i].lane].Add(i);
                    Total++;
                }
            }

            laneNotes = new int[perLane.Length][];
            for (int lane = 0; lane < perLane.Length; lane++)
                laneNotes[lane] = perLane[lane].ToArray();
            laneCursor = new int[perLane.Length];
        }

        /// <summary>노트 하나의 최종 판정이 정해질 때마다(노트당 한 번).</summary>
        public event Action<NoteJudgement> Judged;

        /// <summary>롱노트의 머리를 쳐서 누르기 시작했을 때.</summary>
        public event Action<int, Judgement> HoldStarted;

        public int LaneCount { get; }

        /// <summary>이번 플레이에서 판정 대상인 노트 수.</summary>
        public int Total { get; }

        /// <summary>판정이 끝난 노트 수.</summary>
        public int Resolved { get; private set; }

        public int PerfectCount { get; private set; }

        public int GoodCount { get; private set; }

        public int MissCount { get; private set; }

        public int Combo { get; private set; }

        public int MaxCombo { get; private set; }

        /// <summary>모든 노트의 판정이 끝났는가.</summary>
        public bool IsFinished => Resolved >= Total;

        /// <summary>지금까지 판정된 노트 기준 정확도(0~1). Perfect=1, Good=0.5, Miss=0. 아직 없으면 1.</summary>
        public float Accuracy => Resolved == 0 ? 1f : (PerfectCount + 0.5f * GoodCount) / Resolved;

        /// <summary>정렬된 노트 수(제외된 노트 포함).</summary>
        public int NoteCount => notes.Length;

        public NoteData GetNote(int index) => notes[index];

        public NoteState GetState(int index) => states[index];

        /// <summary>판정이 끝난 노트의 결과. <see cref="GetState"/> 가 Done 일 때만 의미가 있다.</summary>
        public Judgement GetResult(int index) => results[index];

        /// <summary>
        /// 그 레인을 눌렀다. 판정이 났으면 그 결과를, 무시됐으면(칠 노트가 없거나 너무 이름) null 을 돌려준다.
        /// 롱노트의 머리를 쳤을 때도 머리의 결과를 돌려준다.
        /// </summary>
        public Judgement? Press(int lane, double songTime)
        {
            if ((uint)lane >= (uint)LaneCount)
                return null;

            Advance(songTime);
            int index = FirstUnresolved(lane);
            if (index < 0 || states[index] != NoteState.Pending)
                return null;

            double error = songTime - settings.inputLatency - notes[index].time;
            double distance = Math.Abs(error);
            if (distance > settings.goodWindow + BoundaryEpsilon)
                return null;

            Judgement result = distance <= settings.perfectWindow + BoundaryEpsilon ? Judgement.Perfect : Judgement.Good;
            Combo++;
            MaxCombo = Math.Max(MaxCombo, Combo);

            if (notes[index].type == NoteType.Hold && notes[index].holdLength > 0f)
            {
                states[index] = NoteState.Holding;
                headResults[index] = result;
                headErrors[index] = error;
                HoldStarted?.Invoke(index, result);
            }
            else
            {
                Resolve(index, result, JudgementKind.Hit, error);
            }
            return result;
        }

        /// <summary>그 레인을 놓았다. 누르고 있던 롱노트가 끝났거나 끊겼으면 그 최종 결과를, 아니면 null.</summary>
        public Judgement? Release(int lane, double songTime)
        {
            if ((uint)lane >= (uint)LaneCount)
                return null;

            // Advance 가 끝 시각을 지난 롱노트를 먼저 완료 처리해 버리면 놓은 결과를 돌려줄 수 없으므로,
            // 누르고 있는 롱노트를 먼저 확인하고 처리한 뒤에 시간을 흘린다.
            int index = FirstUnresolved(lane);
            Judgement? result = null;
            if (index >= 0 && states[index] == NoteState.Holding)
            {
                double earliestEnd = notes[index].time + notes[index].holdLength - settings.holdTailTolerance;
                if (songTime - settings.inputLatency >= earliestEnd - BoundaryEpsilon)
                {
                    Resolve(index, headResults[index], JudgementKind.Hit, headErrors[index]);
                    result = headResults[index];
                }
                else
                {
                    Resolve(index, Judgement.Miss, JudgementKind.Broken, 0.0);
                    result = Judgement.Miss;
                }
            }

            Advance(songTime);
            return result;
        }

        /// <summary>
        /// 시간이 흘렀다. 판정 창을 지나도록 치지 않은 노트는 Miss 로, 끝 시각까지 눌러 낸 롱노트는 완료로 처리한다.
        /// <see cref="Press"/>/<see cref="Release"/> 가 먼저 호출하므로 보통 매 프레임 한 번만 호출하면 된다.
        /// </summary>
        public void Advance(double songTime)
        {
            double missAfter = settings.goodWindow + Math.Max(0f, settings.inputLatency) + BoundaryEpsilon;
            for (int lane = 0; lane < laneNotes.Length; lane++)
            {
                while (true)
                {
                    int index = FirstUnresolved(lane);
                    if (index < 0)
                        break;

                    if (states[index] == NoteState.Pending && songTime - notes[index].time > missAfter)
                    {
                        Resolve(index, Judgement.Miss, JudgementKind.Missed, 0.0);
                    }
                    else if (states[index] == NoteState.Holding && songTime >= notes[index].time + notes[index].holdLength)
                    {
                        Resolve(index, headResults[index], JudgementKind.Hit, headErrors[index]);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        // 그 레인에서 아직 판정이 끝나지 않은 가장 이른 노트의 번호. 없으면 -1.
        int FirstUnresolved(int lane)
        {
            int[] indices = laneNotes[lane];
            int cursor = laneCursor[lane];
            while (cursor < indices.Length && states[indices[cursor]] == NoteState.Done)
                cursor++;
            laneCursor[lane] = cursor;
            return cursor < indices.Length ? indices[cursor] : -1;
        }

        void Resolve(int index, Judgement result, JudgementKind kind, double timingError)
        {
            states[index] = NoteState.Done;
            results[index] = result;
            Resolved++;

            switch (result)
            {
                case Judgement.Perfect: PerfectCount++; break;
                case Judgement.Good: GoodCount++; break;
                default:
                    MissCount++;
                    Combo = 0;
                    break;
            }
            Judged?.Invoke(new NoteJudgement(index, notes[index], result, kind, timingError));
        }
    }
}
