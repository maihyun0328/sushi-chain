"""Golden data for the C# port of app/audio/analysis.py (AudioAnalyzer.Analyze).

    cd ai-chart-server && .venv/bin/python <worktree>/Tests/LocalChart/tools/make_analysis_golden.py

Writes Tests/LocalChart/golden/analysis_<case>.lcg (format: golden_io.py) with
  * the exact input PCM (float32, mono, 22050 Hz),
  * every intermediate of analyze_audio (full per-frame arrays, slices + sha256 of the big
    (bins, frames) spectrograms), recomputed step by step with the module's own helpers and
    asserted bit-identical to analyze_audio(),
  * the final AudioFeatures and generate_chart() output for easy/normal/hard.
Also writes analysis_constants.lcg (mel / chroma filterbanks, STFT window).

Cases
  southside30  30 s excerpt (from 45 s) of the SOUTHSIDE FREESTYLE mp3, decoded with decode_mono
  synth128     app.audio.synth.synth_track(128 BPM, 8 s, seed 7, pads)
  synth93c     synth_track(93 BPM, 10 s, seed 3, no pads) analysed with CHUNK_FRAMES = 300 so the
               multi-chunk HPSS / tempogram path runs (the C# test passes the same chunk size)

`--full-song` additionally writes a whole-song golden (not committed; ~2.9 min of audio) to
`--out-extra` for a long-track check (> CHUNK_FRAMES frames with the production chunk size), and
`--extra-cases` writes more uncommitted cases there (the server team's tests/golden/input.f32,
four SOUTHSIDE windows, 20-60 s synth tracks, a 3 s track, a 1000-sample input, 2 s of silence).
Run them with `AnalysisParityTest ... --extra <file>` and `compare_charts.py ... --extra <file>`.
The server code is imported read-only; nothing in it is modified (CHUNK_FRAMES is patched on the
imported module object only for the synth93c case and restored afterwards).
"""
from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import golden_io  # noqa: E402


def _find_server() -> Path:
    for p in HERE.parents:
        if (p / "ai-chart-server" / "app").is_dir():
            return p / "ai-chart-server"
    raise SystemExit("ai-chart-server not found above " + str(HERE))


SERVER = _find_server()
sys.path.insert(0, str(SERVER))

import librosa  # noqa: E402
import scipy.ndimage  # noqa: E402

from app.audio import analysis as A  # noqa: E402
from app.audio.decode import decode_mono  # noqa: E402
from app.audio.synth import synth_track  # noqa: E402
from app.chart.generator import generate_chart  # noqa: E402
from app.chart.serialize import to_unity_dict  # noqa: E402
from app.core.constants import CHART_ALGO_VERSION  # noqa: E402

SR, HOP = 22050, 256
DIFFICULTIES = ("easy", "normal", "hard")
SOUTHSIDE = SERVER.parent / "Assets" / "Package" / "BewhY (비와이) - SOUTHSIDE FREESTYLE (Official Video).mp3"
SLICE_W = 32


def sha(a: np.ndarray) -> str:
    return hashlib.sha256(np.ascontiguousarray(a).tobytes()).hexdigest()


def seed_key(pcm: np.ndarray, difficulty: str) -> str:
    # Same shape as SeedKeys.ForLocalFile: the sha here is of the PCM bytes (test-only stand-in).
    return f"upload:{sha(pcm.astype('<f4'))}|{difficulty}|{CHART_ALGO_VERSION}"


