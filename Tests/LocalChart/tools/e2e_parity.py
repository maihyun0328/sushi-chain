"""End-to-end parity: server pipeline vs the C# pipeline on identical PCM.

    cd ai-chart-server
    # optional: full song as raw mono 22050 f32 via the server's decode_mono
    .venv/bin/python <repo>/Tests/LocalChart/tools/e2e_parity.py decode <song.mp3> <out.f32>
    # after E2EParityTest.cs wrote <dir>/<case>.csharp.json:
    .venv/bin/python <repo>/Tests/LocalChart/tools/e2e_parity.py compare <dir> <input> [<input> ...]

<input> is the same list given to E2EParityTest (*.lcg analysis goldens or raw *.f32). For each input
and difficulty the server runs analyze_audio → generate_chart with the seed key the C# side used and
the result is compared note by note (exact = same count, dir, type and float64 Start/EndTime).
Mismatches print a note diff and the chart-server tolerance metrics:
  count within ±5%, ≥90% of server notes matched within ±15 ms, ≥80% lane (dir,type) agreement
  among matched notes, long-note count within ±2.
Exit code 0 when every chart is exact or within tolerance; 1 otherwise.
"""
from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import golden_io  # noqa: E402
from make_analysis_golden import DIFFICULTIES, SR, seed_key, sha  # noqa: E402

from app.audio.analysis import analyze_audio  # noqa: E402
from app.chart.generator import generate_chart  # noqa: E402

MATCH_S = 0.015


def load_pcm(path: Path) -> tuple[str, np.ndarray]:
    if path.suffix == ".lcg":
        arr, meta = golden_io.read(path)
        return str(meta.get("case", path.stem)), np.ascontiguousarray(arr["input"], dtype=np.float32)
    return path.stem, np.fromfile(path, dtype="<f4")


def server_notes(f, diff: str, key: str):
    try:
        nl = generate_chart(f, diff, key)
    except Exception as e:  # noqa: BLE001
        return f"{type(e).__name__}: {e}"
    return [[int(n.dir), int(n.type), float(n.StartTime), float(n.EndTime)] for n in nl.Notes]


def metrics(srv: list, cs: list) -> dict:
    """Greedy one-to-one matching by StartTime within ±15 ms (both lists sorted by time)."""
    used = [False] * len(cs)
    matched = lane_ok = 0
    j0 = 0
    for d, t, s, _ in srv:
        while j0 < len(cs) and cs[j0][2] < s - MATCH_S:
            j0 += 1
        best = None
        j = j0
        while j < len(cs) and cs[j][2] <= s + MATCH_S:
            if not used[j] and (best is None or abs(cs[j][2] - s) < abs(cs[best][2] - s)):
                best = j
            j += 1
        if best is not None:
            used[best] = True
            matched += 1
            lane_ok += int(cs[best][0] == d and cs[best][1] == t)
    n_s, n_c = len(srv), len(cs)
    long_s = sum(1 for n in srv if n[3] > 0)
    long_c = sum(1 for n in cs if n[3] > 0)
    m = {
        "count": f"{n_c}/{n_s}", "count_ok": n_s > 0 and abs(n_c - n_s) <= 0.05 * n_s,
        "match": matched / n_s if n_s else 1.0, "lane": lane_ok / matched if matched else 1.0,
        "long": f"{long_c}/{long_s}", "long_ok": abs(long_c - long_s) <= 2,
    }
    m["match_ok"] = m["match"] >= 0.90
    m["lane_ok"] = m["lane"] >= 0.80
    m["ok"] = m["count_ok"] and m["match_ok"] and m["lane_ok"] and m["long_ok"]
    return m


def note_diff(srv: list, cs: list, limit: int = 12) -> list[str]:
    out = []
    for i in range(max(len(srv), len(cs))):
        a = srv[i] if i < len(srv) else None
        b = cs[i] if i < len(cs) else None
        if a != b:
            out.append(f"      #{i}: server {a}  c# {b}")
            if len(out) >= limit:
                out.append("      ...")
                break
    return out


def compare(dump: Path, inputs: list[Path]) -> int:
    bad = 0
    rows = []
    for p in inputs:
        name, pcm = load_pcm(p)
        cpath = dump / f"{name}.csharp.json"
        if not cpath.is_file():
            print(f"{name}: missing {cpath}")
            bad += 1
            continue
        cs = json.loads(cpath.read_text())
        if cs["sha"] != sha(pcm.astype("<f4")):
            print(f"{name}: PCM sha differs between C# and Python")
            bad += 1
            continue
        t0 = time.perf_counter()
        f = analyze_audio(pcm, SR)
        t_an = time.perf_counter() - t0
        print(f"{name}  ({pcm.size / SR:.1f} s, server analyze {t_an * 1000:.0f} ms, C# analyze "
              f"{'/'.join(f'{t:.0f}' for t in cs['analyze_ms'])} ms)")
        for d in DIFFICULTIES:
            key = seed_key(pcm, d)
            cc = cs["charts"][d]
            assert cc["seed_key"] == key, (cc["seed_key"], key)
            srv = server_notes(f, d, key)
            if isinstance(srv, str) or "error" in cc:
                same = isinstance(srv, str) and "error" in cc and srv.split(": ", 1)[-1] == cc["error"]
                print(f"  {d:<6} {'EXACT (same error)' if same else 'MISMATCH'}  server={srv if isinstance(srv, str) else len(srv)}  c#={cc.get('error', len(cc.get('notes', [])))}")
                rows.append((name, d, "same error" if same else "MISMATCH", ""))
                bad += 0 if same else 1
                continue
            mine = cc["notes"]
            if srv == mine:
                print(f"  {d:<6} EXACT  {len(srv)} notes ({sum(1 for n in srv if n[3] > 0)} long)")
                rows.append((name, d, "EXACT", f"{len(srv)} notes"))
                continue
            m = metrics(srv, mine)
            print(f"  {d:<6} DIFF   count {m['count']}  matched±15ms {m['match']:.1%}  lane {m['lane']:.1%}  "
                  f"long {m['long']}  -> {'within tolerance' if m['ok'] else 'OUT OF TOLERANCE'}")
            print("\n".join(note_diff(srv, mine)))
            rows.append((name, d, "within tol" if m["ok"] else "FAIL", f"{m['count']} {m['match']:.1%} {m['lane']:.1%} {m['long']}"))
            bad += 0 if m["ok"] else 1
    exact = sum(1 for r in rows if r[2] in ("EXACT", "same error"))
    print(f"\n{exact}/{len(rows)} charts exact; {'ALL OK' if bad == 0 else f'{bad} FAILED'}")
    return 0 if bad == 0 else 1


def main() -> int:
    if len(sys.argv) >= 4 and sys.argv[1] == "decode":
        from app.audio.decode import decode_mono
        y = decode_mono(Path(sys.argv[2]), SR, timeout=300)
        y.astype("<f4").tofile(sys.argv[3])
        print(f"{sys.argv[3]}: {y.size} samples ({y.size / SR:.1f} s)")
        return 0
    if len(sys.argv) >= 4 and sys.argv[1] == "compare":
        return compare(Path(sys.argv[2]), [Path(a) for a in sys.argv[3:]])
    print(__doc__)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
