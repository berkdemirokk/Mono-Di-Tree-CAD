"""Capture source back-waist tangent optimizer intermediates for fixed synthetic cases."""
import contextlib,io,json,runpy
import numpy,scipy
with contextlib.redirect_stdout(io.StringIO()): env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
from assets.garment_programs.bodice import BodiceBackHalf
from pygarment.garmentcode import edge_factory
original=edge_factory.minimize
calls=[]
def trace(function,*args,**kwargs):
    result=original(function,*args,**kwargs)
    if function.__name__=='_fit_tangents':
        calls.append({'control':[float(v) for v in result.x],'success':bool(result.success),'objective':float(result.fun)})
    return result
edge_factory.minimize=trace
cases=[('angle5',{'shoulder_incl':5}),('angle18_narrow',{'shoulder_incl':18,'back_width':44,'waist_back_width':33,'bum_points':17}),('angle30',{'shoulder_incl':30}),('angle35_wide',{'shoulder_incl':35,'back_width':50,'waist_back_width':38,'bum_points':19}),('angle40',{'shoulder_incl':40}),('holdout7',{'shoulder_incl':7}),('holdout15',{'shoulder_incl':15}),('holdout25',{'shoulder_incl':25}),('holdout32',{'shoulder_incl':32}),('holdout39',{'shoulder_incl':39})]
rows=[]
try:
    with contextlib.redirect_stdout(io.StringIO()):
        for name,overrides in cases:
            values={**env['base'],**overrides};body=env['BodyParameters'](str(env['here']/'synthetic.yaml'));body.load_from_dict(values);calls.clear()
            BodiceBackHalf('trace_back',body,env['design'])
            if len(calls)!=1 or not calls[0]['success']: raise RuntimeError('Unexpected tangent optimizer result')
            rows.append({'id':name,'overrides':overrides,**calls[0]})
finally:
    edge_factory.minimize=original
print(json.dumps({'source_commit':env['pin'],'numpy':numpy.__version__,'scipy':scipy.__version__,'cases':rows},indent=2,allow_nan=False))
