"""Compare paired outputs of contour_benchmark.py without redistributing images.

Usage: python compare_contours.py FIRST_WORK_DIR SECOND_WORK_DIR OUTPUT.json
"""
import json
from pathlib import Path
import statistics
import sys

first, second, destination = map(Path, sys.argv[1:])
a_summary = json.loads((first / "summary.json").read_text(encoding="utf-8"))
b_summary = json.loads((second / "summary.json").read_text(encoding="utf-8"))
if a_summary["archive_sha256"] != b_summary["archive_sha256"]:
    raise SystemExit("Dataset hashes differ.")
a = json.loads((first / "measurements.json").read_text(encoding="utf-8"))
b = json.loads((second / "measurements.json").read_text(encoding="utf-8"))
if len(a) != len(b) or any((x["ordinal"], x["annotation_id"], x["category"]) != (y["ordinal"], y["annotation_id"], y["category"]) for x, y in zip(a, b)):
    raise SystemExit("Selections or ordering differ.")
paired = [(x, y) for x, y in zip(a, b) if x["proposal_mean"] is not None and y["proposal_mean"] is not None]
result = {}
for category in ("shorts", "tshirt", "all"):
    pairs = [(x, y) for x, y in paired if category == "all" or x["category"] == category]
    mean_delta = [y["proposal_mean"] - x["proposal_mean"] for x, y in pairs]
    local_delta = [y["proposal_max"] - x["proposal_max"] for x, y in pairs]
    result[category] = {"paired": len(pairs),
        "mean_error_change": statistics.mean(mean_delta) if pairs else None,
        "mean_improved_gt_half": sum(d < -.5 for d in mean_delta),
        "mean_worsened_gt_half": sum(d > .5 for d in mean_delta),
        "worst_mean_increase": max(mean_delta, default=None),
        "local_max_improved_gt_one": sum(d < -1 for d in local_delta),
        "local_max_worsened_gt_one": sum(d > 1 for d in local_delta),
        "both_local_over_5": sum(x["proposal_max"] > 5 and y["proposal_max"] > 5 for x, y in pairs),
        "only_first_local_over_5": sum(x["proposal_max"] > 5 and y["proposal_max"] <= 5 for x, y in pairs),
        "only_second_local_over_5": sum(x["proposal_max"] <= 5 and y["proposal_max"] > 5 for x, y in pairs),
        "strict_crossing_first": sum(x["strict_crossing"] for x, _ in pairs),
        "strict_crossing_second": sum(y["strict_crossing"] for _, y in pairs)}
report = {"archive_sha256": a_summary["archive_sha256"], "first_method": a_summary["method"],
          "second_method": b_summary["method"], "total_first": len(a), "total_second": len(b), "categories": result}
if destination.exists():
    raise SystemExit("Output already exists; it will not be overwritten.")
destination.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
