"""Read-only trace of four synthetic source dart cuts; no garment output."""
import contextlib, io, json, runpy, sys
import numpy, scipy
from fractions import Fraction
from pathlib import Path

with contextlib.redirect_stdout(io.StringIO()):
    env = runpy.run_path('experiments/garmentcode-fitted-synthetic/generate.py')

# generate.py independently verifies the pinned source commit and source-file
# manifest. Tracing only observes already-running source code and never patches it.
source_file = (env['root'] / 'pygarment/garmentcode/operators.py').resolve()
golden = json.loads((env['here'] / 'golden.json').read_text(encoding='utf-8-sig'))
expected = {c['id']: c for c in golden['cases']}


def coords(seq):
    if len(seq) != 2:
        raise RuntimeError('Expected a two-edge dart')
    edges = [seq[0], seq[1]]
    if list(edges[0].end) != list(edges[1].start):
        raise RuntimeError('Dart edges do not share their apex')
    vertices = [[float(x) for x in p] for p in [edges[0].start, edges[0].end, edges[1].end]]
    a, tip, b = vertices
    values = [Fraction.from_float(p[0]) for p in vertices]
    two_error = 2 * values[1] - values[0] - values[2]
    return {'vertices': vertices, 'horizontal': a[1] == b[1],
            'twice_midpoint_error_exact': [str(two_error.numerator), str(two_error.denominator)],
            'midpoint_error_cm_approx': float(two_error / 2)}


stages = {239: ('shape', 'target_shape'), 242: ('normalized', 'new_edges'),
          288: ('rotated', 'new_edges'), 291: ('translated', 'new_edges'),
          299: ('orientation_adjusted', 'new_edges')}


def record_case(case_id):
    calls = []
    active = {}
    def trace(frame, event, arg):
        if event == 'call':
            if frame.f_code.co_name != 'cut_into_edge_single' or Path(frame.f_code.co_filename).resolve() != source_file:
                return None
            ancestor = frame.f_back
            is_back = False
            while ancestor is not None:
                obj = ancestor.f_locals.get('self')
                if obj is not None and type(obj).__name__ == 'FittedSkirtPanel' and getattr(obj, 'name', None) == 'skirt_back':
                    is_back = True
                    break
                ancestor = ancestor.f_back
            if not is_back:
                return None
            record = {'stages': {}}
            active[id(frame)] = record
            calls.append(record)
            return trace
        record = active.get(id(frame))
        if record is None:
            return None
        if event == 'line' and frame.f_lineno in stages:
            label, var = stages[frame.f_lineno]
            record['stages'][label] = coords(frame.f_locals[var])
            if label == 'rotated':
                point = frame.f_locals['ins_point']
                first = frame.f_locals['new_edges'][0].start
                record['snap_shift'] = [float(point[i] - first[i]) for i in range(2)]
        if event == 'return':
            if arg is None or len(arg) != 3:
                raise RuntimeError('Cut failed before return')
            record['stages']['returned'] = coords(arg[1])
            record['reflected'] = bool(frame.f_locals['right_position'] != frame.f_locals['right'])
            del active[id(frame)]
        return trace
    try:
        sys.settrace(trace)
        with contextlib.redirect_stdout(io.StringIO()):
            generated = env['run'](case_id)
    finally:
        sys.settrace(None)
    if len(calls) != 4 or active:
        raise RuntimeError(f'{case_id}: expected four complete cuts, got {len(calls)}')
    for call in calls:
        if set(call['stages']) != {label for label, _ in stages.values()} | {'returned'}:
            raise RuntimeError(f'Missing trace stage: {list(call["stages"])}')
        before = call['stages']['rotated']['vertices']
        after = call['stages']['translated']['vertices']
        shift = call['snap_shift']
        residuals = []
        for old, new in zip(before, after):
            if any(float(old[i] + shift[i]) != new[i] for i in range(2)):
                raise RuntimeError('Snap addition did not reproduce translated coordinates')
            r = Fraction.from_float(new[0]) - Fraction.from_float(old[0]) - Fraction.from_float(shift[0])
            residuals.append(r)
        old_error = Fraction(*map(int,call['stages']['rotated']['twice_midpoint_error_exact']))
        new_error = Fraction(*map(int,call['stages']['translated']['twice_midpoint_error_exact']))
        if new_error-old_error != 2*residuals[1]-residuals[0]-residuals[2]:
            raise RuntimeError('Exact snap residual identity failed')
        call['snap_x_rounding_residuals'] = [[str(r.numerator),str(r.denominator)] for r in residuals]
    # Match returned coordinates directly to source fixture dart vertices. This
    # checks the capture was from the expected model, not merely the same label.
    def dart_set(case):
        out = []
        for rule in case['stitches']:
            if rule['owner'] != 'skirt_back' or rule['owner_type'] != 'FittedSkirtPanel':
                continue
            a, b = [rule[x]['edges'][0]['edge'] for x in ('a', 'b')]
            edges = case['panels']['skirt_back']['edges']
            pair = edges[a], edges[b]
            pts = {tuple(edge[k]) for edge in pair for k in ('start', 'end')}
            if len(pts) != 3:
                raise RuntimeError('Fixture dart shape invalid')
            out.append((rule['index'], frozenset(pts)))
        return out
    fixture = dart_set(expected[case_id]); current = dart_set(generated)
    if fixture != current or len(fixture) != 4:
        raise RuntimeError(f'{case_id}: generated dart differs from stored fixture')
    for call in calls:
        pts = frozenset(tuple(v) for v in call['stages']['returned']['vertices'])
        matching = [idx for idx, p in fixture if p == pts]
        if len(matching) != 1:
            raise RuntimeError('Trace does not identify exactly one fixture dart')
        call['source_rule_index'] = matching[0]
        before = call['stages']['rotated']; after = call['stages']['translated']; final = call['stages']['returned']
        if not before['horizontal'] or not after['horizontal'] or not final['horizontal']:
            raise RuntimeError('Unsupported non-horizontal dart trace')
        if int(before['twice_midpoint_error_exact'][0]) != 0:
            raise RuntimeError('Dart already off center before translation')
        if (int(after['twice_midpoint_error_exact'][0]) != 0) != (matching[0] in (0,3)):
            raise RuntimeError('Unexpected source dart midpoint behavior')
        if final['twice_midpoint_error_exact'] != after['twice_midpoint_error_exact']:
            raise RuntimeError('Dart midpoint changed after translation')
    return {'id': case_id, 'calls': calls}

print(json.dumps({'source_commit': env['pin'], 'numpy_version': numpy.__version__, 'scipy_version': scipy.__version__, 'python_version': sys.version,
                  'cases': [record_case(k) for k in ('synthetic-base', 'synthetic-distinct')]},
                 indent=2, allow_nan=False))
