"""Write one synthetic PNG and a path job for the 160,000-pixel search budget.

Usage: python margin_budget_fixture.py NEW_EMPTY_DIRECTORY
The diagonal is synthetic and does not measure garment accuracy.
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
    raise SystemExit("Work directory must be empty.")
size = 512
rows = []
for y in range(size):
    row = bytearray([0])
    for x in range(size):
        value = 0 if 80 <= x <= 430 and abs(x-y) <= 1 else 255
        row.extend((value, value, value, 255))
    rows.append(row)
def chunk(kind, payload):
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xffffffff)
image = directory / "synthetic-diagonal.png"
image.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) +
                  chunk(b"IDAT", zlib.compress(b"".join(rows))) + chunk(b"IEND", b""))
(directory / "jobs.json").write_text(json.dumps([{"Id": 0, "Image": str(image), "X1": 80, "Y1": 80, "X2": 430, "Y2": 430}]), encoding="utf-8")
print("Created one synthetic 512×512 image; endpoints differ by 350 pixels on both axes.")
