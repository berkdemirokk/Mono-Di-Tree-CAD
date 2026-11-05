"""Inspect topology of previously specified local GrabCut jobs; no image output."""
import hashlib,json,sys
from pathlib import Path
if len(sys.argv)!=4: raise SystemExit('Expected OpenCV directory, jobs JSON, new output JSON')
sys.path.insert(0,sys.argv[1]);import cv2,numpy as np
jobs=Path(sys.argv[2]);out=Path(sys.argv[3])
if out.exists():raise SystemExit('Output already exists')
rows=[];cv2.setNumThreads(1)
for j in json.loads(jobs.read_text()):
 raw=Path(j['Image']).read_bytes()
 if hashlib.sha256(raw).hexdigest().upper()!=j['ImageSha256']:raise SystemExit('Image changed')
 image=cv2.imdecode(np.frombuffer(raw,np.uint8),cv2.IMREAD_COLOR);mask=np.zeros(image.shape[:2],np.uint8)
 bg=np.zeros((1,65),np.float64);fg=np.zeros((1,65),np.float64);cv2.setRNGSeed(j['Seed'])
 cv2.grabCut(image,mask,(j['X'],j['Y'],j['Width'],j['Height']),bg,fg,5,cv2.GC_INIT_WITH_RECT)
 binary=np.where((mask==1)|(mask==3),1,0).astype(np.uint8)
 contours,hierarchy=cv2.findContours(binary,cv2.RETR_CCOMP,cv2.CHAIN_APPROX_NONE)
 parents=[] if hierarchy is None else hierarchy[0,:,3].tolist()
 rows.append({'annotation_id':j['AnnotationId'],'mask_sha256':hashlib.sha256(binary.tobytes()).hexdigest(),'outer_contours':sum(x==-1 for x in parents),'holes':sum(x!=-1 for x in parents),'contour_points':sum(len(c) for c in contours),'largest_outer_contour_points':max((len(c) for c,parent in zip(contours,parents) if parent==-1),default=0)})
report={'jobs_sha256':hashlib.sha256(jobs.read_bytes()).hexdigest(),'opencv':cv2.__version__,'count':len(rows),'multiple_outer':sum(r['outer_contours']>1 for r in rows),'with_holes':sum(r['holes']>0 for r in rows),'largest_over_512':sum(r['largest_outer_contour_points']>512 for r in rows),'rows':rows}
with out.open('x',encoding='utf-8') as f:json.dump(report,f,indent=2)
print(json.dumps({k:v for k,v in report.items() if k!='rows'},indent=2))
