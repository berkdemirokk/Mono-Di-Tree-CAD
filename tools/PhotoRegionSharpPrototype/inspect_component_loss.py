"""Measure pixel changes caused by selecting one component and filling its holes."""
import hashlib
import json
import sys
from pathlib import Path

if len(sys.argv) != 5:
    raise SystemExit('Expected OpenCV directory, jobs JSON, baseline JSON, new output JSON')
sys.path.insert(0, sys.argv[1])
import cv2
import numpy as np

jobs_path, baseline_path, output = map(Path, sys.argv[2:])
if output.exists():
    raise SystemExit('Output already exists')
jobs = json.loads(jobs_path.read_text())
baseline = json.loads(baseline_path.read_text())
expected = {row['annotation_id']: row['grabcut_mask_sha256'] for row in baseline['rows']}
if len(expected) != len(jobs) or len({j['AnnotationId'] for j in jobs}) != len(jobs):
    raise SystemExit('Job and baseline identifiers differ')
cv2.setNumThreads(1)
rows = []
for job in jobs:
    raw = Path(job['Image']).read_bytes()
    if hashlib.sha256(raw).hexdigest().upper() != job['ImageSha256']:
        raise SystemExit('Image changed')
    image = cv2.imdecode(np.frombuffer(raw, np.uint8), cv2.IMREAD_COLOR)
    mask = np.zeros(image.shape[:2], np.uint8)
    bg, fg = np.zeros((1, 65), np.float64), np.zeros((1, 65), np.float64)
    cv2.setRNGSeed(job['Seed'])
    cv2.grabCut(image, mask, (job['X'], job['Y'], job['Width'], job['Height']), bg, fg, 5, cv2.GC_INIT_WITH_RECT)
    binary = np.where((mask == 1) | (mask == 3), 1, 0).astype(np.uint8)
    digest = hashlib.sha256(binary.tobytes()).hexdigest()
    if expected.get(job['AnnotationId']) != digest:
        raise SystemExit('Baseline mask mismatch')
    count, labels, stats, _ = cv2.connectedComponentsWithStats(binary, connectivity=8)
    sizes = stats[1:, cv2.CC_STAT_AREA]
    largest_label = 1 + int(np.argmax(sizes)) if count > 1 else 0
    largest = (labels == largest_label) if largest_label else np.zeros_like(binary, dtype=bool)
    foreground = int(binary.sum())
    kept = int(largest.sum())
    background = (np.pad(largest, 1) == 0).astype(np.uint8)
    flood_mask = np.zeros((background.shape[0] + 2, background.shape[1] + 2), np.uint8)
    cv2.floodFill(background, flood_mask, (0, 0), 2, flags=4)
    rows.append({'annotation_id': job['AnnotationId'], 'mask_sha256': digest,
                 'component_count': int(count - 1),
                 'selected_sha256': hashlib.sha256(largest.astype(np.uint8).tobytes()).hexdigest(),
                 'foreground_pixels': foreground, 'largest_component_pixels': kept,
                 'discarded_foreground_pixels': foreground - kept,
                 'discarded_fraction': (foreground - kept) / foreground if foreground else 0,
                 'filled_hole_pixels': int((background == 1).sum()),
                 'other_components_at_least_25_pixels': int(sum(size >= 25 for i, size in enumerate(sizes, 1) if i != largest_label)),
                 'other_components_at_least_100_pixels': int(sum(size >= 100 for i, size in enumerate(sizes, 1) if i != largest_label))})
report = {'jobs_sha256': hashlib.sha256(jobs_path.read_bytes()).hexdigest(),
          'baseline_sha256': hashlib.sha256(baseline_path.read_bytes()).hexdigest(),
          'opencv': cv2.__version__, 'count': len(rows),
          'with_discarded_foreground': sum(r['discarded_foreground_pixels'] > 0 for r in rows),
          'with_discarded_at_least_25_pixels': sum(r['discarded_foreground_pixels'] >= 25 for r in rows),
          'with_discarded_over_1_percent': sum(r['discarded_fraction'] > .01 for r in rows),
          'with_other_component_at_least_25_pixels': sum(r['other_components_at_least_25_pixels'] > 0 for r in rows),
          'with_other_component_at_least_100_pixels': sum(r['other_components_at_least_100_pixels'] > 0 for r in rows),
          'with_filled_hole_pixels': sum(r['filled_hole_pixels'] > 0 for r in rows),
          'max_discarded_fraction': max((r['discarded_fraction'] for r in rows), default=0),
          'max_filled_hole_pixels': max((r['filled_hole_pixels'] for r in rows), default=0), 'rows': rows}
with output.open('x', encoding='utf-8') as f:
    json.dump(report, f, indent=2)
print(json.dumps({k: v for k, v in report.items() if k != 'rows'}, indent=2))
