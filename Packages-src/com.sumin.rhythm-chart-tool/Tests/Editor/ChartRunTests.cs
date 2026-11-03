using System.Collections.Generic;
using NUnit.Framework;

namespace SuminTools.RhythmChart.Tests
{
    public class ChartRunTests
    {
        // 판정 창: Perfect ±0.05, Good ±0.12, 롱노트 끝 허용 0.12
        static JudgementSettings Settings(float latency = 0f)
        {
            return new JudgementSettings { perfectWindow = 0.05f, goodWindow = 0.12f, holdTailTolerance = 0.12f, inputLatency = latency };
        }

        static NoteData Tap(float time, int lane) => new NoteData { time = time, lane = lane, type = NoteType.Tap };

        static NoteData Hold(float time, int lane, float length) =>
            new NoteData { time = time, lane = lane, holdLength = length, type = NoteType.Hold };

        static ChartRun Run(IReadOnlyList<NoteData> notes, double start = 0.0, JudgementSettings settings = null, int lanes = 4)
        {
            return new ChartRun(notes, lanes, settings ?? Settings(), start);
        }

        // ---------------------------------------------------------------- 탭

        [TestCase(10.00, Judgement.Perfect)]
        [TestCase(10.05, Judgement.Perfect)]
        [TestCase(9.95, Judgement.Perfect)]
        [TestCase(10.07, Judgement.Good)]
        [TestCase(9.88, Judgement.Good)]
        [TestCase(10.12, Judgement.Good)]
        public void Press_IsJudgedByTheDistanceToTheNote(double pressTime, Judgement expected)
        {
            ChartRun run = Run(new[] { Tap(10f, 0) });

            Judgement? result = run.Press(0, pressTime);

            Assert.AreEqual(expected, result);
            Assert.AreEqual(1, run.Resolved);
            Assert.AreEqual(1, run.Combo);
            Assert.IsTrue(run.IsFinished);
        }

        [Test]
        public void Press_WellBeforeTheNote_IsIgnored()
        {
            ChartRun run = Run(new[] { Tap(10f, 0) });

            Assert.IsNull(run.Press(0, 9.5), "판정 창 밖의 이른 입력");
            Assert.AreEqual(0, run.Resolved);
            Assert.AreEqual(NoteState.Pending, run.GetState(0));
            Assert.AreEqual(Judgement.Perfect, run.Press(0, 10.0), "그래도 노트는 칠 수 있다");
        }

        [Test]
        public void Press_OnlyAffectsItsOwnLane()
        {
            ChartRun run = Run(new[] { Tap(10f, 1) });

            Assert.IsNull(run.Press(0, 10.0));
            Assert.IsNull(run.Press(2, 10.0));
            Assert.IsNull(run.Press(7, 10.0), "레인 범위 밖");
            Assert.IsNull(run.Press(-1, 10.0));
            Assert.AreEqual(0, run.Resolved);
        }

        [Test]
        public void Press_JudgesTheEarliestPendingNoteFirst()
        {
            ChartRun run = Run(new[] { Tap(10f, 0), Tap(10.2f, 0) });

            Assert.AreEqual(Judgement.Perfect, run.Press(0, 10.0));
            Assert.AreEqual(NoteState.Done, run.GetState(0));
            Assert.AreEqual(NoteState.Pending, run.GetState(1));
            Assert.AreEqual(Judgement.Perfect, run.Press(0, 10.2));
            Assert.AreEqual(2, run.PerfectCount);
        }

        [Test]
        public void Advance_MissesNotesThatWereNotHit()
        {
            ChartRun run = Run(new[] { Tap(10f, 0), Tap(11f, 1) });

            run.Advance(10.11);
            Assert.AreEqual(0, run.MissCount, "아직 Good 창 안");

            run.Advance(10.13);
            Assert.AreEqual(1, run.MissCount);
            Assert.AreEqual(NoteState.Done, run.GetState(0));
            Assert.AreEqual(Judgement.Miss, run.GetResult(0));
            Assert.AreEqual(NoteState.Pending, run.GetState(1));

            run.Advance(11.5);
            Assert.AreEqual(2, run.MissCount);
            Assert.IsTrue(run.IsFinished);
        }

