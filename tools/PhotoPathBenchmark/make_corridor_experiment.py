"""Create an isolated straight-line-distance prototype; never edits product files.

Usage: python make_corridor_experiment.py NEW_DIRECTORY 0.1|0.3
Run the generated contour_benchmark.py with its normal arguments.
"""
import hashlib
from pathlib import Path
import shutil
import sys

if len(sys.argv) != 3 or sys.argv[2] not in ("0.1", "0.3"):
    raise SystemExit("Expected NEW_DIRECTORY and weight 0.1 or 0.3.")
destination, weight = Path(sys.argv[1]).resolve(), sys.argv[2]
root = Path(__file__).resolve().parents[2]
source = (root / "src/TekstilCAD.Desktop/PhotoEdgePath.cs").read_text(encoding="utf-8")
expected = "ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648"
if hashlib.sha256(source.encode("utf-8")).hexdigest() != expected:
    raise SystemExit("Product path source differs from the recorded experiment; use the recorded revision.")
replacements = [
    ("var cost=new double[count];", "var cost=new double[count]; double lineX=(double)ex-sx,lineY=(double)ey-sy; var lineLength=Math.Sqrt(lineX*lineX+lineY*lineY);"),
    ("cost[i]=0.12+64/(64+strength);", "var deviation=Math.Abs(((double)left+x-sx)*lineY-((double)top+y-sy)*lineX)/lineLength; cost[i]=0.12+64/(64+strength)+" + weight + "*deviation/(8+deviation);"),
]
for old, new in replacements:
    if source.count(old) != 1:
        raise SystemExit("Recorded source pattern does not occur exactly once.")
    source = source.replace(old, new)
benchmark = (root / "tools/PhotoPathBenchmark/contour_benchmark.py").read_text(encoding="utf-8")
old = "{anchor_count} independent product path searches including closing interval"
if benchmark.count(old) != 1:
    raise SystemExit("Benchmark method description changed.")
benchmark = benchmark.replace(old, "{anchor_count} independent straight-line-penalty " + weight + " experimental path searches including closing interval")
destination.mkdir(parents=True, exist_ok=False)
(destination / "PhotoEdgePath.cs").write_text(source, encoding="utf-8")
(destination / "contour_benchmark.py").write_text(benchmark, encoding="utf-8")
shutil.copyfile(root / "tools/PhotoPathBenchmark/Program.cs", destination / "Program.cs")
(destination / "PhotoPathBenchmark.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>',
    encoding="utf-8")
print(f"Created experimental weight {weight} prototype in {destination}")
