"""Pinned source tangent optimizer sweep using only fixed synthetic angles."""
import contextlib,io,json,runpy,scipy,numpy
with contextlib.redirect_stdout(io.StringIO()):env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
from assets.garment_programs.bodice import BodiceBackHalf
from assets.garment_programs.base_classes import BaseBodicePanel
original=BaseBodicePanel.add_dart
calls=[]
def wrapped(self,shape,edge,offset,*args,**kwargs):
    if not calls:
        calls.append({'start':[float(x) for x in edge.start],'end':[float(x) for x in edge.end], 'relative_control':[float(v) for v in edge.control_points[0]],'length_cm':float(edge.length())})
    return original(self,shape,edge,offset,*args,**kwargs)
BaseBodicePanel.add_dart=wrapped
cases=[]
with contextlib.redirect_stdout(io.StringIO()):
    for angle in (5,10,18,22,26,30,35,40):
        values=env['base'].copy();values['shoulder_incl']=angle
        body=env['BodyParameters'](str(env['here']/'synthetic.yaml'));body.load_from_dict(values);calls.clear()
        BodiceBackHalf('sweep_back',body,env['design'])
        if len(calls)!=1:raise RuntimeError('Unexpected source stage')
        cases.append({'id':'synthetic-angle-'+str(angle),'angle_degrees':angle,**calls[0]})
print(json.dumps({'source_commit':env['pin'],'numpy':numpy.__version__,'scipy':scipy.__version__,'cases':cases},indent=2,allow_nan=False))
