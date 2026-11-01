using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfBlockOccurrenceTests
{
    [Fact]
    public void NineSeparateBlockLinesCanBeExplicitlyCopiedWithoutProductionClaim()
    {
        var lines=string.Concat(Enumerable.Range(0,9).Select(i=>$"0\nLINE\n8\nA\n10\n{i*20}\n20\n0\n11\n{i*20+10}\n21\n0\n"));
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n"+lines+"0\nENDBLK\n0\nENDSEC\n0\nEOF\n");
        var original=bytes.ToArray();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var links=import.PieceSources!.Value.Where(p=>p.EntityType=="LINE").ToArray();Assert.Equal(9,links.Length);
        var choices=links.Select(p=>new DxfBlockProjectPieceSelection(p.BlockHeaderOrdinal!.Value,p.PieceId)).ToArray();
        var result=DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[],choices,"source.dxf",true);
        Assert.Equal(9,result.WorkingCopy.Pieces.Count);Assert.All(result.WorkingCopy.Pieces,p=> { Assert.False(p.Closed);Assert.Equal(2,p.Points.Count); });
        var loaded=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(result.WorkingCopy)));
        Assert.True(TekstilCAD.Core.DocumentSession.ContentEquals(result.WorkingCopy,loaded));
        Assert.Contains(loaded.ImportProvenance!.Warnings,w=>w.Contains("LINE kaynak kayıtları"));Assert.Equal(original,bytes);
    }

    [Fact]
    public void CoincidentUnselectedLineMakesSelectedLineAmbiguous()
    {
        const string line="0\nLINE\n8\nA\n10\n0\n20\n0\n11\n10\n21\n0\n";
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n"+line+line+"0\nENDBLK\n0\nENDSEC\n0\nEOF\n");
        var import=DxfFormat.Import(new MemoryStream(bytes),"test");var link=import.PieceSources!.Value.First(p=>p.EntityType=="LINE");
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[],
            [new(link.BlockHeaderOrdinal!.Value,link.PieceId)],"source.dxf",true));
    }

    [Fact]
    public void LineChainAcceptsExactSourceJointWithReversedSegment()
    {
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n"+
            "0\nLINE\n8\nA\n10\n0\n20\n0\n11\n10\n21\n0\n"+
            "0\nLINE\n8\nA\n10\n20\n20\n0\n11\n10\n21\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n");
        var import=DxfFormat.Import(new MemoryStream(bytes),"test");var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="LINE"));
        DxfBlockProjectResult Create()=>DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[],[new(link.BlockHeaderOrdinal!.Value,link.PieceId)],"source.dxf",true);
        Assert.Equal(3,Assert.Single(Create().WorkingCopy.Pieces).Points.Count);
    }

    [Fact]
    public void NearButDifferentSourceEndpointsAreNotJoinedByImporter()
    {
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n"+
            "0\nLINE\n8\nA\n10\n0\n20\n0\n11\n10\n21\n0\n"+
            "0\nLINE\n8\nA\n10\n20\n20\n0\n11\n10.0000001\n21\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n");
        var before=bytes.ToArray();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var links=import.PieceSources!.Value.Where(p=>p.EntityType=="LINE").ToArray();
        Assert.Equal(2,links.Length);Assert.All(links,p=>Assert.Single(p.SourceOrdinals));
        Assert.All(import.Project.Pieces,p=>Assert.Equal(2,p.Points.Count));
        foreach(var link in links)
            Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[],[new(link.BlockHeaderOrdinal!.Value,link.PieceId)],"source.dxf",true));
        Assert.Equal(before,bytes);
    }

    [Fact]
    public void PlainSelfIntersectingContourCarriesPersistentUnverifiedGeometryWarning()
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n10\n0\nVERTEX\n10\n0\n20\n10\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var original=bytes.ToArray();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var result=DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[],
            [new(link.BlockHeaderOrdinal!.Value,link.PieceId)],"source.dxf",true);
        Assert.True(TekstilCAD.Core.Geometry.HasSelfIntersections(Assert.Single(result.WorkingCopy.Pieces).Points));
        var reopened=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(result.WorkingCopy)));
        Assert.Contains(reopened.ImportProvenance!.Warnings,w=>w.Contains("Konturun basitliği, alanı ve üretime uygunluğu doğrulanmadı"));
        Assert.Equal(original,bytes);Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));
    }

    [Fact]
    public void ExplicitPlainAndMarkedBlockPiecesShareOneProjectAtomically()
    {
        string Block(string name,string x,bool point)=>"0\nBLOCK\n2\n"+name+"\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n"+x+"\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            (point ? "0\nPOINT\n8\nB\n10\n"+x+"\n20\n0\n" : "")+"0\nENDBLK\n";
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n"+Block("A","0",true)+Block("B","2",false)+"0\nENDSEC\n0\nEOF\n");
        var before=bytes.ToArray();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var links=import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE").ToArray();Assert.Equal(2,links.Length);
        var point=Assert.Single(import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT"));
        var marked=new DxfBlockProjectPointSelection(links[0].BlockHeaderOrdinal!.Value,links[0].PieceId,point.SourceOrdinal,0);
        var plain=new DxfBlockProjectPieceSelection(links[1].BlockHeaderOrdinal!.Value,links[1].PieceId);
        var output=DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[marked],[plain],"source.dxf",true);
        Assert.Equal(2,output.WorkingCopy.Pieces.Count);Assert.Single(output.Assignments);
        Assert.Single(output.WorkingCopy.Pieces.Single(p=>p.Id==marked.PieceId).Notches!);
        Assert.Null(output.WorkingCopy.Pieces.Single(p=>p.Id==plain.PieceId).Notches);
        var saved=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(output.WorkingCopy)));
        Assert.True(TekstilCAD.Core.DocumentSession.ContentEquals(output.WorkingCopy,saved));
        Assert.Contains(saved.ImportProvenance!.Warnings,w=>w.Contains("İşaretsiz blok"));
        var plainOnly=DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[],[plain],"source.dxf",true);
        Assert.Single(plainOnly.WorkingCopy.Pieces);Assert.Empty(plainOnly.Assignments);Assert.Null(plainOnly.WorkingCopy.Pieces[0].Notches);
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[marked],[plain,plain],"source.dxf",true));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[marked],[new(marked.BlockHeaderOrdinal,marked.PieceId)],"source.dxf",true));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[],[plain with { BlockHeaderOrdinal=marked.BlockHeaderOrdinal }],"source.dxf",true));
        var altered=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n"+Block("A","0",true)+Block("B","3",false)+"0\nENDSEC\n0\nEOF\n");
        using(var changing=new SwitchOnRewindStream(bytes,altered))
            Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,changing,[marked],[plain],"source.dxf",true));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfBlockDefinitionPointMapping.CreateProjectWithPieces(import,new MemoryStream(bytes),[marked],[plain],"source.dxf",true,cancelled.Token));
        Assert.Equal(before,bytes);Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));
    }

    [Fact]
    public void ProvenanceEscapesUntrustedLayerDelimitersAndUnicodeControls()
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            "0\nPOINT\n8\nB; POINT 123\\U+202E\n10\n0\n20\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var point=Assert.Single(import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT"));
        var result=DxfBlockDefinitionPointMapping.CreateProject(import,new MemoryStream(bytes),
            [new(link.BlockHeaderOrdinal!.Value,link.PieceId,point.SourceOrdinal,0)],"source.dxf",true);
        var reloaded=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(result.WorkingCopy)));
        var warning=Assert.Single(reloaded.ImportProvenance!.Warnings.Where(w=>w.Contains("POINT katmanı")));
        Assert.Contains(@"B\u003B POINT 123\u005CU+202E",warning);
        Assert.DoesNotContain("B; POINT 123",warning);
        var escape=typeof(DxfBlockDefinitionPointMapping).GetMethod("EscapeAuditText",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        Assert.Equal(@"a\u003Bb\u005Cc\u202Ed\u2028e\u2029f",(string)escape.Invoke(null,["a;b\\c\u202ed\u2028e\u2029f"])!);
    }

    [Fact]
    public void MultipleBlockDefinitionsProduceOneDetachedProjectWithTrace()
    {
        string Block(string x)=>"0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n"+x+"\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            "0\nPOINT\n8\nB\n10\n"+x+"\n20\n0\n0\nENDBLK\n";
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n"+Block("0")+Block("2")+"0\nENDSEC\n0\nEOF\n");
        var original=bytes.ToArray();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var links=import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE").ToArray();
        var points=import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT").ToArray();
        Assert.Equal(2,links.Length);Assert.Equal(2,points.Length);
        var choices=links.Select((p,i)=>new DxfBlockProjectPointSelection(p.BlockHeaderOrdinal!.Value,p.PieceId,points[i].SourceOrdinal,0)).ToArray();
        var result=DxfBlockDefinitionPointMapping.CreateProject(import,new MemoryStream(bytes),choices,"source.dxf",true);
        Assert.Equal(2,result.WorkingCopy.Pieces.Count);Assert.Equal(2,result.Assignments.Length);
        Assert.All(result.Assignments,a=> { Assert.Equal("A",a.SourceLayer);Assert.Equal("B",a.PointLayer); });
        Assert.All(result.WorkingCopy.Pieces,p=>Assert.Single(p.Notches!));
        Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));Assert.Equal(original,bytes);
        var reopened=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(result.WorkingCopy)));
        Assert.True(TekstilCAD.Core.DocumentSession.ContentEquals(result.WorkingCopy,reopened));
        Assert.Equal(import.SourceSha256,reopened.ImportProvenance!.SourceSha256);Assert.Contains("Yerleştirilmemiş",result.Notice);
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProject(import,new MemoryStream(bytes),[choices[0],choices[1] with { VertexIndex=1 }],"source.dxf",true));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProject(import,new MemoryStream(bytes),[choices[0],choices[0]],"source.dxf",true));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProject(import,new MemoryStream(bytes),choices,"source.dxf",false));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProject(import,new MemoryStream(bytes),choices,@"C:\private\source.dxf",true));
        var changed=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n"+Block("0")+Block("3")+"0\nENDSEC\n0\nEOF\n");
        using(var changedSource=new SwitchOnRewindStream(bytes,changed))
            Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateProject(import,changedSource,choices,"source.dxf",true));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfBlockDefinitionPointMapping.CreateProject(import,new MemoryStream(bytes),choices,"source.dxf",true,cancelled.Token));
        Assert.Equal(original,bytes);Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));
    }

    [Theory]
    [InlineData("2",true)]
    [InlineData("0",false)]
    public void OtherThreeDimensionalPointMustNotCoincideInVisiblePlane(string x,bool allowed)
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            "0\nPOINT\n8\nA\n10\n0\n20\n0\n0\nPOINT\n8\nA\n10\n"+x+"\n20\n0\n30\n1\n50\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var point=import.SupplementalGeometry!.Records.First(p=>p.EntityType=="POINT");
        DxfBlockDefinitionPointResult Create()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),new(point.SourceOrdinal,link.PieceId,0),link.BlockHeaderOrdinal!.Value,true);
        if(allowed)Assert.Single(Assert.Single(Create().WorkingCopy.Pieces).Notches!);
        else Assert.Throws<InvalidDataException>(()=>Create());
    }

    [Theory]
    [InlineData("0",false)]
    [InlineData("0.000",false)]
    [InlineData("1e-400",false)]
    [InlineData("not-a-number",false)]
    [InlineData("0",true)]
    public void CoincidentOrUnverifiablePointIsRejectedOnlyWithinItsBlock(string otherX,bool otherBlock)
    {
        const string point="0\nPOINT\n8\nA\n10\n0\n20\n0\n";
        var extra="0\nPOINT\n8\nA\n10\n"+otherX+"\n20\n0\n";
        var poly="0\nPOLYLINE\n8\nA\n70\n1\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n";
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n"+poly+point+(otherBlock ? "" : extra)+"0\nENDBLK\n"+
            (otherBlock ? "0\nBLOCK\n2\nB\n"+extra+"0\nENDBLK\n" : "")+"0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var original=bytes.ToArray();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var selected=import.SupplementalGeometry!.Records.First(p=>p.EntityType=="POINT" && p.BlockHeaderOrdinal==link.BlockHeaderOrdinal);
        DxfBlockDefinitionPointResult Create()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),new(selected.SourceOrdinal,link.PieceId,0),link.BlockHeaderOrdinal!.Value,true);
        if(otherBlock)Assert.Single(Assert.Single(Create().WorkingCopy.Pieces).Notches!);
        else Assert.Throws<InvalidDataException>(()=>Create());
        Assert.Equal(original,bytes);Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));
    }

    [Fact]
    public void MultipleSelectedPointsAreAtomicAndUseOnePiece()
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            "0\nPOINT\n8\nA\n10\n0\n20\n0\n0\nPOINT\n8\nA\n10\n10\n20\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var original=bytes.ToArray();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var points=import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT").ToArray();Assert.Equal(2,points.Length);
        var first=new DxfPointVertexSelection(points[0].SourceOrdinal,link.PieceId,0);
        var second=new DxfPointVertexSelection(points[1].SourceOrdinal,link.PieceId,1);
        var result=DxfBlockDefinitionPointMapping.CreateMany(import,new MemoryStream(bytes),[first,second],link.BlockHeaderOrdinal!.Value,true);
        Assert.Equal(new[]{0,1},Assert.Single(result.WorkingCopy.Pieces).Notches!.Select(n=>n.VertexIndex));
        Assert.Equal(2,result.Assignments.Length);Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));Assert.Equal(original,bytes);
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateMany(import,new MemoryStream(bytes),[first,second with { VertexIndex=2 }],link.BlockHeaderOrdinal!.Value,true));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateMany(import,new MemoryStream(bytes),[first,first],link.BlockHeaderOrdinal!.Value,true));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateMany(import,new MemoryStream(bytes),Enumerable.Repeat(first,33).ToArray(),link.BlockHeaderOrdinal!.Value,true));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateMany(import,new MemoryStream(bytes),[first,second with { PieceId=Guid.NewGuid() }],link.BlockHeaderOrdinal!.Value,true));
        var changed=Encoding.ASCII.GetBytes(text.Replace("10\n10\n20\n0\n0\nENDBLK","10\n11\n20\n0\n0\nENDBLK"));
        using(var unstable=new SwitchOnRewindStream(bytes,changed))
        {
            Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateMany(import,unstable,[first,second],link.BlockHeaderOrdinal!.Value,true));
            Assert.Equal(1,unstable.Rewinds);
        }
        using(var mutated=new MutatingReadStream(bytes,changed))
            Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.CreateMany(import,mutated,[first,second],link.BlockHeaderOrdinal!.Value,true));
        using(var midCancellation=new CancellationTokenSource())
        using(var cancellingStream=new SwitchOnRewindStream(bytes,bytes,midCancellation))
            Assert.Throws<OperationCanceledException>(()=>DxfBlockDefinitionPointMapping.CreateMany(import,cancellingStream,[first,second],link.BlockHeaderOrdinal!.Value,true,midCancellation.Token));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfBlockDefinitionPointMapping.CreateMany(import,new MemoryStream(bytes),[first,second],link.BlockHeaderOrdinal!.Value,true,cancellation.Token));
        Assert.Equal(original,bytes);Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));
    }

    private sealed class SwitchOnRewindStream(byte[] first,byte[] second,CancellationTokenSource? cancelOnRewind=null) : Stream
    {
        private readonly MemoryStream a=new(first),b=new(second);private int rewinds;
        public int Rewinds=>rewinds;
        private MemoryStream Current=>rewinds>=1 ? b : a;
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
        public override long Length=>Current.Length;
        public override long Position { get=>Current.Position; set { if(value!=0)throw new NotSupportedException();rewinds++;Current.Position=0;cancelOnRewind?.Cancel(); } }
        public override int Read(byte[] buffer,int offset,int count)=>Current.Read(buffer,offset,count);
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if(disposing) { a.Dispose();b.Dispose(); } base.Dispose(disposing); }
    }

    private sealed class MutatingReadStream(byte[] first,byte[] second) : Stream
    {
        private int position;
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
        public override long Length=>first.Length;
        public override long Position { get=>position; set=>throw new NotSupportedException(); }
        public override int Read(byte[] buffer,int offset,int count)
        {
            if(position>=first.Length)return 0;
            int half=first.Length/2;var src=position<half ? first : second;
            int take=Math.Min(count,(position<half ? half : first.Length)-position);
            Array.Copy(src,position,buffer,offset,take);position+=take;return take;
        }
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }

    [Theory]
    [InlineData("0\nVERTEX\n10\n0\n20\n0\n")]
    [InlineData("0\nSEQEND\n")]
    [InlineData("0\nSEQEND\n0\nSEQEND\n")]
    [InlineData("0\nPOLYLINE\n8\nA\n70\n1\n0\nVERTEX\n10\n0\n20\n0\n0\nPOLYLINE\n8\nA\n70\n1\n")]
    public void OrphanVertexOrSequenceEndCannotAuthorizeMapping(string orphan)
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+orphan+
            "0\nPOINT\n8\nA\n10\n0\n20\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var before=bytes.ToArray();
        Assert.Throws<InvalidDataException>(()=>
        {
            var import=DxfFormat.Import(new MemoryStream(bytes),"test");
            var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
            var point=Assert.Single(import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT"));
            DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),new(point.SourceOrdinal,link.PieceId,0),link.BlockHeaderOrdinal!.Value,true);
        });
        Assert.Equal(before,bytes);
    }

    [Fact]
    public void TruncatedBlockInventoryCannotAuthorizeMapping()
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            "0\nPOINT\n8\nA\n10\n0\n20\n0\n"+string.Concat(Enumerable.Repeat("0\nTEXT\n8\nA\n",4097))+"0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var point=Assert.Single(import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT"));
        Assert.True(import.BlockEntityInventory!.Truncated);
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),new(point.SourceOrdinal,link.PieceId,0),link.BlockHeaderOrdinal!.Value,true));
    }

    [Theory]
    [InlineData("SOLID",false)]
    [InlineData("VENDOR_UNKNOWN",false)]
    [InlineData("TEXT",true)]
    public void BlockInventoryRejectsUnknownGeometryButAllowsKnownText(string extraType,bool allowed)
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            "0\n"+extraType+"\n8\nA\n10\n0\n20\n0\n0\nPOINT\n8\nA\n10\n0\n20\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var point=Assert.Single(import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT"));
        DxfBlockDefinitionPointResult Create()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),new(point.SourceOrdinal,link.PieceId,0),link.BlockHeaderOrdinal!.Value,true);
        if(allowed)Assert.Single(Assert.Single(Create().WorkingCopy.Pieces).Notches!);
        else Assert.Throws<InvalidDataException>(()=>Create());
        Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitClosingVertexIsNotAnAmbiguousSecondVertex(bool repeatedInternal)
    {
        var duplicate=repeatedInternal ? "0\nVERTEX\n10\n0\n20\n0\n" : "";
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n"+duplicate+"0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nVERTEX\n10\n0\n20\n0\n0\nSEQEND\n"+
            "0\nPOINT\n8\nA\n10\n0\n20\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var point=Assert.Single(import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT"));
        DxfBlockDefinitionPointResult Create()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),new(point.SourceOrdinal,link.PieceId,0),link.BlockHeaderOrdinal!.Value,true);
        if(repeatedInternal)Assert.Throws<InvalidDataException>(()=>Create());
        else Assert.Single(Assert.Single(Create().WorkingCopy.Pieces).Notches!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LineEndpointsInSameBlockAreCheckedForAmbiguity(bool coincident)
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            "0\nLINE\n8\nA\n10\n"+(coincident ? "0" : "3")+"\n20\n0\n11\n2\n21\n2\n0\nPOINT\n8\nA\n10\n0\n20\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var point=Assert.Single(import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT"));
        var line=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="LINE"));
        Assert.Equal(link.BlockHeaderOrdinal,line.BlockHeaderOrdinal);
        DxfBlockDefinitionPointResult Create()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),new(point.SourceOrdinal,link.PieceId,0),link.BlockHeaderOrdinal!.Value,true);
        if(coincident)Assert.Throws<InvalidDataException>(()=>Create());
        else Assert.Single(Assert.Single(Create().WorkingCopy.Pieces).Notches!);
    }

    [Theory]
    [InlineData("-0","0")]
    [InlineData("0","-0")]
    public void MarkerRetainsTargetSignedZeroAcrossScaledSaveAndLoad(string vertexZero,string pointZero)
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n"+
            "0\nVERTEX\n10\n"+vertexZero+"\n20\n1\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n"+
            "0\nPOINT\n8\nA\n10\n"+pointZero+"\n20\n1\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.ASCII.GetBytes(text);var import=DxfFormat.Import(new MemoryStream(bytes),"test",10);
        var link=Assert.Single(import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE"));
        var point=Assert.Single(import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT"));
        var result=DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),new(point.SourceOrdinal,link.PieceId,0),link.BlockHeaderOrdinal!.Value,true);
        var piece=Assert.Single(result.WorkingCopy.Pieces);var marker=Assert.Single(piece.Notches!);
        Assert.Equal(vertexZero=="-0" ? long.MinValue : 0L,BitConverter.DoubleToInt64Bits(piece.Points[0].X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(piece.Points[0].X),BitConverter.DoubleToInt64Bits(marker.Position.X));
        var reopened=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(result.WorkingCopy)));
        var saved=Assert.Single(reopened.Pieces);var savedMarker=Assert.Single(saved.Notches!);
        Assert.Equal(BitConverter.DoubleToInt64Bits(piece.Points[0].X),BitConverter.DoubleToInt64Bits(saved.Points[0].X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(saved.Points[0].X),BitConverter.DoubleToInt64Bits(savedMarker.Position.X));
    }

    [Fact]
    public void ExplicitBlockDefinitionCopiesOnlyMatchingOccurrence()
    {
        const string block="0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n0\nVERTEX\n10\n4662.231\n20\n1\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n0\nPOINT\n8\nB\n10\n4662.2310\n20\n1\n0\nENDBLK\n";
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n"+block+block+"0\nENDSEC\n0\nEOF\n");
        var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var pieces=import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE").ToArray();
        var points=import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT").ToArray();
        var selected=new DxfPointVertexSelection(points[0].SourceOrdinal,pieces[0].PieceId,0);
        var result=DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),selected,pieces[0].BlockHeaderOrdinal!.Value,true);
        Assert.Single(Assert.Single(result.WorkingCopy.Pieces).Notches!);Assert.Contains("Yerleştirilmemiş",result.Notice);
        Assert.All(import.Project.Pieces,p=>Assert.Null(p.Notches));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),selected,pieces[1].BlockHeaderOrdinal!.Value,true));
        Assert.Throws<InvalidDataException>(()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(bytes),selected,pieces[0].BlockHeaderOrdinal!.Value,false));
        var changed=bytes.ToArray();changed[^2]^=1;
        Assert.ThrowsAny<Exception>(()=>DxfBlockDefinitionPointMapping.Create(import,new MemoryStream(changed),selected,pieces[0].BlockHeaderOrdinal!.Value,true));
    }

    [Theory]
    [InlineData("0\nSECTION\n2\nENTITIES\n0\nBLOCK\n2\nA\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nSECTION\n2\nBLOCKS\n0\nPOINT\n10\n0\n20\n0\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nPOINT\n10\n0\n20\n0\n0\nEOF\n")]
    [InlineData("0\nSECTION\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nSECTION\n2\nENTITIES\n2\nBLOCKS\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nSECTION\n2\nENTITIES\n0\nEOF\n")]
    [InlineData("0\nEOF\n0\nPOINT\n10\n0\n20\n0\n")]
    public void MalformedBlockBoundariesCannotProduceSourceEvidence(string source)
    {
        var bytes=Encoding.ASCII.GetBytes(source);var original=bytes.ToArray();
        Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(new MemoryStream(bytes),"test"));
        Assert.Equal(original,bytes);
    }

    [Fact]
    public void BlockOccurrenceCannotBeSetThroughPublicReportApi()
    {
        foreach(var type in new[]{typeof(DxfPieceSource),typeof(DxfSourceGeometryRecord),typeof(DxfSupplementalGeometryRecord)})
            Assert.Null(type.GetProperty("BlockHeaderOrdinal"));
    }

    [Fact]
    public void DuplicateBlockNamesKeepDistinctSourceOccurrencesAndChildren()
    {
        const string block="0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n0\nPOINT\n8\nA\n10\n0\n20\n0\n0\nENDBLK\n";
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n"+block+block+"0\nENDSEC\n0\nEOF\n");
        var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var pieces=import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE").ToArray();
        var points=import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT").ToArray();
        Assert.Equal(2,pieces.Length);Assert.Equal(2,points.Length);
        Assert.NotNull(pieces[0].BlockHeaderOrdinal);Assert.NotEqual(pieces[0].BlockHeaderOrdinal,pieces[1].BlockHeaderOrdinal);
        for(int i=0;i<2;i++)
        {
            Assert.Equal(pieces[i].BlockHeaderOrdinal,points[i].BlockHeaderOrdinal);
            var source=Assert.Single(import.SourceGeometry!.Records.Where(r=>r.SourceOrdinal==pieces[i].SourceOrdinals[0]));
            Assert.Equal(source.BlockHeaderOrdinal,pieces[i].BlockHeaderOrdinal);
            Assert.Equal(4,pieces[i].ChildSourceOrdinals.Length);
        }
        Assert.True(import.ImportedBlockDefinitions);
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.CaptureFingerprint(import));
    }
}