        [Test]
        public void Press_AfterTheWindow_StillMissesTheOldNoteBeforeJudgingTheNextOne()
        {
            ChartRun run = Run(new[] { Tap(10f, 0), Tap(10.3f, 0) });

            Judgement? result = run.Press(0, 10.3);   // 첫 노트는 이미 지나감 → Miss, 이 입력은 두 번째 노트에 쓰인다

            Assert.AreEqual(Judgement.Perfect, result);
            Assert.AreEqual(Judgement.Miss, run.GetResult(0));
            Assert.AreEqual(Judgement.Perfect, run.GetResult(1));
            Assert.AreEqual(1, run.Combo);
        }

        [Test]
        public void Combo_CountsHits_AndBreaksOnMiss()
        {
            ChartRun run = Run(new[] { Tap(1f, 0), Tap(2f, 0), Tap(3f, 0), Tap(4f, 0) });

            run.Press(0, 1.0);
            run.Press(0, 2.0);
            Assert.AreEqual(2, run.Combo);

            run.Advance(3.5);       // 세 번째를 놓침
            Assert.AreEqual(0, run.Combo);
            Assert.AreEqual(2, run.MaxCombo);

            run.Press(0, 4.0);
            Assert.AreEqual(1, run.Combo);
            Assert.AreEqual(2, run.MaxCombo);
        }

        [Test]
        public void Accuracy_WeightsPerfectGoodAndMiss()
        {
            ChartRun run = Run(new[] { Tap(1f, 0), Tap(2f, 1), Tap(3f, 2), Tap(4f, 3) });
            Assert.AreEqual(1f, run.Accuracy, "아직 판정된 노트가 없으면 100%");

            run.Press(0, 1.0);      // Perfect
            run.Press(1, 2.1);      // Good
            run.Advance(3.5);       // Miss
            run.Press(3, 4.0);      // Perfect

            Assert.AreEqual((2f + 0.5f) / 4f, run.Accuracy, 1e-5f);
            Assert.AreEqual(2, run.PerfectCount);
            Assert.AreEqual(1, run.GoodCount);
            Assert.AreEqual(1, run.MissCount);
        }

        [Test]
        public void Notes_BeforeTheStartTime_AreSkipped()
        {
            ChartRun run = Run(new[] { Tap(1f, 0), Tap(5f, 0), Tap(9f, 1) }, start: 5.0);

            Assert.AreEqual(2, run.Total, "5초 이후 노트만 대상");
            Assert.AreEqual(NoteState.Skipped, run.GetState(0));
            Assert.IsNull(run.Press(0, 1.0), "건너뛴 노트는 칠 수 없다");

            run.Press(0, 5.0);
            run.Press(1, 9.0);
            Assert.IsTrue(run.IsFinished);
            Assert.AreEqual(2, run.PerfectCount);
        }

        [Test]
        public void NotesOutsideTheLaneRange_AreSkipped_AndEmptyChartIsFinished()
        {
            ChartRun run = Run(new[] { Tap(1f, 5), Tap(2f, 0) }, lanes: 4);
            Assert.AreEqual(1, run.Total);

            Assert.IsTrue(Run(new List<NoteData>()).IsFinished);
        }

        [Test]
        public void InputLatency_ShiftsTheJudgedTime()
        {
            ChartRun late = Run(new[] { Tap(10f, 0) }, settings: Settings(0.04f));
            Assert.AreEqual(Judgement.Perfect, late.Press(0, 10.09), "입력이 0.04초 늦게 기록되는 환경에서 0.09 → 0.05 로 보정");

            ChartRun uncompensated = Run(new[] { Tap(10f, 0) });
            Assert.AreEqual(Judgement.Good, uncompensated.Press(0, 10.09));
        }

