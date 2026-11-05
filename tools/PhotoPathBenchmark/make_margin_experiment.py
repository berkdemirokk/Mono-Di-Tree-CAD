"""Create an isolated wider search-margin prototype without changing product code.

Usage: python make_margin_experiment.py NEW_DIRECTORY 32|64
"""
import hashlib
from pathlib import Path
import shutil
import sys

if len(sys.argv) != 3 or sys.argv[2] not in ("32", "64"):
    raise SystemExit("Expected NEW_DIRECTORY and margin 32 or 64.")
destination, margin = Path(sys.argv[1]).resolve(), sys.argv[2]
root = Path(__file__).resolve().parents[2]
source = (root / "src/TekstilCAD.Desktop/PhotoEdgePath.cs").read_text(encoding="utf-8")
expected = "ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648"
if hashlib.sha256(source.encode("utf-8")).hexdigest() != expected:
    raise SystemExit("Product path source differs from the recorded experiment; use the recorded revision.")
old = "private const int Margin=16;"
if source.count(old) != 1:
    raise SystemExit("Recorded margin occurs an unexpected number of times.")
source = source.replace(old, "private const int Margin=" + margin + ";")
benchmark = (root / "tools/PhotoPathBenchmark/contour_benchmark.py").read_text(encoding="utf-8")
old = "{anchor_count} independent product path searches including closing interval"
if benchmark.count(old) != 1:
    raise SystemExit("Benchmark method description changed.")
benchmark = benchmark.replace(old, "{anchor_count} independent experimental margin " + margin + " path searches including closing interval")
destination.mkdir(parents=True, exist_ok=False)
(destination / "PhotoEdgePath.cs").write_text(source, encoding="utf-8")
(destination / "contour_benchmark.py").write_text(benchmark, encoding="utf-8")
shutil.copyfile(root / "tools/PhotoPathBenchmark/Program.cs", destination / "Program.cs")
(destination / "PhotoPathBenchmark.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>',
    encoding="utf-8")
print(f"Created experimental margin {margin} prototype in {destination}")
