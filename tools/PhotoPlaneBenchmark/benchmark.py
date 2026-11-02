"""Compare planar correction on real checkerboard images; no images redistributed.

Usage: python benchmark.py IMAGE_DIRECTORY OPENCV_PYTHON_DIR DOTNET_EXE EMPTY_WORK_DIR
Requires opencv-python-headless 4.10.0.84 and numpy, used for this offline benchmark only.
The physical square size is unknown: results are in square-side units, not mm.
"""
import hashlib
import json
import math
from pathlib import Path
import random
import statistics
import subprocess
import sys

images, cvdir, dotnet, work = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve(), sys.argv[3], Path(sys.argv[4]).resolve()
work.mkdir(parents=True, exist_ok=True)
if any(work.iterdir()):
    raise SystemExit("Work directory must be empty; existing files are not overwritten.")
sys.path.insert(0,str(cvdir))
import cv2
root=Path(__file__).resolve().parent
files=sorted(images.glob("left*.jpg"))
if not files:raise SystemExit("No left*.jpg images found.")
jobs=[];failures=[];hashes={};conditions={};rng=random.Random(4837);detected=0
for path in files:
    hashes[path.name]=hashlib.sha256(path.read_bytes()).hexdigest()
    gray=cv2.imread(str(path),cv2.IMREAD_GRAYSCALE)
    if gray is None:
        failures.append({"image":path.name,"reason":"decode failed"});continue
    ok,corners=cv2.findChessboardCorners(gray,(9,6),cv2.CALIB_CB_ADAPTIVE_THRESH | cv2.CALIB_CB_NORMALIZE_IMAGE)
    if not ok:
        failures.append({"image":path.name,"reason":"9x6 detector failed"});continue
    detected+=1
    corners=cv2.cornerSubPix(gray,corners,(5,5),(-1,-1),(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_MAX_ITER,30,0.001)).reshape(6,9,2)
    q=[corners[0,0].tolist(),corners[0,8].tolist(),corners[5,8].tolist(),corners[5,0].tolist()]
    samples=[corners[r,c].tolist() for r in range(1,5) for c in range(1,8)]
    jobs.append({"Name":path.name,"Width":int(gray.shape[1]),"Height":int(gray.shape[0]),"Corners":q,"Samples":samples})
    conditions[path.name]="baseline"
    for k in range(20):
        name=path.name+f"#jitter{k:02d}"
        jittered=[[point[0]+rng.uniform(-1,1),point[1]+rng.uniform(-1,1)] for point in q]
        jobs.append({"Name":name,"Width":int(gray.shape[1]),"Height":int(gray.shape[0]),"Corners":jittered,"Samples":samples})
        conditions[name]="jitter"
if not jobs:raise SystemExit("No checkerboards detected.")
inputs,outputs=work/"jobs.json",work/"results.json";inputs.write_text(json.dumps(jobs),encoding="utf-8")
subprocess.run([dotnet,"build",str(root/"PhotoPlaneBenchmark.csproj"),"-c","Release","--nologo","-v:q"],check=True)
subprocess.run([dotnet,str(root/"bin/Release/net10.0/PhotoPlaneBenchmark.dll"),str(inputs),str(outputs)],check=True)
results=json.loads(outputs.read_text(encoding="utf-8"));assert len(results)==len(jobs)
edges=[(r*7+c,r*7+c+1) for r in range(4) for c in range(6)] + [(r*7+c,(r+1)*7+c) for r in range(3) for c in range(7)]
plane_errors=[];single_errors=[];jitter_errors=[];per_image=[];jitter_success=0
for item in results:
    if item["Error"] is not None:
        failures.append({"image":item["Name"],"reason":item["Error"]});continue
    assert len(item["Plane"])==len(item["Single"])==28
    plane=[abs(math.dist(item["Plane"][a],item["Plane"][b])-1) for a,b in edges]
    single=[abs(math.dist(item["Single"][a],item["Single"][b])-1) for a,b in edges]
    if conditions[item["Name"]]=="jitter":
        jitter_success+=1;jitter_errors.extend(plane);continue
    plane_errors.extend(plane);single_errors.extend(single)
    per_image.append({"image":item["Name"],"plane_mean":statistics.mean(plane),"single_mean":statistics.mean(single),"plane_max":max(plane),"single_max":max(single)})
if not per_image:raise SystemExit("Every rectification failed; inspect results.json.")
def summary(values):
    values=sorted(values)
    return {"count":len(values),"mean":statistics.mean(values),"median":statistics.median(values),"p95":values[math.ceil(.95*len(values))-1],"max":max(values)}
report={"opencv_version":cv2.__version__,"files_seen":len(files),"detected":detected,"evaluated":len(per_image),"jitter_trials":detected*20,"jitter_succeeded":jitter_success,"failed":failures,"hashes":hashes,
        "method":"9x6 detected inner corners; four extremes define 8x5 square units; 28 interior corners and 45 adjacent edges per image; absolute length error from one square side; no lens correction; 20 deterministic trials/image with independent uniform +/-1 pixel x/y corner perturbations",
        "plane_error_square_sides":summary(plane_errors),"single_error_square_sides":summary(single_errors),"plane_jitter_error_square_sides":summary(jitter_errors) if jitter_errors else None,"per_image":per_image}
(work/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print(json.dumps(report,indent=2))
