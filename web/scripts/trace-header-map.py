"""Trace header-map-ref.png to SVG (run when source art changes)."""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import potrace
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "public" / "images" / "header-map-ref.png"
OUT = ROOT / "public" / "images" / "header-map-ref.svg"


def point_str(p) -> str:
    return f"{p.x:.3f} {p.y:.3f}"


def curve_to_d(curve) -> str:
    parts = [f"M {point_str(curve.start_point)}"]
    for seg in curve.segments:
        s = seg._segment
        c = s.c
        if s.tag == potrace.POTRACE_CURVETO:
            parts.append(
                f"C {point_str(c[0])} {point_str(c[1])} {point_str(c[2])}"
            )
        else:
            parts.append(f"L {point_str(c[2])}")
    parts.append("Z")
    return " ".join(parts)


def collect_paths(curve, out: list[str]) -> None:
    out.append(curve_to_d(curve))
    children = curve.children
    if not children:
        return
    for child in children:
        collect_paths(child, out)


def main() -> int:
    if not SRC.is_file():
        print(f"Missing source: {SRC}", file=sys.stderr)
        return 1

    im = Image.open(SRC).convert("L")
    w, h = im.size
    data = np.array(im) < 200
    curves = potrace.Bitmap(data).trace(turdsize=2, alphamax=1.0, opttolerance=0.2)

    ds: list[str] = []
    for curve in curves:
        collect_paths(curve, ds)

    svg = "\n".join(
        [
            '<?xml version="1.0" encoding="UTF-8"?>',
            f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" fill="none">',
            f'  <g fill="#000000" fill-rule="evenodd" stroke="none">',
            *(f'    <path d="{d}"/>' for d in ds),
            "  </g>",
            "</svg>",
        ]
    )
    OUT.write_text(svg, encoding="utf-8")
    print(f"Wrote {OUT} ({len(ds)} paths, {OUT.stat().st_size // 1024} KiB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
