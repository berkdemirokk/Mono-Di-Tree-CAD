"""Create an isolated binomial-blur path prototype; never edits product files.

Usage: python make_blur_experiment.py NEW_DIRECTORY 3|5
Run the generated contour_benchmark.py with its normal arguments.
"""
import hashlib
from pathlib import Path
import shutil
import sys

if len(sys.argv) != 3 or sys.argv[2] not in ("3", "5"):
    raise SystemExit("Expected NEW_DIRECTORY and kernel width 3 or 5.")
destination, width = Path(sys.argv[1]).resolve(), sys.argv[2]
root = Path(__file__).resolve().parents[2]
source = (root / "src/TekstilCAD.Desktop/PhotoEdgePath.cs").read_text(encoding="utf-8")
expected = "ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648"
if hashlib.sha256(source.encode("utf-8")).hexdigest() != expected:
    raise SystemExit("Product path source differs from the recorded experiment; use the recorded revision.")
kernel = "1,2,1" if width == "3" else "1,4,6,4,1"
addition = '''        var kernel=new[] { KERNEL }; int radius=kernel.Length/2; int total=kernel.Sum(); int denominator=total*total;
        var horizontal=new int[count]; var filtered=new byte[count];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var i=y*width+x;if((i&4095)==0)token.ThrowIfCancellationRequested();int sum=0;
            for(int k=0;k<kernel.Length;k++)sum+=kernel[k]*gray[y*width+Math.Clamp(x+k-radius,0,width-1)];
            horizontal[i]=sum;
        }
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var i=y*width+x;if((i&4095)==0)token.ThrowIfCancellationRequested();int sum=0;
            for(int k=0;k<kernel.Length;k++)sum+=kernel[k]*horizontal[Math.Clamp(y+k-radius,0,height-1)*width+x];
            filtered[i]=(byte)((sum+denominator/2)/denominator);
        }
        gray=filtered;
        var cost=new double[count];'''.replace("KERNEL", kernel)
old = "        var cost=new double[count];"
if source.count(old) != 1:
    raise SystemExit("Recorded source pattern does not occur exactly once.")
source = source.replace(old, addition)
benchmark = (root / "tools/PhotoPathBenchmark/contour_benchmark.py").read_text(encoding="utf-8")
old = "{anchor_count} independent product path searches including closing interval"
if benchmark.count(old) != 1:
    raise SystemExit("Benchmark method description changed.")
benchmark = benchmark.replace(old, "{anchor_count} independent binomial-blur " + width + " experimental path searches including closing interval")
destination.mkdir(parents=True, exist_ok=False)
(destination / "PhotoEdgePath.cs").write_text(source, encoding="utf-8")
(destination / "contour_benchmark.py").write_text(benchmark, encoding="utf-8")
shutil.copyfile(root / "tools/PhotoPathBenchmark/Program.cs", destination / "Program.cs")
(destination / "PhotoPathBenchmark.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>',
    encoding="utf-8")
print(f"Created experimental kernel {width} prototype in {destination}")
