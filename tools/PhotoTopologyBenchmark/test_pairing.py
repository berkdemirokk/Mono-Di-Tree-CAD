"""Regression checks for matching photo contour and topology evidence."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SUMMARY = Path(__file__).with_name('summarize.py')


class PairingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=ROOT / 'work', prefix='photo-pairing-test-')
        self.base = Path(self.temp.name)
        self.paths = [self.base / name for name in ('topology.json', 'measurements.json', 'summary.json', 'out.json')]
        self.rows = [{'ordinal': i, 'category': 'shorts' if i < 40 else 'tshirt',
                      'proposal_max': 2, 'strict_crossing': False} for i in range(80)]
        self.topology = {'version': 1, 'jobs_sha256': 'a'*64, 'results_sha256': 'b'*64,
                         'images_sha256': ['c'*64]*80,
                         'rows': [{'Ordinal': i, 'PointsAccepted': True, 'ProposalAccepted': True,
                                   'PixelGeometryValid': True, 'CrossingPairFound': None} for i in range(80)]}
        self.source = {'archive_sha256': 'd'*64, 'method': 'test', 'selection': {'name':'holdout'}}
        self.write()

    def tearDown(self):
        self.temp.cleanup()

    def write(self):
        raw = json.dumps(self.rows).encode()
        self.paths[1].write_bytes(raw)
        self.source['pairing'] = {k:self.topology[k] for k in ('version', 'jobs_sha256', 'results_sha256', 'images_sha256')}
        self.source['pairing']['measurements_sha256'] = hashlib.sha256(raw).hexdigest()
        self.paths[0].write_text(json.dumps(self.topology), encoding='utf-8')
        self.paths[2].write_text(json.dumps(self.source), encoding='utf-8')

    def run_summary(self):
        return subprocess.run([sys.executable, str(SUMMARY), *(str(p) for p in self.paths)], capture_output=True, text=True)

    def test_matching_inputs_are_summarized(self):
        result = self.run_summary()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(80, json.loads(self.paths[3].read_text())['categories']['all']['pixel_geometry_valid'])

    def test_different_jobs_results_or_images_are_rejected(self):
        for key in ('jobs_sha256', 'results_sha256', 'images_sha256'):
            with self.subTest(key=key):
                self.write()
                self.topology[key] = ['f'*64]*80 if key == 'images_sha256' else 'f'*64
                self.paths[0].write_text(json.dumps(self.topology), encoding='utf-8')
                self.assertNotEqual(0, self.run_summary().returncode)
                self.assertFalse(self.paths[3].exists())

    def test_changed_measurement_and_legacy_evidence_are_rejected(self):
        self.paths[1].write_text(json.dumps(self.rows) + ' ', encoding='utf-8')
        self.assertNotEqual(0, self.run_summary().returncode)
        self.write(); self.paths[0].write_text(json.dumps(self.topology['rows']), encoding='utf-8')
        self.assertNotEqual(0, self.run_summary().returncode)
        self.assertFalse(self.paths[3].exists())

if __name__ == '__main__':
    unittest.main()
