"""Pinned-source, fixed synthetic stage trace; no export or personal data."""
import contextlib, copy, io, json, runpy
import numpy, scipy
with contextlib.redirect_stdout(io.StringIO()):
    env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
from assets.garment_programs.bodice import BodiceHalf
from assets.garment_programs.meta_garment import MetaGarment

def measure(interface):
    return {'edge_count':len(interface.edges),'length_cm':float(interface.edges.length()),'projected_cm':float(interface.projecting_lengths().sum()),
            'ruffle': [{'coefficient':float(r['coeff']),'section':[int(x) for x in r['sec']]} for r in interface.ruffle]}
def capture(obj):
    return {side:{name:measure(panel.interfaces[name]) for name in ('outside','shoulder','bottom')}
            for side,panel in (('front',obj.ftorso),('back',obj.btorso))}
trace=[]
for method_name in ('add_sleeves','add_collars'):
    original=getattr(BodiceHalf,method_name)
    def wrapper(self,*args,_name=method_name,_original=original,**kwargs):
        before=capture(self);result=_original(self,*args,**kwargs);after=capture(self)
        if self.name=="right":trace.append({'panel':self.name,'operation':_name,'before':before,'after':after})
        return result
    setattr(BodiceHalf,method_name,wrapper)

cases=[]
for ident,overrides in (
    ('synthetic-base',{}),
    ('synthetic-distinct',{'bust':96,'back_width':43,'waist':74,'waist_back_width':34,'hips':102,'hip_back_width':54,'waist_line':39,'waist_over_bust_line':42}),
    ('synthetic-front-length-plus-one',{'waist_over_bust_line':42}),
    ('synthetic-shoulder-18',{'shoulder_incl':18}),
    ('synthetic-shoulder-30',{'shoulder_incl':30}),
):
    values=env['base'].copy();values.update(overrides)
    body=env['BodyParameters'](str(env['here']/'synthetic.yaml'));body.load_from_dict(values);trace.clear()
    with contextlib.redirect_stdout(io.StringIO()):garment=MetaGarment(ident,body,copy.deepcopy(env['design']))
    upper,lower=garment.subs
    top=measure(upper.interfaces['bottom']);bottom=measure(lower.interfaces['top'])
    sides=[{'a':measure(rule.int1),'b':measure(rule.int2)} for rule in lower.stitching_rules.rules]
    cases.append({'id':ident,'overrides':overrides,'body_waist_cm':float(values['waist']),
                  'body_back_waist_cm':float(values['waist_back_width']),
                  'stage_trace':list(trace),'upper_waist':top,'lower_waist':bottom,'skirt_sides':sides})
print(json.dumps({'source_commit':env['pin'],'numpy':numpy.__version__,'scipy':scipy.__version__,'cases':cases},indent=2,allow_nan=False))
