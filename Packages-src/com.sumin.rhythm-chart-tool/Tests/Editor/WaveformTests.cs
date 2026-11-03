using NUnit.Framework;
using SuminTools.RhythmChart.Editor;
using UnityEngine;

namespace SuminTools.RhythmChart.Tests
{
    public class WaveformTests
    {
        const int SampleRate = 8000;

        [Test]
        public void MinMax_FollowTheSamples()
        {
            var samples = new float[SampleRate];     // 1초 모노
            for (int i = 0; i < samples.Length; i++)
                samples[i] = i % 2 == 0 ? 0.8f : -0.4f;

            WaveformData data = WaveformData.FromSamples(samples, 1, SampleRate);
            var min = new float[4];
            var max = new float[4];
            data.GetColumns(0.0, 0.25, 4, min, max);

            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(-0.4f, min[i], 1e-5f);
                Assert.AreEqual(0.8f, max[i], 1e-5f);
            }
        }

        [Test]
        public void Stereo_UsesTheLoudestOfBothChannels()
        {
            var samples = new float[SampleRate * 2];
            for (int frame = 0; frame < SampleRate; frame++)
            {
                samples[frame * 2] = 0.2f;
                samples[frame * 2 + 1] = -0.7f;
            }

            WaveformData data = WaveformData.FromSamples(samples, 2, SampleRate);
            var min = new float[1];
            var max = new float[1];
            data.GetColumns(0.0, 1.0, 1, min, max);

            Assert.AreEqual(-0.7f, min[0], 1e-5f);
            Assert.AreEqual(0.2f, max[0], 1e-5f);
        }

        [Test]
        public void CoarseLevels_KeepTheLoudestSpike()
        {
            var samples = new float[SampleRate * 10];     // 10초, 대부분 조용하고 5초 지점에 한 프레임짜리 큰 소리
            samples[SampleRate * 5] = 1f;

            WaveformData data = WaveformData.FromSamples(samples, 1, SampleRate);
            Assert.Greater(data.LevelCount, 3, "여러 단계로 줄여 둔다");

            var min = new float[1];
            var max = new float[1];
            data.GetColumns(0.0, 10.0, 1, min, max);     // 화면 한 열이 곡 전체를 덮는 극단적인 축소
            Assert.AreEqual(1f, max[0], 1e-5f, "축소해도 가장 큰 소리는 사라지지 않는다");

            min = new float[4];
            max = new float[4];
            data.GetColumns(4.9, 0.05, 4, min, max);     // 4.90~5.10초를 열 4개로 확대
            Assert.AreEqual(0f, max[0], 1e-5f, "스파이크에서 먼 열은 조용하다");
            Assert.AreEqual(0f, max[3], 1e-5f);
            Assert.IsTrue(max[1] > 0.99f || max[2] > 0.99f, "확대하면 스파이크가 있는 쪽 열에 보인다");
        }

        [Test]
        public void ColumnsOutsideTheSong_AreZero()
        {
            WaveformData data = WaveformData.FromSamples(new float[SampleRate], 1, SampleRate);
            var min = new float[3] { 9f, 9f, 9f };
            var max = new float[3] { 9f, 9f, 9f };

            data.GetColumns(50.0, 0.1, 3, min, max);

            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, min);
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, max);
        }

        [Test]
        public void Duration_MatchesTheSampleCount()
        {
            WaveformData data = WaveformData.FromSamples(new float[SampleRate * 3 + 100], 1, SampleRate);
            Assert.AreEqual(3.0 + 100.0 / SampleRate, data.DurationSeconds, 1e-6);
            Assert.AreEqual(SampleRate, data.SampleRate);
        }

        [Test]
        public void Builder_ChunkedInputGivesTheSameResultAsOneShot()
        {
            var samples = new float[SampleRate * 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Sin(i * 0.05f) * (i % 1000) / 1000f;

            WaveformData oneShot = WaveformData.FromSamples(samples, 1, SampleRate);

            var builder = new WaveformData.Builder(1, SampleRate);
            int cursor = 0;
            foreach (int chunk in new[] { 77, 4096, 1, 5000, samples.Length })   // 64프레임 경계와 맞지 않는 조각들
            {
                int count = Mathf.Min(chunk, samples.Length - cursor);
                var part = new float[count];
                System.Array.Copy(samples, cursor, part, 0, count);
                builder.Add(part, count);
                cursor += count;
            }
            WaveformData chunked = builder.Finish();

            var minA = new float[20]; var maxA = new float[20]; var minB = new float[20]; var maxB = new float[20];
            oneShot.GetColumns(0.0, 0.1, 20, minA, maxA);
            chunked.GetColumns(0.0, 0.1, 20, minB, maxB);
            CollectionAssert.AreEqual(minA, minB);
            CollectionAssert.AreEqual(maxA, maxB);
        }

        [Test]
        public void Cache_BuildsFromARealAudioClip_AndReusesIt()
        {
            AudioClip clip = AudioClip.Create("waveform-test", SampleRate, 1, SampleRate, false);
            var samples = new float[SampleRate];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.5f;
            clip.SetData(samples, 0);

            try
            {
                WaveformData first = WaveformCache.Get(clip, out string error);
                Assert.IsNotNull(first, error);

                var min = new float[1];
                var max = new float[1];
                first.GetColumns(0.0, 1.0, 1, min, max);
                Assert.AreEqual(0.5f, max[0], 1e-4f);

                Assert.AreSame(first, WaveformCache.Get(clip, out _), "같은 클립은 다시 만들지 않는다");
            }
            finally
            {
                WaveformCache.Clear();
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void Cache_ExplainsWhyItCannotBuild()
        {
            Assert.IsNull(WaveformCache.Get(null, out string error));
            Assert.IsNotEmpty(error);
        }
    }
}
