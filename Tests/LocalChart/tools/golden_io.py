"""LCG1 container: gzip( b"LCG1" + u32 header_len + JSON header + raw little-endian blobs ).

Header: {"meta": {...}, "arrays": [{"name", "dtype", "shape", "offset", "nbytes"}]}
dtype is a numpy little-endian code without the '<' ("f4", "f8", "i1", "i4", "i8", "u1").
Floats are stored as their exact bytes, so C# and Python compare bit for bit.
Read by Tests/LocalChart/GoldenIO.cs.
"""
from __future__ import annotations

import gzip
import json
import struct
from pathlib import Path

import numpy as np

MAGIC = b"LCG1"
_DTYPES = {"float32": "f4", "float64": "f8", "int8": "i1", "int32": "i4", "int64": "i8", "uint8": "u1", "bool": "u1"}


def write(path: Path, arrays: dict[str, np.ndarray], meta: dict) -> None:
    entries, blobs, off = [], [], 0
    for name, a in arrays.items():
        a = np.asarray(a)
        code = _DTYPES[str(a.dtype)]
        b = np.ascontiguousarray(a.astype("<" + code if code != "u1" else np.uint8)).tobytes()
        entries.append({"name": name, "dtype": code, "shape": list(a.shape), "offset": off, "nbytes": len(b)})
        blobs.append(b)
        off += len(b)
    header = json.dumps({"meta": meta, "arrays": entries}, separators=(",", ":")).encode("utf-8")
    raw = MAGIC + struct.pack("<I", len(header)) + header + b"".join(blobs)
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with gzip.GzipFile(path, "wb", compresslevel=9, mtime=0) as f:
        f.write(raw)


def read(path: Path) -> tuple[dict[str, np.ndarray], dict]:
    with gzip.open(path, "rb") as f:
        raw = f.read()
    if raw[:4] != MAGIC:
        raise ValueError(f"{path}: not an LCG1 file")
    (hlen,) = struct.unpack("<I", raw[4:8])
    header = json.loads(raw[8 : 8 + hlen].decode("utf-8"))
    base = 8 + hlen
    arrays = {}
    for e in header["arrays"]:
        buf = raw[base + e["offset"] : base + e["offset"] + e["nbytes"]]
        arrays[e["name"]] = np.frombuffer(buf, dtype="<" + e["dtype"]).reshape(e["shape"]).copy()
    return arrays, header["meta"]
