"""Verify the stored synthetic trace using only Python's standard library."""
from fractions import Fraction
from pathlib import Path
import json

HERE=Path(__file__).resolve().parent
PIN='d449629979028123a5c4dc9e732a2ec19b7fce31'

def verify(trace,fixture):
    if trace['source_commit']!=PIN or fixture['source_commit']!=PIN:
        raise ValueError('unexpected source commit')
    fixture_cases=fixture['cases'];trace_cases=trace['cases'];expected={'synthetic-base','synthetic-distinct'}
    fixture_ids=[case['id'] for case in fixture_cases];trace_ids=[case['id'] for case in trace_cases]
    if len(fixture_ids)!=2 or len(set(fixture_ids))!=2 or set(fixture_ids)!=expected or len(trace_ids)!=2 or len(set(trace_ids))!=2 or set(trace_ids)!=expected:
        raise ValueError('unexpected fixture set')
    originals={case['id']:case for case in fixture_cases}
    for case in trace['cases']:
        source=originals[case['id']]
        if len(case['calls'])!=4 or {c['source_rule_index'] for c in case['calls']}!={0,1,2,3}:
            raise ValueError('unexpected dart set')
        for call in case['calls']:
            idx=call['source_rule_index']; stages=call['stages']
            rule=next(r for r in source['stitches'] if r['owner']=='skirt_back' and r['owner_type']=='FittedSkirtPanel' and r['index']==idx)
            refs=[rule[k]['edges'][0] for k in ('a','b')]
            if any(ref['panel']!='skirt_back' for ref in refs):raise ValueError('panel mismatch')
            edges=[source['panels']['skirt_back']['edges'][r['edge']] for r in refs]
            expected={tuple(e[k]) for e in edges for k in ('start','end')}
            observed={tuple(p) for p in stages['returned']['vertices']}
            if len(expected)!=3 or expected!=observed:raise ValueError('fixture vertex mismatch')
            if call['reflected']:raise ValueError('unexpected reflection')
            def F(x):return Fraction.from_float(x)
            def midpoint(stage):
                p=stages[stage]['vertices']
                if len(p)!=3 or p[0][1]!=p[2][1]:raise ValueError('non-horizontal dart')
                return F(p[1][0])-(F(p[0][0])+F(p[2][0]))/2
            if any(midpoint(s)!=0 for s in ('shape','normalized','rotated')):raise ValueError('early asymmetry')
            before=stages['rotated']['vertices'];after=stages['translated']['vertices'];shift=call['snap_shift']
            err=[]
            for a,b in zip(before,after):
                if any(float(a[i]+shift[i])!=b[i] for i in range(2)):raise ValueError('translated coordinate mismatch')
                err.append(F(b[0])-F(a[0])-F(shift[0]))
            if midpoint('translated')-midpoint('rotated')!=(2*err[1]-err[0]-err[2])/2:raise ValueError('addition residual mismatch')
            expected_error=Fraction((-1 if case['id']=='synthetic-base' or idx==3 else 1),2**49) if idx in (0,3) else Fraction(0)
            if midpoint('translated')!=expected_error or midpoint('returned')!=expected_error:raise ValueError('unexpected midpoint displacement')
            if err!=[Fraction(0),expected_error,Fraction(0)]:raise ValueError('unexpected rounding location')
    return True

if __name__=='__main__':
    trace=json.loads((HERE/'skirt-dart-rounding-trace.json').read_text(encoding='utf-8-sig'))
    fixture=json.loads((HERE/'golden.json').read_text(encoding='utf-8-sig'))
    assert verify(trace,fixture)
    # A changed translated apex must fail, as must a mismatched source identity.
    altered=json.loads(json.dumps(trace)); altered['cases'][0]['calls'][0]['stages']['translated']['vertices'][1][0]+=0.01
    try: verify(altered,fixture)
    except ValueError: pass
    else: raise RuntimeError('changed apex accepted')
    altered=json.loads(json.dumps(trace));altered['source_commit']='other'
    try: verify(altered,fixture)
    except ValueError: pass
    else: raise RuntimeError('wrong source identity accepted')
    altered=json.loads(json.dumps(trace));altered['cases'].append(altered['cases'][0])
    try: verify(altered,fixture)
    except ValueError: pass
    else: raise RuntimeError('duplicate trace case accepted')
    altered_fixture=json.loads(json.dumps(fixture));altered_fixture['cases'].append(altered_fixture['cases'][0])
    try: verify(trace,altered_fixture)
    except ValueError: pass
    else: raise RuntimeError('duplicate source fixture case accepted')
    altered=json.loads(json.dumps(trace));altered['cases'][1]=altered['cases'][0]
    try: verify(altered,fixture)
    except ValueError: pass
    else: raise RuntimeError('duplicate two-case trace accepted')
    altered_fixture=json.loads(json.dumps(fixture));altered_fixture['cases'][1]=altered_fixture['cases'][0]
    try: verify(trace,altered_fixture)
    except ValueError: pass
    else: raise RuntimeError('duplicate two-case fixture accepted')
    print('PASS: eight synthetic source dart traces, exact snap arithmetic and negative cases')
