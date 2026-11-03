using System;
using System.Reflection;
using UnityEngine;

namespace SuminTools.RhythmChart.Editor
{
    /// <summary>
    /// 에디터에서 AudioClip 을 미리 듣는다. 공식 API 가 없어서 Unity 가 인스펙터 미리듣기에 쓰는 내부 클래스(UnityEditor.AudioUtil)를
    /// 리플렉션으로 호출한다. 내부 API 라 Unity 버전에 따라 없어질 수 있으므로 모든 호출을 이 클래스 안에 가두고,
    /// 메서드를 찾지 못하면 <see cref="IsAvailable"/> 이 false 가 되어 재생 기능만 꺼진다.
    /// </summary>
    internal static class AudioPreviewPlayer
    {
        static readonly MethodInfo Play;
        static readonly MethodInfo Stop;
        static readonly MethodInfo IsPlayingMethod;
        static readonly MethodInfo PositionMethod;

        static AudioPreviewPlayer()
        {
            Type util = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");
            if (util == null)
                return;

            const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Play = util.GetMethod("PlayPreviewClip", Flags, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
            Stop = util.GetMethod("StopAllPreviewClips", Flags, null, Type.EmptyTypes, null);
            IsPlayingMethod = util.GetMethod("IsPreviewClipPlaying", Flags, null, Type.EmptyTypes, null);
            PositionMethod = util.GetMethod("GetPreviewClipPosition", Flags, null, Type.EmptyTypes, null);
        }

        /// <summary>이 Unity 버전에서 미리듣기를 쓸 수 있는가.</summary>
        public static bool IsAvailable => Play != null && Stop != null && IsPlayingMethod != null && PositionMethod != null;

        public static bool IsPlaying => IsAvailable && (bool)IsPlayingMethod.Invoke(null, null);

        /// <summary>재생 위치(곡 시작 기준 초).</summary>
        public static double PositionSeconds => IsAvailable ? (float)PositionMethod.Invoke(null, null) : 0.0;

        /// <summary><paramref name="startSeconds"/> 부터 재생한다. 이미 재생 중이면 먼저 멈춘다.</summary>
        public static void PlayFrom(AudioClip clip, double startSeconds)
        {
            if (!IsAvailable || clip == null)
                return;
            Stop.Invoke(null, null);
            int startSample = Mathf.Clamp((int)(startSeconds * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
            Play.Invoke(null, new object[] { clip, startSample, false });
        }

        public static void StopAll()
        {
            if (IsAvailable)
                Stop.Invoke(null, null);
        }
    }
}
