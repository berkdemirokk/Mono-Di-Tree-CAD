"""Measure full closed contours using annotation-derived anchors; no dataset is redistributed.

Usage: python contour_benchmark.py DATASET.zip DOTNET_EXE EMPTY_WORK_DIRECTORY
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
root, prefix = Path(__file__).resolve().parent, "artf-test_resized_512x256/"
jobs, contours = [], []
with zipfile.ZipFile(archive) as z:
    data = json.loads(z.read(prefix + "annotations.json"))
    images = {i["id"]: i for i in data["images"]}
    names = {c["id"]: c["name"] for c in data["categories"]}
    choices = collections.defaultdict(list)
    for ann in data["annotations"]:
        if names[ann["category_id"]] in ("shorts", "tshirt") and ann.get("segmentation"):
            choices[names[ann["category_id"]]].append(ann)
    rng = random.Random(73114)
    chosen = [(category, a) for category in sorted(choices)
              for a in rng.sample(sorted(choices[category], key=lambda a: a["id"]), 40)]
    for category, ann in chosen:
        polygon = max((p for p in ann["segmentation"] if len(p) >= 6), key=len)
        points = list(zip(polygon[::2], polygon[1::2]))
        points.append(points[0])
        lengths = [math.dist(a, b) for a, b in zip(points, points[1:])]
        perimeter = sum(lengths)
        anchors, index, traversed = [], 0, 0.0
        for k in range(12):
            distance = perimeter * k / 12
            while index < len(lengths) - 1 and traversed + lengths[index] < distance:
                traversed += lengths[index]
                index += 1
            t = (distance - traversed) / lengths[index] if lengths[index] else 0
            a, b = points[index:index + 2]
            anchors.append((a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1])))
        info = images[ann["image_id"]]
        image = work / (str(info["id"]) + ".png")
        if not image.exists():
            image.write_bytes(z.read(prefix + info["file_name"]))
        ids = []
        for a, b in zip(anchors, anchors[1:] + anchors[:1]):
            ids.append(len(jobs))
            jobs.append({"Id": ids[-1], "Image": str(image), "X1": a[0], "Y1": a[1], "X2": b[0], "Y2": b[1]})
        contours.append((category, points, anchors, ids))

input_file, output_file = work / "jobs.json", work / "results.json"
input_file.write_text(json.dumps(jobs), encoding="utf-8")
subprocess.run([dotnet, "build", str(root / "PhotoPathBenchmark.csproj"), "-c", "Release", "--nologo", "-v:q"], check=True)
subprocess.run([dotnet, str(root / "bin/Release/net10.0-windows/PhotoPathBenchmark.dll"), str(input_file), str(output_file)], check=True)
results = json.loads(output_file.read_text(encoding="utf-8"))
assert len(results) == len(jobs) and len({r["Id"] for r in results}) == len(jobs)
results = {r["Id"]: r for r in results}

def samples(polyline):
    out = []
    for a, b in zip(polyline, polyline[1:]):
        n = max(1, math.ceil(math.dist(a, b)))
        out.extend((a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n) for i in range(n))
    return out

def mean_distance(source, target):
    segments = []
    for a, b in zip(target, target[1:]):
        dx, dy = b[0] - a[0], b[1] - a[1]
        segments.append((a[0], a[1], dx, dy, dx * dx + dy * dy))
    distances = []
    for x, y in samples(source):
        best = float("inf")
        for ax, ay, dx, dy, den in segments:
            t = max(0, min(1, ((x - ax) * dx + (y - ay) * dy) / den)) if den else 0
            best = min(best, (x - ax - t * dx) ** 2 + (y - ay - t * dy) ** 2)
        distances.append(math.sqrt(best))
    return sum(distances) / len(distances), max(distances)

def error(a, b):
    ab, ba = mean_distance(a, b), mean_distance(b, a)
    return (ab[0] + ba[0]) / 2, max(ab[1], ba[1])

def proper_crossings(poly):
    # Strict crossings only: touching and collinear overlaps are not counted.
    def orient(a, b, c):
        return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    segments = list(zip(poly, poly[1:]))
    for i, (a, b) in enumerate(segments):
        for j in range(i+2, len(segments)):
            if i == 0 and j == len(segments)-1:
                continue
            c, d = segments[j]
            if orient(a,b,c)*orient(a,b,d) < 0 and orient(c,d,a)*orient(c,d,b) < 0:
                return True
    return False

def product_interior(path):
    for a, p, b in zip(path, path[1:], path[2:]):
        if (p[0]-a[0])*(b[1]-p[1]) == (p[1]-a[1])*(b[0]-p[0]) and (p[0]-a[0])*(b[0]-p[0])+(p[1]-a[1])*(b[1]-p[1]) > 0:
            continue
        yield p

rows = []
for category, truth, anchors, ids in contours:
    baseline = anchors + anchors[:1]
    if all(results[i]["Found"] for i in ids):
        # Product retains the selected, possibly fractional, anchors instead of rounded search endpoints.
        proposed = [anchors[0]]
        accepted = True
        for j, i in enumerate(ids):
            for p in product_interior(results[i]["Path"]):
                if tuple(p) == tuple(proposed[-1]) or (j == len(ids)-1 and tuple(p) == tuple(anchors[0])):
                    accepted = False
                proposed.append(p)
            end = anchors[(j + 1) % len(anchors)]
            if tuple(end) == tuple(proposed[-1]):
                accepted = False
            if j != len(ids)-1:
                proposed.append(end)
        # Two additional reference points consume the product's 512-point limit.
        accepted = accepted and len(proposed) <= 510 and len(proposed) > len(anchors)
        proposed.append(anchors[0])
        rows.append((category, error(baseline, truth), error(proposed, truth) if accepted else None,
                     proper_crossings(proposed) if accepted else None, True))
    else:
        rows.append((category, error(baseline, truth), None, None, False))

summary = {}
for category in ("shorts", "tshirt", "all"):
    subset = [r for r in rows if category == "all" or r[0] == category]
    complete = [r for r in subset if r[2] is not None]
    summary[category] = {"contours": len(subset), "all_segments_found": sum(r[4] for r in subset), "proposal_accepted_by_simulated_point_checks": len(complete),
        "mean_straight_complete": sum(r[1][0] for r in complete) / len(complete) if complete else None,
        "mean_proposed_complete": sum(r[2][0] for r in complete) / len(complete) if complete else None,
        "improved_gt_half_pixel": sum(r[1][0] - r[2][0] > .5 for r in complete),
        "worsened_gt_half_pixel": sum(r[2][0] - r[1][0] > .5 for r in complete),
        "max_contour_mean_error": max((r[2][0] for r in complete), default=None),
        "max_sampled_local_error": max((r[2][1] for r in complete), default=None),
        "local_error_over_5_pixels": sum(r[2][1] > 5 for r in complete),
        "strict_self_crossing": sum(r[3] for r in complete)}
with archive.open("rb") as f:
    digest = hashlib.file_digest(f, "sha256").hexdigest()
report = {"archive_sha256": digest, "method": "Same 80 selected test annotations as earlier experiments; 12 equally spaced perimeter anchors from ground truth; 12 independent product path searches including closing interval; symmetric mean and maximum sampled nearest-segment distance on complete contours only; strict crossings only, no full topology or garment-pattern validation", "categories": summary}
(work / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
