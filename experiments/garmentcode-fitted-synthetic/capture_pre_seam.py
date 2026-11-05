"""Re-run the pinned synthetic source, capturing MetaGarment stitch inputs before mutation."""
import contextlib,io,json,runpy
with contextlib.redirect_stdout(io.StringIO()):
    env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
from pygarment.garmentcode.connector import StitchingRule
original=StitchingRule.__init__
records=[]
def snapshot(interface):
    coefficients=[]
    for part in interface.ruffle:
        coefficients.extend([float(part['coeff'])] * (part['sec'][1]-part['sec'][0]))
    if len(coefficients)!=len(interface.edges):raise RuntimeError('ruffle mismatch')
    edges=[]
    for edge,panel,flip,right,coef in zip(interface.edges,interface.panel,interface.edges_flipping,interface.right_wrong,coefficients):
        item={'panel':panel.name,'kind':type(edge).__name__,'start':[float(v) for v in edge.start], 'end':[float(v) for v in edge.end], 'flipped':bool(flip),'right_wrong':bool(right),'coefficient':coef,'length_cm':float(edge.length())}
        if hasattr(edge,'control_points'):item['relative_controls']=[[float(v) for v in p] for p in edge.control_points]
        if hasattr(edge,'control_y'):item['control_y']=float(edge.control_y)
        edges.append(item)
    return edges
def wrapped(self,int1,int2,*args,**kwargs):
    names1={p.name for p in int1.panel};names2={p.name for p in int2.panel}
    if any('torso' in n for n in names1) and any(n.startswith('skirt_') for n in names2):
        records.append({'upper':snapshot(int1),'lower':snapshot(int2)})
    return original(self,int1,int2,*args,**kwargs)
StitchingRule.__init__=wrapped
cases=[]
with contextlib.redirect_stdout(io.StringIO()):
    for name in ['synthetic-base','synthetic-distinct']:
        records.clear(); env['run'](name)
        if len(records)!=1: raise RuntimeError('Unexpected waist connection count')
        cases.append({'id':name,**records[0]})
print(json.dumps({'source_commit':env['pin'],'unit':'cm','cases':cases},indent=2,allow_nan=False))

