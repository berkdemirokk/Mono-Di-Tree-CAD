"""Write one synthetic PNG and job; both colours have identical product grayscale.

Usage: python make_isoluminant_fixture.py NEW_EMPTY_DIRECTORY
This is a targeted diagnostic, not a garment or accuracy benchmark.
"""
import json
from pathlib import Path
import struct
import sys
import zlib

if len(sys.argv) != 2:
    raise SystemExit("Expected NEW_EMPTY_DIRECTORY.")
directory = Path(sys.argv[1]).resolve()
directory.mkdir(parents=True, exist_ok=True)
if any(directory.iterdir()):
    raise SystemExit("The fixture directory must be empty.")
width = height = 64
red, green = (150, 0, 0), (0, 77, 0)
gray = lambda rgb: (77*rgb[0] + 150*rgb[1] + 29*rgb[2] + 128) >> 8
assert gray(red) == gray(green) == 45

def boundary(y):
    return 16 + max(0, (y-4)*(56-y))/80

rows = []
for y in range(height):
    row = bytearray([0])  # PNG filter: none
    for x in range(width):
        row.extend((* (red if x < boundary(y) else green), 255))
    rows.append(row)

def chunk(kind, payload):
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xffffffff)

image = directory / "isoluminant.png"
image.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
                  chunk(b"IDAT", zlib.compress(b"".join(rows))) + chunk(b"IEND", b""))
(directory / "jobs.json").write_text(json.dumps([{"Id": 0, "Image": str(image), "X1": 16, "Y1": 4, "X2": 16, "Y2": 56}]), encoding="utf-8")
print("Created synthetic 64×64 PNG; both opaque colours produce grayscale 45.")
