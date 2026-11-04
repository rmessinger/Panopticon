#!/usr/bin/env python3
"""Header-only LiDAR worker for the sedan integrity producer.

Runs ONLY inside the isolated lidar venv (laspy 2.x). Executed under any other
interpreter it prints a clear error and exits 3; it never installs anything.
Reads header metadata via laspy.open and never touches point data: no
chunk_iterator, read_points, point access or Z access.

Request JSON (single file argument): {"tile_path": str, "output_path": str}.
Writes one small JSON object to output_path and a one-line summary to stdout.
Exit 0 on success, 3 on any failure.
"""

import json
import math
import os
import sys

EXIT_OK = 0
EXIT_FAIL = 3


def fail(message):
    print(f"sedan_lidar_header_worker: {message}", file=sys.stderr, flush=True)
    sys.exit(EXIT_FAIL)


def load_request(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            request = json.load(handle)
    except (OSError, ValueError) as error:
        fail(f"request is not readable JSON: {error}")
    if not isinstance(request, dict):
        fail("request is not a JSON object")
    for key in ("tile_path", "output_path"):
        if not isinstance(request.get(key), str) or not request[key]:
            fail(f"request carries no non-empty {key}")
    return request


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def read_header(tile_path):
    import laspy

    with laspy.open(tile_path) as reader:
        header = reader.header
        crs = None
        try:
            crs_obj = header.parse_crs()
            if crs_obj is not None:
                epsg = crs_obj.to_epsg()
                crs = {"epsg": epsg, "name": crs_obj.name}
        except Exception:
            crs = None
        return {
            "las_version": f"{header.version.major}.{header.version.minor}",
            "point_format_id": int(header.point_format.id),
            "point_count": int(header.point_count),
            "x_min": float(header.x_min),
            "x_max": float(header.x_max),
            "y_min": float(header.y_min),
            "y_max": float(header.y_max),
            "x_scale": float(header.x_scale),
            "y_scale": float(header.y_scale),
            "z_scale": float(header.z_scale),
            "x_offset": float(header.x_offset),
            "y_offset": float(header.y_offset),
            "z_offset": float(header.z_offset),
            "crs": crs,
        }


def main(argv):
    if len(argv) != 2:
        fail("usage: sedan_lidar_header_worker.py REQUEST_JSON")
    try:
        import laspy  # noqa: F401  (isolated venv only; never installed from here)
    except ImportError as error:
        fail(
            "laspy is unavailable from this interpreter; this worker runs ONLY inside the "
            "isolated lidar venv (<cache>/tools/lidar-venv); never run it with the system "
            f"python and never install anything from here (import error: {error})"
        )
    request = load_request(argv[1])
    tile_path = request["tile_path"]
    output_path = request["output_path"]
    if not os.path.isfile(tile_path):
        fail(f"tile missing at {tile_path}")
    header = read_header(tile_path)
    payload = {
        "header_version": 1,
        "tile_path": tile_path,
        "tile_bytes": os.path.getsize(tile_path),
        "header": header,
    }
    text = json.dumps(payload, sort_keys=True, allow_nan=False)
    try:
        with open(output_path, "x", encoding="utf-8") as handle:
            handle.write(text)
    except (OSError, ValueError) as error:
        fail(f"cannot write header JSON at {output_path}: {error}")
    print(f"header ok: {header['point_count']} points, las {header['las_version']}, "
          f"format {header['point_format_id']}", flush=True)
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main(sys.argv))
