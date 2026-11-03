using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SuminTools.RhythmChart.Tests
{
    public class ChartDataAndJsonTests
    {
        const string PackageName = "com.sumin.rhythm-chart-tool";
        const float FloatTolerance = 1e-4f;

        ChartData chart;

        [SetUp]
        public void SetUp()
        {
            chart = ScriptableObject.CreateInstance<ChartData>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(chart);
            ChartJsonMigrations.Unregister(0);
        }

        static List<OnsetData> Onsets()
        {
            var onsets = new List<OnsetData>();
            for (int i = 0; i < 200; i++)
                onsets.Add(new OnsetData { time = 0.5f + i * 0.25f, strength = 0.2f + (i * 37 % 80) / 100f, band = (OnsetBand)(i % 3) });
            return onsets;
        }

        void Analyze(ChartData target, float bpm = 120f)
        {
            target.ApplyAnalysis(null, 1, bpm, 0.1f, 60f, new[] { 0.1f, 0.6f, 1.1f }, Onsets());
        }

        [Test]
        public void NewChart_HasFourEmptyDifficultiesAndNoPresets()
        {
            Assert.IsFalse(chart.HasAnyNotes);
            for (int i = 0; i < ChartConstants.DifficultyCount; i++)
            {
                Assert.AreEqual(0, chart.GetNotes((Difficulty)i).Count);
                Assert.IsNull(chart.GetPreset((Difficulty)i));
            }
            Assert.AreEqual(ChartConstants.DefaultLaneCount, chart.LaneCount);
        }

        [Test]
        public void SetLaneCount_IsClamped()
        {
            chart.SetLaneCount(1);
            Assert.AreEqual(ChartConstants.MinLaneCount, chart.LaneCount);
            chart.SetLaneCount(50);
            Assert.AreEqual(ChartConstants.MaxLaneCount, chart.LaneCount);
        }

        [Test]
        public void GenerateAll_FillsEveryDifficulty_AndFollowsTheLaneCount()
        {
            Analyze(chart);
            chart.SetLaneCount(6);
            chart.GenerateAll();

            for (int i = 0; i < ChartConstants.DifficultyCount; i++)
            {
                IReadOnlyList<NoteData> notes = chart.GetNotes((Difficulty)i);
                Assert.Greater(notes.Count, 0, $"{(Difficulty)i}");
                foreach (NoteData note in notes)
                    Assert.Less(note.lane, 6);
            }
            Assert.IsTrue(chart.HasAnyNotes);
        }

        [Test]
        public void Generate_UsesTheAssignedPreset()
        {
            Analyze(chart);
            chart.Generate(Difficulty.Normal);
            int normalCount = chart.GetNotes(Difficulty.Normal).Count;

            var sparse = ScriptableObject.CreateInstance<DifficultyPreset>();
            DifficultySettings settings = DifficultySettings.CreateDefault(Difficulty.Normal);
            settings.keepTopPercent = 10f;
            sparse.Set(Difficulty.Normal, settings);
            chart.SetPreset(Difficulty.Normal, sparse);
            chart.Generate(Difficulty.Normal);

            Assert.Less(chart.GetNotes(Difficulty.Normal).Count, normalCount);
            UnityEngine.Object.DestroyImmediate(sparse);
        }

        [Test]
        public void ReAnalysis_KeepsExistingNotes()
        {
            Analyze(chart);
            chart.GenerateAll();
            var before = new List<NoteData>(chart.GetNotes(Difficulty.Hard));

            chart.ApplyAnalysis(null, 1, 90f, 0.2f, 61f, new float[0], new List<OnsetData>());

            CollectionAssert.AreEqual(before, new List<NoteData>(chart.GetNotes(Difficulty.Hard)));
            Assert.AreEqual(90f, chart.Bpm);
        }

        [Test]
        public void Json_RoundTripKeepsAnalysisSettingsAndNotes()
        {
            Analyze(chart);
            chart.SetLaneCount(5);
            chart.SetGenerationSeed(777);
            chart.GenerateAll();

            ChartData loaded = ChartJson.CreateFromJson(ChartJson.ToJson(chart));
            try
            {
                Assert.AreEqual(chart.Bpm, loaded.Bpm, FloatTolerance);
                Assert.AreEqual(chart.Offset, loaded.Offset, FloatTolerance);
                Assert.AreEqual(chart.Duration, loaded.Duration, FloatTolerance);
                Assert.AreEqual(5, loaded.LaneCount);
                Assert.AreEqual(777, loaded.GenerationSeed);
                Assert.AreEqual(chart.Beats.Count, loaded.Beats.Count);
                Assert.AreEqual(chart.Onsets.Count, loaded.Onsets.Count);
                for (int i = 0; i < chart.Onsets.Count; i++)
                {
                    Assert.AreEqual(chart.Onsets[i].time, loaded.Onsets[i].time, FloatTolerance);
                    Assert.AreEqual(chart.Onsets[i].band, loaded.Onsets[i].band);
                }
                for (int d = 0; d < ChartConstants.DifficultyCount; d++)
                {
                    IReadOnlyList<NoteData> a = chart.GetNotes((Difficulty)d), b = loaded.GetNotes((Difficulty)d);
                    Assert.AreEqual(a.Count, b.Count, $"{(Difficulty)d}");
                    for (int i = 0; i < a.Count; i++)
                    {
                        Assert.AreEqual(a[i].time, b[i].time, FloatTolerance);
                        Assert.AreEqual(a[i].lane, b[i].lane);
                        Assert.AreEqual(a[i].holdLength, b[i].holdLength, FloatTolerance);
                        Assert.AreEqual(a[i].type, b[i].type);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(loaded);
            }
        }

        [Test]
        public void Json_ContainsTheCurrentSchemaVersion()
        {
            Analyze(chart);
            StringAssert.Contains($"\"schemaVersion\": {ChartJson.CurrentSchemaVersion}", ChartJson.ToJson(chart));
        }

        [Test]
        public void Json_NewerSchema_IsRejected()
        {
            string json = ChartJson.ToJson(chart).Replace(
                $"\"schemaVersion\": {ChartJson.CurrentSchemaVersion}", $"\"schemaVersion\": {ChartJson.CurrentSchemaVersion + 1}");
            Assert.Throws<NotSupportedException>(() => ChartJson.FromJson(json, chart));
        }

        [Test]
        public void Json_OlderSchemaWithoutMigration_IsRejected()
        {
            Assert.Throws<NotSupportedException>(() => ChartJson.FromJson("{\"schemaVersion\":0}", chart));
        }

        [Test]
        public void Json_OlderSchema_GoesThroughTheMigrationHook()
        {
            Analyze(chart);
            string current = ChartJson.ToJson(chart, false);
            // 0 버전 파일을 흉내 낸다: schemaVersion 이 0 이고 'bpm' 대신 'tempo' 라는 키를 썼다고 가정한다.
            string legacy = current
                .Replace($"\"schemaVersion\":{ChartJson.CurrentSchemaVersion}", "\"schemaVersion\":0")
                .Replace("\"bpm\":", "\"tempo\":");
            int calls = 0;
            ChartJsonMigrations.Register(0, json =>
            {
                calls++;
                return json.Replace("\"schemaVersion\":0", $"\"schemaVersion\":{ChartJson.CurrentSchemaVersion}")
                           .Replace("\"tempo\":", "\"bpm\":");
            });

            ChartData loaded = ChartJson.CreateFromJson(legacy);
            try
            {
                Assert.AreEqual(1, calls);
                Assert.AreEqual(120f, loaded.Bpm, FloatTolerance);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(loaded);
            }
        }

        [Test]
        public void Json_BadMigrationResult_IsRejected()
        {
            ChartJsonMigrations.Register(0, json => json);   // schemaVersion 을 올리지 않는 잘못된 마이그레이션
            Assert.Throws<InvalidDataException>(() => ChartJson.FromJson("{\"schemaVersion\":0}", chart));
        }

        [Test]
        public void Json_InvalidContent_IsRejectedWithoutChangingTheChart()
        {
            Analyze(chart);
            chart.GenerateAll();
            int before = chart.GetNotes(Difficulty.Easy).Count;
            string good = ChartJson.ToJson(chart, false);

            string outOfRange = good.Replace("\"lane\":0", "\"lane\":9").Replace("\"lane\":1", "\"lane\":9");
            Assert.Throws<InvalidDataException>(() => ChartJson.FromJson(outOfRange, chart));

            string badType = good.Replace("\"type\":\"Tap\"", "\"type\":\"Bogus\"");
            Assert.Throws<InvalidDataException>(() => ChartJson.FromJson(badType, chart));

            Assert.Throws<InvalidDataException>(() => ChartJson.FromJson("this is not json", chart));

            Assert.AreEqual(before, chart.GetNotes(Difficulty.Easy).Count);
            Assert.AreEqual(120f, chart.Bpm);
        }

        [Test]
        public void PackagePresets_MatchTheCodeDefaults()
        {
            for (int i = 0; i < ChartConstants.DifficultyCount; i++)
            {
                var difficulty = (Difficulty)i;
                string path = $"Packages/{PackageName}/Runtime/Presets/{difficulty}.asset";
                var preset = AssetDatabase.LoadAssetAtPath<DifficultyPreset>(path);
                Assert.IsNotNull(preset, $"{path} 가 없다");
                Assert.AreEqual(difficulty, preset.Difficulty);
                Assert.IsTrue(preset.Settings.ValuesEqual(DifficultySettings.CreateDefault(difficulty)),
                    $"{difficulty} 프리셋 에셋이 코드 기본값과 다르다. DefaultPresets.Rebuild() 로 다시 만들어라.");
            }
        }
    }
}
