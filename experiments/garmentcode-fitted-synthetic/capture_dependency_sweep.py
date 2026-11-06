"""Pinned-source, fixed-synthetic finite differences; no real-person input."""
import contextlib, hashlib, io, json, runpy, sys
from decimal import Decimal
from pathlib import Path
import numpy, scipy
with contextlib.redirect_stdout(io.StringIO()):
    env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
run=env['run']; g=run.__globals__; base=dict(g['base'])
fields=['bust','back_width','waist','waist_back_width','bust_points','shoulder_w','shoulder_incl','waist_line','waist_over_bust_line','bust_line','vert_bust_line','bum_points','neck_w','armscye_depth','hips','hip_back_width','hips_line','height','head_l','hip_inclination']
with contextlib.redirect_stdout(io.StringIO()): baseline=run('synthetic-base')
golden_text=(env['here']/'golden.json').read_text(encoding='utf-8-sig')
if hashlib.sha256(golden_text.encode('utf-8')).hexdigest()!='7fc3c5dcf7ad38e33bc37ab6466290fe28e17cc375aff2af5f61d8ce66d4a207':raise RuntimeError('Golden fixture identity mismatch')
golden=json.loads(golden_text)
fixture=next(c for c in golden['cases'] if c['id']=='synthetic-base')
if baseline['body_input_cm_degree']!=fixture['body_input_cm_degree'] or set(baseline['panels'])!=set(fixture['panels']):raise RuntimeError('Fixture baseline mismatch')
# Only compare represented contour definitions, not serialized measurements,
# sample points, display lengths or arbitrary optimizer metadata.
def contour(panel):
    keep=('kind','start','end','relative_controls','control_y')
    return [{k:e[k] for k in keep if k in e} for e in panel['edges']]
prior={k:contour(v) for k,v in baseline['panels'].items()}
golden_matches={k:prior[k]==contour(fixture['panels'][k]) for k in prior}
if not all(golden_matches.values()):raise RuntimeError('Baseline represented contours differ from stored fixture')
def delta_metrics(old,new):
    if len(old)!=len(new):return {'structure_changed':True,'endpoint_max_cm':None,'control_max':None}
    endpoint=0.0;control=0.0
    for a,b in zip(old,new):
        if a.keys()!=b.keys() or a['kind']!=b['kind']:return {'structure_changed':True,'endpoint_max_cm':None,'control_max':None}
        for key in ('start','end'):
            endpoint=max(endpoint,*(abs(x-y) for x,y in zip(a[key],b[key])))
        if 'relative_controls' in a:
            if len(a['relative_controls'])!=len(b['relative_controls']):return {'structure_changed':True,'endpoint_max_cm':None,'control_max':None}
            for p,q in zip(a['relative_controls'],b['relative_controls']):control=max(control,*(abs(x-y) for x,y in zip(p,q)))
        if 'control_y' in a:control=max(control,abs(a['control_y']-b['control_y']))
    return {'structure_changed':False,'endpoint_max_cm':endpoint,'control_max':control}
rows=[]
try:
    for field in fields:
        delta=Decimal('1') if field in ('shoulder_incl','hip_inclination') else Decimal('.1')
        changed=dict(base);changed[field]=float(Decimal(str(base[field]))+delta);g['base']=changed
        with contextlib.redirect_stdout(io.StringIO()): candidate=run('synthetic-base')
        if set(candidate['panels'])!=set(prior):raise RuntimeError('Panel set changed')
        contours={k:contour(v) for k,v in candidate['panels'].items()}
        affected=sorted(k for k in contours if contours[k]!=prior[k])
        rows.append({'field':field,'delta':str(delta),'changed_panels':affected,
                     'panel_deltas':{k:delta_metrics(prior[k],contours[k]) for k in affected}})
finally:
    g['base']=base
print(json.dumps({'source_commit':env['pin'],'fixture':'synthetic-base','numpy':numpy.__version__,'scipy':scipy.__version__,'python':sys.version,'baseline_contour_equal_to_golden':golden_matches,'cases':rows},indent=2))
