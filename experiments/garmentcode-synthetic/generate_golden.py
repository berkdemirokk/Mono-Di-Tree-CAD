"""Synthetic-only reference extraction from GarmentCode commit d449629979028123a5c4dc9e732a2ec19b7fce31.
Run from its unmodified checkout with dependencies on PYTHONPATH; no user input.
"""
import json
import pygarment as pyg
from assets.garment_programs.tee import TorsoFrontHalfPanel, TorsoBackHalfPanel
from assets.garment_programs.skirt_paneled import SkirtPanel

cases=[]
for name, scale in [('synthetic-base', 1.0), ('synthetic-scaled', 1.2)]:
    body={'bust':100*scale,'back_width':44*scale,'waist':80*scale,'waist_back_width':36*scale,
          'waist_line':40*scale,'_shoulder_incl':12,'height':170*scale,'head_l':24*scale,'shoulder_w':40*scale}
    design={'shirt':{'width':{'v':1.1},'flare':{'v':1.0},'length':{'v':1.0}}}
    panels={
      'front_half':TorsoFrontHalfPanel('front_half',body,design),
      'back_half':TorsoBackHalfPanel('back_half',body,design),
      'skirt_front':SkirtPanel('skirt_front',body['waist']-body['waist_back_width'],60*scale,1,body['waist']-body['waist_back_width'],0,12*scale),
      'skirt_back':SkirtPanel('skirt_back',body['waist_back_width'],60*scale,1,body['waist_back_width'],0,12*scale),
    }
    rules=pyg.Stitches((panels['skirt_front'].interfaces['right'],panels['skirt_back'].interfaces['right']),
                       (panels['skirt_front'].interfaces['left'],panels['skirt_back'].interfaces['left']))
    def panel(p):
        return {'edges':[{'start':[float(x) for x in e.start], 'end':[float(x) for x in e.end], 'length':float(e.length()),'type':type(e).__name__} for e in p.edges],
                'self_intersecting':bool(p.is_self_intersecting()), 'interfaces':{name:{'lengths':[float(e.length()) for e in interface.edges], 'projected_lengths':[float(v) for v in interface.projecting_lengths()]} for name,interface in p.interfaces.items() if name in ('bottom','top','left','right')}}
    cases.append({'id':name,'scale':scale,'body_cm':body,'shirt':{'width':1.1,'flare':1.0,'length':1.0},'skirt':{'length_cm':60*scale,'flare_cm':12*scale},'panels':{n:panel(p) for n,p in panels.items()},
                  'source_stitches':[{'a':'skirt_front:right','b':'skirt_back:right','projected_cm':[float(rules[0].int1.projecting_lengths().sum()),float(rules[0].int2.projecting_lengths().sum())]},{'a':'skirt_front:left','b':'skirt_back:left','projected_cm':[float(rules[1].int1.projecting_lengths().sum()),float(rules[1].int2.projecting_lengths().sum())]}],
                  'upper_lower_stitches':'not_generated'})
print(json.dumps({'source_commit':'d449629979028123a5c4dc9e732a2ec19b7fce31','unit':'cm','cases':cases},indent=2,allow_nan=False))

