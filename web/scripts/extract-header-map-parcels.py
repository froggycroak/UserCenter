"""Extract city-block regions from header-map-ref.png → JSON for canvas line art."""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import ConvexHull

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "public" / "images" / "header-map-ref.png"
OUT = ROOT / "src" / "data" / "header-map-parcels.json"

SRC_W = 1804
SRC_H = 872

MIN_BLOCK_AREA = 280
MAX_BLOCK_AREA = 95_000
MIN_FOOTPRINT_AREA = 28
MAX_FOOTPRINT_AREA = 279


def mask_to_hull_polygon(mask: np.ndarray) -> list[list[float]] | None:
    eroded = ndimage.binary_erosion(mask)
    boundary = mask & ~eroded
    ys, xs = np.where(boundary)
    if len(xs) < 4:
        ys, xs = np.where(mask)
    if len(xs) < 4:
        return None
    pts = np.column_stack([xs.astype(np.float64), ys.astype(np.float64)])
    if len(pts) > 400:
        step = max(1, len(pts) // 400)
        pts = pts[::step]
    try:
        hull = ConvexHull(pts)
    except Exception:
        return None
    verts = pts[hull.vertices]
    return [[float(x), float(y)] for x, y in verts]


def touches_outer_border(mask: np.ndarray, pad: int) -> bool:
    return bool(
        mask[pad, :].any()
        or mask[-pad - 1, :].any()
        or mask[:, pad].any()
        or mask[:, -pad - 1].any()
    )


def collect_regions(
    labeled: np.ndarray,
    min_area: int,
    max_area: int,
    pad: int,
    skip_border: bool,
) -> list[list[list[float]]]:
    out: list[list[list[float]]] = []
    for label_id in range(1, int(labeled.max()) + 1):
        mask = labeled == label_id
        area = int(mask.sum())
        if area < min_area or area > max_area:
            continue
        if skip_border and touches_outer_border(mask, pad):
            continue
        poly = mask_to_hull_polygon(mask)
        if poly and len(poly) >= 3:
            out.append(poly)
    return out


def main() -> int:
    if not SRC.is_file():
        print(f"Missing {SRC}", file=sys.stderr)
        return 1

    gray = np.array(Image.open(SRC).convert("L"))
    h, w = gray.shape
    if w != SRC_W or h != SRC_H:
        print(f"Expected {SRC_W}x{SRC_H}, got {w}x{h}", file=sys.stderr)

    black = gray < 200
    black = ndimage.binary_dilation(black, iterations=1)
    white = ~black
    pad = 2
    white_pad = np.pad(white, pad, constant_values=False)
    labeled, _ = ndimage.label(white_pad)

    blocks = collect_regions(
        labeled, MIN_BLOCK_AREA, MAX_BLOCK_AREA, pad, skip_border=True
    )
    footprints = collect_regions(
        labeled, MIN_FOOTPRINT_AREA, MAX_FOOTPRINT_AREA, pad, skip_border=False
    )

    payload = {
        "width": SRC_W,
        "height": SRC_H,
        "blocks": blocks,
        "footprints": footprints,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(payload), encoding="utf-8")
    print(
        f"Wrote {OUT} — blocks {len(blocks)}, footprints {len(footprints)}, "
        f"{OUT.stat().st_size // 1024} KiB"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
