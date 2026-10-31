using System.Globalization;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfExportStructureTests
{
    private static string Target()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root missing.");
        var folder=Path.Combine(root.FullName,"work","dxf-export-structure",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);return Path.Combine(folder,"test.dxf");
    }
    private sealed record Entry(string Type,List<(int Code,string Value)> Fields)
    {
        public string One(int code)=>Assert.Single(Fields,p=>p.Code==code).Value;
        public bool Has(int code,string value)=>Fields.Any(p=>p.Code==code && p.Value==value);
    }
    private static List<Entry> ParseRecords(string text)
    {
        var lines=text.Split('\n');var entries=new List<Entry>();
        for(var i=0;i+1<lines.Length;i+=2)
        {
            var code=int.Parse(lines[i],CultureInfo.InvariantCulture);var value=lines[i+1].TrimEnd('\r');
            if(code==0)entries.Add(new Entry(value,[]));else if(entries.Count>0)entries[^1].Fields.Add((code,value));
        }
        return entries;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionedTableAndEntityRecordsHaveUniqueHandlesAndRequiredStructure(bool includesZero)
    {
        var source=new ProjectDocument { Pieces=[new PatternPiece { Name=includesZero?"0":"Parça",Points=[new(0,0),new(10,0),new(10,10)] },
            new PatternPiece { Name="İkinci",Closed=false,Points=[new(20,0),new(30,0)] }] };
        var path=Target();DxfFormat.Export(source,path);
        var records=ParseRecords(File.ReadAllText(path));
        var tables=records.Where(r=>r.Type=="TABLE").ToList();
        Assert.Equal(new[]{"LTYPE","LAYER"},tables.Select(t=>t.One(2)));
        Assert.All(tables,t=>Assert.True(t.Has(100,"AcDbSymbolTable")));
        var lineType=Assert.Single(records,r=>r.Type=="LTYPE");
        Assert.Equal("CONTINUOUS",lineType.One(2));Assert.True(lineType.Has(100,"AcDbLinetypeTableRecord"));
        Assert.Equal(tables[0].One(5),lineType.One(330));
        var layerRecords=records.Where(r=>r.Type=="LAYER").ToArray();
        Assert.Single(layerRecords,r=>r.One(2)=="0");
        Assert.Equal(layerRecords.Length,int.Parse(tables[1].One(70),CultureInfo.InvariantCulture));
        Assert.Equal(layerRecords.Length,layerRecords.Select(r=>r.One(2)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(layerRecords,r=> { Assert.True(r.Has(100,"AcDbSymbolTableRecord") && r.Has(100,"AcDbLayerTableRecord"));
            Assert.Equal(tables[1].One(5),r.One(330));Assert.Equal("0",r.One(70));Assert.Equal("7",r.One(62));Assert.Equal("1",r.One(290));Assert.Equal("CONTINUOUS",r.One(6)); });
        var polylines=records.Where(r=>r.Type=="LWPOLYLINE").ToArray();Assert.Equal(2,polylines.Length);
        Assert.All(polylines,p=> { Assert.True(p.Has(100,"AcDbEntity") && p.Has(100,"AcDbPolyline"));
            Assert.Contains(layerRecords,l=>string.Equals(l.One(2),p.One(8),StringComparison.OrdinalIgnoreCase)); });
        var handles=records.Where(r=>r.Type is "TABLE" or "LTYPE" or "LAYER" or "LWPOLYLINE").Select(r=>r.One(5)).ToArray();
        Assert.Equal(handles.Length,handles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(handles,h=>Assert.True(ulong.TryParse(h,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _)));
        var imported=DxfFormat.Import(path);
        Assert.Equal(source.Pieces.Count,imported.Project.Pieces.Count);
        for(var i=0;i<source.Pieces.Count;i++) { Assert.Equal(source.Pieces[i].Points,imported.Project.Pieces[i].Points);Assert.Equal(source.Pieces[i].Closed,imported.Project.Pieces[i].Closed); }
    }

    [Fact]
    public void EmptyProjectCannotReplaceExistingTarget()
    {
        var path=Target();var bytes=new byte[]{1,2,3,4};File.WriteAllBytes(path,bytes);
        Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(new ProjectDocument(),path));
        Assert.Equal(bytes,File.ReadAllBytes(path));
    }

    [Fact]
    public void ExportDeclaresDxf2007AndWritesValidUtf8TurkishLayerNames()
    {
        var piece=new PatternPiece { Name="Ön ŞİĞ çığ",Points=[new(0,0),new(10,0),new(10,10)] };
        var path=Target();DxfFormat.Export(new ProjectDocument { Pieces=[piece] },path);
        var bytes=File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(new byte[]{0xEF,0xBB,0xBF}));
        var text=new UTF8Encoding(false,true).GetString(bytes);
        Assert.Contains("9\n$ACADVER\n1\nAC1021\n",text);
        Assert.True(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(piece.Name))>=0);
        var imported=DxfFormat.Import(path);
        Assert.Equal(piece.Name,Assert.Single(imported.Project.Pieces).Name);
        Assert.Equal(piece.Points,imported.Project.Pieces[0].Points);
    }

    [Fact]
    public void InvalidLegacyBytesFailExplicitlyWithoutReplacingDifferentLayerNames()
    {
        // 0xFE and 0xF0 are different CP1254 characters. Replacement UTF-8
        // decoding previously turned both into the same replacement character.
        var template="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nA\u00FE\n10\n0\n20\n0\n11\n10\n21\n0\n"+
            "0\nLINE\n8\nA\u00F0\n10\n20\n20\n0\n11\n30\n21\n0\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.Latin1.GetBytes(template);using var input=new MemoryStream(bytes);
        var error=Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(input,"legacy"));
        Assert.Contains("UTF-8",error.Message);Assert.Contains("ANSI",error.Message);
        Assert.Equal(bytes,input.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidUtf8WithOrWithoutBomPreservesDistinctTurkishLayers(bool bom)
    {
        var text="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nAş\n10\n0\n20\n0\n11\n10\n21\n0\n"+
            "0\nLINE\n8\nAğ\n10\n20\n20\n0\n11\n30\n21\n0\n0\nENDSEC\n0\nEOF\n";
        var content=Encoding.UTF8.GetBytes(text);
        if(bom)content=new byte[]{0xEF,0xBB,0xBF}.Concat(content).ToArray();
        using var input=new MemoryStream(content);
        var result=DxfFormat.Import(input,"utf8");
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Project.Pieces,p=>p.Name=="Aş");
        Assert.Contains(result.Project.Pieces,p=>p.Name=="Ağ");
    }

    [Fact]
    public void CancelledUtf8ImportStillHonorsCancellation()
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes("0\nEOF\n"));
        using var token=new CancellationTokenSource();token.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfFormat.Import(input,"cancel",1,token.Token));
    }
}
