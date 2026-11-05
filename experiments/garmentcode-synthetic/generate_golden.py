"""Synthetic-only reference extraction from GarmentCode commit d449629979028123a5c4dc9e732a2ec19b7fce31.
Run from its unmodified checkout with dependencies on PYTHONPATH; no user input.
"""
import hashlib, json, os, subprocess, sys
from pathlib import Path

# Verify the pinned checkout before importing project code. This check assumes
# the checkout is not being concurrently modified; it is not a secure sandbox.
root=Path(os.environ['GARMENTCODE_ROOT']).resolve(strict=True)
commit='d449629979028123a5c4dc9e732a2ec19b7fce31'
if subprocess.check_output(['git','-C',str(root),'rev-parse','HEAD'],text=True).strip()!=commit:
    raise RuntimeError('Unexpected checkout HEAD')
manifest=json.loads((Path(__file__).parent/'source-hashes.json').read_text(encoding='utf-8-sig'))
for relative, expected in manifest.items():
    path=(root/relative).resolve(strict=True)
    if not path.is_relative_to(root) or hashlib.sha256(path.read_bytes()).hexdigest()!=expected:
        raise RuntimeError('Source or license hash mismatch: '+relative)
sys.path.insert(0,str(root))
from assets.garment_programs.tee import TorsoFrontHalfPanel, TorsoBackHalfPanel
from assets.garment_programs.skirt_paneled import Skirt2
for name,module in tuple(sys.modules.items()):
    if name=='pygarment' or name.startswith('pygarment.') or name.startswith('assets.garment_programs.'):
        location=getattr(module,'__file__',None)
        if location:
            path=Path(location).resolve(strict=True)
            if not path.is_relative_to(root) or path.relative_to(root).as_posix() not in manifest:
                raise RuntimeError('Unpinned imported module: '+name)
cases=[]
for name, scale in [('synthetic-base', 1.0), ('synthetic-scaled', 1.2)]:
    body={'bust':100*scale,'back_width':44*scale,'waist':80*scale,'waist_back_width':36*scale,
          'waist_line':40*scale,'_shoulder_incl':12,'height':170*scale,'head_l':24*scale,'shoulder_w':40*scale, 'hips':100*scale,'hips_line':20*scale,'hip_back_width':48*scale,'_waist_level':106*scale}
    design={'shirt':{'width':{'v':1.1},'flare':{'v':1.0},'length':{'v':1.0}}}
    skirt_design={'skirt':{'rise':{'v':1.0},'ruffle':{'v':1.0},'flare':{'v':12*scale},'bottom_cut':{'v':0.0},'length':{'v':1.0}}}
    skirt=Skirt2(body,skirt_design,length=60*scale,rise=1.0,slit=False,top_ruffles=False)
    panels={'front_half':TorsoFrontHalfPanel('front_half',body,design),
            'back_half':TorsoBackHalfPanel('back_half',body,design),
            'skirt_front':skirt.front,'skirt_back':skirt.back}
    # Derive identities by object identity from Skirt2's actual interfaces.
    interfaces={id(interface):name+':'+key for name,panel_obj in [('skirt_front',skirt.front),('skirt_back',skirt.back)] for key,interface in panel_obj.interfaces.items()}
    stitches=[]
    for rule in skirt.stitching_rules.rules:
        if id(rule.int1) not in interfaces or id(rule.int2) not in interfaces:
            raise RuntimeError('Unknown Skirt2 stitch interface')
        stitches.append({'a':interfaces[id(rule.int1)],'b':interfaces[id(rule.int2)],
                         'projected_cm':[float(rule.int1.projecting_lengths().sum()),float(rule.int2.projecting_lengths().sum())]})
    def panel(p):
        return {'edges':[{'start':[float(x) for x in e.start], 'end':[float(x) for x in e.end], 'length':float(e.length()),'type':type(e).__name__} for e in p.edges],
                'self_intersecting':bool(p.is_self_intersecting()), 'interfaces':{name:{'lengths':[float(e.length()) for e in interface.edges], 'projected_lengths':[float(v) for v in interface.projecting_lengths()]} for name,interface in p.interfaces.items() if name in ('bottom','top','left','right')}}
    cases.append({'id':name,'scale':scale,'body_cm':body,'shirt':{'width':1.1,'flare':1.0,'length':1.0},'skirt':{'length_cm':60*scale,'flare_cm':12*scale},'panels':{n:panel(p) for n,p in panels.items()},
                  'source_stitches':stitches,
                  'upper_lower_stitches':'not_generated'})
print(json.dumps({'source_commit':'d449629979028123a5c4dc9e732a2ec19b7fce31','unit':'cm','cases':cases},indent=2,allow_nan=False))


