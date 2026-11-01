using System.Text;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class RotatedNestingTraceCodecTests
{
    private static (RotatedNestingRecord Record,ProjectDocument Layout) Fixture()
    {
        var negativeZero=BitConverter.Int64BitsToDouble(long.MinValue);
        var p=new PatternPiece { Name="Ön",Points=[new(0,0),new(6,0),new(6,4),new(0,4)],
            Notches=[new NotchMark { VertexIndex=0,Position=new(negativeZero,0) }],
            Grainline=new Grainline { Start=new(1,1),End=new(1,2) },Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 } };
        var source=new ProjectDocument { Pieces=[p] };var angles=new Dictionary<(Guid,int),int> { [(p.Id,1)]=0,[(p.Id,2)]=90 };
        var result=PerCopyOrthogonalNesting.Arrange(source,[p.Id],angles,100,1,100,0,GrainDirectionPolicy.Direction.TwoWay,90);
        var record=RotatedNestingRecord.Capture(source,[p.Id],angles,result,GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100);
        return (record,record.CopyResult().Plan.Nesting.Layout);
    }

    [Fact]
    public void VerifiedBytesRoundTripAndRetainMarkerBits()
    {
        var (record,layout)=Fixture();var bytes=RotatedNestingTraceCodec.SerializeVerified(record);
        var restored=RotatedNestingTraceCodec.DeserializeAndVerify(bytes,layout);
        Assert.True(restored.Verify());Assert.True(RotatedNestingTrace.ContentEquals(record.ToTrace(),restored.ToTrace()));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(restored.CopySource().Pieces[0].Notches![0].Position.X));
    }

    [Fact]
    public void MissingDuplicateUnknownAndInvalidRawFieldsAreRejected()
    {
        var (record,layout)=Fixture();var bytes=RotatedNestingTraceCodec.SerializeVerified(record);
        byte[] Edit(Action<JsonObject> mutate) { var root=JsonNode.Parse(bytes)!.AsObject();mutate(root);return Encoding.UTF8.GetBytes(root.ToJsonString()); }
        Assert.Throws<InvalidDataException>(()=>RotatedNestingTraceCodec.DeserializeAndVerify(Edit(x=>x.Remove("MaxLength")),layout));
        Assert.Throws<InvalidDataException>(()=>RotatedNestingTraceCodec.DeserializeAndVerify(Edit(x=>x["Unknown"]=1),layout));
        Assert.Throws<InvalidDataException>(()=>RotatedNestingTraceCodec.DeserializeAndVerify(Edit(x=>x["Copies"]![0]!.AsObject().Remove("Degrees")),layout));
        Assert.Throws<InvalidDataException>(()=>RotatedNestingTraceCodec.DeserializeAndVerify(Edit(x=>x["SourcePieces"]![0]!["Points"]![0]!.AsObject().Remove("X")),layout));
        var text=Encoding.UTF8.GetString(bytes);var duplicate=text.Insert(1,"\"Schema\":\"duplicate\",");
        Assert.Throws<InvalidDataException>(()=>RotatedNestingTraceCodec.DeserializeAndVerify(Encoding.UTF8.GetBytes(duplicate),layout));
    }

    [Fact]
    public void ForgedMarkerWithRecomputedSourceHashStillFailsGeometricProof()
    {
        var (record,layout)=Fixture();var trace=record.ToTrace();
        trace.SourcePieces[0].Notches![0].VertexIndex=2;trace.SourcePieces[0].Notches![0].Position=trace.SourcePieces[0].Points[2];
        trace.SourceFingerprint=NestingSnapshot.ComputeLayoutFingerprint(new ProjectDocument { Pieces=trace.SourcePieces });
        var bytes=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(trace,new System.Text.Json.JsonSerializerOptions { IgnoreReadOnlyProperties=true });
        Assert.Throws<InvalidOperationException>(()=>RotatedNestingTraceCodec.DeserializeAndVerify(bytes,layout));
    }
}
