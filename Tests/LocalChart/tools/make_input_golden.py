"""Golden data for the Input track: ffmpeg `-ac 1 -ar 22050` (libswresample defaults) reference.

Writes Tests/LocalChart/golden/input/<case>.in.s16 (interleaved s16le), <case>.out.f32 (mono f32le from
ffmpeg) and cases.json. The C# test converts s16 -> float with x / 32768f (exact), which is exactly the
float PCM fed to ffmpeg here (as raw f32le), so both sides see the same input samples.

Same command shape as the server (app/audio/decode.py decode_mono):
    ffmpeg -f f32le -ar <rate> -ch_layout <layout> -i <raw> -ac 1 -ar 22050 -f f32le -acodec pcm_f32le pipe:1

Run from anywhere:
    /Users/aiden/dev/game/sushi-chain/ai-chart-server/.venv/bin/python Tests/LocalChart/tools/make_input_golden.py
"""
from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
WT = HERE.parents[2]
OUT = WT / "Tests" / "LocalChart" / "golden" / "input"
REPO = WT
for cand in (WT, *WT.parents):
    if (cand / "Assets" / "Package").is_dir() and (cand / "ai-chart-server").is_dir():
        REPO = cand
        break
BEWHY = REPO / "Assets" / "Package" / "BewhY (비와이) - SOUTHSIDE FREESTYLE (Official Video).mp3"

LAYOUTS = {1: "mono", 2: "stereo", 6: "5.1"}


def ffmpeg_mono22050(x: np.ndarray, rate: int, channels: int) -> np.ndarray:
    with tempfile.TemporaryDirectory() as td:
        raw = Path(td) / "in.f32"
        raw.write_bytes(x.astype("<f4").tobytes())
        args = [
            "ffmpeg", "-nostdin", "-hide_banner", "-v", "error",
            "-f", "f32le", "-ar", str(rate), "-ch_layout", LAYOUTS[channels], "-i", f"file:{raw}",
            "-ac", "1", "-ar", "22050", "-f", "f32le", "-acodec", "pcm_f32le", "pipe:1",
        ]
        out = subprocess.run(args, check=True, capture_output=True).stdout
    return np.frombuffer(out, dtype="<f4").copy()


def quantize(x: np.ndarray) -> np.ndarray:
    return np.clip(np.round(x * 32768.0), -32768, 32767).astype(np.int16)


def synth(rate: int, seconds: float, channels: int, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    n = int(round(rate * seconds))
    t = np.arange(n) / rate
    chans = []
    for c in range(channels):
        f0 = 220.0 * (c + 1)
        sig = 0.3 * np.sin(2 * np.pi * f0 * t)
        sig += 0.2 * np.sin(2 * np.pi * (100.0 + 4000.0 * t / seconds) * t)  # chirp
        sig += 0.05 * rng.standard_normal(n)
        if rate > 30000:
            sig += 0.05 * np.sin(2 * np.pi * 12000.0 * t)  # above the 22050 Nyquist: must be filtered
        chans.append(sig)
    return np.stack(chans, axis=1).reshape(-1)


def bewhy_excerpt(start: float, seconds: float) -> np.ndarray:
    args = [
        "ffmpeg", "-nostdin", "-hide_banner", "-v", "error", "-ss", str(start), "-t", str(seconds),
        "-i", f"file:{BEWHY}", "-map", "0:a:0", "-f", "f32le", "-acodec", "pcm_f32le", "pipe:1",
    ]
    out = subprocess.run(args, check=True, capture_output=True).stdout
    return np.frombuffer(out, dtype="<f4").copy()


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    cases = [
        # name, rate, channels, data (interleaved float)
        ("bewhy_44100_st", 44100, 2, bewhy_excerpt(60.0, 8.0)),
        ("synth_48000_st", 48000, 2, synth(48000, 2.0, 2, 1)),
        ("synth_32000_mono", 32000, 1, synth(32000, 1.0, 1, 2)),
        ("synth_16000_mono", 16000, 1, synth(16000, 1.0, 1, 3)),   # upsampling path
        ("synth_44100_51", 44100, 6, synth(44100, 0.5, 6, 4)),     # 5.1 downmix
        ("synth_44056_st", 44056, 2, synth(44056, 0.5, 2, 5)),     # inexact ratio -> 1024 phases + linear
        ("synth_22050_st", 22050, 2, synth(22050, 0.5, 2, 6)),     # no resampling, downmix only
        ("tiny_44100_mono", 44100, 1, synth(44100, 0.004, 1, 7)),  # 176 samples (shorter than 2x filter)
    ]
    meta = []
    for name, rate, ch, x in cases:
        q = quantize(x)
        xf = (q.astype(np.float32) / np.float32(32768.0)).astype(np.float32)
        y = ffmpeg_mono22050(xf, rate, ch)
        (OUT / f"{name}.in.s16").write_bytes(q.astype("<i2").tobytes())
        (OUT / f"{name}.out.f32").write_bytes(y.astype("<f4").tobytes())
        frames = q.size // ch
        meta.append({
            "name": name, "rate": rate, "channels": ch, "frames": int(frames), "outSamples": int(y.size),
            "outSha256": hashlib.sha256(y.astype("<f4").tobytes()).hexdigest(),
        })
        print(f"{name}: in {frames} frames @{rate}x{ch} -> out {y.size}")
    (OUT / "cases.json").write_text(json.dumps({"ffmpeg": ffmpeg_version(), "cases": meta}, indent=2) + "\n")

    # Output length table: ffmpeg's sample count for (rate, N) -> 22050 (no data stored, lengths only)
    rows = []
    for rate in LENGTH_RATES:
        for n in LENGTH_NS:
            x = np.random.default_rng(n).uniform(-0.5, 0.5, n).astype(np.float32)
            rows.append(f"{rate} {n} {ffmpeg_mono22050(x, rate, 1).size}")
    (OUT / "lengths.txt").write_text("# in_rate input_samples ffmpeg_output_samples (-> 22050 Hz)\n" + "\n".join(rows) + "\n")
    print(f"lengths.txt: {len(rows)} rows")
    return 0


LENGTH_RATES = (8000, 11025, 16000, 24000, 32000, 44056, 44100, 48000, 88200, 96000, 192000)
LENGTH_NS = (200, 1000, 4097, 44100, 44101, 123457)


def ffmpeg_version() -> str:
    return subprocess.run(["ffmpeg", "-version"], capture_output=True, text=True).stdout.splitlines()[0]


if __name__ == "__main__":
    sys.exit(main())
