"""Extract only two hardcoded synthetic garments from the pinned source checkout."""
import hashlib,json,os,pathlib,socket,subprocess,sys
from pathlib import Path
root=Path(os.environ['GARMENTCODE_ROOT']).resolve(strict=True)
here=Path(__file__).resolve().parent
pin='d449629979028123a5c4dc9e732a2ec19b7fce31'
if subprocess.check_output(['git','-C',str(root),'rev-parse','HEAD'],text=True,timeout=10).strip()!=pin: raise RuntimeError('Unexpected source commit')
manifest=json.loads((here.parent/'garmentcode-synthetic'/'source-hashes.json').read_text(encoding='utf-8-sig'))
manifest['assets/design_params/default.yaml']='2ebe2f5dc848e72788530f878239f3dd23fb1bb0d108447d70dab1fa6f2b5b35'
for rel,digest in manifest.items():
    path=(root/rel).resolve(strict=True)
    if not path.is_relative_to(root) or hashlib.sha256(path.read_bytes()).hexdigest()!=digest: raise RuntimeError('Source hash mismatch: '+rel)
if hashlib.sha256((here/'synthetic.yaml').read_text(encoding='utf8').encode('utf8')).hexdigest()!='14d96d4fcbff44d6e99791c7a07ce6c6f4fbed574aea5afbfaa317100a91c0c4': raise RuntimeError('Synthetic input mismatch')
def blocked(*args,**kwargs): raise RuntimeError('Network disabled for synthetic probe')
socket.socket.connect=blocked; socket.socket.connect_ex=blocked; socket.create_connection=blocked
sys.path.insert(0,str(root))
import yaml
from assets.bodies.body_params import BodyParameters
from assets.garment_programs.meta_garment import MetaGarment
from pygarment.garmentcode.panel import Panel
from pygarment.garmentcode.component import Component
from pygarment.garmentcode.edge import CurveEdge,CircleEdge
for name,module in tuple(sys.modules.items()):
    if name=='pygarment' or name.startswith(('pygarment.','assets.garment_programs.','assets.bodies.')):
        loc=getattr(module,'__file__',None)
        if loc:
            path=Path(loc).resolve(strict=True)
            if not path.is_relative_to(root) or path.relative_to(root).as_posix() not in manifest: raise RuntimeError('Unpinned module '+name)
base=yaml.safe_load((here/'synthetic.yaml').read_text())['body']
design=yaml.safe_load((root/'assets/design_params/default.yaml').read_text())['design']
design['meta']['upper']['v']='FittedShirt'; design['meta']['bottom']['v']='PencilSkirt'; design['meta']['wb']['v']=None

def run(case):
    values=base.copy()
    if case=='synthetic-distinct': values.update({'bust':96,'back_width':43,'waist':74,'waist_back_width':34,'hips':102,'hip_back_width':54,'waist_line':39,'waist_over_bust_line':42})
    body=BodyParameters(str(here/'synthetic.yaml')); body.load_from_dict(values)
    garment=MetaGarment(case,body,design)
    visited=set(); panels={}; components=[]
    def walk(obj):
        if id(obj) in visited:return
        visited.add(id(obj))
        if isinstance(obj,Panel):
            if obj.name in panels: raise RuntimeError('duplicate panel name')
            panels[obj.name]=obj
            components.append(obj)
        if isinstance(obj,Component):
            components.append(obj)
            for child in obj._get_subcomponents():walk(child)
    walk(garment)
    if not panels:raise RuntimeError('No panels')
    indexes={id(e):(name,i) for name,panel in panels.items() for i,e in enumerate(panel.edges)}
    if len(indexes)!=sum(len(p.edges) for p in panels.values()):raise RuntimeError('Aliased contour edges')
    def xy(v):return [float(v[0]),float(v[1])]
    def edge(e):
        c=e.as_curve(); data={'kind':type(e).__name__,'start':xy(e.start),'end':xy(e.end),'label':e.label or '',
                  'samples':[[float(c.point(t).real),float(c.point(t).imag)] for t in [0.25,0.5,0.75]],'length':float(e.length())}
        if isinstance(e,CurveEdge):data['relative_controls']=[xy(x) for x in e.control_points]
        if isinstance(e,CircleEdge):data['control_y']=float(e.control_y)
        return data
    outpanels={name:{'edges':[edge(e) for e in panel.edges]} for name,panel in sorted(panels.items())}
    def side(interface):
        if len(interface.edges)!=len(interface.panel):raise RuntimeError('Interface inconsistency')
        result=[]
        for edge_obj,panel,flip,right in zip(interface.edges,interface.panel,interface.edges_flipping,interface.right_wrong):
            key=indexes.get(id(edge_obj))
            if key is None or key[0]!=panel.name:raise RuntimeError('Unbound stitch edge')
            result.append({'panel':key[0],'edge':key[1],'flipped':bool(flip),'right_wrong':bool(right)})
        return {'edges':result,'ruffle':[{'coeff':float(r['coeff']),'sec':[int(v) for v in r['sec']]} for r in interface.ruffle]}
    rules=[]
    for comp in sorted(components,key=lambda c:(type(c).__name__,c.name)):
        for index,rule in enumerate(comp.stitching_rules.rules):
            rules.append({'owner':comp.name,'owner_type':type(comp).__name__,'index':index,'a':side(rule.int1),'b':side(rule.int2)})
    return {'id':case,'body_input_cm_degree':values,'derived':{k:float(v) for k,v in body.params.items() if k.startswith('_')},'panels':outpanels,'stitches':rules}
print(json.dumps({'source_commit':pin,'unit':'cm','design':'default FittedShirt + PencilSkirt, no waistband','cases':[run('synthetic-base'),run('synthetic-distinct')]},indent=2,allow_nan=False))
