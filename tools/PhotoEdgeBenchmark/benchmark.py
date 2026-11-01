"""Offline benchmark against the aRTF test split; dataset remains outside the repository.

Usage: python benchmark.py DATASET.zip DOTNET_EXE WORK_DIR
The work directory receives temporary images and JSON. No images are redistributed.
"""
import collections
import hashlib
import json
import math
from pathlib import Path
import random
import subprocess
import sys
import zipfile

archive, dotnet, work = Path(sys.argv[1]).resolve(), sys.argv[2], Path(sys.argv[3]).resolve()
work.mkdir(parents=True, exist_ok=True)
if any(work.iterdir()):
    raise SystemExit("Work directory must be empty; existing files are not overwritten.")
root = Path(__file__).resolve().parent
prefix = "artf-test_resized_512x256/"
with zipfile.ZipFile(archive) as z:
    data = json.loads(z.read(prefix + "annotations.json"))
    images = {i["id"]: i for i in data["images"]}
    categories = {c["id"]: c["name"] for c in data["categories"]}
    by_category = collections.defaultdict(list)
    for ann in data["annotations"]:
        if categories[ann["category_id"]] in ("shorts", "tshirt") and ann.get("segmentation"):
            by_category[categories[ann["category_id"]]].append(ann)
    rng = random.Random(73114)
    chosen = []
    for category in sorted(by_category):
        population = sorted(by_category[category], key=lambda a: a["id"])
        chosen.extend((category, a) for a in rng.sample(population, min(40, len(population))))
    jobs, shapes, category_for = [], {}, {}
    for category, ann in chosen:
        info = images[ann["image_id"]]
        segments = [p for p in ann["segmentation"] if len(p) >= 6]
        if not segments:
            continue
        polygon = max(segments, key=len)
        points = list(zip(polygon[::2], polygon[1::2]))
        image_path = work / (str(info["id"]) + ".png")
        image_path.write_bytes(z.read(prefix + info["file_name"]))
        for k in range(20):
            i = k * len(points) // 20
            x, y = points[i]
            before, after = points[(i-3) % len(points)], points[(i+3) % len(points)]
            dx, dy = after[0]-before[0], after[1]-before[1]
            length = math.hypot(dx, dy)
            if length == 0:
                continue
            for sign in (-1, 1):
                sx, sy = x + sign*4*(-dy/length), y + sign*4*(dx/length)
                if not (0 <= sx < info["width"]-1 and 0 <= sy < info["height"]-1):
                    continue
                ident = len(jobs)
                jobs.append({"Id": ident, "Image": str(image_path), "X": sx, "Y": sy})
                shapes[ident] = points
                category_for[ident] = category

input_file, output_file = work / "jobs.json", work / "results.json"
input_file.write_text(json.dumps(jobs), encoding="utf-8")
subprocess.run([dotnet, "build", str(root / "PhotoEdgeBenchmark.csproj"), "-c", "Release", "--nologo", "-v:q"], check=True)
subprocess.run([dotnet, str(root / "bin/Release/net10.0-windows/PhotoEdgeBenchmark.dll"), str(input_file), str(output_file)], check=True)
results = json.loads(output_file.read_text(encoding="utf-8"))
assert len(results) == len(jobs) and len({r["Id"] for r in results}) == len(jobs)

def distance(x, y, polygon):
    best = float("inf")
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        dx, dy = b[0]-a[0], b[1]-a[1]
        denominator = dx*dx + dy*dy
        t = max(0, min(1, ((x-a[0])*dx+(y-a[1])*dy)/denominator)) if denominator else 0
        best = min(best, math.hypot(x-a[0]-t*dx, y-a[1]-t*dy))
    return best

summaries = {}
for category in ("shorts", "tshirt", "all"):
    rows = []
    for result in results:
        i = result["Id"]
        if category != "all" and category_for[i] != category:
            continue
        j, polygon = jobs[i], shapes[i]
        before = distance(j["X"], j["Y"], polygon)
        after = distance(result["X"], result["Y"], polygon) if result["Found"] else before
        rows.append((before, after, result["Found"]))
    n = len(rows)
    summaries[category] = {"samples": n, "found": sum(r[2] for r in rows),
        "improved_gt_half_pixel": sum(a-b > .5 for a,b,_ in rows),
        "worsened_gt_half_pixel": sum(b-a > .5 for a,b,_ in rows),
        "within_two_pixels_before": sum(a <= 2 for a,_,_ in rows),
        "within_two_pixels_after": sum(b <= 2 for _,b,_ in rows),
        "mean_before": sum(a for a,_,_ in rows)/n, "mean_after": sum(b for _,b,_ in rows)/n}
report = {"archive_sha256": hashlib.file_digest(archive.open("rb"), "sha256").hexdigest(),
          "image_count": len(chosen), "method": "40 images/category; 20 boundary positions/image; +/-4 pixel local normal; declined suggestions leave seed unchanged; full polygon distance", "categories": summaries}
(work / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
