using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DesignRequestSavePathTests
{
    [Theory]
    [InlineData("Ayşe A kesim","Ayşe A kesim.tcad-request.json")]
    [InlineData("Ayşe.tcad-request.json","Ayşe.tcad-request.json")]
    [InlineData("Ayşe.TCAD-REQUEST.JSON","Ayşe.TCAD-REQUEST.JSON")]
    public void CompletesOnlyMissingSuffix(string input,string expected)=>Assert.Equal(expected,DesignRequestSavePath.Complete(input));

    [Theory]
    [InlineData("Ayşe.json")]
    [InlineData("Ayşe.tcad")]
    [InlineData("Ayşe.txt")]
    [InlineData("Ayşe.")]
    [InlineData("Ayşe ")]
    public void DoesNotSilentlyRewriteDifferentOrAmbiguousNames(string input)=>Assert.Throws<InvalidDataException>(()=>DesignRequestSavePath.Complete(input));

    [Fact]
    public void DirectoryDotsAreNotFileExtensionsAndDestinationIsNotRedirected()
    {
        var name=Path.Combine("folder.with.dots","İstek");
        Assert.Equal(name+DesignRequestSavePath.Extension,DesignRequestSavePath.Complete(name));
    }

    [Fact]
    public void CompletedNameStillUsesAtomicNewFileGuard()
    {
        var input=Path.Combine(Path.GetTempPath(),"request-name-"+Guid.NewGuid().ToString("N"));
        var target=DesignRequestSavePath.Complete(input);
        try
        {
            File.WriteAllText(target,"existing sentinel");
            Assert.Throws<ProjectConflictException>(()=>DesignRequestStorage.SaveNewV2(target,new("","",null,"aline",2)));
            Assert.Equal("existing sentinel",File.ReadAllText(target));Assert.False(File.Exists(input));
        }
        finally { if(File.Exists(target))File.Delete(target); }
    }
}
