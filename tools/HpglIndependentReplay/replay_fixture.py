"""Independently replay one synthetic HPGL job; no device or network access.

Usage: python replay_fixture.py FILE.plt
Only the producer's IN/SP/PA/PU/PD subset is interpreted. Expected positions
are derived here from the fixture specification, not from production previews.
"""
from pathlib import Path
import re
import sys


def replay(text):
    if not text.endswith(';\n'):
        raise ValueError('incomplete terminator')
    commands = text.split(';')
    if commands[-1].strip():
        raise ValueError('trailing data')
    state = 'start'
    position = None
    paths = []
    current = None
    for raw in commands[:-1]:
        command = raw.strip()
        if not command:
            raise ValueError('empty command')
        op, args = command[:2], command[2:]
        if state == 'start' and command == 'IN':
            state = 'initialized'; continue
        if state == 'initialized' and command == 'SP1':
            state = 'selected'; continue
        if state == 'selected' and command == 'PA':
            state = 'drawing'; continue
        if state != 'drawing':
            raise ValueError('unexpected setup or command after finish')
        if op == 'SP':
            if args != '0' or current is not None:
                raise ValueError('invalid pen deselection')
            state = 'finished'; continue
        if op not in ('PU', 'PD'):
            raise ValueError('unsupported command')
        if op == 'PD' and len(command.encode('ascii')) + 1 > 128:
            raise ValueError('pen-down command too long')
        if args:
            tokens = args.split(',')
            if len(tokens) % 2 or any(not re.fullmatch(r'-?\d+', x) for x in tokens):
                raise ValueError('invalid coordinate pairs')
            coords = [(int(tokens[i]), int(tokens[i+1])) for i in range(0,len(tokens),2)]
        else:
            coords = []
        if op == 'PU':
            if current is not None:
                paths.append(current); current = None
            if len(coords) > 1:
                raise ValueError('unexpected multi-coordinate pen-up')
            if coords:
                position = coords[0]
        else:
            if position is None or not coords:
                raise ValueError('pen-down without a starting position')
            if current is None:
                current = [position]
            current.extend(coords)
            position = coords[-1]
    if state != 'finished' or current is not None:
        raise ValueError('missing end or pen lift')
    return paths


def verify(text):
    expected = [
        [(-500,-100),(-100,-100),(-100,300),(-500,300),(-500,-100)],
        [(800+10*i,400+10*(i%2)) for i in range(100)],
    ]
    actual = replay(text)
    if actual != expected:
        raise ValueError('replayed motion differs from specified fixture')
    return len(actual), sum(len(p)-1 for p in actual)


def main():
    if len(sys.argv) != 2:
        raise SystemExit('Expected a fixture HPGL path.')
    text = Path(sys.argv[1]).read_text(encoding='ascii')
    paths, segments = verify(text)
    # Mutations model an extra drawn move, a missing lift, a changed coordinate, and missing termination.
    mutations = [text.replace('PU800,400','PD800,400',1),
                 text.replace(';PU;\nPU800,400',';PD800,400',1),
                 text.replace('-500,-100','-501,-100',1),
                 text.replace('SP0;','',1)]
    if any(m == text for m in mutations):
        raise SystemExit('A mutation did not apply.')
    for mutated in mutations:
        try:
            verify(mutated)
        except ValueError:
            pass
        else:
            raise SystemExit('A deliberately corrupted job was not rejected.')
    print(f'PASS: {paths} paths, {segments} drawn segments, 4 corruptions rejected; no device used.')

if __name__ == '__main__':
    main()
