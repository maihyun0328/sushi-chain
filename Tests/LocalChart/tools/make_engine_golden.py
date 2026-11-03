"""Engine 트랙 골든 데이터 생성기.

서버(ai-chart-server)의 analyze_audio / generate_chart 를 그대로 실행해서
  * AudioFeatures (배열은 raw little-endian 바이트의 base64 — 비트 단위로 정확)
  * easy/normal/hard x seed_key 2종의 NoteList (시간은 float64 비트 hex 도 함께) 또는 예외
  * PCG64 (default_rng) 첫 1000개 random() 과 SeedSequence state
  * Python round(t, 3) 테스트 벡터
를 Tests/LocalChart/golden/engine/ 아래 JSON 으로 쓴다.

실행 (서버 venv, 서버 저장소는 읽기만 한다):
    cd ai-chart-server && .venv/bin/python <worktree>/Tests/LocalChart/tools/make_engine_golden.py [--mp3 PATH] [--out DIR]
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import struct
import sys
from dataclasses import replace
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
WORKTREE = HERE.parents[2]
DEFAULT_MP3 = WORKTREE / "Assets/Package/BewhY (비와이) - SOUTHSIDE FREESTYLE (Official Video).mp3"
DEFAULT_OUT = HERE.parent / "golden" / "engine"
# The server package is imported from the current directory (run from ai-chart-server/).
sys.path.insert(0, str(Path.cwd()))

from app.audio.analysis import analyze_audio  # noqa: E402
from app.audio.decode import decode_mono  # noqa: E402
from app.audio.features import AudioFeatures  # noqa: E402
from app.audio.synth import synth_track  # noqa: E402
from app.chart.generator import generate_chart, seed_from_key  # noqa: E402
from app.chart.serialize import round_time  # noqa: E402
from app.core.constants import ANALYSIS_SAMPLE_RATE as SR  # noqa: E402
from app.core.constants import CHART_ALGO_VERSION  # noqa: E402

DIFFICULTIES = ("easy", "normal", "hard")

# Contract dtypes (LocalChartContracts.cs AudioFeatures).
ARRAY_DTYPES = {
    "beat_times": "<f8",
    "frame_times": "<f8",
    "onset_env": "<f4",
    "onset_env_low": "<f4",
    "onset_env_mid": "<f4",
    "onset_env_high": "<f4",
    "onset_times": "<f8",
    "onset_strengths": "<f4",
    "rms": "<f4",
    "harmonic_rms": "<f4",
    "spectral_centroid": "<f4",
    "chroma_argmax": "<i4",  # server: int8, values 0..11 (widened losslessly)
}


def f64_hex(x: float) -> str:
    return struct.pack("<d", float(x)).hex()


def b64(a: np.ndarray, dtype: str) -> str:
    return base64.b64encode(np.ascontiguousarray(a, dtype=dtype).tobytes()).decode("ascii")


def dump_features(f: AudioFeatures) -> dict:
    out = {
        "sample_rate": int(f.sample_rate),
        "hop_length": int(f.hop_length),
        "duration": f64_hex(f.duration),
        "tempo": f64_hex(f.tempo),
        "downbeat_phase": int(f.downbeat_phase),
        "arrays": {},
    }
    for name, dt in ARRAY_DTYPES.items():
        a = np.asarray(getattr(f, name))
        if name == "chroma_argmax":
            assert a.dtype.kind in "iu", (name, a.dtype)
        else:
            assert a.dtype == np.dtype(dt), (name, a.dtype, dt)
        out["arrays"][name] = {"dtype": dt[1:], "n": int(a.size), "b64": b64(a, dt)}
    return out


def run_chart(f: AudioFeatures, difficulty: str, seed_key: str) -> dict:
    res = {"difficulty": difficulty, "seed_key": seed_key}
    try:
        chart = generate_chart(f, difficulty, seed_key)
    except Exception as exc:  # ChartGenerationError / ChartValidationError
        res["error"] = {"type": type(exc).__name__, "message": str(exc)}
        return res
    res["notes"] = [
        [int(n.dir), int(n.type), f64_hex(n.StartTime), f64_hex(n.EndTime), float(n.StartTime), float(n.EndTime)]
        for n in chart.Notes
    ]
    return res


def seed_keys(source_key: str, difficulty: str) -> list[str]:
    return [f"{source_key}|{difficulty}|{CHART_ALGO_VERSION}", f"alt-seed:{source_key}|{difficulty}|{CHART_ALGO_VERSION}"]


def write_case(out_dir: Path, name: str, f: AudioFeatures, source_key: str, info: dict) -> dict:
    charts = []
    for d in DIFFICULTIES:
        for sk in seed_keys(source_key, d):
            charts.append(run_chart(f, d, sk))
    doc = {"case": name, "info": info, "features": dump_features(f), "charts": charts}
    path = out_dir / f"case_{name}.json"
    path.write_text(json.dumps(doc, ensure_ascii=False, separators=(",", ":")))
    summary = {c["difficulty"] + ("" if not c["seed_key"].startswith("alt") else "/alt"):
               (len(c["notes"]) if "notes" in c else c["error"]["type"]) for c in charts}
    print(f"{name}: F={f.frame_times.size} beats={f.beat_times.size} onsets={f.onset_times.size} {summary}")
    return summary


def rng_golden(out_dir: Path) -> None:
    keys = [
        "golden:synth128|easy|1.0.0",
        "upload:" + "0" * 64 + "|hard|1.0.0",
        "upload:" + hashlib.sha256(b"sushi").hexdigest() + "|normal|1.0.0",
        "",
        "한글 키|easy|1.0.0",
    ]
    seeds = [{"seed_key": k, "seed": str(seed_from_key(k))} for k in keys]
    # Raw integer seeds that exercise SeedSequence word-count edge cases (0, < 2^32, >= 2^32).
    for s in (0, 1, 12345, 2**32 - 1, 2**32, 2**64 - 1):
        seeds.append({"seed_key": None, "seed": str(s)})
    for e in seeds:
        seed = int(e["seed"])
        ss = np.random.SeedSequence(seed)
        e["state_u64"] = [str(int(v)) for v in ss.generate_state(4, np.uint64)]
        bg = np.random.PCG64(seed)
        e["raw_u64_b64"] = b64(bg.random_raw(16).astype(np.uint64), "<u8")
        g = np.random.default_rng(seed)
        # Drawn 4 at a time, exactly like lanes.lane_costs.
        draws = np.concatenate([g.random(4) for _ in range(250)])
        e["random_f64_b64"] = b64(draws, "<f8")
    (out_dir / "rng.json").write_text(json.dumps({"seeds": seeds}, ensure_ascii=False, indent=1))
    print(f"rng: {len(seeds)} seeds x 1000 draws")


def round_golden(out_dir: Path) -> None:
    rng = np.random.default_rng(2026)
    vals = list(rng.random(4000) * 600.0)
    vals += list(np.round(rng.random(2000) * 600.0, 4))  # many near/at x.xxx5 decimal ties
    vals += [k / 16.0 for k in range(0, 2000)]  # exact binary halves: 0.0625 -> 0.062 (half-even)
    vals += [k / 2048.0 for k in range(0, 4000)]
    vals += [0.0005, 0.0015, 0.0025, 1.0005, 2.0625, 2.6875, 1e-300, 5e-324, 123456.7895, 0.9995, -0.0, -0.0004]
    pairs = [[f64_hex(v), f64_hex(round_time(v))] for v in vals]
    (out_dir / "round_time.json").write_text(json.dumps({"pairs": pairs}, separators=(",", ":")))
    print(f"round_time: {len(pairs)} vectors")


def validate_golden(out_dir: Path) -> None:
    """Hand-made NoteLists through validate_chart (model_construct skips pydantic's dir check)."""
    from app.chart.validate import ChartValidationError, validate_chart
    from app.models.notes import NoteData, NoteList

    def nl(rows):
        return NoteList.model_construct(Notes=[NoteData.model_construct(dir=d, type=t, StartTime=s, EndTime=e)
                                              for d, t, s, e in rows])

    nan = float("nan")
    cases = {
        "valid": ([(0, 0, 2.0, 0.0), (1, 2, 2.5, 0.0), (0, 1, 3.0, 4.0), (1, 3, 4.5, 0.0)], 10.0),
        "dir_mismatch": ([(0, 2, 2.0, 0.0), (1, 0, 3.0, 0.0)], 10.0),
        "unknown_type": ([(0, 7, 2.0, 0.0)], 10.0),
        "negative_beyond_unsorted": ([(0, 0, 5.0, 0.0), (0, 1, -1.0, 0.0), (1, 2, 11.0, 12.0)], 10.0),
        "bad_long_end": ([(0, 0, 3.0, 2.5), (1, 2, 4.0, 4.0)], 10.0),
        "lane_overlap": ([(0, 0, 2.0, 4.0), (0, 0, 3.0, 0.0), (0, 0, 4.0005, 0.0), (1, 3, 5.0, 0.0), (1, 3, 5.0, 0.0)], 10.0),
        "lead_in": ([(0, 0, 1.9, 0.0), (1, 2, 1.9984, 0.0), (0, 1, 2.6, 0.0)], 10.0),
        "triple": ([(0, 0, 3.0, 0.0), (0, 1, 3.0, 0.0), (1, 2, 3.0, 0.0)], 10.0),
        "chord_same_side": ([(0, 0, 3.0, 0.0), (0, 1, 3.0, 0.0), (1, 2, 4.0, 0.0), (1, 3, 4.0004, 0.0)], 10.0),
        "small_gap": ([(0, 0, 3.0, 0.0), (1, 2, 3.2, 0.0), (0, 1, 3.4, 0.0), (1, 3, 3.5, 0.0)], 10.0),
        "held_side": ([(0, 0, 3.0, 5.0), (0, 1, 4.0, 0.0), (1, 2, 4.5, 0.0), (0, 1, 5.0, 0.0), (0, 1, 6.0, 0.0)], 10.0),
        "non_finite": ([(0, 0, nan, 0.0), (1, 2, 3.0, float("inf"))], 10.0),
        "bad_duration": ([(0, 0, 3.0, 0.0)], 0.0),
    }
    out = []
    for name, (rows, duration) in cases.items():
        for diff in (None, *DIFFICULTIES):
            try:
                validate_chart(nl(rows), duration, diff)
                problems = []
            except ChartValidationError as exc:
                problems = exc.problems
            out.append({"name": name, "difficulty": diff, "duration": duration,
                        "notes": [[d, t, f64_hex(s), f64_hex(e)] for d, t, s, e in rows], "problems": problems})
    (out_dir / "validate.json").write_text(json.dumps({"cases": out}, indent=1))
    print(f"validate: {len(out)} cases")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mp3", type=Path, default=DEFAULT_MP3)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--excerpt-start", type=float, default=60.0)
    ap.add_argument("--excerpt-seconds", type=float, default=30.0)
    args = ap.parse_args()
    out: Path = args.out
    out.mkdir(parents=True, exist_ok=True)

    meta = {"numpy": np.__version__, "chart_algo_version": CHART_ALGO_VERSION, "cases": {}}

    # 1) SOUTHSIDE 30 s excerpt (ffmpeg decode, exactly like the worker).
    y = decode_mono(args.mp3)
    a = int(round(args.excerpt_start * SR))
    b = a + int(round(args.excerpt_seconds * SR))
    clip = y[a:b]
    f_south = analyze_audio(clip, SR)
    meta["cases"]["southside30"] = write_case(out, "southside30", f_south, "upload:" + hashlib.sha256(clip.tobytes()).hexdigest(), {
        "source": args.mp3.name, "start_sample": a, "end_sample": b,
        "pcm_sha256": hashlib.sha256(clip.astype("<f4").tobytes()).hexdigest(),
    })

    # 1b) The whole song (long track: many density windows, long notes, chords).
    f_full = analyze_audio(y, SR)
    meta["cases"]["southside_full"] = write_case(out, "southside_full", f_full, "upload:" + hashlib.sha256(y.tobytes()).hexdigest(), {
        "source": args.mp3.name, "pcm_sha256": hashlib.sha256(y.astype("<f4").tobytes()).hexdigest(), "n_samples": int(y.size)})

    # 2) Synthetic tracks at different tempi.
    synth = {}
    for name, bpm, secs, seed, pads in (("synth128", 128.0, 30.0, 7, True), ("synth92", 92.0, 24.0, 3, True),
                                        ("synth174", 174.0, 20.0, 11, False)):
        ys, _ = synth_track(bpm=bpm, seconds=secs, sr=SR, seed=seed, sustained_pads=pads)
        fs = analyze_audio(ys, SR)
        synth[name] = fs
        meta["cases"][name] = write_case(out, name, fs, f"golden:{name}", {
            "generator": f"synth_track(bpm={bpm}, seconds={secs}, sr={SR}, seed={seed}, sustained_pads={pads})"})

    # 3) Edge cases.
    silent = analyze_audio(np.zeros(SR * 8, dtype=np.float32), SR)
    meta["cases"]["silence"] = write_case(out, "silence", silent, "golden:silence", {"generator": "zeros(8 s)"})

    ys, _ = synth_track(bpm=128.0, seconds=1.5, sr=SR, seed=1)
    meta["cases"]["short"] = write_case(out, "short", analyze_audio(ys, SR), "golden:short",
                                        {"generator": "synth_track(bpm=128, seconds=1.5, seed=1)"})

    base = synth["synth92"]
    # Tracker produced nothing: grid from tempo, anchored at the first onset.
    meta["cases"]["no_beats"] = write_case(out, "no_beats", replace(base, beat_times=np.zeros(0, dtype=np.float64)),
                                           "golden:no_beats", {"mutation": "synth92 with beat_times=[]"})
    # No beats and an implausible tempo: empty grid -> ChartGenerationError.
    meta["cases"]["no_grid"] = write_case(out, "no_grid", replace(base, beat_times=np.zeros(0, dtype=np.float64), tempo=0.0),
                                          "golden:no_grid", {"mutation": "synth92 with beat_times=[], tempo=0"})
    # Messy tracker output: unsorted, duplicates, a near-double detection, NaN; odd downbeat phase;
    # NaN / out-of-range onset strengths.
    bt = base.beat_times.copy()
    messy = np.concatenate([bt[::-1], bt[5:9], [bt[10] + 0.01, np.nan, np.inf]]).astype(np.float64)
    st = base.onset_strengths.copy()
    st[3] = np.nan
    st[7] = 1.5
    st[11] = -0.2
    meta["cases"]["messy"] = write_case(out, "messy", replace(base, beat_times=messy, downbeat_phase=2, onset_strengths=st),
                                        "golden:messy", {"mutation": "synth92 messy beats, phase 2, bad strengths"})
    # Frame array length mismatch -> ChartGenerationError.
    meta["cases"]["mismatch"] = write_case(out, "mismatch", replace(base, rms=base.rms[:-1].copy()),
                                           "golden:mismatch", {"mutation": "synth92 with rms[:-1]"})

    rng_golden(out)
    validate_golden(out)
    round_golden(out)
    (out / "meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
