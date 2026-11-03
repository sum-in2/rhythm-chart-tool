using System;
using System.Collections.Generic;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 파형을 그리기 위한 최솟값/최댓값 피라미드. 가장 촘촘한 단계(64프레임마다 한 쌍)에서 시작해
    /// 4배씩 줄인 단계를 겹쳐 두므로, 곡을 얼마나 확대하든 한 화면 폭에 필요한 만큼만 읽는다.
    /// </summary>
    internal sealed class WaveformData
    {
        public const int BaseWindowFrames = 64;
        public const int LevelFactor = 4;
        const int MinBucketsInTopLevel = 16;

        readonly List<float[]> mins;
        readonly List<float[]> maxs;

        WaveformData(int sampleRate, double durationSeconds, List<float[]> mins, List<float[]> maxs)
        {
            SampleRate = sampleRate;
            DurationSeconds = durationSeconds;
            this.mins = mins;
            this.maxs = maxs;
        }

        public int SampleRate { get; }

        public double DurationSeconds { get; }

        public int LevelCount => mins.Count;

        /// <summary>그 단계에서 한 칸이 덮는 프레임 수.</summary>
        public static long WindowFrames(int level)
        {
            long frames = BaseWindowFrames;
            for (int i = 0; i < level; i++)
                frames *= LevelFactor;
            return frames;
        }

        public int BucketCount(int level) => mins[level].Length;

        public float Min(int level, int bucket) => mins[level][bucket];

        public float Max(int level, int bucket) => maxs[level][bucket];

        /// <summary>인터리브된 샘플 배열 전체로 한 번에 만든다(작은 데이터와 테스트용).</summary>
        public static WaveformData FromSamples(float[] samples, int channels, int sampleRate)
        {
            var builder = new Builder(channels, sampleRate);
            builder.Add(samples, samples.Length / channels);
            return builder.Finish();
        }

        /// <summary>
        /// 화면의 각 열(픽셀)에 해당하는 최솟값/최댓값을 채운다. 열 하나가 덮는 시간(<paramref name="secondsPerColumn"/>)보다
        /// 한 칸이 짧은 단계 중 가장 거친 것을 골라 읽는 양을 줄인다. 곡 밖의 열은 0 이다.
        /// </summary>
        public void GetColumns(double startSeconds, double secondsPerColumn, int columns, float[] minOut, float[] maxOut)
        {
            int level = 0;
            while (level + 1 < LevelCount && WindowFrames(level + 1) / (double)SampleRate <= secondsPerColumn)
                level++;

            double framesPerBucket = WindowFrames(level);
            float[] levelMin = mins[level], levelMax = maxs[level];
            for (int i = 0; i < columns; i++)
            {
                double first = (startSeconds + i * secondsPerColumn) * SampleRate / framesPerBucket;
                double last = (startSeconds + (i + 1) * secondsPerColumn) * SampleRate / framesPerBucket;
                int from = (int)Math.Floor(first);
                int to = Math.Max(from + 1, (int)Math.Ceiling(last));

                float min = 0f, max = 0f;
                bool any = false;
                for (int b = Math.Max(0, from); b < Math.Min(to, levelMin.Length); b++)
                {
                    min = any ? Math.Min(min, levelMin[b]) : levelMin[b];
                    max = any ? Math.Max(max, levelMax[b]) : levelMax[b];
                    any = true;
                }
                minOut[i] = min;
                maxOut[i] = max;
            }
        }

        /// <summary>샘플을 조각 단위로 받아 피라미드를 만든다. 곡 전체를 한 배열로 들고 있지 않아도 된다.</summary>
        public sealed class Builder
        {
            readonly int channels;
            readonly int sampleRate;
            readonly List<float> baseMin = new List<float>();
            readonly List<float> baseMax = new List<float>();
            long totalFrames;
            int framesInBucket;
            float bucketMin = float.MaxValue, bucketMax = float.MinValue;

            public Builder(int channels, int sampleRate)
            {
                if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
                this.channels = channels;
                this.sampleRate = sampleRate;
            }

            /// <summary>인터리브된 샘플 <paramref name="frameCount"/> 프레임을 추가한다.</summary>
            public void Add(float[] samples, int frameCount)
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    int baseIndex = frame * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        float value = samples[baseIndex + c];
                        if (value < bucketMin) bucketMin = value;
                        if (value > bucketMax) bucketMax = value;
                    }

                    if (++framesInBucket == BaseWindowFrames)
                        FlushBucket();
                }
                totalFrames += frameCount;
            }

            public WaveformData Finish()
            {
                if (framesInBucket > 0)
                    FlushBucket();

                var mins = new List<float[]> { baseMin.ToArray() };
                var maxs = new List<float[]> { baseMax.ToArray() };
                while (mins[mins.Count - 1].Length > MinBucketsInTopLevel)
                {
                    float[] previousMin = mins[mins.Count - 1], previousMax = maxs[maxs.Count - 1];
                    int count = (previousMin.Length + LevelFactor - 1) / LevelFactor;
                    var nextMin = new float[count];
                    var nextMax = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        float min = float.MaxValue, max = float.MinValue;
                        for (int k = i * LevelFactor; k < Math.Min(previousMin.Length, (i + 1) * LevelFactor); k++)
                        {
                            if (previousMin[k] < min) min = previousMin[k];
                            if (previousMax[k] > max) max = previousMax[k];
                        }
                        nextMin[i] = min;
                        nextMax[i] = max;
                    }
                    mins.Add(nextMin);
                    maxs.Add(nextMax);
                }
                return new WaveformData(sampleRate, totalFrames / (double)sampleRate, mins, maxs);
            }

            void FlushBucket()
            {
                baseMin.Add(bucketMin);
                baseMax.Add(bucketMax);
                framesInBucket = 0;
                bucketMin = float.MaxValue;
                bucketMax = float.MinValue;
            }
        }
    }
}
