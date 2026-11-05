"""Extract synthetic BodiceFrontHalf before collar and armhole cuts."""
import contextlib,io,json,runpy
with contextlib.redirect_stdout(io.StringIO()): env=runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')
from assets.garment_programs.bodice import BodiceFrontHalf
cases=[]
for name in ('synthetic-base','synthetic-distinct'):
    body_values=env['base'].copy()
    if name=='synthetic-distinct': body_values.update({'bust':96,'back_width':43,'waist':74,'waist_back_width':34,'hips':102,'hip_back_width':54,'waist_line':39,'waist_over_bust_line':42})
    body=env['BodyParameters'](str(env['here']/'synthetic.yaml'));body.load_from_dict(body_values)
    panel=BodiceFrontHalf('precut_front',body,env['design'])
    edges=[{'kind':type(e).__name__,'start':[float(v) for v in e.start],'end':[float(v) for v in e.end]} for e in panel.edges]
    def edge_index(edge):
        matches=[i for i,candidate in enumerate(panel.edges) if candidate is edge]
        if len(matches)!=1: raise RuntimeError('Unbound dart edge')
        return matches[0]
    stitches=[]
    for rule in panel.stitching_rules.rules:
        if len(rule.int1.edges)!=1 or len(rule.int2.edges)!=1: raise RuntimeError('Unexpected dart structure')
        stitches.append({'a':edge_index(rule.int1.edges[0]),'b':edge_index(rule.int2.edges[0]),
                         'a_flip':bool(rule.int1.edges_flipping[0]),'b_flip':bool(rule.int2.edges_flipping[0])})
    cases.append({'id':name,'edges':edges,'stitches':stitches})
print(json.dumps({'source_commit':env['pin'],'cases':cases},indent=2,allow_nan=False))

