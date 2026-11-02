"""Exploratory classical segmentation against annotated aRTF photos.

Usage: python benchmark.py DATASET.zip OPENCV_PYTHON_DIR EMPTY_WORK_DIR
Ground-truth bounding boxes and masks generate idealized seeds; this is not
an evaluation of automatic detection or real users. No images are redistributed.
"""
import collections
import hashlib
import json
import math
from pathlib import Path
import random
import statistics
import sys
import zipfile

archive,cvdir,work=Path(sys.argv[1]).resolve(),Path(sys.argv[2]).resolve(),Path(sys.argv[3]).resolve()
work.mkdir(parents=True,exist_ok=True)
if any(work.iterdir()):raise SystemExit("Work directory must be empty; existing files are not overwritten.")
sys.path.insert(0,str(cvdir))
import cv2
import numpy as np

prefix="artf-test_resized_512x256/"
with zipfile.ZipFile(archive) as z:
    data=json.loads(z.read(prefix+"annotations.json"))
    images={i["id"]:i for i in data["images"]};names={c["id"]:c["name"] for c in data["categories"]}
    populations=collections.defaultdict(list)
    for ann in data["annotations"]:
        if names[ann["category_id"]] in ("shorts","tshirt") and ann.get("segmentation"):
            populations[names[ann["category_id"]]].append(ann)
    rng=random.Random(73114);chosen=[]
    for category in sorted(populations):
        chosen.extend((category,a) for a in rng.sample(sorted(populations[category],key=lambda a:a["id"]),40))
    rows=[];failures=[]
    for category,a in chosen:
        info=images[a["image_id"]];raw=z.read(prefix+info["file_name"])
        im=cv2.imdecode(np.frombuffer(raw,np.uint8),cv2.IMREAD_COLOR)
        if im is None:failures.append({"id":info["id"],"reason":"decode failed"});continue
        h,w=im.shape[:2];truth=np.zeros((h,w),np.uint8)
        polygons=[np.array(p,dtype=np.float32).reshape(-1,2).round().astype(np.int32) for p in a["segmentation"] if len(p)>=6]
        if not polygons:failures.append({"id":info["id"],"reason":"no polygon"});continue
        cv2.fillPoly(truth,polygons,255)
        distance=cv2.distanceTransform(truth,cv2.DIST_L2,3);_,maximum,_,(sx,sy)=cv2.minMaxLoc(distance)
        x,y,bw,bh=a["bbox"];margin=max(4,math.ceil(max(bw,bh)*.05))
        x0=max(0,math.floor(x)-margin);y0=max(0,math.floor(y)-margin);x1=min(w,math.ceil(x+bw)+margin);y1=min(h,math.ceil(y+bh)+margin)
        if maximum<=0 or not(x0<=sx<x1 and y0<=sy<y1):failures.append({"id":info["id"],"reason":"invalid ideal seed"});continue
        roi=im[y0:y1,x0:x1];seed=(sx-x0,sy-y0)
        lab=cv2.GaussianBlur(cv2.cvtColor(roi,cv2.COLOR_BGR2LAB),(5,5),0)
        floodmask=np.zeros((lab.shape[0]+2,lab.shape[1]+2),np.uint8)
        _,_,filled,_=cv2.floodFill(lab.copy(),floodmask,seed,(0,0,0),(50,50,50),(50,50,50),
            flags=4|cv2.FLOODFILL_MASK_ONLY|cv2.FLOODFILL_FIXED_RANGE|(255<<8))
        region=(filled[1:-1,1:-1]>0)
        markers=np.zeros(roi.shape[:2],np.int32);markers[0,:]=markers[-1,:]=markers[:,0]=markers[:,-1]=1
        cv2.circle(markers,seed,5,2,-1);cv2.watershed(cv2.GaussianBlur(roi,(5,5),0),markers)
        watershed=(markers==2)
        def iou(predicted):
            full=np.zeros_like(truth,dtype=bool);full[y0:y1,x0:x1]=predicted;actual=truth>0
            union=np.count_nonzero(full|actual)
            return float(np.count_nonzero(full&actual)/union) if union else 0.0
        rows.append({"id":info["id"],"category":category,"flood_iou":iou(region),"watershed_iou":iou(watershed)})

def summary(key):
    values=[r[key] for r in rows]
    return {"count":len(values),"mean":statistics.mean(values),"median":statistics.median(values),
        "at_least_0_8":sum(v>=.8 for v in values),"below_0_5":sum(v<.5 for v in values)}
if not rows:raise SystemExit("No images could be evaluated.")
with archive.open("rb") as f:digest=hashlib.file_digest(f,"sha256").hexdigest()
report={"archive_sha256":digest,"opencv_version":cv2.__version__,"sample_count":len(chosen),"evaluated":len(rows),"failures":failures,
    "method":"Post-hoc exploratory settings; annotation-derived ideal foreground seed and box; 40 shorts and 40 tshirts, seed 73114; polygon-rasterized IoU; no real user or automatic box detection",
    "flood_lab_blur5_fixed50":summary("flood_iou"),"watershed_blur5_radius5":summary("watershed_iou"),"images":rows}
(work/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf-8");print(json.dumps({k:v for k,v in report.items() if k!="images"},indent=2))
