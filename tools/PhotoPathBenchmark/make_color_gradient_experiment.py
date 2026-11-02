"""Create an isolated RGB-gradient path prototype without editing product code.

Usage: python make_color_gradient_experiment.py NEW_DIRECTORY
The generated contour benchmark accepts the ordinary dataset arguments.
"""
import hashlib
from pathlib import Path
import shutil
import sys

if len(sys.argv) != 2:
    raise SystemExit("Expected NEW_DIRECTORY.")
destination = Path(sys.argv[1]).resolve()
root = Path(__file__).resolve().parents[2]
source = (root / "src/TekstilCAD.Desktop/PhotoEdgePath.cs").read_text(encoding="utf-8")
if hashlib.sha256(source.encode("utf-8")).hexdigest() != "ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648":
    raise SystemExit("Product path source differs from the recorded experiment; use the recorded revision.")
replacements = [
    ("var gray=new byte[count];", "var blue=new byte[count];var green=new byte[count];var red=new byte[count];"),
    ("int p=i*4,a=pixels[p+3],v=(29*pixels[p]+150*pixels[p+1]+77*pixels[p+2]+128)>>8;\n            gray[i]=(byte)((v*a+255*(255-a)+127)/255);",
     "int p=i*4,a=pixels[p+3];\n            blue[i]=(byte)((pixels[p]*a+255*(255-a)+127)/255);green[i]=(byte)((pixels[p+1]*a+255*(255-a)+127)/255);red[i]=(byte)((pixels[p+2]*a+255*(255-a)+127)/255);"),
    ("int gx=gray[y*width+Math.Min(x+1,width-1)]-gray[y*width+Math.Max(x-1,0)];\n            int gy=gray[Math.Min(y+1,height-1)*width+x]-gray[Math.Max(y-1,0)*width+x];\n            var strength=Math.Sqrt(gx*gx+gy*gy);",
     "int leftIndex=y*width+Math.Max(x-1,0),rightIndex=y*width+Math.Min(x+1,width-1),upIndex=Math.Max(y-1,0)*width+x,downIndex=Math.Min(y+1,height-1)*width+x;\n            static int Power(byte[] channel,int left,int right,int up,int down) { int dx=channel[right]-channel[left],dy=channel[down]-channel[up];return dx*dx+dy*dy; }\n            var strength=Math.Sqrt((29d*Power(blue,leftIndex,rightIndex,upIndex,downIndex)+150d*Power(green,leftIndex,rightIndex,upIndex,downIndex)+77d*Power(red,leftIndex,rightIndex,upIndex,downIndex))/256d);"),
]
for old, new in replacements:
    if source.count(old) != 1:
        raise SystemExit("Recorded source pattern does not occur exactly once.")
    source = source.replace(old, new)
benchmark = (root / "tools/PhotoPathBenchmark/contour_benchmark.py").read_text(encoding="utf-8")
old = "{anchor_count} independent product path searches including closing interval"
if benchmark.count(old) != 1:
    raise SystemExit("Benchmark method description changed.")
benchmark = benchmark.replace(old, "{anchor_count} independent RGB-gradient experimental path searches including closing interval")
destination.mkdir(parents=True, exist_ok=False)
(destination / "PhotoEdgePath.cs").write_text(source, encoding="utf-8")
(destination / "contour_benchmark.py").write_text(benchmark, encoding="utf-8")
shutil.copyfile(root / "tools/PhotoPathBenchmark/Program.cs", destination / "Program.cs")
(destination / "PhotoPathBenchmark.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>',
    encoding="utf-8")
print(f"Created experimental RGB-gradient prototype in {destination}")
