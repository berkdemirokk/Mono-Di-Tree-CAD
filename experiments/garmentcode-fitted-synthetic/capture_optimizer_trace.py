"""Pinned, synthetic-only SciPy tangent optimizer diagnostic; no geometry export."""
import contextlib, io, json, runpy
import numpy, scipy
with contextlib.redirect_stdout(io.StringIO()):
    env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
from assets.garment_programs.bodice import BodiceBackHalf
from pygarment.garmentcode import edge_factory
original=edge_factory.minimize
calls=[]
def traced(function,*args,**kwargs):
    result=original(function,*args,**kwargs)
    if function.__name__=='_fit_tangents':
        calls.append({'success':bool(result.success),'status':int(result.status),
                      'iterations':int(result.nit),'evaluations':int(result.nfev),
                      'objective':float(result.fun),'control':[float(x) for x in result.x],
                      'gradient':[float(x) for x in result.jac]})
    return result
edge_factory.minimize=traced
cases=[]
with contextlib.redirect_stdout(io.StringIO()):
    for angle in (5,10,18,22,26,30,35,40):
        values=env['base'].copy();values['shoulder_incl']=angle
        body=env['BodyParameters'](str(env['here']/'synthetic.yaml'));body.load_from_dict(values);calls.clear()
        BodiceBackHalf('trace_back',body,env['design'])
        if len(calls)!=1:raise RuntimeError('Unexpected source tangent optimizer count')
        cases.append({'angle_degrees':angle,**calls[0]})
print(json.dumps({'source_commit':env['pin'],'numpy':numpy.__version__,'scipy':scipy.__version__,'cases':cases},indent=2,allow_nan=False))
