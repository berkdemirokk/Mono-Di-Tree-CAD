"""Explore disagreement between two photo paths as an error warning.

Usage: python disagreement_assessment.py BASELINE_DIR ALTERNATIVE_DIR NEW_OUTPUT.json [DATASET.zip]
Uses previously generated paired 12-anchor contour experiments. No image model,
production changes or device interaction. Ground truth is used only for scoring.
"""
import hashlib
import json
from pathlib import Path
import sys
import math
import zipfile

if len(sys.argv) not in (4,5):
    raise SystemExit('Expected baseline directory, alternative directory, a new output path and optional dataset archive.')
a, b, output = map(Path, sys.argv[1:4])
if output.exists():
    raise SystemExit('Output already exists.')

def read(folder, name):
    return json.loads((folder / name).read_text(encoding='utf-8'))

aj, bj = read(a, 'jobs.json'), read(b, 'jobs.json')
ar, br = read(a, 'results.json'), read(b, 'results.json')
am, bm = read(a, 'measurements.json'), read(b, 'measurements.json')
asum, bsum = read(a, 'summary.json'), read(b, 'summary.json')
if len(aj) != 960 or len(bj) != 960 or len(am) != 80 or len(bm) != 80:
    raise SystemExit('Expected 80 complete 12-anchor contours.')
if asum['archive_sha256'] != bsum['archive_sha256'] or asum.get('selection', {}).get('name', 'development') != bsum.get('selection', {}).get('name', 'development') or asum.get('jitter_pixels', 0) != bsum.get('jitter_pixels', 0):
    raise SystemExit('Archive, selection or jitter differs.')
if any(x['ordinal'] != i or y['ordinal'] != i or x['annotation_id'] != y['annotation_id'] or x['category'] != y['category']
       for i, (x, y) in enumerate(zip(am, bm))):
    raise SystemExit('Contour identities differ.')
if len(ar) != 960 or len(br) != 960 or len({x['Id'] for x in ar}) != 960 or len({x['Id'] for x in br}) != 960:
    raise SystemExit('Path results are missing or duplicated.')
ar = {x['Id']: x for x in ar}; br = {x['Id']: x for x in br}
for i, (x, y) in enumerate(zip(aj, bj)):
    if x['Id'] != i or y['Id'] != i or any(x[k] != y[k] for k in ('X1','Y1','X2','Y2')):
        raise SystemExit('Anchor jobs differ.')
    if i % 12 == 0:
        if hashlib.sha256(Path(x['Image']).read_bytes()).digest() != hashlib.sha256(Path(y['Image']).read_bytes()).digest():
            raise SystemExit('Image contents differ.')
    elif x['Image'] != aj[i - i%12]['Image'] or y['Image'] != bj[i - i%12]['Image']:
        raise SystemExit('Unexpected image grouping.')
    for result in (ar[i], br[i]):
        if not result['Found'] or not result['Path'] or any(len(p) != 2 or any(type(v) is not int for v in p) for p in result['Path']):
            raise SystemExit('Expected complete integer path outputs.')

# Discrete point-set Hausdorff threshold. Paths consist of every raster step;
# this is not a continuous contour or physical distance guarantee.
def exceeds(path1, path2, radius):
    offsets = [(x,y) for x in range(-radius,radius+1) for y in range(-radius,radius+1) if x*x+y*y <= radius*radius]
    def directed(first, second):
        points = {tuple(p) for p in second}
        return any(not any((x+dx,y+dy) in points for dx,dy in offsets) for x,y in first)
    return directed(path1,path2) or directed(path2,path1)

segment_errors = None
if len(sys.argv)==5:
    archive=Path(sys.argv[4])
    with archive.open('rb') as file:
        if hashlib.file_digest(file,'sha256').hexdigest()!=asum['archive_sha256']:
            raise SystemExit('Dataset hash differs.')
    with zipfile.ZipFile(archive) as file:
        data=json.loads(file.read('artf-test_resized_512x256/annotations.json'))
    annotations={row['id']:row for row in data['annotations']}
    segment_errors=[]
    for ordinal, measurement in enumerate(am):
        annotation=annotations[measurement['annotation_id']]
        polygon=max((p for p in annotation['segmentation'] if len(p)>=6),key=len)
        points=list(zip(polygon[::2],polygon[1::2]));points.append(points[0])
        edges=[]
        for (x1,y1),(x2,y2) in zip(points,points[1:]):
            dx,dy=x2-x1,y2-y1;edges.append((x1,y1,dx,dy,dx*dx+dy*dy))
        def distance2(point):
            x,y=point;best=float('inf')
            for ax,ay,dx,dy,den in edges:
                t=max(0,min(1,((x-ax)*dx+(y-ay)*dy)/den)) if den else 0
                best=min(best,(x-ax-t*dx)**2+(y-ay-t*dy)**2)
            return best
        segment_errors.extend(max(distance2(p) for p in ar[ordinal*12+j]['Path']) > 25 for j in range(12))

results = {}
for radius in (1,2,3,5):
    segment_marked = [exceeds(ar[i]['Path'],br[i]['Path'],radius) for i in range(960)]
    marked = [any(segment_marked[i*12:(i+1)*12]) for i in range(80)]
    bad = [m['proposal_max'] is not None and m['proposal_max'] > 5 for m in am]
    if any(m['proposal_max'] is None for m in am):
        raise SystemExit('Incomplete baseline contours cannot be scored.')
    tp = sum(w and e for w,e in zip(marked,bad)); fp = sum(w and not e for w,e in zip(marked,bad))
    fn = sum(not w and e for w,e in zip(marked,bad)); tn = sum(not w and not e for w,e in zip(marked,bad))
    results[str(radius)] = {'warned':tp+fp,'bad_and_warned':tp,'bad_without_warning':fn,
                            'good_and_warned':fp,'good_without_warning':tn,
                            'recall_bad':tp/(tp+fn) if tp+fn else None,
                            'precision_warned':tp/(tp+fp) if tp+fp else None}
    if segment_errors is not None:
        stp=sum(w and e for w,e in zip(segment_marked,segment_errors))
        sfp=sum(w and not e for w,e in zip(segment_marked,segment_errors))
        sfn=sum(not w and e for w,e in zip(segment_marked,segment_errors))
        stn=sum(not w and not e for w,e in zip(segment_marked,segment_errors))
        results[str(radius)]['segments']={'warned_and_path_vertex_over_5px':stp,'warned_without_path_vertex_over_5px':sfp,
                                           'unwarned_and_path_vertex_over_5px':sfn,'unwarned_without_path_vertex_over_5px':stn}
report = {'archive_sha256':asum['archive_sha256'], 'baseline_method':asum['method'],
          'alternative_method':bsum['method'], 'jitter_pixels':asum.get('jitter_pixels',0),
          'method':'Any segment with symmetric raster-point disagreement exceeding a fixed radius; scored against baseline sampled local error over 5 px. Ground-truth-derived anchors; exploratory, not user or physical validation.',
          'contours':80,'bad_baseline':sum(m['proposal_max']>5 for m in am),'thresholds_pixels':results}
if segment_errors is not None:
    report['segment_method']='Baseline raster vertices to nearest annotation segment, one direction only; omission of unvisited boundary is not detected. Threshold strictly greater than 5 pixels.'
    report['segments_with_path_vertex_over_5px']=sum(segment_errors)
with output.open('x',encoding='utf-8') as f:
    json.dump(report,f,indent=2)
print(json.dumps(report,indent=2))
