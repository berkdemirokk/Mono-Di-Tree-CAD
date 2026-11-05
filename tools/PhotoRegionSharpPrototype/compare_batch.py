"""Compare bound C# masks with Python benchmark; no images are copied.

Usage: python compare_batch.py PYTHON_REPORT.json JOBS.json CSHARP_RESULT.json NEW_REPORT.json
"""
import hashlib,json,sys
from pathlib import Path
if len(sys.argv)!=5:raise SystemExit('Expected Python report, jobs, C# result and new report.')
source,jobs_path,result_path,out=map(Path,sys.argv[1:])
if out.exists():raise SystemExit('Output already exists.')
read=lambda p:json.loads(p.read_text(encoding='utf-8'))
digest=lambda b:hashlib.sha256(b).hexdigest()
a=read(source);jobs=read(jobs_path);b=read(result_path);rows=a['rows'];values=b['Results']
if b['Version']!=1 or b['JobsSha256']!=digest(jobs_path.read_bytes()).upper():raise SystemExit('C# result is not bound to this job file.')
if a['selection']!='holdout' or a['box_jitter_fraction']!=0 or len(rows)!=80 or len(jobs)!=80 or len(values)!=80:
    raise SystemExit('Expected 80 reference jobs.')
for i,(row,job,result) in enumerate(zip(rows,jobs,values)):
    x0,y0,x1,y1=row['rectangle']
    if row['ordinal']!=i or row['annotation_id']!=job['AnnotationId'] or job['AnnotationId']!=result['AnnotationId'] or job['Seed']!=100000+job['AnnotationId'] or (job['X'],job['Y'],job['Width'],job['Height'])!=(x0,y0,x1-x0,y1-y0):
        raise SystemExit('Job identities, seed or rectangles differ.')
    if digest(Path(job['Image']).read_bytes()).upper()!=job['ImageSha256']:raise SystemExit('Image changed after the run.')
matched=sum(row['grabcut_mask_sha256'].upper()==result['MaskSha256'] for row,result in zip(rows,values))
report={'python_report_sha256':digest(source.read_bytes()),'jobs_sha256':digest(jobs_path.read_bytes()),'csharp_result_sha256':digest(result_path.read_bytes()),
    'archive_sha256':a['archive_sha256'],'python_opencv':a['opencv_version'],'csharp_opencv':b['OpenCvVersion'],
    'compared':len(rows),'identical_masks':matched,'different_masks':len(rows)-matched,
    'method':'Same image bytes, annotation-derived box and seed; C# uses one initial and four evaluation iterations. Hash equality is mask equality, not physical pattern accuracy.'}
with out.open('x',encoding='utf-8') as f:json.dump(report,f,indent=2)
print(json.dumps(report,indent=2))