def stages(y: np.ndarray) -> tuple[dict, dict]:
    """Step-by-step analyze_audio with every intermediate (uses A.CHUNK_FRAMES as currently set)."""
    sr, hop = SR, HOP
    arr: dict[str, np.ndarray] = {}
    meta: dict = {"chunk_frames": int(A.CHUNK_FRAMES)}

    x = A.preprocess(y)
    meta["sha_preprocessed"] = sha(x)
    arr["preprocessed_head"] = x[:4096]
    duration = float(x.size) / float(sr)
    if x.size < A.N_FFT:
        x = np.pad(x, (0, A.N_FFT - x.size))

    mag = A._stft_magnitude(x, hop)
    n_frames = mag.shape[1]
    s0 = max(0, n_frames // 2 - SLICE_W // 2) // 2 * 2
    s1 = min(n_frames, s0 + SLICE_W)
    meta["slice"] = [s0, s1]
    meta["sha_mag"] = sha(mag)
    arr["mag_slice"] = mag[:, s0:s1]
    arr["mag_colsum"] = mag.astype(np.float64).sum(axis=0)

    rms_raw = librosa.feature.rms(S=mag, frame_length=A.N_FFT)[0].astype(np.float32)
    freqs = librosa.fft_frequencies(sr=sr, n_fft=A.N_FFT).astype(np.float32)
    mag_sum = mag.sum(axis=0)
    centroid_num = freqs @ mag
    centroid = np.where(mag_sum > A._EPS, centroid_num / np.maximum(mag_sum, A._EPS), 0.0).astype(np.float32)
    arr["rms_raw"] = rms_raw
    arr["mag_sum"] = mag_sum
    arr["centroid_num"] = centroid_num
    arr["centroid"] = centroid

    perc, harmonic_rms_raw, chroma = A._hpss_features(mag, sr, hop)
    meta["sha_perc"] = sha(perc)
    meta["sha_chroma"] = sha(chroma)
    arr["perc_slice"] = perc[:, s0:s1]
    arr["perc_colsum"] = perc.astype(np.float64).sum(axis=0)
    arr["harmonic_rms_raw"] = harmonic_rms_raw
    arr["chroma"] = chroma
    if n_frames <= A.CHUNK_FRAMES:  # single-chunk HPSS internals (pooled masks)
        bins, frames = mag.shape
        pb, pf = bins // 2 * 2, frames // 2 * 2
        low = mag[:pb, :pf].reshape(pb // 2, 2, pf // 2, 2).mean(axis=(1, 3))
        kh, kp = max(3, A.HPSS_KERNEL[0] // 2 | 1), max(3, A.HPSS_KERNEL[1] // 2 | 1)
        h_med = scipy.ndimage.median_filter(low, size=(1, kh), mode="reflect")
        p_med = scipy.ndimage.median_filter(low, size=(kp, 1), mode="reflect")
        mask = librosa.util.softmask(h_med, p_med, power=2.0, split_zeros=False)
        meta["sha_pooled"] = sha(low)
        meta["sha_hmed"] = sha(h_med)
        meta["sha_pmed"] = sha(p_med)
        meta["sha_mask_pooled"] = sha(mask)
        arr["pooled_slice"] = low[:, s0 // 2 : s1 // 2]
        arr["mask_pooled_slice"] = mask[:, s0 // 2 : s1 // 2]
    chroma_argmax = np.argmax(chroma, axis=0).astype(np.int8)
    chroma_flux = A._spectral_flux(chroma)
    harmonic_change = A._robust_unit(chroma_flux)
    arr["chroma_flux_raw"] = chroma_flux
    arr["harmonic_change"] = harmonic_change

    p2 = np.square(perc)
    mel_basis = librosa.filters.mel(sr=sr, n_fft=A.N_FFT, n_mels=A.N_MELS, fmax=min(A.MEL_FMAX, sr / 2))
    mel = (mel_basis.astype(np.float32) @ p2).astype(np.float32)
    meta["sha_mel"] = sha(mel)
    arr["mel_slice"] = mel[:, s0:s1]
    ref = float(np.max(mel)) if mel.size else 1.0
    meta["mel_ref"] = ref
    mel_db = librosa.power_to_db(mel, ref=ref, top_db=80.0).astype(np.float32, copy=False)
    meta["sha_mel_db"] = sha(mel_db)
    arr["mel_db_slice"] = mel_db[:, s0:s1]
    centers = librosa.mel_frequencies(n_mels=A.N_MELS + 2, fmax=min(A.MEL_FMAX, sr / 2))[1:-1]
    lowm = centers < A.LOW_BAND_MAX_HZ
    highm = centers >= A.HIGH_BAND_MIN_HZ
    midm = ~(lowm | highm)
    envs = {}
    for name, sel in (("full", slice(None)), ("low", lowm), ("mid", midm), ("high", highm)):
        flux = A._spectral_flux(mel_db[sel])
        arr[f"flux_{name}"] = flux
        envs[name] = A._robust_unit(flux)
        arr[f"env_{name}"] = envs[name]
    onset_env = envs["full"]

    # tempo
    tempo_raw = A._estimate_tempo(onset_env, sr, hop)
    meta["tempo_raw"] = tempo_raw
    tempo0 = A._fold_tempo(tempo_raw)
    meta["tempo0"] = tempo0
    if onset_env.any():
        win = int(librosa.time_to_frames(A.TEMPOGRAM_SECONDS, sr=sr, hop_length=hop))
        # Same chunk-averaged mean as _estimate_tempo (recomputed to expose it).
        total = np.zeros(win, dtype=np.float64)
        n = onset_env.shape[-1]
        for start in range(0, n, A.CHUNK_FRAMES):
            stop = min(n, start + A.CHUNK_FRAMES)
            lo, hi = max(0, start - win), min(n, stop + win)
            tg = librosa.feature.tempogram(onset_envelope=onset_env[lo:hi], sr=sr, hop_length=hop, win_length=win)
            total += tg[:, start - lo : stop - lo].sum(axis=1)
        tg_mean = total / n
        bpms = librosa.tempo_frequencies(win, hop_length=hop, sr=sr)
        logprior = -0.5 * ((np.log2(bpms) - np.log2(A.TEMPO_START_BPM)) / 1.0) ** 2
        logprior[: int(np.argmax(bpms < 320.0))] = -np.inf
        objective = np.log1p(1e6 * tg_mean) + logprior
        assert float(bpms[int(np.argmax(objective))]) == tempo_raw
        arr["tempogram_mean"] = tg_mean
        arr["tempo_objective"] = objective

    # beats
    if tempo0 > 0:
        bd = librosa.beat.__dict__
        fpb = np.round(float(sr) / hop * 60.0 / np.atleast_1d(tempo0))
        norm_env = bd["__normalize_onsets"](onset_env)
        localscore = np.empty_like(onset_env)
        bd["__beat_local_score"](norm_env, fpb, localscore)
        backlink, cumscore = bd["__beat_track_dp"](localscore, fpb, A.BEAT_TIGHTNESS)
        tail = bd["__last_beat"](cumscore)
        beats_raw = np.zeros_like(onset_env, dtype=bool)
        bd["__dp_backtrack"](backlink, tail, beats_raw)
        arr["beat_norm_env"] = norm_env
        arr["beat_localscore"] = localscore
        arr["beat_cumscore"] = cumscore
        arr["beat_backlink"] = backlink.astype(np.int32)
        arr["beat_frames_untrimmed"] = np.flatnonzero(beats_raw).astype(np.int64)
        meta["frames_per_beat"] = float(fpb[0])
        meta["beat_tail"] = int(np.asarray(tail).reshape(-1)[0])
        _, beat_frames = librosa.beat.beat_track(onset_envelope=onset_env, sr=sr, hop_length=hop, bpm=tempo0,
                                                 tightness=A.BEAT_TIGHTNESS, trim=True, units="frames")
        beat_frames = np.asarray(beat_frames, dtype=np.int64)
    else:
        beat_frames = np.zeros(0, dtype=np.int64)
    arr["beat_frames"] = beat_frames
    beat_times = librosa.frames_to_time(beat_frames, sr=sr, hop_length=hop).astype(np.float64)
    meta["tempo_refined"] = A._refine_tempo(beat_times, tempo0)
    meta["downbeat_phase"] = A._downbeat_phase(beat_frames, envs["low"], envs["mid"], harmonic_change)

    if onset_env.any():
        onset_frames = np.asarray(librosa.onset.onset_detect(onset_envelope=onset_env, sr=sr, hop_length=hop,
                                                             units="frames", backtrack=False, normalize=False),
                                  dtype=np.int64)
    else:
        onset_frames = np.zeros(0, dtype=np.int64)
    arr["onset_frames"] = onset_frames
    meta["rms_scale"] = float(np.percentile(rms_raw, A.RMS_NORM_PERCENTILE)) if rms_raw.size else 0.0
    meta["duration"] = duration

    # Production entry point; the stage-by-stage values above must be what it computes.
    f = A.analyze_audio(y, sr)
    assert np.array_equal(f.onset_env, onset_env) and np.array_equal(f.spectral_centroid, centroid)
    assert np.array_equal(f.chroma_argmax, chroma_argmax)
    assert f.downbeat_phase == meta["downbeat_phase"] and f.tempo == round(float(meta["tempo_refined"]), 3)
    for k in ("low", "mid", "high"):
        assert np.array_equal(getattr(f, f"onset_env_{k}"), envs[k])
    return arr, meta, f


FEATURE_FIELDS = (
    "beat_times", "frame_times", "onset_env", "onset_env_low", "onset_env_mid", "onset_env_high",
    "onset_times", "onset_strengths", "rms", "harmonic_rms", "spectral_centroid", "chroma_argmax",
)


def feature_arrays(f) -> tuple[dict, dict]:
    arr = {f"feat_{k}": np.asarray(getattr(f, k)) for k in FEATURE_FIELDS}
    meta = {"feat_sample_rate": f.sample_rate, "feat_hop_length": f.hop_length, "feat_duration": f.duration,
            "feat_tempo": f.tempo, "feat_downbeat_phase": f.downbeat_phase}
    return arr, meta


def charts_for(f, pcm: np.ndarray) -> dict:
    out = {}
    for d in DIFFICULTIES:
        try:
            out[d] = to_unity_dict(generate_chart(f, d, seed_key(pcm, d)))
        except Exception as e:  # noqa: BLE001 - recorded, the C# side must fail the same way
            out[d] = {"error": f"{type(e).__name__}: {e}"}
    return out


def build_case(name: str, pcm: np.ndarray, chunk_frames: int | None, out_dir: Path) -> None:
    pcm = np.ascontiguousarray(pcm, dtype=np.float32)
    old = A.CHUNK_FRAMES
    try:
        if chunk_frames is not None:
            A.CHUNK_FRAMES = chunk_frames
        arr, meta, f = stages(pcm)
    finally:
        A.CHUNK_FRAMES = old
    farr, fmeta = feature_arrays(f)
    arr = {"input": pcm, **arr, **farr}
    meta.update(fmeta)
    meta["case"] = name
    meta["n_samples"] = int(pcm.size)
    meta["sha_input"] = sha(pcm)
    meta["charts"] = charts_for(f, pcm)
    meta["seed_keys"] = {d: seed_key(pcm, d) for d in DIFFICULTIES}
    meta["versions"] = {"numpy": np.__version__, "librosa": librosa.__version__, "scipy": scipy.__version__}
    path = out_dir / f"analysis_{name}.lcg"
    golden_io.write(path, arr, meta)
    n_notes = {d: len(c.get("Notes", [])) for d, c in meta["charts"].items()}
    print(f"{path.name}: {pcm.size} samples, {f.frame_times.size} frames, tempo {f.tempo}, "
          f"{f.beat_times.size} beats, {f.onset_times.size} onsets, notes {n_notes}, {path.stat().st_size} bytes")


def constants(out_dir: Path, pcm: np.ndarray) -> None:
    import scipy.fft
    from scipy.signal import get_window

    # FFT accuracy probes: the exact float64 frames scipy.fft sees inside librosa.stft / autocorrelate.
    x = A.preprocess(pcm)
    win = get_window("hann", A.N_FFT, fftbins=True)
    starts = [0, 200 * HOP, 1291 * HOP, 2400 * HOP]
    fr = np.stack([win * x[s : s + A.N_FFT] for s in starts])  # float64
    spec = scipy.fft.rfft(fr, axis=-1)
    tg_win = get_window("hann", 689, fftbins=True)
    env = np.abs(np.diff(x[: 689 * 5 + 1]))[: 689 * 4].reshape(4, 689).astype(np.float32)
    odf = env * tg_win
    spec2 = scipy.fft.rfft(odf, n=1440, axis=-1)
    pw = spec2.real ** 2 + spec2.imag ** 2
    ac = scipy.fft.irfft(pw, n=1440, axis=-1)

    arr = {
        "mel_basis": librosa.filters.mel(sr=SR, n_fft=A.N_FFT, n_mels=A.N_MELS, fmax=min(A.MEL_FMAX, SR / 2)),
        "chroma_basis": librosa.filters.chroma(sr=SR, n_fft=A.N_FFT, tuning=0.0),
        "stft_window": get_window("hann", A.N_FFT, fftbins=True),
        "tempogram_window": get_window("hann", 689, fftbins=True),
        "mel_centers": librosa.mel_frequencies(n_mels=A.N_MELS + 2, fmax=min(A.MEL_FMAX, SR / 2)),
        "fft_freqs": librosa.fft_frequencies(sr=SR, n_fft=A.N_FFT).astype(np.float32),
        "fft2048_in": fr, "fft2048_re": spec.real.copy(), "fft2048_im": spec.imag.copy(),
        "fft1440_in": np.pad(odf, ((0, 0), (0, 1440 - 689))), "fft1440_re": spec2.real.copy(), "fft1440_im": spec2.imag.copy(),
        "ifft1440_in": pw, "ifft1440_out": ac,
    }
    golden_io.write(out_dir / "analysis_constants.lcg", arr, {})
    print("analysis_constants.lcg written")


def southside_excerpt(start_s: float = 45.0, seconds: float = 30.0) -> np.ndarray:
    y = decode_mono(SOUTHSIDE, sr=SR)
    a = int(start_s * SR)
    return y[a : a + int(seconds * SR)].copy()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=HERE.parent / "golden")
    ap.add_argument("--full-song", action="store_true")
    ap.add_argument("--out-extra", type=Path, default=None)
    ap.add_argument("--extra-cases", action="store_true")
    args = ap.parse_args()

    ss = southside_excerpt()
    constants(args.out, ss)
    build_case("southside30", ss, None, args.out)
    y128, _ = synth_track(128.0, 8.0, sr=SR, seed=7, sustained_pads=True)
    build_case("synth128", y128, None, args.out)
    y93, _ = synth_track(93.0, 10.0, sr=SR, seed=3, sustained_pads=False)
    build_case("synth93c", y93, 300, args.out)
    extra = args.out_extra or args.out
    if args.full_song:
        build_case("southside_full", decode_mono(SOUTHSIDE, sr=SR), None, extra)
    if args.extra_cases:
        server_input = SERVER / "tests" / "golden" / "input.f32"
        if server_input.is_file():
            build_case("server_synth128_30", np.fromfile(server_input, dtype="<f4"), None, extra)
        full = decode_mono(SOUTHSIDE, sr=SR)
        for off in (0, 20, 100, 140):
            build_case(f"southside_at{off}", full[off * SR : (off + 25) * SR], None, extra)
        for bpm, sec, seed, pads in ((174.0, 20.0, 11, True), (75.0, 40.0, 5, False), (140.0, 60.0, 2, True)):
            y, _ = synth_track(bpm, sec, sr=SR, seed=seed, sustained_pads=pads)
            build_case(f"synth{int(bpm)}_{int(sec)}s", y, None, extra)
        y, _ = synth_track(120.0, 3.0, sr=SR, seed=1)
        build_case("synth120_short3s", y, None, extra)
        build_case("tiny_1000samples", y[:1000], None, extra)
        build_case("silence_2s", np.zeros(2 * SR, np.float32), None, extra)


if __name__ == "__main__":
    main()
