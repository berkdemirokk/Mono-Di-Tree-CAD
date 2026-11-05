"""Create hash-checked C# benchmark jobs from the prior Python experiment.

Usage: python make_jobs.py DATASET.zip IMAGE_DIR PYTHON_REPORT.json NEW_JOBS.json
Local image paths are written only to the job file; do not commit that file.
"""
import hashlib,json,sys,zipfile
from pathlib import Path
if len(sys.argv)!=5:raise SystemExit('Expected archive, extracted image dir, prior report, new output.')
archive,image_dir,report_path,out=map(Path,sys.argv[1:])
if out.exists():raise SystemExit('Output already exists.')
digest=lambda b:hashlib.sha256(b).hexdigest()
with archive.open('rb') as f:archive_hash=hashlib.file_digest(f,'sha256').hexdigest()
report=json.loads(report_path.read_text(encoding='utf-8'))
if report['archive_sha256']!=archive_hash or report['selection']!='holdout' or report['box_jitter_fraction']!=0:
    raise SystemExit('Report or archive does not match the intended reference.')
rows=report['rows']
if len(rows)!=80 or [r['ordinal'] for r in rows]!=list(range(80)):raise SystemExit('Expected 80 ordered rows.')
jobs=[];prefix='artf-test_resized_512x256/'
with zipfile.ZipFile(archive) as z:
    data=json.loads(z.read(prefix+'annotations.json'));images={x['id']:x['file_name'] for x in data['images']};annotations={x['id']:x for x in data['annotations']}
    for r in rows:
        ann=annotations[r['annotation_id']]
        if ann['image_id']!=r['image_id']:raise SystemExit('Annotation image mismatch.')
        source=image_dir/(str(r['image_id'])+'.png');raw=source.read_bytes()
        if digest(raw)!=digest(z.read(prefix+images[r['image_id']])):raise SystemExit('Extracted image differs from source archive.')
        x0,y0,x1,y1=r['rectangle'];jobs.append({'AnnotationId':r['annotation_id'],'Image':str(source.resolve()),'ImageSha256':digest(raw).upper(),
            'X':x0,'Y':y0,'Width':x1-x0,'Height':y1-y0,'Seed':100000+r['annotation_id']})
with out.open('x',encoding='utf-8') as f:json.dump(jobs,f,indent=2)
print('Verified jobs:',len(jobs))
