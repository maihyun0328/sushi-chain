"""Decisive parity check: run the SERVER's generate_chart on the C# AudioFeatures.

    cd ai-chart-server && .venv/bin/python <worktree>/Tests/LocalChart/tools/compare_charts.py \
        <dump dir with <case>.features.lcg> [golden dir] [--extra <analysis_x.lcg> ...]

For every case it loads the C# features (exact float bytes, written by AnalysisParityTest --dump),
builds app.audio.features.AudioFeatures, runs generate_chart for easy/normal/hard with the case's
seed keys, and compares the NoteLists with generate_chart on the server's own features (rebuilt
from the golden file) and with the charts stored in the golden file. Also reports per-field
feature differences. Exit code 0 only if every chart is identical.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import golden_io  # noqa: E402
from make_analysis_golden import DIFFICULTIES, FEATURE_FIELDS, SERVER  # noqa: E402,F401

from app.audio.features import AudioFeatures  # noqa: E402
from app.chart.generator import generate_chart  # noqa: E402
from app.chart.serialize import to_unity_dict  # noqa: E402

DTYPES = {
    "beat_times": np.float64, "frame_times": np.float64, "onset_times": np.float64,
    "onset_env": np.float32, "onset_env_low": np.float32, "onset_env_mid": np.float32, "onset_env_high": np.float32,
    "onset_strengths": np.float32, "rms": np.float32, "harmonic_rms": np.float32, "spectral_centroid": np.float32,
    "chroma_argmax": np.int8,
}


def load_csharp(path: Path) -> AudioFeatures:
    arr, meta = golden_io.read(path)
    kw = {k: np.asarray(arr[k]).astype(DTYPES[k]) for k in DTYPES}
    return AudioFeatures(sample_rate=int(meta["sample_rate"]), hop_length=int(meta["hop_length"]),
                         duration=float(meta["duration"]), tempo=float(meta["tempo"]),
                         downbeat_phase=int(meta["downbeat_phase"]), **kw)


def load_server(arr: dict, meta: dict) -> AudioFeatures:
    kw = {k: np.asarray(arr[f"feat_{k}"]) for k in FEATURE_FIELDS}
    return AudioFeatures(sample_rate=int(meta["feat_sample_rate"]), hop_length=int(meta["feat_hop_length"]),
                         duration=float(meta["feat_duration"]), tempo=float(meta["feat_tempo"]),
                         downbeat_phase=int(meta["feat_downbeat_phase"]), **kw)


def chart(f: AudioFeatures, d: str, key: str) -> dict:
    try:
        return to_unity_dict(generate_chart(f, d, key))
    except Exception as e:  # noqa: BLE001
        return {"error": f"{type(e).__name__}: {e}"}


def note_diff(a: dict, b: dict) -> str:
    if "error" in a or "error" in b:
        return f"mine={a.get('error', 'ok')} server={b.get('error', 'ok')}"
    na, nb = a["Notes"], b["Notes"]
    sa = {tuple(sorted(n.items())) for n in na}
    sb = {tuple(sorted(n.items())) for n in nb}
    only_a, only_b = sorted(sa - sb), sorted(sb - sa)
    return f"{len(na)} vs {len(nb)} notes; only C#: {[dict(x) for x in only_a[:6]]}; only server: {[dict(x) for x in only_b[:6]]}"


def feature_report(mine: AudioFeatures, srv: AudioFeatures) -> None:
    for k in ("duration", "tempo", "downbeat_phase"):
        a, b = getattr(mine, k), getattr(srv, k)
        print(f"    {k:18s} {'same' if a == b else f'{a!r} vs {b!r}'}")
    for k in FEATURE_FIELDS:
        a, b = np.asarray(getattr(mine, k)), np.asarray(getattr(srv, k))
        if a.shape != b.shape:
            print(f"    {k:18s} SHAPE {a.shape} vs {b.shape}")
            continue
        same = int(np.sum(a == b))
        md = float(np.max(np.abs(a.astype(np.float64) - b.astype(np.float64)))) if a.size else 0.0
        print(f"    {k:18s} n={a.size:<6} equal={same:<6} maxAbs={md:.3g}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("dump", type=Path)
    ap.add_argument("golden", type=Path, nargs="?", default=HERE.parent / "golden")
    ap.add_argument("--extra", type=Path, action="append", default=[])
    args = ap.parse_args()
    files = sorted(p for p in args.golden.glob("analysis_*.lcg") if p.name != "analysis_constants.lcg") + args.extra
    bad = 0
    for gpath in files:
        arr, meta = golden_io.read(gpath)
        name = meta["case"]
        mpath = args.dump / f"{name}.features.lcg"
        if not mpath.is_file():
            print(f"== {name}: no C# dump at {mpath}")
            bad += 1
            continue
        mine, srv = load_csharp(mpath), load_server(arr, meta)
        print(f"== {name}")
        feature_report(mine, srv)
        for d in DIFFICULTIES:
            key = meta["seed_keys"][d]
            cm, cs = chart(mine, d, key), chart(srv, d, key)
            stored = meta["charts"][d]
            same = cm == cs
            print(f"  {d:6s} C#-features chart {'IDENTICAL' if same else 'DIFFERENT'} to server-features chart "
                  f"({len(cs.get('Notes', []))} notes; stored golden {'matches' if cs == stored else 'DIFFERS'})")
            if not same:
                bad += 1
                print("    " + note_diff(cm, cs))
    print("ALL CHARTS IDENTICAL" if bad == 0 else f"{bad} CHART(S) DIFFERENT")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
