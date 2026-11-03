using System;

namespace SuminTools.RhythmChart
{
    /// <summary>
    /// 오디오(dsp) 시각을 곡 시각으로 바꾸는 시계. AudioSettings.dspTime 은 오디오 버퍼 단위(수십 ms)로만 갱신되므로
    /// 프레임마다 그대로 쓰면 노트가 덜덜 떨린다. dsp 값이 바뀌지 않은 동안은 실시간 흐름으로 이어 붙이되,
    /// 오디오와 어긋나지 않도록 <see cref="MaxExtrapolation"/> 까지만 앞서 간다. 순수 C# 이라 시각을 직접 넣어 테스트할 수 있다.
    /// </summary>
    public sealed class SongClock
    {
        /// <summary>dsp 값이 갱신되지 않아도 실시간으로 이어 붙이는 최대 시간(초).</summary>
        public const double MaxExtrapolation = 0.1;

        double dspSongZero;
        double lastDsp = double.NaN;
        double lastDspRealtime;

        /// <summary>곡 시각 0 에 해당하는 dsp 시각을 지정하고 시계를 초기화한다. 음수 시각(시작 전 대기)도 표현할 수 있다.</summary>
        public void Start(double dspTimeAtSongZero)
        {
            dspSongZero = dspTimeAtSongZero;
            lastDsp = double.NaN;
        }

        /// <summary>지금의 곡 시각(초). 매 프레임 한 번 호출한다.</summary>
        /// <param name="dspTime">AudioSettings.dspTime.</param>
        /// <param name="realtime">Time.realtimeSinceStartupAsDouble 처럼 끊김 없이 흐르는 실시간.</param>
        public double Sample(double dspTime, double realtime)
        {
            if (double.IsNaN(lastDsp) || dspTime != lastDsp)
            {
                lastDsp = dspTime;
                lastDspRealtime = realtime;
            }

            double extrapolated = Math.Min(Math.Max(0.0, realtime - lastDspRealtime), MaxExtrapolation);
            return lastDsp + extrapolated - dspSongZero;
        }

        /// <summary>
        /// <paramref name="eventRealtime"/> 에 일어난 입력이 곡 시각으로 언제였는지. 입력 이벤트는 프레임 중간에 생겼을 수 있어서
        /// 지금 시각에서 그 사이 흐른 시간을 빼 준다.
        /// </summary>
        public double SongTimeAt(double nowSongTime, double nowRealtime, double eventRealtime)
        {
            double age = nowRealtime - eventRealtime;
            return nowSongTime - Math.Min(Math.Max(0.0, age), MaxExtrapolation);
        }
    }
}
