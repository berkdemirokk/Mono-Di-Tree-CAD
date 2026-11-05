using TekstilCAD.Core;
using Xunit;

namespace TekstilCAD.Tests;

public sealed class PhotoRasterConversionTests
{
    [Fact]
    public void BgraIsCompositedOnWhiteInBgrOrderWithoutMutatingSource()
    {
        byte[] input=[10,20,30,255, 0,0,0,0, 20,100,200,128];
        var before=(byte[])input.Clone();
        var result=PhotoRasterConversion.BgraToBgrOnWhite(input,3,1);
        Assert.Equal(new byte[] { 10,20,30, 255,255,255, 137,177,227 },result);
        Assert.Equal(before,input);
        result[0]=0;
        Assert.Equal(10,input[0]);
    }

    [Fact]
    public void InvalidBuffersAndCancellationAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>PhotoRasterConversion.BgraToBgrOnWhite(new byte[3],1,1));
        Assert.Throws<ArgumentException>(()=>PhotoRasterConversion.BgraToBgrOnWhite(Array.Empty<byte>(),0,1));
        Assert.Throws<ArgumentException>(()=>PhotoRasterConversion.BgraToBgrOnWhite(Array.Empty<byte>(),12001,1));
        Assert.Throws<OperationCanceledException>(()=>PhotoRasterConversion.BgraToBgrOnWhite(new byte[4],1,1,new CancellationToken(true)));
    }
}
