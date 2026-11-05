"""Compare recorded margin results for the two synthetic fixtures.

Usage: python measure_margin_fixtures.py CURVED_DIR BUDGET_DIR NEW_OUTPUT.json
This measures only synthetic behavior, not a garment or production tolerance.
"""
import json
import math
from pathlib import Path
import sys

if len(sys.argv) != 4:
    raise SystemExit("Expected CURVED_DIR BUDGET_DIR NEW_OUTPUT.json.")
curved, budget, destination = map(Path, sys.argv[1:])
if destination.exists():
    raise SystemExit("Output exists and will not be overwritten.")

def load(directory, margin, start, end):
    values = json.loads((directory / f"result{margin}.json").read_text(encoding="utf-8"))
    if len(values) != 1 or values[0]["Id"] != 0:
        raise SystemExit("Unexpected results.")
    result = values[0]
    if result["Found"]:
        if not result["Path"] or result["Path"][0] != list(start) or result["Path"][-1] != list(end):
            raise SystemExit("Unexpected path endpoints.")
    elif result["Path"]:
        raise SystemExit("Rejected path contains points.")
    return result

report = {"method": "Artificial sinusoidal boundary; horizontal point-to-function error, not nearest-boundary or garment error; separate 160000-pixel search-budget fixture", "curved": {}, "budget": {}}
for margin in (16, 32):
    result = load(curved, margin, (60,20), (60,120))
    if not result["Found"]:
        raise SystemExit("Curve path missing.")
    errors = [abs(x - (60 + 24 * math.sin(math.pi * max(0, min(100, y-20)) / 100))) for x, y in result["Path"]]
    report["curved"][str(margin)] = {"points": len(errors), "mean_horizontal_pixels": sum(errors)/len(errors), "max_horizontal_pixels": max(errors)}
    result = load(budget, margin, (80,80), (430,430))
    report["budget"][str(margin)] = {"search_pixels": (350 + 2*margin + 1)**2, "found": result["Found"], "points": len(result["Path"])}
destination.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
