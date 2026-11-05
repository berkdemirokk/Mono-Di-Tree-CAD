"""Compare aggregate contour topology of C# and Python OpenCV runs."""
import hashlib
import json
import sys
from pathlib import Path

if len(sys.argv) != 4:
    raise SystemExit('Expected topology JSON, C# result JSON, new output JSON')
topology_path, sharp_path, output = map(Path, sys.argv[1:])
if output.exists():
    raise SystemExit('Output already exists')
topology = json.loads(topology_path.read_text())
sharp = json.loads(sharp_path.read_text())
if topology['jobs_sha256'].upper() != sharp['JobsSha256'] or topology['opencv'] != sharp['OpenCvVersion']:
    raise SystemExit('Jobs or OpenCV version differ')
left = {r['annotation_id']: r for r in topology['rows']}
right = {r['AnnotationId']: r for r in sharp['Results']}
if len(left) != len(topology['rows']) or len(right) != len(sharp['Results']) or left.keys() != right.keys():
    raise SystemExit('Result identifiers differ')
fields = [('outer_contours', 'OuterContours'), ('holes', 'Holes'),
          ('contour_points', 'ContourPoints'), ('largest_outer_contour_points', 'LargestOuterContourPoints')]
for key in left:
    a, b = left[key], right[key]
    if a['mask_sha256'].upper() != b['MaskSha256'] or any(a[x] != b[y] for x, y in fields):
        raise SystemExit(f'Contour result mismatch for annotation {key}')
report = {'count': len(left), 'all_mask_hashes_and_topology_counts_match': True,
          'jobs_sha256': topology['jobs_sha256'], 'opencv': topology['opencv'],
          'topology_sha256': hashlib.sha256(topology_path.read_bytes()).hexdigest(),
          'csharp_results_sha256': hashlib.sha256(sharp_path.read_bytes()).hexdigest()}
with output.open('x', encoding='utf-8') as f:
    json.dump(report, f, indent=2)
print(json.dumps(report, indent=2))
