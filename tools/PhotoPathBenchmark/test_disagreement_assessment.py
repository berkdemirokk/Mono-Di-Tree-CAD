"""Check that disagreement scoring refuses mismatched inputs and existing output."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
SCRIPT=Path(__file__).with_name('disagreement_assessment.py')
BASE=ROOT/'work/photo-gray-bound-holdout'
ALT=ROOT/'work/photo-standard-color-holdout-bound'

class PairingGuardTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(dir=ROOT/'work',prefix='photo-disagreement-test-')
        self.folder=Path(self.tmp.name);self.copy=self.folder/'copy';self.copy.mkdir()
        for name in ('jobs.json','results.json','summary.json','measurements.json'):
            shutil.copyfile(ALT/name,self.copy/name)
        self.output=self.folder/'result.json'
    def tearDown(self): self.tmp.cleanup()
    def run_tool(self, other=None):
        return subprocess.run([sys.executable,str(SCRIPT),str(BASE),str(other or self.copy),str(self.output)],capture_output=True,text=True)
    def test_rejects_changed_anchor(self):
        path=self.copy/'jobs.json';jobs=json.loads(path.read_text());jobs[0]['X1']+=1;path.write_text(json.dumps(jobs))
        self.assertNotEqual(0,self.run_tool().returncode);self.assertFalse(self.output.exists())
    def test_rejects_other_image(self):
        path=self.copy/'jobs.json';jobs=json.loads(path.read_text());jobs[0]['Image']=jobs[12]['Image'];path.write_text(json.dumps(jobs))
        self.assertNotEqual(0,self.run_tool().returncode);self.assertFalse(self.output.exists())
    def test_rejects_modified_result(self):
        path=self.copy/'results.json';rows=json.loads(path.read_text());rows[0]['Path'][0][0]+=1;path.write_text(json.dumps(rows))
        self.assertNotEqual(0,self.run_tool().returncode);self.assertFalse(self.output.exists())
    def test_rejects_modified_measurement(self):
        path=self.copy/'measurements.json';rows=json.loads(path.read_text());rows[0]['proposal_max']+=1;path.write_text(json.dumps(rows))
        self.assertNotEqual(0,self.run_tool().returncode);self.assertFalse(self.output.exists())
    def test_rejects_unbound_legacy_run(self):
        path=self.copy/'summary.json';data=json.loads(path.read_text());data.pop('pairing');path.write_text(json.dumps(data))
        self.assertNotEqual(0,self.run_tool().returncode);self.assertFalse(self.output.exists())
    def test_never_overwrites_existing_output(self):
        self.output.write_text('keep',encoding='utf-8')
        self.assertNotEqual(0,self.run_tool().returncode);self.assertEqual('keep',self.output.read_text())

if __name__=='__main__': unittest.main()
