"""Summarize production-canvas/validator observations against contour measurements.

Usage: python summarize.py TOPOLOGY.json MEASUREMENTS.json CONTOUR_SUMMARY.json NEW_OUTPUT.json
"""
import json
from pathlib import Path
import sys

if len(sys.argv) != 5:
    raise SystemExit("Expected topology, measurements, contour summary and a new output path.")
topology_path, measurements_path, summary_path, output_path = map(Path, sys.argv[1:])
if output_path.exists():
    raise SystemExit("Output already exists; it will not be overwritten.")
topology = json.loads(topology_path.read_text(encoding="utf-8"))
measurements = json.loads(measurements_path.read_text(encoding="utf-8"))
source = json.loads(summary_path.read_text(encoding="utf-8"))
if len(topology) != 80 or len(measurements) != 80 or any(
    t["Ordinal"] != i or m["ordinal"] != i for i, (t, m) in enumerate(zip(topology, measurements))
):
    raise SystemExit("Expected 80 ordered matching contours.")
if source.get("selection", {}).get("name") != "holdout":
    raise SystemExit("Expected the holdout contour experiment.")
result = {}
for category in ("shorts", "tshirt", "all"):
    pairs = [(t, m) for t, m in zip(topology, measurements) if category == "all" or m["category"] == category]
    complete = [(t, m) for t, m in pairs if t["ProposalAccepted"]]
    valid = [(t, m) for t, m in complete if t["PixelGeometryValid"] is True]
    if any(m["proposal_max"] is None for _, m in valid):
        raise SystemExit("A geometrically valid proposal has no corresponding distance measurement.")
    result[category] = {
        "contours": len(pairs),
        "points_accepted": sum(t["PointsAccepted"] for t, _ in pairs),
        "proposals_accepted": len(complete),
        "pixel_geometry_invalid": sum(t["PixelGeometryValid"] is False for t, _ in complete),
        "crossing_pair_found": sum(t["CrossingPairFound"] is True for t, _ in complete),
        "strict_crossing_counter": sum(m["strict_crossing"] is True for _, m in complete),
        "strict_crossing_not_flagged": sum(m["strict_crossing"] is True and t["PixelGeometryValid"] is not False for t, m in complete),
        "strict_crossing_without_pair": sum(m["strict_crossing"] is True and t["CrossingPairFound"] is not True for t, m in complete),
        "pixel_geometry_valid": len(valid),
        "geometry_valid_local_over_5_pixels": sum(m["proposal_max"] > 5 for _, m in valid),
        "geometry_valid_max_sampled_local_pixels": max((m["proposal_max"] for _, m in valid), default=None),
    }
report = {"archive_sha256": source["archive_sha256"], "contour_method": source["method"],
          "jitter_pixels": source.get("jitter_pixels", 0), "categories": result}
with output_path.open("x", encoding="utf-8") as output:
    json.dump(report, output, indent=2)
print(json.dumps(report, indent=2))
