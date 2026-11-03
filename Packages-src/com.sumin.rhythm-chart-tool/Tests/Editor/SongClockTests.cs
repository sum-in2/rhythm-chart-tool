using NUnit.Framework;

namespace SuminTools.RhythmChart.Tests
{
    public class SongClockTests
    {
        const double Tolerance = 1e-9;

        [Test]
        public void Sample_ReturnsDspTimeRelativeToSongZero()
        {
            var clock = new SongClock();
            clock.Start(100.0);

            Assert.AreEqual(5.0, clock.Sample(105.0, 1000.0), Tolerance);
        }

        [Test]
        public void SongTime_CanBeNegativeBeforeTheSongStarts()
        {
            var clock = new SongClock();
            clock.Start(100.0);

            Assert.AreEqual(-2.0, clock.Sample(98.0, 5.0), Tolerance);
        }

        [Test]
        public void WhileDspTimeStandsStill_TheClockAdvancesWithRealTime_UpToTheCap()
        {
            var clock = new SongClock();
            clock.Start(0.0);
            Assert.AreEqual(10.0, clock.Sample(10.0, 500.0), Tolerance);

            Assert.AreEqual(10.016, clock.Sample(10.0, 500.016), Tolerance, "dsp 가 그대로여도 실시간만큼 이어 붙는다");
            Assert.AreEqual(10.0 + SongClock.MaxExtrapolation, clock.Sample(10.0, 501.0), Tolerance, "오래 갱신이 없으면 상한에서 멈춘다");
        }

        [Test]
        public void WhenDspTimeAdvances_TheClockFollowsIt_WithoutGoingBackwards()
        {
            var clock = new SongClock();
            clock.Start(0.0);
            clock.Sample(10.0, 500.0);
            double extrapolated = clock.Sample(10.0, 500.03);        // 10.03 까지 이어 붙임

            double next = clock.Sample(10.043, 500.033);              // dsp 가 갱신됨(버퍼 단위로 점프)

            Assert.AreEqual(10.043, next, Tolerance);
            Assert.GreaterOrEqual(next, extrapolated, "시계가 뒤로 가면 노트가 떨린다");
        }

        [Test]
        public void SongTimeAt_BackdatesInputEvents_ButOnlyWithinTheCap()
        {
            var clock = new SongClock();
            clock.Start(0.0);

            Assert.AreEqual(9.98, clock.SongTimeAt(10.0, 500.0, 499.98), Tolerance);
            Assert.AreEqual(10.0, clock.SongTimeAt(10.0, 500.0, 500.5), Tolerance, "미래의 타임스탬프는 무시");
            Assert.AreEqual(10.0 - SongClock.MaxExtrapolation, clock.SongTimeAt(10.0, 500.0, 300.0), Tolerance, "너무 오래된 타임스탬프는 상한까지만");
        }

        [Test]
        public void Start_ResetsTheClock()
        {
            var clock = new SongClock();
            clock.Start(0.0);
            clock.Sample(50.0, 1.0);

            clock.Start(200.0);

            Assert.AreEqual(-150.0, clock.Sample(50.0, 1.0), Tolerance);
        }
    }
}
