"""Compare classical GrabCut with ideal annotation rectangle to ideal-seed flood fill.

python benchmark_grabcut.py DATASET.zip OPENCV_DIR OUTPUT.json development|holdout [0|0.1]
Creates only a new JSON report; no images or weights are redistributed. Both inputs
are annotation-derived and optimistic, not detector or human selection results.
"""
import hashlib,json,math,random,statistics,sys,zipfile
from pathlib import Path

if len(sys.argv) not in (5,6) or sys.argv[4] not in ('development','holdout'):
    raise SystemExit('Expected archive, OpenCV directory, new output and development|holdout.')
archive,cvdir,output=map(Path,sys.argv[1:4]);selection=sys.argv[4]
box_jitter=float(sys.argv[5]) if len(sys.argv)==6 else 0.0
if box_jitter not in (0.0,0.1):raise SystemExit('Box jitter must be 0 or 0.1.')
if output.exists():raise SystemExit('Output already exists.')
with archive.open('rb') as f: digest=hashlib.file_digest(f,'sha256').hexdigest()
if digest!='af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6':
    raise SystemExit('Unexpected dataset hash.')
sys.path.insert(0,str(cvdir.resolve()))
import cv2
import numpy as np
cv2.setNumThreads(1)
root=Path(__file__).resolve().parents[2]
rowsfile=root/'docs'/('PHOTO_MARGIN32_DEVELOPMENT_ROWS.json' if selection=='development' else 'PHOTO_HOLDOUT_REFERENCE_ROWS.json')
rows=json.loads(rowsfile.read_text(encoding='utf-8'))
if len(rows)!=80 or [r['ordinal'] for r in rows]!=list(range(80)):
    raise SystemExit('Unexpected sample selection.')
results=[];prefix='artf-test_resized_512x256/'
with zipfile.ZipFile(archive) as z:
    data=json.loads(z.read(prefix+'annotations.json'))
    annotations={a['id']:a for a in data['annotations']};images={im['id']:im for im in data['images']};categories={c['id']:c['name'] for c in data['categories']}
    for row in rows:
        a=annotations[row['annotation_id']];info=images[a['image_id']];category=categories[a['category_id']]
        if category!=row['category']:raise SystemExit('Annotation category mismatch.')
        image=cv2.imdecode(np.frombuffer(z.read(prefix+info['file_name']),np.uint8),cv2.IMREAD_COLOR)
        if image is None:raise SystemExit('Image decode failed.')
        h,w=image.shape[:2];truth=np.zeros((h,w),np.uint8)
        polygons=[np.asarray(p,dtype=np.float32).reshape(-1,2).round().astype(np.int32) for p in a['segmentation'] if len(p)>=6]
        if not polygons:raise SystemExit('Missing annotation polygon.')
        cv2.fillPoly(truth,polygons,1)
        x,y,bw,bh=a['bbox'];margin=max(4,math.ceil(max(bw,bh)*0.05))
        rng=random.Random(451827+int(a['id']))
        dx0,dy0,dx1,dy1=(rng.uniform(-box_jitter,box_jitter)*size for size in (bw,bh,bw,bh))
        x0=max(0,min(w,math.floor(x)-margin+round(dx0)));y0=max(0,min(h,math.floor(y)-margin+round(dy0)))
        x1=max(0,min(w,math.ceil(x+bw)+margin+round(dx1)));y1=max(0,min(h,math.ceil(y+bh)+margin+round(dy1)))
        if x1<=x0 or y1<=y0:raise SystemExit('Invalid annotation rectangle.')
        distance=cv2.distanceTransform(truth,cv2.DIST_L2,3);_,maximum,_,(sx,sy)=cv2.minMaxLoc(distance)
        if maximum<=0:raise SystemExit('Invalid ideal seed.')
        flood_full=None
        if x0<=sx<x1 and y0<=sy<y1:
            roi=image[y0:y1,x0:x1];lab=cv2.GaussianBlur(cv2.cvtColor(roi,cv2.COLOR_BGR2LAB),(5,5),0)
            flood=np.zeros((roi.shape[0]+2,roi.shape[1]+2),np.uint8)
            cv2.floodFill(lab.copy(),flood,(sx-x0,sy-y0),(0,0,0),(50,50,50),(50,50,50),flags=4|cv2.FLOODFILL_MASK_ONLY|cv2.FLOODFILL_FIXED_RANGE|(255<<8))
            flood_full=np.zeros_like(truth);flood_full[y0:y1,x0:x1]=(flood[1:-1,1:-1]>0)
        def iou(candidate):
            union=np.count_nonzero((candidate>0)|(truth>0))
            return float(np.count_nonzero((candidate>0)&(truth>0))/union) if union else 0.0
        result={'ordinal':row['ordinal'],'annotation_id':a['id'],'image_id':info['id'],'category':category,'flood_iou':iou(flood_full) if flood_full is not None else None,'grabcut_iou':None,'boundary_max_pixels':None,'boundary_mean_pixels':None,'failure':None, 'rectangle':[x0,y0,x1,y1], 'reference_pixels_outside_box':int(np.count_nonzero(truth)-np.count_nonzero(truth[y0:y1,x0:x1]))}
        mask=np.zeros((h,w),np.uint8);bg=np.zeros((1,65),np.float64);fg=np.zeros((1,65),np.float64)
        try:
            cv2.setRNGSeed(100000+int(a['id']))
            cv2.grabCut(image,mask,(x0,y0,x1-x0,y1-y0),bg,fg,5,cv2.GC_INIT_WITH_RECT)
            candidate=np.where((mask==cv2.GC_FGD)|(mask==cv2.GC_PR_FGD),1,0).astype(np.uint8)
            result['grabcut_iou']=iou(candidate)
            _,_,component_stats,_=cv2.connectedComponentsWithStats(candidate,connectivity=8)
            result['components_at_least_25_pixels']=int(np.count_nonzero(component_stats[1:,cv2.CC_STAT_AREA]>=25))
            # Inner one-pixel boundaries (including holes), symmetric nearest
            # pixel-centre distances; these are not physical or curve distances.
            kernel=np.ones((3,3),np.uint8)
            boundary=lambda m: (m>0)&(cv2.erode(m,kernel,borderType=cv2.BORDER_CONSTANT,borderValue=0)==0)
            t=boundary(truth);p=boundary(candidate)
            if np.any(t) and np.any(p):
                to_truth=cv2.distanceTransform((~t).astype(np.uint8),cv2.DIST_L2,cv2.DIST_MASK_PRECISE)[p]
                to_pred=cv2.distanceTransform((~p).astype(np.uint8),cv2.DIST_L2,cv2.DIST_MASK_PRECISE)[t]
                result['boundary_max_pixels']=float(max(to_truth.max(),to_pred.max()))
                result['boundary_mean_pixels']=float((to_truth.mean()+to_pred.mean())/2)
            result['grabcut_mask_sha256']=hashlib.sha256(candidate.tobytes()).hexdigest()
        except cv2.error as ex:
            result['failure']='opencv_grabcut_error'
        results.append(result)

