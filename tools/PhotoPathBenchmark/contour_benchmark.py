"""Measure full closed contours using annotation-derived anchors; no dataset is redistributed.

Usage: python contour_benchmark.py DATASET.zip DOTNET_EXE EMPTY_WORK_DIRECTORY [ANCHORS [JITTER_PIXELS [development|holdout]]]
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

if len(sys.argv) not in (4, 5, 6, 7):
    raise SystemExit("Expected DATASET.zip DOTNET_EXE EMPTY_WORK_DIRECTORY [ANCHORS [JITTER_PIXELS [development|holdout]]].")
archive, dotnet, work = Path(sys.argv[1]).resolve(), sys.argv[2], Path(sys.argv[3]).resolve()
anchor_count = int(sys.argv[4]) if len(sys.argv) >= 5 else 12
jitter = float(sys.argv[5]) if len(sys.argv) >= 6 else 0.0
selection = sys.argv[6] if len(sys.argv) == 7 else "development"
if selection not in ("development", "holdout"):
    raise SystemExit("Selection must be development or holdout.")
if not 3 <= anchor_count <= 17:
    raise SystemExit("Anchor count must be between 3 and 17.")
if not math.isfinite(jitter) or not 0 <= jitter <= 10:
    raise SystemExit("Jitter must be finite and between 0 and 10 pixels.")
work.mkdir(parents=True, exist_ok=True)
if any(work.iterdir()):
    raise SystemExit("Work directory must be empty; existing files are not overwritten.")
root, prefix = Path(__file__).resolve().parent, "artf-test_resized_512x256/"
jobs, contours = [], []
clamped_anchors = 0
with zipfile.ZipFile(archive) as z:
    data = json.loads(z.read(prefix + "annotations.json"))
    images = {i["id"]: i for i in data["images"]}
    names = {c["id"]: c["name"] for c in data["categories"]}
    choices = collections.defaultdict(list)
    for ann in data["annotations"]:
        if names[ann["category_id"]] in ("shorts", "tshirt") and ann.get("segmentation"):
            choices[names[ann["category_id"]]].append(ann)
    rng = random.Random(73114)
    development = [(category, a) for category in sorted(choices)
                   for a in rng.sample(sorted(choices[category], key=lambda a: a["id"]), 40)]
    chosen = development
    if selection == "holdout":
        # Exclude both previously selected image IDs and byte-identical images
        # under other IDs. This does not rule out visually similar photographs.
        old_ids = {a["image_id"] for _, a in development}
        def image_hash(image_id):
            return hashlib.sha256(z.read(prefix + images[image_id]["file_name"])).digest()
        used_hashes = {image_hash(image_id) for image_id in old_ids}
        used_ids = set(old_ids)
        rng = random.Random(451827)
        chosen = []
        for category in sorted(choices):
            by_image = {}
            for ann in sorted(choices[category], key=lambda a: a["id"]):
                if ann["image_id"] not in old_ids and any(len(p) >= 6 for p in ann["segmentation"]):
                    by_image.setdefault(ann["image_id"], ann)
            image_ids = sorted(by_image)
            rng.shuffle(image_ids)
            count = 0
            for image_id in image_ids:
                if image_id in used_ids:
                    continue
                digest = image_hash(image_id)
                if digest in used_hashes:
                    continue
                chosen.append((category, by_image[image_id]))
                used_ids.add(image_id); used_hashes.add(digest); count += 1
                if count == 40:
                    break
            if count != 40:
                raise SystemExit(f"Not enough distinct holdout images for {category}: {count}.")
    for category, ann in chosen:
        polygon = max((p for p in ann["segmentation"] if len(p) >= 6), key=len)
        points = list(zip(polygon[::2], polygon[1::2]))
        points.append(points[0])
        lengths = [math.dist(a, b) for a, b in zip(points, points[1:])]
        perimeter = sum(lengths)
        anchors, index, traversed = [], 0, 0.0
        for k in range(anchor_count):
            distance = perimeter * k / anchor_count
            while index < len(lengths) - 1 and traversed + lengths[index] < distance:
                traversed += lengths[index]
                index += 1
            t = (distance - traversed) / lengths[index] if lengths[index] else 0
            a, b = points[index:index + 2]
            anchors.append((a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1])))
        info = images[ann["image_id"]]
        if jitter:
            # A deterministic stress test, not a model of real user clicks.
            # Seed each annotation independently to keep the paired selection stable.
            jitter_rng = random.Random(982451653 + ann["id"])
            shifted = []
            for x, y in anchors:
                angle = jitter_rng.uniform(0, math.tau)
                px, py = x + jitter * math.cos(angle), y + jitter * math.sin(angle)
                clamped = (max(0.0, min(info["width"] - 1.0, px)),
                           max(0.0, min(info["height"] - 1.0, py)))
                clamped_anchors += clamped != (px, py)
                shifted.append(clamped)
            anchors = shifted
        image = work / (str(info["id"]) + ".png")
        if not image.exists():
            image.write_bytes(z.read(prefix + info["file_name"]))
        ids = []
        for a, b in zip(anchors, anchors[1:] + anchors[:1]):
            ids.append(len(jobs))
            jobs.append({"Id": ids[-1], "Image": str(image), "X1": a[0], "Y1": a[1], "X2": b[0], "Y2": b[1]})
        contours.append((category, points, anchors, ids, ann["id"]))

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
for category, truth, anchors, ids, annotation_id in contours:
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
                     proper_crossings(proposed) if accepted else None, True, annotation_id))
    else:
        rows.append((category, error(baseline, truth), None, None, False, annotation_id))

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
report = {"archive_sha256": digest, "method": f"Same 80 selected test annotations as earlier experiments; {anchor_count} equally spaced perimeter anchors from ground truth; {anchor_count} independent product path searches including closing interval; symmetric mean and maximum sampled nearest-segment distance on complete contours only; strict crossings only, no full topology or garment-pattern validation", "categories": summary}
if selection == "holdout":
    report["method"] = report["method"].replace("Same 80 selected test annotations as earlier experiments", "80 holdout annotations from distinct images, excluding development image IDs and byte-identical files")
    report["selection"] = {"name": "holdout", "seed": 451827,
                           "development_image_ids": sorted(old_ids),
                           "holdout_image_ids": [a["image_id"] for _, a in chosen]}
if jitter:
    report["method"] += f"; anchors independently shifted {jitter:g} pixels in deterministic random directions and clamped to image bounds; synthetic perturbation, not observed user clicks"
    report["jitter_pixels"] = jitter
    report["jitter_seed_base"] = 982451653
    report["clamped_anchors"] = clamped_anchors
(work / "measurements.json").write_text(json.dumps([
    {"ordinal": i, "annotation_id": r[5], "category": r[0], "straight_mean": r[1][0], "straight_max": r[1][1],
     "proposal_mean": r[2][0] if r[2] is not None else None, "proposal_max": r[2][1] if r[2] is not None else None,
     "strict_crossing": r[3], "all_segments_found": r[4]} for i, r in enumerate(rows)
], indent=2), encoding="utf-8")
def sha256_file(path):
    with path.open("rb") as file:
        return hashlib.file_digest(file, "sha256").hexdigest()
report["pairing"] = {"version": 1, "jobs_sha256": sha256_file(input_file),
                     "results_sha256": sha256_file(output_file),
                     "measurements_sha256": sha256_file(work / "measurements.json"),
                     "images_sha256": [sha256_file(Path(jobs[i*anchor_count]["Image"])) for i in range(80)]}
(work / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
