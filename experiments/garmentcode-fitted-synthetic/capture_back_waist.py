"""Capture synthetic BodiceBackHalf curved dart stages from pinned source."""
import contextlib,io,json,runpy
with contextlib.redirect_stdout(io.StringIO()):env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
from assets.garment_programs.bodice import BodiceBackHalf
from assets.garment_programs.base_classes import BaseBodicePanel
from pygarment.garmentcode.edge import CurveEdge
original=BaseBodicePanel.add_dart
calls=[]
def e_data(edge):
    result={'kind':type(edge).__name__,'start':[float(x) for x in edge.start],'end':[float(x) for x in edge.end],'length_cm':float(edge.length())}
    if isinstance(edge,CurveEdge):result['relative_controls']=[[float(v) for v in p] for p in edge.control_points]
    return result
def wrapped(self,shape,edge,offset,*args,**kwargs):
    before=e_data(edge); shape_data=[e_data(e) for e in shape]
    result=original(self,shape,edge,offset,*args,**kwargs)
    calls.append({'base':before,'shape':shape_data,'offset_cm':float(offset),'result':[e_data(e) for e in result[0]],'interface':[e_data(e) for e in result[1]]})
    return result
BaseBodicePanel.add_dart=wrapped
cases=[]
with contextlib.redirect_stdout(io.StringIO()):
    for name in ('synthetic-base','synthetic-distinct','synthetic-third','synthetic-small-difference','synthetic-fifth'):
        values=env['base'].copy()
        if name=='synthetic-distinct': values.update({'bust':96,'back_width':43,'waist':74,'waist_back_width':34,'hips':102,'hip_back_width':54,'waist_line':39,'waist_over_bust_line':42})
        if name=='synthetic-third': values.update({'bust':104,'back_width':50,'waist':84,'waist_back_width':38,'waist_line':38,'shoulder_incl':18,'bum_points':19,'bust_line':25,'vert_bust_line':22})
        if name=='synthetic-small-difference': values.update({'back_width':44,'waist_back_width':38,'waist_line':40,'shoulder_incl':30,'bum_points':18,'bust_line':24,'vert_bust_line':22})
        if name=='synthetic-fifth': values.update({'bust':102,'back_width':46,'waist':78,'waist_back_width':35,'waist_line':38,'shoulder_incl':15,'bum_points':17,'bust_line':25,'vert_bust_line':20})
        body=env['BodyParameters'](str(env['here']/'synthetic.yaml'));body.load_from_dict(values);calls.clear()
        panel=BodiceBackHalf('precut_back',body,env['design'])
        def index(edge):
            ids=[i for i,e in enumerate(panel.edges) if e is edge]
            if len(ids)!=1: raise RuntimeError('Unbound dart edge')
            return ids[0]
        seams=[]
        for rule in panel.stitching_rules.rules:
            if len(rule.int1.edges)!=1 or len(rule.int2.edges)!=1: raise RuntimeError('Unexpected dart structure')
            seams.append({'a':index(rule.int1.edges[0]),'b':index(rule.int2.edges[0]),'a_flip':bool(rule.int1.edges_flipping[0]),'b_flip':bool(rule.int2.edges_flipping[0])})
        cases.append({'id':name,'body_input_cm_degree':values,'derived':{k:float(v) for k,v in body.params.items() if k.startswith('_')},'calls':list(calls),'edges':[e_data(e) for e in panel.edges],'stitches':seams})
print(json.dumps({'source_commit':env['pin'],'cases':cases},indent=2,allow_nan=False))
