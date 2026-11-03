#!/usr/bin/env python3
"""오디오 파일을 분석해 BPM, 박자(beats), onset(시점/강도/대역)을 JSON으로 출력한다.

사용법:
    python analyze.py <오디오 경로> <출력 JSON 경로> [--separate]

stdout: 진행 상황을 ``PROGRESS <0.0~1.0>`` 형식의 한 줄로 출력한다(Unity에서 파싱).
stderr: 실패 메시지. 실패 시 종료 코드 1을 반환한다(인자 오류 포함).
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

SCHEMA_VERSION = 1

# --- 분석 상수 ---------------------------------------------------------------
SAMPLE_RATE = 22050   # 분석용 리샘플 주파수(Hz)
HOP_LENGTH = 512      # onset/STFT 프레임 간격(샘플)
N_FFT = 2048

# 주파수 대역 경계(Hz): low < LOW_MAX_HZ <= mid < MID_MAX_HZ <= high
LOW_MAX_HZ = 200.0
MID_MAX_HZ = 2000.0
BANDS = ("low", "mid", "high")

# onset 검출 민감도. 난이도별 선별은 strength 로 하므로 후보는 넉넉히 뽑는다.
# 실제 곡 1개로 측정: 기본값 0.07 은 4분음표보다 성기게(1.4개/초) 뽑았고, 0.03 까지 추가된 onset 은
# BPM 그리드에 우연보다 훨씬 잘 맞았으나(8분 그리드 69% vs 우연 30%) 0.02 부터는 우연 수준에 가까워졌다.
ONSET_DELTA = 0.03
STRENGTH_NORMALIZE_PERCENTILE = 95.0  # onset strength 를 이 백분위수 = 1.0 으로 정규화(상위는 1.0 으로 자름)

# onset envelope 피크가 실제 타격보다 늦는 프레임 수(합성 드럼 음원으로 측정: 약 +22ms ≈ 1프레임)
ONSET_LATENCY_FRAMES = 1

# 고정 BPM 그리드 정밀화: 세밀한 envelope 위에서 시드 BPM 주변을 훑어 그리드 점수가 최대인 (bpm, 위상)을 찾는다.
GRID_HOP_LENGTH = 128        # 그리드 탐색용 envelope 프레임 간격(샘플), 약 5.8ms
GRID_SEARCH_RATIO = 0.03     # 시드 BPM 의 ±3% 범위를 탐색
GRID_BPM_STEP = 0.01
GRID_LATENCY_SEC = 0.012      # 세밀한 envelope 피크가 실제 타격보다 늦는 시간(초)

# 보정된 onset 프레임 주변에서 대역 활성도를 볼 범위(프레임).
# STFT 창(2048샘플)이 길어서 타격이 창에 걸리기 시작하는 프레임은 타격 시각보다 앞선다.
BAND_WINDOW_BEFORE = 2
BAND_WINDOW_AFTER = 1
BAND_NORMALIZE_PERCENTILE = 99.0  # 대역별 활성도를 이 백분위수로 정규화(대역 간 비교용)
BAND_LOG_COMPRESSION = 100.0      # log1p(C * 진폭) 압축 계수
EPSILON = 1e-9

TIME_DECIMALS = 4
STRENGTH_DECIMALS = 3
BPM_DECIMALS = 2

# demucs 드럼 분리
DEMUCS_STEM = "drums"


class AnalysisError(Exception):
    """사용자에게 그대로 보여줄 수 있는 분석 실패."""


class _Parser(argparse.ArgumentParser):
    """인자 오류도 종료 코드 1로 통일한다(argparse 기본은 2)."""

    def error(self, message):
        self.print_usage(sys.stderr)
        sys.stderr.write(f"error: {message}\n")
        sys.exit(1)


def progress(value: float) -> None:
    print(f"PROGRESS {min(max(value, 0.0), 1.0):.2f}", flush=True)


def configure_streams() -> None:
    """Windows 기본 코드페이지(cp949 등)로 한글 경로/메시지가 깨지지 않게 UTF-8로 고정한다."""
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")


def separate_drums(audio_path: Path, work_dir: Path) -> Path:
    """demucs로 드럼 스템을 분리하고 그 파일 경로를 반환한다."""
    try:
        import demucs  # noqa: F401
    except ImportError as e:
        raise AnalysisError(
            "--separate 에는 demucs 가 필요합니다. "
            "requirements-separate.txt 를 설치하세요."
        ) from e

    cmd = [
        sys.executable, "-m", "demucs.separate",
        f"--two-stems={DEMUCS_STEM}",
        "-o", str(work_dir),
        str(audio_path),
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise AnalysisError(f"demucs 실행 실패:\n{result.stderr.strip()}")

    stems = sorted(work_dir.rglob(f"{DEMUCS_STEM}.*"))
    if not stems:
        raise AnalysisError("demucs 출력에서 drums 스템을 찾지 못했습니다.")
    return stems[0]


def latency_seconds() -> float:
    return ONSET_LATENCY_FRAMES * HOP_LENGTH / SAMPLE_RATE


def fit_beat_grid(beat_times, np):
    """박자 시점에 직선을 맞춰 시드 BPM 을 구한다. 박자가 2개 미만이면 None.

    beat_track 의 박자는 envelope 프레임(약 23ms)에 양자화되고 위상이 흔들려서,
    이 값을 그대로 고정 그리드로 쓰면 곡 후반에 크게 어긋난다. 시드로만 쓰고 refine_grid 로 다듬는다.
    """
    if len(beat_times) < 2:
        return None
    period = np.polyfit(np.arange(len(beat_times)), beat_times, 1)[0]
    return 60.0 / period if period > 0 else None


def refine_grid(y, sr, seed_bpm, librosa, np):
    """시드 BPM 주변에서 onset envelope 에 가장 잘 맞는 고정 그리드의 (bpm, offset) 을 찾는다.

    offset 은 0 이상 한 박 길이 미만이다.
    """
    env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=GRID_HOP_LENGTH)
    fps = sr / GRID_HOP_LENGTH
    best_score, best_bpm, best_phase = -1.0, seed_bpm, 0.0
    for bpm in np.arange(seed_bpm * (1 - GRID_SEARCH_RATIO), seed_bpm * (1 + GRID_SEARCH_RATIO), GRID_BPM_STEP):
        period = 60.0 / bpm * fps  # 프레임 단위(실수)
        phases = np.arange(int(period))
        beat_count = int(len(env) / period)
        if beat_count < 2:
            continue
        idx = np.round(phases[:, None] + np.arange(beat_count)[None, :] * period).astype(int)
        scores = env[np.minimum(idx, len(env) - 1)].mean(axis=1)
        best_idx = int(np.argmax(scores))
        if scores[best_idx] > best_score:
            best_score, best_bpm, best_phase = float(scores[best_idx]), float(bpm), float(phases[best_idx])
    period_sec = 60.0 / best_bpm
    return best_bpm, (best_phase / fps - GRID_LATENCY_SEC) % period_sec


def detect_beats(y, sr, librosa, np):
    """(bpm, offset, beat_times) 반환. 박자를 못 찾으면 beat_times 는 빈 배열, offset 은 0.

    bpm/offset 은 고정 그리드(refine_grid)이고, beat_times 는 beat_track 이 찾은 실제 박자 시점이다.
    """
    env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=HOP_LENGTH)
    tempo, beat_frames = librosa.beat.beat_track(onset_envelope=env, sr=sr, hop_length=HOP_LENGTH)
    beat_times = np.maximum(
        librosa.frames_to_time(beat_frames, sr=sr, hop_length=HOP_LENGTH) - latency_seconds(), 0.0)
    seed_bpm = fit_beat_grid(beat_times, np) or float(np.atleast_1d(tempo)[0])
    if seed_bpm <= 0:
        return 0.0, 0.0, beat_times
    bpm, offset = refine_grid(y, sr, seed_bpm, librosa, np)
    return bpm, offset, beat_times


def detect_onsets(y, sr, librosa, np):
    """(onset_frames, onset_times, strengths) 반환. strength 는 0~1 로 정규화한 onset envelope 값.

    envelope 의 피크는 실제 타격보다 ONSET_LATENCY_FRAMES 만큼 늦으므로 프레임/시각 모두 그만큼 당긴다.
    """
    env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=HOP_LENGTH)
    peaks = librosa.onset.onset_detect(
        onset_envelope=env, sr=sr, hop_length=HOP_LENGTH, delta=ONSET_DELTA, backtrack=False)
    peak_values = env[peaks]
    # 최댓값으로 나누면 곡 시작의 과도음 하나가 나머지 강도를 전부 눌러버린다. 상위 백분위수로 정규화한다.
    scale = float(np.percentile(peak_values, STRENGTH_NORMALIZE_PERCENTILE)) if peaks.size else 0.0
    strengths = peak_values / scale if scale > EPSILON else np.zeros(len(peaks))
    frames = np.maximum(peaks - ONSET_LATENCY_FRAMES, 0)
    times = librosa.frames_to_time(frames, sr=sr, hop_length=HOP_LENGTH)
    return frames, times, np.clip(strengths, 0.0, 1.0)


def classify_bands(y, sr, onset_frames, librosa, np):
    """각 onset 이 저/중/고 중 어느 대역에서 가장 두드러지는지 판별한다.

    대역 평균 스펙트럼 flux(로그 압축 진폭의 양의 변화량)를 대역별 백분위수로 정규화한 뒤,
    onset 프레임 주변에서 가장 큰 대역을 고른다. 정규화하지 않으면 저역 에너지가 항상 이긴다.
    """
    if len(onset_frames) == 0:
        return []

    spec = np.abs(librosa.stft(y, n_fft=N_FFT, hop_length=HOP_LENGTH))
    freqs = librosa.fft_frequencies(sr=sr, n_fft=N_FFT)
    log_spec = np.log1p(BAND_LOG_COMPRESSION * spec)
    flux = np.maximum(0.0, np.diff(log_spec, axis=1, prepend=log_spec[:, :1]))

    masks = (
        freqs < LOW_MAX_HZ,
        (freqs >= LOW_MAX_HZ) & (freqs < MID_MAX_HZ),
        freqs >= MID_MAX_HZ,
    )
    band_flux = np.stack([flux[m].mean(axis=0) for m in masks])
    scale = np.maximum(np.percentile(band_flux, BAND_NORMALIZE_PERCENTILE, axis=1, keepdims=True), EPSILON)
    normalized = band_flux / scale

    frame_count = normalized.shape[1]
    bands = []
    for frame in onset_frames:
        lo = max(0, int(frame) - BAND_WINDOW_BEFORE)
        hi = min(frame_count, int(frame) + BAND_WINDOW_AFTER + 1)
        bands.append(BANDS[int(np.argmax(normalized[:, lo:hi].max(axis=1)))])
    return bands


def analyze(audio_path: Path, separate: bool) -> dict:
    progress(0.0)
    try:
        import librosa
        import numpy as np
    except ImportError as e:
        raise AnalysisError(
            f"필수 패키지를 불러오지 못했습니다({e}). Python 환경 설정을 다시 실행하세요."
        ) from e

    with tempfile.TemporaryDirectory(prefix="rhythmchart_") as tmp:
        onset_source = audio_path
        if separate:
            progress(0.05)
            onset_source = separate_drums(audio_path, Path(tmp))
        progress(0.2)

        try:
            y, sr = librosa.load(str(audio_path), sr=SAMPLE_RATE, mono=True)
            y_onset = y if onset_source == audio_path else librosa.load(
                str(onset_source), sr=SAMPLE_RATE, mono=True)[0]
        except Exception as e:  # 디코딩 실패(손상/미지원 포맷)
            raise AnalysisError(f"오디오를 읽지 못했습니다: {audio_path} ({e})") from e
    if y.size == 0:
        raise AnalysisError(f"오디오가 비어 있습니다: {audio_path}")
    progress(0.4)

    duration = float(librosa.get_duration(y=y, sr=sr))
    bpm, offset, beat_times = detect_beats(y, sr, librosa, np)
    progress(0.6)

    onset_frames, onset_times, strengths = detect_onsets(y_onset, sr, librosa, np)
    progress(0.75)

    bands = classify_bands(y_onset, sr, onset_frames, librosa, np)
    progress(0.95)

    return {
        "schemaVersion": SCHEMA_VERSION,
        "bpm": round(bpm, BPM_DECIMALS),
        "offset": round(offset, TIME_DECIMALS),
        "duration": round(duration, TIME_DECIMALS),
        "beats": [round(float(t), TIME_DECIMALS) for t in beat_times],
        "onsets": [
            {"time": round(float(t), TIME_DECIMALS), "strength": round(float(s), STRENGTH_DECIMALS), "band": b}
            for t, s, b in zip(onset_times, strengths, bands)
        ],
    }


def write_json(data: dict, out_path: Path) -> None:
    """임시 파일에 쓴 뒤 교체한다. 중간에 취소돼도 깨진 JSON 이 남지 않는다."""
    out_path.parent.mkdir(parents=True, exist_ok=True)
    tmp_path = out_path.with_name(out_path.name + ".tmp")
    with open(tmp_path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    os.replace(tmp_path, out_path)


def main(argv=None) -> int:
    configure_streams()
    parser = _Parser(description="오디오를 분석해 BPM/beats/onsets JSON을 생성한다.")
    parser.add_argument("audio", type=Path, help="입력 오디오(mp3 등)")
    parser.add_argument("output", type=Path, help="출력 JSON 경로")
    parser.add_argument("--separate", action="store_true", help="demucs로 드럼을 분리해 onset 을 검출(기본 꺼짐)")
    args = parser.parse_args(argv)

    try:
        if not args.audio.is_file():
            raise AnalysisError(f"입력 파일이 없습니다: {args.audio}")
        data = analyze(args.audio, args.separate)
        write_json(data, args.output)
        progress(1.0)
    except AnalysisError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 1
    except Exception as e:  # 예상 못한 실패도 종료 코드 1 로 통일
        print(f"ERROR: 분석 중 예기치 못한 오류: {type(e).__name__}: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