def summary(key):
    v=[r[key] for r in results if r[key] is not None]
    return {'count':len(v),'mean':statistics.mean(v) if v else None,'median':statistics.median(v) if v else None,
            'at_least_0_8':sum(x>=0.8 for x in v),'below_0_5':sum(x<0.5 for x in v)}
report={'archive_sha256':digest,'opencv_version':cv2.__version__,'numpy_version':np.__version__,
        'selection':selection,'box_jitter_fraction':box_jitter,'selection_file_sha256':hashlib.sha256(rowsfile.read_bytes()).hexdigest(),
        'method':'Exploratory; 5 GrabCut iterations and annotation box plus 5% margin and specified deterministic per-side box jitter; flood baseline uses same box plus ground-truth interior seed; 512x256 polygon-mask IoU and symmetric inner boundary pixel distances; no human input or physical measurement',
        'attempted':len(results),'grabcut_failures':sum(r['failure'] is not None for r in results),
        'grabcut':summary('grabcut_iou'),'flood':summary('flood_iou'),
        'reference_clipped_by_box':sum(r['reference_pixels_outside_box']>0 for r in results),
        'multiple_large_components':sum(r.get('components_at_least_25_pixels',0)>1 for r in results),
        'boundary':{'measured':sum(r['boundary_max_pixels'] is not None for r in results),
                    'max_over_5_pixels':sum(r['boundary_max_pixels'] is not None and r['boundary_max_pixels']>5 for r in results),
                    'mean_symmetric_pixels':(statistics.mean(r['boundary_mean_pixels'] for r in results if r['boundary_mean_pixels'] is not None) if any(r['boundary_mean_pixels'] is not None for r in results) else None)},'rows':results}
with output.open('x',encoding='utf-8') as f:json.dump(report,f,indent=2)
print(json.dumps({k:v for k,v in report.items() if k!='rows'},indent=2))
