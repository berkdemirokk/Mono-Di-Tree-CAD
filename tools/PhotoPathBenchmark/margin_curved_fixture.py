"""Create one synthetic curved contrast boundary for margin experiments.

Usage: python margin_curved_fixture.py NEW_EMPTY_DIRECTORY
The known function is an artificial image boundary, not a garment edge.
"""
import json
import math
from pathlib import Path
import struct
import sys
import zlib

if len(sys.argv) != 2:
    raise SystemExit("Expected NEW_EMPTY_DIRECTORY.")
directory = Path(sys.argv[1]).resolve()
directory.mkdir(parents=True, exist_ok=True)
if any(directory.iterdir()):
    raise SystemExit("Work directory must be empty.")
width, height = 160, 140
def boundary(y):
    return 60 + 24 * math.sin(math.pi * max(0, min(100, y-20)) / 100)
rows = []
for y in range(height):
    row = bytearray([0])
    for x in range(width):
        value = 0 if x < boundary(y) else 255
        row.extend((value, value, value, 255))
    rows.append(row)
def chunk(kind, payload):
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xffffffff)
image = directory / "curved-boundary.png"
image.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
                  chunk(b"IDAT", zlib.compress(b"".join(rows))) + chunk(b"IEND", b""))
(directory / "jobs.json").write_text(json.dumps([{"Id": 0, "Image": str(image), "X1": 60, "Y1": 20, "X2": 60, "Y2": 120}]), encoding="utf-8")
print("Created an artificial sinusoidal boundary with a 24-pixel deviation.")
