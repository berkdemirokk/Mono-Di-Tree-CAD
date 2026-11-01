using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetLocalSampleDiagnosticTests
{
    [Fact]
    public void ComparisonReportsTheFirstSourceVertexOrEdgeMidpoint()
    {
        var method=typeof(PatternTools).GetMethod("LocallyMatchesWithDiagnostic",BindingFlags.NonPublic|BindingFlags.Static)!;
        var source=new Path64 { new(0,0),new(10,0) };
        var vertexTarget=new Path64 { new(0,2),new(10,2),new(10,3),new(0,3) };
        object?[] vertexArgs=[source,vertexTarget,0.1,CancellationToken.None,null,null];
        Assert.False((bool)method.Invoke(null,vertexArgs)!);
        Assert.Equal(0,(int)vertexArgs[4]!);Assert.Equal(OffsetLocalSample.Vertex,(OffsetLocalSample)vertexArgs[5]!);
        var midpointTarget=new Path64 { new(0,0),new(0,1),new(10,1),new(10,0),new(10,-1),new(0,-1) };
        object?[] midpointArgs=[source,midpointTarget,0.1,CancellationToken.None,null,null];
        Assert.False((bool)method.Invoke(null,midpointArgs)!);
        Assert.Equal(0,(int)midpointArgs[4]!);Assert.Equal(OffsetLocalSample.EdgeMidpoint,(OffsetLocalSample)midpointArgs[5]!);
    }
}
