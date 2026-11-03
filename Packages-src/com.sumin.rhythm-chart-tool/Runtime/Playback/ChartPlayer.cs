using System;
using UnityEngine;

namespace SuminTools.RhythmChart
{
    /// <summary>
    /// 차트를 오디오와 함께 재생하며 판정한다. 곡 시각은 <c>AudioSettings.dspTime</c> 기준이라 프레임 간격이 흔들려도 노트가 오디오에서 벗어나지 않는다.
    /// 입력은 직접 받지 않는다. 게임의 입력 시스템에서 <see cref="Press(int)"/> / <see cref="Release(int)"/> 를 호출하면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Rhythm Chart/Chart Player")]
    public class ChartPlayer : MonoBehaviour
    {
        /// <summary>노래가 끝난 뒤(또는 마지막 노트 뒤) 결과를 보여 주기 전에 기다리는 시간(초).</summary>
        const double EndPaddingSeconds = 1.0;

        [SerializeField] ChartData chart;
        [SerializeField] Difficulty difficulty = Difficulty.Normal;
        [SerializeField] JudgementSettings judgement = new JudgementSettings();

        [Tooltip("재생을 시작하기 전에 노트가 화면에 나타날 시간을 주는 대기(초). 오디오는 이 시간 뒤에 시작한다.")]
        [SerializeField, Min(0f)] float leadInSeconds = 2f;

        [Tooltip("비워 두면 이 오브젝트에 AudioSource 를 만든다.")]
        [SerializeField] AudioSource audioSource;

        readonly SongClock clock = new SongClock();
        double endTime;

        /// <summary>재생할 차트.</summary>
        public ChartData Chart
        {
            get => chart;
            set => chart = value;
        }

        /// <summary>재생할 난이도.</summary>
        public Difficulty Difficulty
        {
            get => difficulty;
            set => difficulty = value;
        }

        /// <summary>판정 창과 지연 보정.</summary>
        public JudgementSettings JudgementWindows => judgement;

        /// <summary>시작 전 대기 시간(초).</summary>
        public float LeadInSeconds
        {
            get => leadInSeconds;
            set => leadInSeconds = Mathf.Max(0f, value);
        }

        /// <summary>이번 플레이의 판정 상태. <see cref="Play"/> 를 부르기 전에는 null.</summary>
        public ChartRun Run { get; private set; }

        public bool IsPlaying { get; private set; }

        /// <summary>곡이 끝나 결과가 확정되었는가.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>지금의 곡 시각(초). 시작 위치보다 앞선 대기 동안에는 시작 위치 − 대기 시간부터 흐른다.</summary>
        public double SongTime { get; private set; }

        /// <summary>이번 플레이를 시작한 곡 위치(초).</summary>
        public double StartSeconds { get; private set; }

        /// <summary>노트 하나가 최종 판정될 때마다.</summary>
        public event Action<NoteJudgement> Judged;

        /// <summary>롱노트의 머리를 쳤을 때.</summary>
        public event Action<int, Judgement> HoldStarted;

        /// <summary>곡이 끝났을 때.</summary>
        public event Action Finished;

        /// <summary>
        /// 처음부터 또는 <paramref name="startSeconds"/> 부터 재생한다. 그 위치보다 앞선 노트는 판정에서 빠진다.
        /// 이미 재생 중이면 멈추고 새로 시작한다.
        /// </summary>
        public void Play(double startSeconds = 0.0)
        {
            if (chart == null)
                throw new InvalidOperationException("ChartPlayer 에 차트(ChartData)가 지정되지 않았습니다.");
            Stop();

            startSeconds = Math.Max(0.0, startSeconds);
            StartSeconds = startSeconds;
            Run = new ChartRun(chart.GetNotes(difficulty), chart.LaneCount, judgement, startSeconds);
            Run.Judged += result => Judged?.Invoke(result);
            Run.HoldStarted += (index, head) => HoldStarted?.Invoke(index, head);

            // 오디오를 지금이 아니라 약간 뒤 dsp 시각에 예약해야 오디오 스레드가 준비할 시간이 생긴다.
            double dspStart = AudioSettings.dspTime + Math.Max(0.0, leadInSeconds) + 0.05;
            clock.Start(dspStart - startSeconds);
            if (chart.AudioClip != null)
            {
                AudioSource source = EnsureAudioSource();
                source.clip = chart.AudioClip;
                source.time = (float)Math.Min(startSeconds, Math.Max(0f, chart.AudioClip.length - 0.01f));
                source.PlayScheduled(dspStart);
            }

            endTime = Math.Max(chart.Duration, LastNoteEnd()) + EndPaddingSeconds;
            SongTime = clock.Sample(AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble);
            IsFinished = false;
            IsPlaying = true;
        }

        /// <summary>재생을 멈춘다. 판정 상태는 그대로 남는다.</summary>
        public void Stop()
        {
            if (audioSource != null)
                audioSource.Stop();
            IsPlaying = false;
        }

        /// <summary>그 레인을 눌렀다(지금 이 순간). 판정 결과 또는 무시되면 null.</summary>
        public Judgement? Press(int lane)
        {
            return IsPlaying ? Run.Press(lane, SongTime) : null;
        }

        /// <summary>
        /// 그 레인을 눌렀다. 입력 장치가 알려 주는 이벤트 시각(<c>Time.realtimeSinceStartupAsDouble</c> 기준)이 있으면
        /// 프레임 중간에 눌린 시각까지 반영해 더 정확히 판정한다.
        /// </summary>
        public Judgement? Press(int lane, double eventRealtime)
        {
            return IsPlaying ? Run.Press(lane, SongTimeAt(eventRealtime)) : null;
        }

        /// <summary>그 레인을 놓았다(지금 이 순간).</summary>
        public Judgement? Release(int lane)
        {
            return IsPlaying ? Run.Release(lane, SongTime) : null;
        }

        /// <summary>그 레인을 놓았다. <paramref name="eventRealtime"/> 은 <see cref="Press(int, double)"/> 와 같다.</summary>
        public Judgement? Release(int lane, double eventRealtime)
        {
            return IsPlaying ? Run.Release(lane, SongTimeAt(eventRealtime)) : null;
        }

        void Update()
        {
            if (!IsPlaying)
                return;

            SongTime = clock.Sample(AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble);
            Run.Advance(SongTime);

            if (SongTime >= endTime)
            {
                IsPlaying = false;
                IsFinished = true;
                Finished?.Invoke();
            }
        }

        void OnDisable()
        {
            Stop();
        }

        double SongTimeAt(double eventRealtime)
        {
            return clock.SongTimeAt(SongTime, Time.realtimeSinceStartupAsDouble, eventRealtime);
        }

        double LastNoteEnd()
        {
            double last = 0.0;
            foreach (NoteData note in chart.GetNotes(difficulty))
                last = Math.Max(last, note.time + note.holdLength);
            return last;
        }

        AudioSource EnsureAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                    audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            return audioSource;
        }
    }
}
