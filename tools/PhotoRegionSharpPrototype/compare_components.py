"""Compare C# component selection against independent OpenCV Python results."""
import hashlib
import json
import sys
from pathlib import Path

if len(sys.argv) != 4:
    raise SystemExit('Expected Python result, C# result, new output JSON')
python_path, sharp_path, output = map(Path, sys.argv[1:])
if output.exists():
    raise SystemExit('Output already exists')
python = json.loads(python_path.read_text())
sharp = json.loads(sharp_path.read_text())
if python['jobs_sha256'].upper() != sharp['JobsSha256'] or python['opencv'] != sharp['OpenCvVersion']:
    raise SystemExit('Jobs or OpenCV version differ')
p = {r['annotation_id']: r for r in python['rows']}
s = {r['AnnotationId']: r for r in sharp['Results']}
if len(p) != len(python['rows']) or len(s) != len(sharp['Results']) or p.keys() != s.keys():
    raise SystemExit('Result identifiers differ')
for key in p:
    a, b = p[key], s[key]
    if (a['mask_sha256'].upper() != b['MaskSha256'] or
        a['selected_sha256'].upper() != b['SelectedSha256'] or
        a['component_count'] != b['ComponentCount'] or
        a['foreground_pixels'] != b['ForegroundPixels'] or
        a['largest_component_pixels'] != b['LargestPixels'] or
        b['LargestPixels'] != b['SelectedPixels']):
        raise SystemExit(f'Component result mismatch for annotation {key}')
report = {'count': len(p), 'all_masks_and_selections_match': True,
          'jobs_sha256': python['jobs_sha256'], 'opencv': python['opencv'],
          'python_results_sha256': hashlib.sha256(python_path.read_bytes()).hexdigest(),
          'csharp_results_sha256': hashlib.sha256(sharp_path.read_bytes()).hexdigest()}
with output.open('x', encoding='utf-8') as f:
    json.dump(report, f, indent=2)
print(json.dumps(report, indent=2))
