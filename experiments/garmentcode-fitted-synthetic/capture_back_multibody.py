"""Capture fixed, non-person source contours across synthetic back measurements."""
import contextlib, io, json, runpy
import numpy, scipy
with contextlib.redirect_stdout(io.StringIO()):
    env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
run=env['run']; g=run.__globals__; base=dict(g['base'])
cases=[('angle5',{'shoulder_incl':5}),('angle18_narrow',{'shoulder_incl':18,'back_width':44,'waist_back_width':33,'bum_points':17}),('angle30',{'shoulder_incl':30}),('angle35_wide',{'shoulder_incl':35,'back_width':50,'waist_back_width':38,'bum_points':19}),('angle40',{'shoulder_incl':40})]
rows=[]
try:
    for name,overrides in cases:
        g['base']={**base,**overrides}
        with contextlib.redirect_stdout(io.StringIO()): result=run('synthetic-base')
        edges=result['panels']['right_btorso']['edges']
        rows.append({'id':name,'overrides':overrides,'edges':[{k:e[k] for k in ('kind','start','end','relative_controls','control_y') if k in e} for e in edges]})
finally:
    g['base']=base
print(json.dumps({'source_commit':env['pin'],'numpy':numpy.__version__,'scipy':scipy.__version__,'cases':rows},indent=2,allow_nan=False))
