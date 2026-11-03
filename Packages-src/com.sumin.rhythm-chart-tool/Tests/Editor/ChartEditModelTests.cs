using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SuminTools.RhythmChart.Editor;
using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Tests
{
    public class ChartEditModelTests
    {
        const Difficulty D = Difficulty.Normal;
        const float Duration = 100f;

        ChartData chart;
        ChartEditModel model;

        [SetUp]
        public void SetUp()
        {
            chart = ScriptableObject.CreateInstance<ChartData>();
            chart.ApplyAnalysis(null, 1, 120f, 0f, Duration, new float[0], new List<OnsetData>());
            chart.SetLaneCount(4);
            model = new ChartEditModel(chart, D);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearUndo(chart);
            Object.DestroyImmediate(chart);
        }

        static NoteData Tap(float time, int lane) => new NoteData { time = time, lane = lane, type = NoteType.Tap };

        void Seed(params NoteData[] notes)
        {
            chart.SetNotes(D, notes);
            model.Reload();
        }

        // ---------------------------------------------------------------- 추가

        [Test]
        public void TryAdd_AddsTapAndHold_InTimeOrder()
        {
            Assert.IsNotNull(model.TryAdd(5f, 1, 0f));
            NoteEntry hold = model.TryAdd(2f, 0, 1.5f);

            Assert.IsNotNull(hold);
            Assert.AreEqual(NoteType.Hold, hold.note.type);
            Assert.AreEqual(1.5f, hold.note.holdLength);
            Assert.AreEqual(new[] { 2f, 5f }, model.Entries.Select(e => e.note.time).ToArray(), "시간 순으로 정렬된다");
        }

        [Test]
        public void TryAdd_VeryShortHold_BecomesATap()
        {
            NoteEntry entry = model.TryAdd(3f, 2, ChartEditModel.MinHoldLength / 2f);
            Assert.AreEqual(NoteType.Tap, entry.note.type);
            Assert.AreEqual(0f, entry.note.holdLength);
        }

        [Test]
        public void TryAdd_RejectsOverlapInTheSameLane_ButAllowsOtherLanes()
        {
            model.TryAdd(10f, 1, 2f);   // 10 ~ 12

            Assert.IsNull(model.TryAdd(10f, 1, 0f), "같은 시각");
            Assert.IsNull(model.TryAdd(11f, 1, 0f), "롱노트 한가운데");
            Assert.IsNull(model.TryAdd(9f, 1, 1.5f), "뒤쪽이 겹침");
            Assert.IsNull(model.TryAdd(12f, 1, 0f), "롱노트 끝과 같은 시각");
            Assert.IsNotNull(model.TryAdd(10f, 2, 0f), "다른 레인은 가능");
            Assert.IsNotNull(model.TryAdd(12.5f, 1, 0f), "끝난 뒤는 가능");
        }

        [Test]
        public void TryAdd_RejectsOutOfRange()
        {
            Assert.IsNull(model.TryAdd(5f, -1, 0f));
            Assert.IsNull(model.TryAdd(5f, 4, 0f), "레인 수는 4");
            Assert.IsNull(model.TryAdd(-1f, 0, 0f));
            Assert.IsNull(model.TryAdd(Duration + 1f, 0, 0f));
            Assert.IsNull(model.TryAdd(Duration - 1f, 0, 5f), "롱노트가 곡 끝을 넘음");
        }

        // ---------------------------------------------------------------- 삭제 / 선택

        [Test]
        public void Delete_RemovesSelectedNotesOnly()
        {
            Seed(Tap(1f, 0), Tap(2f, 1), Tap(3f, 2));
            model.SelectOnly(model.Entries[1]);

            Assert.AreEqual(1, model.DeleteSelected());
            Assert.AreEqual(new[] { 1f, 3f }, model.Entries.Select(e => e.note.time).ToArray());

            Assert.IsTrue(model.Delete(model.Entries[0]));
            Assert.AreEqual(1, model.Entries.Count);
        }

        [Test]
        public void Selection_ReplaceAndAdditive()
        {
            Seed(Tap(1f, 0), Tap(2f, 1), Tap(3f, 2), Tap(4f, 3));

            model.SelectWhere(e => e.note.time <= 2f, false);
            Assert.AreEqual(2, model.SelectedCount);

            model.SelectWhere(e => e.note.time >= 4f, true);
            Assert.AreEqual(3, model.SelectedCount, "추가 선택은 기존 선택을 유지한다");

            model.SelectWhere(e => e.note.time == 3f, false);
            Assert.AreEqual(1, model.SelectedCount, "추가가 아니면 기존 선택을 바꾼다");

            model.SelectAll();
            Assert.AreEqual(4, model.SelectedCount);
            model.ClearSelection();
            Assert.AreEqual(0, model.SelectedCount);
        }

        // ---------------------------------------------------------------- 이동

        [Test]
        public void Move_ShiftsAllSelectedNotesTogether()
        {
            Seed(Tap(1f, 0), Tap(2f, 1), Tap(9f, 3));
            model.SelectWhere(e => e.note.time < 5f, false);

            model.BeginMove();
            Assert.IsTrue(model.PreviewMove(0.5f, 1));
            Assert.IsTrue(model.EndMove());

            CollectionAssert.AreEqual(new[] { 1.5f, 2.5f, 9f }, model.Entries.Select(e => e.note.time).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, model.Entries.Select(e => e.note.lane).ToArray());
            CollectionAssert.AreEqual(model.Entries.Select(e => e.note).ToArray(), chart.GetNotes(D).ToArray(), "차트에 반영된다");
        }

        [Test]
        public void Move_IntoAnotherNote_IsRejectedAndKeepsTheLastValidPosition()
        {
            Seed(Tap(1f, 0), Tap(5f, 0));
            model.SelectOnly(model.Entries[0]);

            model.BeginMove();
            Assert.IsTrue(model.PreviewMove(1f, 0));
            Assert.IsFalse(model.PreviewMove(4f, 0), "5초에 있는 다른 노트와 겹침");
            Assert.AreEqual(2f, model.Entries[0].note.time, "마지막으로 유효했던 위치");
            Assert.IsTrue(model.EndMove());
        }

        [Test]
        public void Move_OutOfRange_IsRejected()
        {
            Seed(Tap(1f, 0), Tap(2f, 3));
            model.SelectAll();

            model.BeginMove();
            Assert.IsFalse(model.PreviewMove(-1.5f, 0), "0초 앞");
            Assert.IsFalse(model.PreviewMove(0f, 1), "레인 4");
            Assert.IsFalse(model.PreviewMove(0f, -1), "레인 -1");
            Assert.IsFalse(model.EndMove(), "아무 변화가 없으면 반영하지 않는다");
        }

        [Test]
        public void Move_HoldMayShiftWithinItsOwnSpan_AndSelectedNotesDoNotBlockEachOther()
        {
            // 롱노트(10~14)를 1초 옮기면 자기 자신의 이전 위치와 겹치지만 막히면 안 된다.
            Seed(new NoteData { time = 10f, lane = 0, holdLength = 4f, type = NoteType.Hold }, Tap(12f, 1));
            model.SelectWhere(e => e.note.lane == 0, false);

            model.BeginMove();
            Assert.IsTrue(model.PreviewMove(1f, 0));
            Assert.AreEqual(11f, model.Entries.First(e => e.note.lane == 0).note.time);

            // 선택된 두 노트를 같이 옮길 때 서로의 이전 위치 때문에 막히지 않는다.
            model.CancelMove();
            model.SelectAll();
            model.BeginMove();
            Assert.IsTrue(model.PreviewMove(0f, 1), "레인 0→1, 1→2 로 같이 이동");
        }

        [Test]
        public void Move_CancelRestoresTheStartPositions()
        {
            Seed(Tap(1f, 0), Tap(2f, 1));
            model.SelectAll();

            model.BeginMove();
            model.PreviewMove(3f, 1);
            model.CancelMove();

            Assert.AreEqual(new[] { 1f, 2f }, model.Entries.Select(e => e.note.time).ToArray());
            Assert.IsFalse(model.IsMoving);
        }

        [Test]
        public void Move_HoldNoteMustFitInsideTheSong()
        {
            Seed(new NoteData { time = 10f, lane = 0, holdLength = 4f, type = NoteType.Hold });
            model.SelectAll();

            model.BeginMove();
            Assert.IsTrue(model.PreviewMove(86f, 0), "끝이 정확히 곡 끝(100초)");
            Assert.IsFalse(model.PreviewMove(88f, 0), "끝이 곡 길이를 넘음");
        }

        // ---------------------------------------------------------------- 반영 / Undo

        [Test]
        public void MatchesChart_IsFalseUntilCommitted()
        {
            Assert.IsTrue(model.MatchesChart());
            model.TryAdd(5f, 0, 0f);
            Assert.IsFalse(model.MatchesChart(), "아직 차트에는 반영되지 않았다");

            model.Commit("test");
            Assert.IsTrue(model.MatchesChart());
        }

        [Test]
        public void Commit_WritesTheChartAndMarksItDirty_AndUndoRestoresIt()
        {
            Undo.IncrementCurrentGroup();
            model.TryAdd(5f, 0, 0f);
            model.TryAdd(6f, 1, 0f);
            model.Commit("노트 추가");
            Assert.AreEqual(2, chart.GetNotes(D).Count);

            Undo.PerformUndo();
            Assert.AreEqual(0, chart.GetNotes(D).Count, "한 번의 Undo 로 되돌아간다");

            Undo.PerformRedo();
            Assert.AreEqual(2, chart.GetNotes(D).Count);
        }

        [Test]
        public void ExternalChange_CanBeDetectedAndReloaded()
        {
            Seed(Tap(1f, 0));
            chart.SetNotes(D, new[] { Tap(1f, 0), Tap(2f, 1) });   // 인스펙터에서 노트를 다시 생성한 상황

            Assert.IsFalse(model.MatchesChart());
            model.Reload();
            Assert.IsTrue(model.MatchesChart());
            Assert.AreEqual(2, model.Entries.Count);
        }

        [Test]
        public void SetDifficulty_LoadsThatDifficultysNotes()
        {
            chart.SetNotes(Difficulty.Hard, new[] { Tap(7f, 2) });
            model.SetDifficulty(Difficulty.Hard);

            Assert.AreEqual(Difficulty.Hard, model.Difficulty);
            Assert.AreEqual(1, model.Entries.Count);
            Assert.AreEqual(7f, model.Entries[0].note.time);
        }

        [Test]
        public void StatsReflectNotesPerSecondAndHoldRatio()
        {
            var notes = new List<NoteData>
            {
                Tap(1f, 0), Tap(2f, 1),
                new NoteData { time = 3f, lane = 2, holdLength = 1f, type = NoteType.Hold },
                new NoteData { time = 5f, lane = 3, holdLength = 1f, type = NoteType.Hold },
            };
            ChartStats stats = ChartStats.Compute(notes, 10f);

            Assert.AreEqual(4, stats.NoteCount);
            Assert.AreEqual(0.4f, stats.NotesPerSecond, 1e-4f);
            Assert.AreEqual(0.5f, stats.HoldRatio, 1e-4f);

            ChartStats empty = ChartStats.Compute(new List<NoteData>(), 0f);
            Assert.AreEqual(0, empty.NoteCount);
            Assert.AreEqual(0f, empty.NotesPerSecond);
            Assert.AreEqual(0f, empty.HoldRatio);
        }
    }
}
