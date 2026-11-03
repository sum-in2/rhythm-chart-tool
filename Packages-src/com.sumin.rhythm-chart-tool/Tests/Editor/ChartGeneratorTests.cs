using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SuminTools.RhythmChart.Tests
{
    public class ChartGeneratorTests
    {
        const float Bpm = 120f;
        const float Offset = 0.1f;
        const float Duration = 100f;
        const float TimeTolerance = 1e-3f;

        // 시드 몇 개만 보면 드물게 나오는 경우(동시치기 직후 등)를 놓친다.
        static readonly int[] Seeds = Enumerable.Range(1, 15).ToArray();

        static readonly Difficulty[] AllDifficulties =
            { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard, Difficulty.Expert };

        // 16분음표(0.125s) 간격으로 조금씩 흔들린 시각, 무작위 세기/대역의 onset. 시드가 같으면 항상 같다.
        static List<OnsetData> DenseOnsets(int seed = 7)
        {
            var random = new System.Random(seed);
            var onsets = new List<OnsetData>();
            for (int i = 0; Offset + i * 0.125f < Duration - 0.5f; i++)
            {
                onsets.Add(new OnsetData
                {
                    time = Offset + i * 0.125f + (float)(random.NextDouble() - 0.5) * 0.02f,
                    strength = 0.1f + (float)random.NextDouble() * 0.9f,
                    band = (OnsetBand)random.Next(3),
                });
            }
            return onsets;
        }

        // 2초 간격의 성긴 onset(4박). 세기는 모두 달라서 순위가 분명하다.
        static List<OnsetData> SparseOnsets(int count)
        {
            var onsets = new List<OnsetData>();
            for (int i = 0; i < count; i++)
            {
                onsets.Add(new OnsetData
                {
                    time = Offset + i * 2f,
                    strength = 0.1f + 0.9f * ((i * 7) % count) / count,
                    band = (OnsetBand)(i % 3),
                });
            }
            return onsets;
        }

        static List<NoteData> Generate(
            IReadOnlyList<OnsetData> onsets, DifficultySettings settings, int lanes = 4, int seed = 1,
            float bpm = Bpm, float duration = Duration)
        {
            return ChartGenerator.Generate(onsets, bpm, Offset, duration, lanes, settings, seed);
        }

        static DifficultySettings Plain()
        {
            return new DifficultySettings
            {
                keepTopPercent = 100f, gridSnap = GridSnap.Sixteenth, minNoteInterval = 0.05f,
                holdRatio = 0f, chordRatio = 0f, patternChance = 0f, maxLaneJump = 5,
            };
        }

        // 같은 시각의 노트를 한 묶음(동시치기)으로 본다.
        static List<List<NoteData>> Groups(IEnumerable<NoteData> notes)
        {
            var groups = new List<List<NoteData>>();
            foreach (NoteData note in notes)
            {
                if (groups.Count > 0 && Math.Abs(groups[groups.Count - 1][0].time - note.time) < 1e-5f)
                    groups[groups.Count - 1].Add(note);
                else
                    groups.Add(new List<NoteData> { note });
            }
            return groups;
        }

        [Test]
        public void SameSeed_GivesIdenticalNotes()
        {
            List<OnsetData> onsets = DenseOnsets();
            foreach (Difficulty difficulty in AllDifficulties)
            {
                DifficultySettings settings = DifficultySettings.CreateDefault(difficulty);
                List<NoteData> a = Generate(onsets, settings, 5, 99);
                List<NoteData> b = Generate(onsets, settings, 5, 99);
                CollectionAssert.AreEqual(a, b, $"{difficulty}");
            }
        }

        [Test]
        public void DifferentSeed_ChangesLanes()
        {
            List<OnsetData> onsets = DenseOnsets();
            DifficultySettings settings = DifficultySettings.CreateDefault(Difficulty.Normal);
            List<NoteData> a = Generate(onsets, settings, 4, 1);
            List<NoteData> b = Generate(onsets, settings, 4, 2);
            Assert.AreEqual(a.Count, b.Count, "시드는 노트 개수/시각을 바꾸지 않는다");
            CollectionAssert.AreNotEqual(a.Select(n => n.lane).ToList(), b.Select(n => n.lane).ToList());
        }

        [Test]
        public void DeriveSeed_DiffersPerDifficulty()
        {
            var seeds = AllDifficulties.Select(d => ChartGenerator.DeriveSeed(12345, d)).Distinct().ToList();
            Assert.AreEqual(AllDifficulties.Length, seeds.Count);
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Lanes_StayInRange(int laneCount)
        {
            List<OnsetData> onsets = DenseOnsets();
            foreach (Difficulty difficulty in AllDifficulties)
            {
                foreach (NoteData note in Generate(onsets, DifficultySettings.CreateDefault(difficulty), laneCount))
                    Assert.That(note.lane, Is.InRange(0, laneCount - 1), $"{difficulty}");
            }
        }

        [TestCase(2, 4)]
        [TestCase(99, 6)]
        public void LaneCount_IsClampedToSupportedRange(int requested, int effective)
        {
            foreach (NoteData note in Generate(DenseOnsets(), DifficultySettings.CreateDefault(Difficulty.Expert), requested))
                Assert.That(note.lane, Is.InRange(0, effective - 1));
        }

        [Test]
        public void ConsecutiveGroups_NeverReuseALane()
        {
            List<OnsetData> onsets = DenseOnsets();
            foreach (int laneCount in new[] { 4, 5, 6 })
            foreach (Difficulty difficulty in AllDifficulties)
            foreach (int seed in Seeds)
            {
                List<List<NoteData>> groups = Groups(Generate(onsets, DifficultySettings.CreateDefault(difficulty), laneCount, seed));
                for (int i = 1; i < groups.Count; i++)
                {
                    var previous = new HashSet<int>(groups[i - 1].Select(n => n.lane));
                    foreach (NoteData note in groups[i])
                        Assert.IsFalse(previous.Contains(note.lane),
                            $"{difficulty} lanes={laneCount} seed={seed}: {groups[i - 1][0].time:0.###}s → {note.time:0.###}s 가 레인 {note.lane} 을 반복");
                }
            }
        }

        [Test]
        public void SingleNotes_RespectLaneJumpLimit()
        {
            List<OnsetData> onsets = DenseOnsets();
            foreach (Difficulty difficulty in AllDifficulties)
            foreach (int seed in Seeds)
            {
                DifficultySettings settings = DifficultySettings.CreateDefault(difficulty);
                settings.chordRatio = 0f;   // 동시치기 직후에는 제한이 완화되므로 단일 노트만 검사한다
                List<NoteData> notes = Generate(onsets, settings, 5, seed);
                for (int i = 1; i < notes.Count; i++)
                    Assert.LessOrEqual(Math.Abs(notes[i].lane - notes[i - 1].lane), settings.maxLaneJump,
                        $"{difficulty} seed={seed} index={i}");
            }
        }

        [Test]
        public void Notes_RespectMinimumInterval()
        {
            List<OnsetData> onsets = DenseOnsets();
            foreach (Difficulty difficulty in AllDifficulties)
            {
                DifficultySettings settings = DifficultySettings.CreateDefault(difficulty);
                List<List<NoteData>> groups = Groups(Generate(onsets, settings));
                for (int i = 1; i < groups.Count; i++)
                    Assert.GreaterOrEqual(groups[i][0].time - groups[i - 1][0].time, settings.minNoteInterval - 1e-4f, $"{difficulty}");
            }
        }

        [Test]
        public void Notes_AreSnappedToTheChosenGrid()
        {
            List<OnsetData> onsets = DenseOnsets();
            foreach (Difficulty difficulty in AllDifficulties)
            {
                DifficultySettings settings = DifficultySettings.CreateDefault(difficulty);
                double step = 60.0 / Bpm / (int)settings.gridSnap;
                foreach (NoteData note in Generate(onsets, settings))
                {
                    double slots = (note.time - Offset) / step;
                    Assert.That(Math.Abs(slots - Math.Round(slots)), Is.LessThan(TimeTolerance), $"{difficulty} t={note.time}");
                }
            }
        }

        [Test]
        public void DefaultPresets_ProduceMoreNotesAtHigherDifficulty()
        {
            List<OnsetData> onsets = DenseOnsets();
            int previous = 0;
            foreach (Difficulty difficulty in AllDifficulties)
            {
                int count = Generate(onsets, DifficultySettings.CreateDefault(difficulty)).Count;
                Assert.Greater(count, previous, $"{difficulty} 는 이전 난이도보다 노트가 많아야 한다");
                previous = count;
            }
        }

        [Test]
        public void KeepTopPercent_KeepsTheStrongestOnsets()
        {
            List<OnsetData> onsets = SparseOnsets(40);
            DifficultySettings settings = Plain();
            settings.keepTopPercent = 50f;

            List<NoteData> notes = Generate(onsets, settings);

            Assert.AreEqual(20, notes.Count);
            // 성긴 onset 은 이미 그리드 위에 있으므로 스냅 후에도 원래 시각 그대로다.
            List<float> strongest = onsets.OrderByDescending(o => o.strength).Take(20).Select(o => o.time).OrderBy(t => t).ToList();
            for (int i = 0; i < strongest.Count; i++)
                Assert.AreEqual(strongest[i], notes[i].time, TimeTolerance);
        }

        [Test]
        public void SameGridSlot_KeepsOnlyTheStrongestOnset()
        {
            var onsets = new List<OnsetData>
            {
                new OnsetData { time = 1.00f, strength = 0.3f, band = OnsetBand.Low },
                new OnsetData { time = 1.02f, strength = 0.9f, band = OnsetBand.High },   // 같은 4분 칸
                new OnsetData { time = 3.00f, strength = 0.5f, band = OnsetBand.Mid },
            };
            DifficultySettings settings = Plain();
            settings.gridSnap = GridSnap.Quarter;

            List<NoteData> notes = Generate(onsets, settings);

            Assert.AreEqual(2, notes.Count);
        }

        [Test]
        public void HoldRatio_MakesHoldsThatEndBeforeTheNextNote()
        {
            List<OnsetData> onsets = SparseOnsets(40);
            DifficultySettings settings = Plain();
            settings.holdRatio = 0.5f;
            settings.holdMinBeats = 1f;
            settings.holdMaxBeats = 3f;
            settings.minNoteInterval = 0.2f;

            List<NoteData> notes = Generate(onsets, settings);

            var holds = notes.Where(n => n.type == NoteType.Hold).ToList();
            Assert.AreEqual(20, holds.Count);
            foreach (NoteData hold in holds)
            {
                Assert.GreaterOrEqual(hold.holdLength, 1f * 0.5f - 1e-4f);
                Assert.LessOrEqual(hold.holdLength, 3f * 0.5f + 1e-4f);
                NoteData next = notes.FirstOrDefault(n => n.time > hold.time + 1e-4f);
                float nextTime = next.time > 0f ? next.time : Duration;
                Assert.LessOrEqual(hold.time + hold.holdLength, nextTime - settings.minNoteInterval + 1e-3f);
            }
            foreach (NoteData tap in notes.Where(n => n.type == NoteType.Tap))
                Assert.AreEqual(0f, tap.holdLength);
        }

        [Test]
        public void ChordRatio_MakesTwoLaneGroupsOnTheStrongestNotes()
        {
            List<OnsetData> onsets = SparseOnsets(40);
            DifficultySettings settings = Plain();
            settings.chordRatio = 0.25f;

            List<NoteData> notes = Generate(onsets, settings);
            List<List<NoteData>> chords = Groups(notes).Where(g => g.Count == 2).ToList();

            Assert.AreEqual(10, chords.Count);
            Assert.AreEqual(50, notes.Count);
            foreach (List<NoteData> chord in chords)
                Assert.AreNotEqual(chord[0].lane, chord[1].lane);

            float weakestChordStrength = chords
                .Select(g => onsets.OrderBy(o => Math.Abs(o.time - g[0].time)).First().strength).Min();
            float strongestTapStrength = Groups(notes).Where(g => g.Count == 1)
                .Select(g => onsets.OrderBy(o => Math.Abs(o.time - g[0].time)).First().strength).Max();
            Assert.GreaterOrEqual(weakestChordStrength, strongestTapStrength);
        }

        [Test]
        public void Patterns_ProduceTrillsAndStairs()
        {
            List<OnsetData> onsets = DenseOnsets();

            DifficultySettings trill = Plain();
            trill.patternChance = 1f; trill.stairsWeight = 0f; trill.trillWeight = 1f;
            trill.patternMinLength = 6; trill.patternMaxLength = 6; trill.maxLaneJump = 2;
            List<NoteData> trillNotes = Generate(onsets, trill, 5);
            bool hasTrill = false;
            for (int i = 0; i + 3 < trillNotes.Count; i++)
                hasTrill |= trillNotes[i].lane == trillNotes[i + 2].lane && trillNotes[i + 1].lane == trillNotes[i + 3].lane;
            Assert.IsTrue(hasTrill, "트릴(A B A B)이 한 번도 나오지 않았다");

            DifficultySettings stairs = Plain();
            stairs.patternChance = 1f; stairs.stairsWeight = 1f; stairs.trillWeight = 0f;
            stairs.patternMinLength = 6; stairs.patternMaxLength = 6; stairs.maxLaneJump = 1;
            List<NoteData> stairNotes = Generate(onsets, stairs, 6);
            bool hasStairs = false;
            for (int i = 0; i + 2 < stairNotes.Count; i++)
            {
                int d1 = stairNotes[i + 1].lane - stairNotes[i].lane, d2 = stairNotes[i + 2].lane - stairNotes[i + 1].lane;
                hasStairs |= Math.Abs(d1) == 1 && d1 == d2;
            }
            Assert.IsTrue(hasStairs, "계단(같은 방향으로 한 칸씩 3노트)이 한 번도 나오지 않았다");
        }

        [Test]
        public void WithoutBpm_UsesOriginalTimesAndDoesNotThrow()
        {
            List<OnsetData> onsets = SparseOnsets(10);
            List<NoteData> notes = null;
            Assert.DoesNotThrow(() => notes = Generate(onsets, Plain(), bpm: 0f));
            CollectionAssert.AreEqual(onsets.Select(o => o.time), notes.Select(n => n.time));
        }

        [Test]
        public void OnsetsOutsideTheSong_AreIgnored()
        {
            var onsets = new List<OnsetData>
            {
                new OnsetData { time = -1f, strength = 1f },
                new OnsetData { time = 5f, strength = 0.5f },
                new OnsetData { time = Duration + 5f, strength = 1f },
                new OnsetData { time = float.NaN, strength = 1f },
            };
            Assert.AreEqual(1, Generate(onsets, Plain()).Count);
        }

        [Test]
        public void NoOnsets_GivesNoNotes()
        {
            Assert.AreEqual(0, Generate(new List<OnsetData>(), DifficultySettings.CreateDefault(Difficulty.Expert)).Count);
        }

        [Test]
        public void NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => ChartGenerator.Generate(null, Bpm, Offset, Duration, 4, Plain(), 1));
            Assert.Throws<ArgumentNullException>(() => ChartGenerator.Generate(new List<OnsetData>(), Bpm, Offset, Duration, 4, null, 1));
        }

        [Test]
        public void Output_IsSortedByTimeThenLane()
        {
            DifficultySettings settings = DifficultySettings.CreateDefault(Difficulty.Expert);
            List<NoteData> notes = Generate(DenseOnsets(), settings);
            for (int i = 1; i < notes.Count; i++)
            {
                bool ordered = notes[i].time > notes[i - 1].time
                    || (notes[i].time == notes[i - 1].time && notes[i].lane > notes[i - 1].lane);
                Assert.IsTrue(ordered, $"index {i}");
            }
        }
    }
}
