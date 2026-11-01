"""Evaluate local path proposals with annotated endpoints; no dataset is redistributed.

Usage: python benchmark.py DATASET.zip DOTNET_EXE EMPTY_WORK_DIRECTORY
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
jobs, targets, categories = [], {}, {}
with zipfile.ZipFile(archive) as z:
    data = json.loads(z.read(prefix+"annotations.json"))
    images = {i["id"]: i for i in data["images"]}
    names = {c["id"]: c["name"] for c in data["categories"]}
    choices = collections.defaultdict(list)
    for ann in data["annotations"]:
        if names[ann["category_id"]] in ("shorts", "tshirt") and ann.get("segmentation"):
            choices[names[ann["category_id"]]].append(ann)
    rng = random.Random(73114)
    chosen = []
    for category in sorted(choices):
        chosen.extend((category, a) for a in rng.sample(sorted(choices[category], key=lambda a:a["id"]), 40))
    for category, ann in chosen:
        info = images[ann["image_id"]]
        polygon = max((p for p in ann["segmentation"] if len(p)>=6), key=len)
        points = list(zip(polygon[::2],polygon[1::2]))
        image = work / (str(info["id"])+".png")
        image.write_bytes(z.read(prefix+info["file_name"]))
        for k in range(5):
            i=k*len(points)//5
            target=[points[i]]
            length=0.0
            while length<40 and len(target)<len(points):
                nxt=points[(i+len(target))%len(points)]
                length+=math.dist(target[-1],nxt);target.append(nxt)
            if len(target)<2 or length<40 or math.dist(target[0],target[-1])<2:
                continue
            ident=len(jobs);a,b=target[0],target[-1]
            jobs.append({"Id":ident,"Image":str(image),"X1":a[0],"Y1":a[1],"X2":b[0],"Y2":b[1]})
            targets[ident]=target;categories[ident]=category

input_file, output_file=work/"jobs.json",work/"results.json"
input_file.write_text(json.dumps(jobs),encoding="utf-8")
subprocess.run([dotnet,"build",str(root/"PhotoPathBenchmark.csproj"),"-c","Release","--nologo","-v:q"],check=True)
subprocess.run([dotnet,str(root/"bin/Release/net10.0-windows/PhotoPathBenchmark.dll"),str(input_file),str(output_file)],check=True)
results=json.loads(output_file.read_text(encoding="utf-8"))
assert len(results)==len(jobs) and len({r["Id"] for r in results})==len(jobs)

def samples(polyline):
    out=[]
    for a,b in zip(polyline,polyline[1:]):
        n=max(1,math.ceil(math.dist(a,b)))
        out.extend((a[0]+(b[0]-a[0])*i/n,a[1]+(b[1]-a[1])*i/n) for i in range(n))
    out.append(tuple(polyline[-1]))
    return out

def mean_distance(source,target):
    out=[]
    for x,y in samples(source):
        best=float("inf")
        for a,b in zip(target,target[1:]):
            dx,dy=b[0]-a[0],b[1]-a[1];den=dx*dx+dy*dy
            t=max(0,min(1,((x-a[0])*dx+(y-a[1])*dy)/den)) if den else 0
            best=min(best,math.hypot(x-a[0]-t*dx,y-a[1]-t*dy))
        out.append(best)
    return sum(out)/len(out)

def error(a,b):
    return (mean_distance(a,b)+mean_distance(b,a))/2

summaries={}
for category in ("shorts","tshirt","all"):
    rows=[]
    for r in results:
        i=r["Id"]
        if category!="all" and categories[i]!=category:continue
        truth=targets[i];baseline=[truth[0],truth[-1]]
        rows.append((error(baseline,truth),error(r["Path"],truth) if r["Found"] else error(baseline,truth),r["Found"]))
    summaries[category]={"segments":len(rows),"found":sum(r[2] for r in rows),
        "mean_straight":sum(r[0] for r in rows)/len(rows),"mean_proposed":sum(r[1] for r in rows)/len(rows),
        "improved_gt_half_pixel":sum(a-b>.5 for a,b,_ in rows),"worsened_gt_half_pixel":sum(b-a>.5 for a,b,_ in rows)}
with archive.open("rb") as f: digest=hashlib.file_digest(f,"sha256").hexdigest()
report={"archive_sha256":digest,"image_count":len(chosen),"method":"40 images/category; 5 segments/image; >=40-pixel annotation subpaths; annotated endpoints; symmetric mean nearest-segment distance; straight line on failure","categories":summaries}
(work/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print(json.dumps(report,indent=2))