        [Test]
        public void Judged_FiresOncePerNote_WithTheTimingError()
        {
            ChartRun run = Run(new[] { Tap(1f, 0), Tap(2f, 1) });
            var events = new List<NoteJudgement>();
            run.Judged += events.Add;

            run.Press(0, 1.03);
            run.Advance(2.5);
            run.Advance(3.0);

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(Judgement.Perfect, events[0].Result);
            Assert.AreEqual(JudgementKind.Hit, events[0].Kind);
            Assert.AreEqual(0.03, events[0].TimingError, 1e-6);
            Assert.AreEqual(JudgementKind.Missed, events[1].Kind);
            Assert.AreEqual(1, events[1].NoteIndex);
        }

        // ---------------------------------------------------------------- 롱노트

        [Test]
        public void Hold_HoldingToTheEnd_CompletesWithTheHeadsJudgement()
        {
            ChartRun run = Run(new[] { Hold(10f, 0, 2f) });
            int started = 0;
            run.HoldStarted += (index, head) => started++;

            Assert.AreEqual(Judgement.Good, run.Press(0, 10.08));
            Assert.AreEqual(NoteState.Holding, run.GetState(0));
            Assert.AreEqual(1, started);
            Assert.AreEqual(0, run.Resolved, "끝나기 전에는 최종 판정이 없다");

            Judgement? finished = run.Release(0, 12.0);

            Assert.AreEqual(Judgement.Good, finished);
            Assert.AreEqual(1, run.GoodCount);
            Assert.AreEqual(1, run.Combo);
            Assert.IsTrue(run.IsFinished);
        }

        [Test]
        public void Hold_ReleasedWithinTheTailTolerance_StillCounts()
        {
            ChartRun run = Run(new[] { Hold(10f, 0, 2f) });
            run.Press(0, 10.0);

            Assert.AreEqual(Judgement.Perfect, run.Release(0, 11.9), "끝(12.0)보다 0.1초 일찍");
        }

        [Test]
        public void Hold_ReleasedEarly_BreaksTheComboAndMisses()
        {
            ChartRun run = Run(new[] { Hold(10f, 0, 2f), Tap(13f, 0) });
            var events = new List<NoteJudgement>();
            run.Judged += events.Add;

            run.Press(0, 10.0);
            Judgement? released = run.Release(0, 11.0);

            Assert.AreEqual(Judgement.Miss, released);
            Assert.AreEqual(JudgementKind.Broken, events[0].Kind);
            Assert.AreEqual(0, run.Combo);
            Assert.AreEqual(1, run.MissCount);
            Assert.AreEqual(0, run.PerfectCount, "끊긴 롱노트는 머리를 잘 쳤어도 Perfect 로 세지 않는다");
        }

        [Test]
        public void Hold_KeptPressedPastTheEnd_CompletesOnItsOwn()
        {
            ChartRun run = Run(new[] { Hold(10f, 0, 2f) });
            run.Press(0, 10.0);

            run.Advance(12.01);

            Assert.IsTrue(run.IsFinished);
            Assert.AreEqual(1, run.PerfectCount);
            Assert.IsNull(run.Release(0, 12.5), "이미 끝난 롱노트를 놓아도 아무 일도 없다");
        }

        [Test]
        public void Hold_PressingAgainWhileHolding_IsIgnored()
        {
            ChartRun run = Run(new[] { Hold(10f, 0, 2f) });
            run.Press(0, 10.0);

            Assert.IsNull(run.Press(0, 10.5));
            Assert.AreEqual(NoteState.Holding, run.GetState(0));
        }

        [Test]
        public void Hold_NeverPressed_IsMissedAsAWhole()
        {
            ChartRun run = Run(new[] { Hold(10f, 0, 2f) });

            run.Advance(10.2);

            Assert.AreEqual(1, run.MissCount);
            Assert.IsTrue(run.IsFinished);
        }

        [Test]
        public void Release_WithoutAHold_DoesNothing()
        {
            ChartRun run = Run(new[] { Tap(10f, 0) });
            Assert.IsNull(run.Release(0, 10.0));
            Assert.AreEqual(0, run.Resolved);
        }
    }
}
