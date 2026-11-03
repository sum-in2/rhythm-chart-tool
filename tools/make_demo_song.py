"""Basic Playback 샘플의 데모곡(SampleSong.ogg)을 합성하는 스크립트. 128BPM, 32마디(약 60초).

직접 만든 곡이라 라이선스 문제가 없다(저장소와 같은 MIT). 사용법:
    python tools/make_demo_song.py out.wav && ffmpeg -i out.wav -c:a libvorbis -q:a 4 SampleSong.ogg
필요한 패키지: numpy, soundfile
"""
import sys
import numpy as np
import soundfile as sf

SR = 44100
BPM = 128.0
BARS = 32
BEAT = 60.0 / BPM
rng = np.random.default_rng(2024)
n = int(SR * BEAT * 4 * BARS) + SR  # 1초 여유
mix = np.zeros(n, dtype=np.float64)


def add(t, sig, gain=1.0):
    i = int(t * SR)
    if i >= n:
        return
    j = min(n, i + len(sig))
    mix[i:j] += sig[: j - i] * gain


def tt(sec):
    return np.arange(int(sec * SR)) / SR


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = a * acc + (1 - a) * v
        y[i] = acc
    return y


def kick():
    t = tt(0.28)
    f = 45 + 95 * np.exp(-t * 28)
    phase = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(phase) * np.exp(-t * 11) * 1.0


def snare():
    t = tt(0.2)
    noise = rng.standard_normal(len(t))
    spec = np.fft.rfft(noise)
    fr = np.fft.rfftfreq(len(t), 1 / SR)
    spec[(fr < 400) | (fr > 6000)] = 0
    body = np.fft.irfft(spec, len(t))
    body = body / (np.max(np.abs(body)) + 1e-9)
    return (body * np.exp(-t * 22) * 0.55) + np.sin(2 * np.pi * 190 * t) * np.exp(-t * 30) * 0.4


def hat(open_=False):
    t = tt(0.16 if open_ else 0.05)
    noise = rng.standard_normal(len(t))
    spec = np.fft.rfft(noise)
    fr = np.fft.rfftfreq(len(t), 1 / SR)
    spec[fr < 7000] = 0
    x = np.fft.irfft(spec, len(t))
    x = x / (np.max(np.abs(x)) + 1e-9)
    return x * np.exp(-t * (18 if open_ else 70)) * 0.28


def saw_bass(freq, dur):
    t = tt(dur)
    saw = 2 * ((t * freq) % 1.0) - 1
    env = np.minimum(1, t * 200) * np.exp(-t * 2.2)
    return lowpass(saw, 420) * env * 1.4


def pluck(freq, dur=0.3):
    t = tt(dur)
    tone = np.sin(2 * np.pi * freq * t) + 0.35 * np.sin(2 * np.pi * freq * 2 * t) + 0.15 * np.sin(2 * np.pi * freq * 3 * t)
    return tone * np.exp(-t * 9) * 0.22


def pad(freqs, dur):
    t = tt(dur)
    x = sum(np.sin(2 * np.pi * f * t) for f in freqs) / len(freqs)
    env = np.minimum(1, t * 3) * np.minimum(1, (dur - t) * 3)
    return x * env * 0.16


# Am - F - C - G
CHORDS = [
    (55.00, [220.00, 261.63, 329.63]),
    (43.65, [174.61, 220.00, 261.63]),
    (65.41, [261.63, 329.63, 392.00]),
    (49.00, [196.00, 246.94, 293.66]),
]

for bar in range(BARS):
    t0 = bar * 4 * BEAT
    root, tones = CHORDS[bar % 4]
    has_snare = bar >= 4
    has_bass = bar >= 4
    has_lead = bar >= 8
    sixteenth_hats = bar >= 24

    if bar < 8:
        add(t0, pad(tones, 4 * BEAT))
    for b in range(4):
        tb = t0 + b * BEAT
        add(tb, kick(), 0.95)
        if has_snare and b in (1, 3):
            add(tb, snare(), 0.8)
        # 하이햇: 8분(오프비트가 더 큼), 후반부는 16분
        steps = 4 if sixteenth_hats else 2
        for s in range(steps):
            ts = tb + s * BEAT / steps
            add(ts, hat(open_=(s == steps // 2 and b == 3 and bar % 2 == 1)), 0.9 if s % 2 == 1 else 0.6)
        if has_bass:
            add(tb, saw_bass(root, BEAT * 0.9), 0.7)
            add(tb + BEAT / 2, saw_bass(root * (2 if b % 2 else 1), BEAT * 0.45), 0.55)
        if has_lead:
            arp = [tones[0], tones[1], tones[2], tones[1]]
            for s in range(4):
                add(tb + s * BEAT / 4, pluck(arp[(s + b) % 4] * 2), 1.0 if s == 0 else 0.75)

peak = np.max(np.abs(mix))
mix = mix / peak * 0.7
fade = int(SR * 1.5)
mix[-fade:] *= np.linspace(1, 0, fade)
mix = mix[: int(SR * (BEAT * 4 * BARS + 0.5))]
sf.write(sys.argv[1], mix.astype(np.float32), SR)
print("written", sys.argv[1], "duration %.2fs" % (len(mix) / SR))
