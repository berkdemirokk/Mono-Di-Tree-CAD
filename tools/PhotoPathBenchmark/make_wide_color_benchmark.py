"""Create an isolated benchmark for the actual opt-in wide color path.

Usage: python make_wide_color_benchmark.py NEW_DIRECTORY standard|wide
Only copies source; it does not change the application or start a model.
"""
import hashlib
import json
from pathlib import Path
import shutil
import sys

if len(sys.argv) != 3 or sys.argv[2] not in ("standard", "wide"):
    raise SystemExit("Expected NEW_DIRECTORY and standard or wide.")
destination, mode = Path(sys.argv[1]).resolve(), sys.argv[2]
root = Path(__file__).resolve().parents[2]
paths = [root / "src/TekstilCAD.Desktop" / name for name in ("PhotoEdgePath.cs", "PhotoColorEdgePath.cs", "PhotoWideEdgePath.cs")]
sources = {path.name: path.read_bytes() for path in paths}
program = (root / "tools/PhotoPathBenchmark/Program.cs").read_text(encoding="utf-8")
old = "var ok=PhotoEdgePath.TryFind(image,new Point(job.X1,job.Y1),new Point(job.X2,job.Y2),out var path);"
new = ("var ok=PhotoWideEdgePath.TryFind(image,new Point(job.X1,job.Y1),new Point(job.X2,job.Y2),true,out var path,out var usedStandard);if(usedStandard)throw new InvalidOperationException(\"Unexpected standard-area fallback in this benchmark.\");" if mode == "wide" else
       "var ok=PhotoColorEdgePath.TryFind(image,new Point(job.X1,job.Y1),new Point(job.X2,job.Y2),out var path);")
if program.count(old) != 1:
    raise SystemExit("Benchmark call differs from the expected version.")
benchmark = (root / "tools/PhotoPathBenchmark/contour_benchmark.py").read_text(encoding="utf-8")
label = "{anchor_count} independent product path searches including closing interval"
if benchmark.count(label) != 1:
    raise SystemExit("Benchmark method description differs.")
benchmark = benchmark.replace(label, "{anchor_count} independent " + mode + " RGB product path searches including closing interval")
destination.mkdir(parents=True, exist_ok=False)
for name, content in sources.items():
    (destination / name).write_bytes(content)
(destination / "Program.cs").write_text(program.replace(old, new), encoding="utf-8")
(destination / "contour_benchmark.py").write_text(benchmark, encoding="utf-8")
(destination / "PhotoPathBenchmark.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>', encoding="utf-8")
(destination / "source-hashes.json").write_text(json.dumps({name: hashlib.sha256(content).hexdigest().upper() for name, content in sources.items()}, indent=2), encoding="utf-8")
print("Created isolated " + mode + " RGB benchmark; unexpected wide-mode fallback is a hard failure.")
