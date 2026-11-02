"""Measure horizontal deviations from the recorded synthetic boundary.

Usage: python measure_isoluminant.py BASELINE_JSON RGB_JSON
This single constructed example does not measure garment accuracy.
"""
import json
from pathlib import Path
import sys

if len(sys.argv) != 3:
    raise SystemExit("Expected baseline and RGB result JSON paths.")

def summarize(path):
    jobs = json.loads(Path(path).read_text(encoding="utf-8"))
    if len(jobs) != 1 or jobs[0].get("Id") != 0 or not jobs[0].get("Found"):
        raise SystemExit(f"Expected one successful fixture result in {path}.")
    points = jobs[0].get("Path", [])
    if not points or points[0] != [16, 4] or points[-1] != [16, 56]:
        raise SystemExit(f"Unexpected fixture endpoints in {path}.")
    deviations = []
    for point in points:
        if len(point) != 2 or any(type(v) is not int for v in point):
            raise SystemExit("Expected integer pixel coordinates.")
        x, y = point
        if not 4 <= y <= 56:
            raise SystemExit("Fixture path left the expected vertical range.")
        expected_x = 16 + max(0, (y - 4) * (56 - y)) / 80
        deviations.append(abs(x - expected_x))
    return {"points": len(points), "mean_horizontal_pixels": sum(deviations) / len(deviations),
            "max_horizontal_pixels": max(deviations), "x_range": [min(p[0] for p in points), max(p[0] for p in points)]}

print(json.dumps({"metric": "absolute horizontal distance from analytic boundary at each path pixel; not shortest geometric distance",
                  "baseline": summarize(sys.argv[1]), "rgb": summarize(sys.argv[2])}, indent=2))
