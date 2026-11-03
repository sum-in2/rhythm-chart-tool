using System.Collections.Generic;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>AudioClip 마다 파형 데이터를 한 번만 만들어 둔다. 도메인 리로드 때 비워진다.</summary>
    internal static class WaveformCache
    {
        const int ChunkFrames = 1 << 16;

        static readonly Dictionary<int, WaveformData> Cache = new Dictionary<int, WaveformData>();

        /// <summary>
        /// 파형 데이터를 돌려준다. 만들 수 없으면(클립이 없거나 GetData 를 쓸 수 없는 로드 방식) null 과 사유를 돌려준다.
        /// </summary>
        public static WaveformData Get(AudioClip clip, out string error)
        {
            error = null;
            if (clip == null)
            {
                error = "오디오 클립이 없습니다.";
                return null;
            }

            int id = clip.GetInstanceID();
            if (Cache.TryGetValue(id, out WaveformData cached))
                return cached;

            WaveformData data = Build(clip, out error);
            if (data != null)
                Cache[id] = data;
            return data;
        }

        /// <summary>캐시를 비운다(오디오를 다시 임포트했을 때 등).</summary>
        public static void Clear()
        {
            Cache.Clear();
        }

        static WaveformData Build(AudioClip clip, out string error)
        {
            error = null;
            if (!clip.preloadAudioData || clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();

            int channels = clip.channels;
            int totalFrames = clip.samples;
            var builder = new WaveformData.Builder(channels, clip.frequency);
            var buffer = new float[ChunkFrames * channels];

            for (int offset = 0; offset < totalFrames; offset += ChunkFrames)
            {
                int frames = Mathf.Min(ChunkFrames, totalFrames - offset);
                if (frames != ChunkFrames)
                    buffer = new float[frames * channels];

                if (!clip.GetData(buffer, offset))
                {
                    error = "파형을 읽을 수 없습니다. 오디오 임포트 설정의 Load Type 을 'Decompress On Load' 로 바꾸세요.";
                    return null;
                }
                builder.Add(buffer, frames);
            }
            return builder.Finish();
        }
    }
}
