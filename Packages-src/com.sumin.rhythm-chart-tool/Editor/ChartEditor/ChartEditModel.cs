using System;
using System.Collections.Generic;
using UnityEditor;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>편집 중인 노트 하나와 선택 상태.</summary>
    internal sealed class NoteEntry
    {
        public NoteData note;
        public bool selected;

        public float Start => note.time;

        public float End => note.time + note.holdLength;
    }

    /// <summary>
    /// 한 난이도의 노트 편집 로직(추가, 삭제, 이동, 선택, 겹침 검사, Undo). 화면과 무관해서 테스트할 수 있다.
    /// 차트 에셋은 <see cref="Commit"/> 할 때만 바뀐다. 드래그 중의 변화는 이 객체의 메모리에만 있다가 마우스를 놓을 때 한 번에 반영된다.
    /// </summary>
    internal sealed class ChartEditModel
    {
        /// <summary>같은 레인에서 이보다 가까운 두 노트는 겹친 것으로 본다(초).</summary>
        public const float OverlapEpsilon = 1e-4f;

        /// <summary>롱노트로 인정하는 최소 길이(초). 이보다 짧으면 탭 노트가 된다.</summary>
        public const float MinHoldLength = 0.01f;

        readonly List<NoteEntry> entries = new List<NoteEntry>();
        List<KeyValuePair<NoteEntry, NoteData>> moveOrigins;
        bool moved;

        public ChartEditModel(ChartData chart, Difficulty difficulty)
        {
            Chart = chart ?? throw new ArgumentNullException(nameof(chart));
            Difficulty = difficulty;
            Reload();
        }

        public ChartData Chart { get; }

        public Difficulty Difficulty { get; private set; }

        public IReadOnlyList<NoteEntry> Entries => entries;

        public bool IsMoving => moveOrigins != null;

        public int SelectedCount
        {
            get
            {
                int count = 0;
                foreach (NoteEntry entry in entries)
                    if (entry.selected)
                        count++;
                return count;
            }
        }

        /// <summary>다른 난이도로 바꾼다. 선택은 지워진다.</summary>
        public void SetDifficulty(Difficulty difficulty)
        {
            Difficulty = difficulty;
            Reload();
        }

        /// <summary>차트 에셋의 노트로 다시 불러온다. 선택과 진행 중인 이동은 버려진다.</summary>
        public void Reload()
        {
            moveOrigins = null;
            entries.Clear();
            foreach (NoteData note in Chart.GetNotes(Difficulty))
                entries.Add(new NoteEntry { note = note });
        }

        /// <summary>메모리의 노트가 차트 에셋과 같은가(밖에서 차트가 바뀌었는지 알아내는 데 쓴다).</summary>
        public bool MatchesChart()
        {
            IReadOnlyList<NoteData> notes = Chart.GetNotes(Difficulty);
            if (notes.Count != entries.Count)
                return false;
            for (int i = 0; i < entries.Count; i++)
            {
                NoteData a = entries[i].note, b = notes[i];
                if (a.time != b.time || a.lane != b.lane || a.holdLength != b.holdLength || a.type != b.type)
                    return false;
            }
            return true;
        }

        // ---------- 선택 ----------

        public void ClearSelection()
        {
            foreach (NoteEntry entry in entries)
                entry.selected = false;
        }

        public void SelectAll()
        {
            foreach (NoteEntry entry in entries)
                entry.selected = true;
        }

        /// <summary>조건에 맞는 노트를 선택한다. additive 가 아니면 기존 선택을 먼저 지운다.</summary>
        public void SelectWhere(Func<NoteEntry, bool> predicate, bool additive)
        {
            foreach (NoteEntry entry in entries)
            {
                bool hit = predicate(entry);
                entry.selected = additive ? entry.selected || hit : hit;
            }
        }

        /// <summary>그 노트만 선택한다.</summary>
        public void SelectOnly(NoteEntry target)
        {
            foreach (NoteEntry entry in entries)
                entry.selected = entry == target;
        }

        // ---------- 추가 / 삭제 ----------

        /// <summary>
        /// 노트를 추가한다. 곡 범위나 레인 범위를 벗어나거나 같은 레인의 다른 노트와 겹치면 추가하지 않고 null 을 돌려준다.
        /// <paramref name="holdLength"/> 가 <see cref="MinHoldLength"/> 이상이면 롱노트가 된다.
        /// </summary>
        public NoteEntry TryAdd(float time, int lane, float holdLength)
        {
            float length = holdLength >= MinHoldLength ? holdLength : 0f;
            if (!IsInside(time, time + length, lane) || Overlaps(time, time + length, lane, false))
                return null;

            var entry = new NoteEntry
            {
                note = new NoteData
                {
                    time = time,
                    lane = lane,
                    holdLength = length,
                    type = length > 0f ? NoteType.Hold : NoteType.Tap,
                },
            };
            entries.Add(entry);
            Sort();
            return entry;
        }

        /// <summary>선택된 노트를 모두 지우고 지운 개수를 돌려준다.</summary>
        public int DeleteSelected()
        {
            return entries.RemoveAll(e => e.selected);
        }

        /// <summary>그 노트를 지운다. 있었으면 true.</summary>
        public bool Delete(NoteEntry entry)
        {
            return entries.Remove(entry);
        }

        // ---------- 이동 ----------

        /// <summary>선택된 노트의 현재 위치를 기억하고 이동을 시작한다.</summary>
        public void BeginMove()
        {
            moveOrigins = new List<KeyValuePair<NoteEntry, NoteData>>();
            moved = false;
            foreach (NoteEntry entry in entries)
                if (entry.selected)
                    moveOrigins.Add(new KeyValuePair<NoteEntry, NoteData>(entry, entry.note));
        }

        /// <summary>
        /// 시작 위치 기준으로 시간 <paramref name="deltaTime"/>, 레인 <paramref name="deltaLane"/> 만큼 옮긴 위치를 미리 적용한다.
        /// 하나라도 곡/레인 범위를 벗어나거나 선택되지 않은 노트와 겹치면 아무것도 바꾸지 않고 false 를 돌려준다.
        /// </summary>
        public bool PreviewMove(float deltaTime, int deltaLane)
        {
            if (moveOrigins == null)
                return false;

            var targets = new List<NoteData>(moveOrigins.Count);
            foreach (KeyValuePair<NoteEntry, NoteData> origin in moveOrigins)
            {
                NoteData note = origin.Value;
                note.time += deltaTime;
                note.lane += deltaLane;
                if (!IsInside(note.time, note.time + note.holdLength, note.lane))
                    return false;
                // 움직이는(선택된) 노트끼리는 같이 움직이므로 서로 겹치지 않는다. 선택되지 않은 노트하고만 비교한다.
                if (Overlaps(note.time, note.time + note.holdLength, note.lane, true))
                    return false;
                targets.Add(note);
            }

            for (int i = 0; i < moveOrigins.Count; i++)
                moveOrigins[i].Key.note = targets[i];
            moved = deltaTime != 0f || deltaLane != 0;
            return true;
        }

        /// <summary>이동을 끝낸다. 실제로 움직였으면 정렬하고 차트에 반영한 뒤 true.</summary>
        public bool EndMove()
        {
            bool changed = moved;
            moveOrigins = null;
            moved = false;
            if (changed)
            {
                Sort();
                Commit("노트 이동");
            }
            return changed;
        }

        /// <summary>이동을 취소하고 시작 위치로 되돌린다.</summary>
        public void CancelMove()
        {
            if (moveOrigins == null)
                return;
            foreach (KeyValuePair<NoteEntry, NoteData> origin in moveOrigins)
                origin.Key.note = origin.Value;
            moveOrigins = null;
            moved = false;
        }

        // ---------- 반영 ----------

        /// <summary>메모리의 노트를 차트 에셋에 쓴다. Undo 한 단계로 기록되고 에셋은 dirty 가 된다.</summary>
        public void Commit(string undoName)
        {
            Undo.RecordObject(Chart, undoName);
            var notes = new List<NoteData>(entries.Count);
            foreach (NoteEntry entry in entries)
                notes.Add(entry.note);
            Chart.SetNotes(Difficulty, notes);
            EditorUtility.SetDirty(Chart);
        }

        // ---------- 내부 ----------

        bool IsInside(float start, float end, int lane)
        {
            return lane >= 0 && lane < Chart.LaneCount
                && start >= -OverlapEpsilon && end <= Chart.Duration + OverlapEpsilon;
        }

        // 같은 레인에서 [start, end] 와 겹치는 노트가 있는가. unselectedOnly 이면 선택된 노트는 건너뛴다(이동 중인 노트들).
        bool Overlaps(float start, float end, int lane, bool unselectedOnly)
        {
            foreach (NoteEntry other in entries)
            {
                if (other.note.lane != lane || (unselectedOnly && other.selected))
                    continue;
                if (start < other.End + OverlapEpsilon && other.Start < end + OverlapEpsilon)
                    return true;
            }
            return false;
        }

        void Sort()
        {
            entries.Sort((a, b) =>
            {
                int byTime = a.note.time.CompareTo(b.note.time);
                return byTime != 0 ? byTime : a.note.lane.CompareTo(b.note.lane);
            });
        }
    }
}
