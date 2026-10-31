using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public class GrainlineExportGuardTests
{
    private static string DirectoryForTest()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root not found.");
        var directory=Path.Combine(root.FullName,"work","grainline-export-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);return directory;
    }
    private static ProjectDocument Document(bool withGrainline)
    {
        var piece=new PatternPiece { Name="Ön", Points=[new(0,0),new(20,0),new(20,30),new(0,30)] };
        if(withGrainline)piece.Grainline=new Grainline { Start=new(5,5),End=new(5,25),NeedsReview=false };
        return new ProjectDocument { Name="Model",Pieces=[piece] };
    }

    [Theory]
    [InlineData("dxf",false)] [InlineData("dxf",true)]
    [InlineData("hpgl",false)] [InlineData("hpgl",true)]
    public void GrainlineExportFailsBeforeReplacingExistingFile(string format,bool conditional)
    {
        var directory=DirectoryForTest();var path=Path.Combine(directory,"existing."+format);
        var original=System.Text.Encoding.UTF8.GetBytes("Önceki kullanıcı dosyası");File.WriteAllBytes(path,original);
        var version=ProjectStorage.CaptureVersion(path);var document=Document(true);var before=document.Clone();
        var error=Assert.Throws<InvalidDataException>(()=>
        {
            switch(format)
            {
                case "dxf": if(conditional)DxfFormat.Export(document,path,version);else DxfFormat.Export(document,path);break;
                default: if(conditional)HpglFormat.Export(document,path,40,version);else HpglFormat.Export(document,path,40);break;
            }
        });
        Assert.Contains("İplik yönü",error.Message,StringComparison.Ordinal);
        Assert.Equal(original,File.ReadAllBytes(path));Assert.True(DocumentSession.ContentEquals(before,document));
        Assert.Empty(Directory.GetFiles(directory,"*.tmp"));
    }

    [Fact]
    public void GrainlineHpglPrepareRejectsWithoutCreatingAnyFile()
    {
        var document=Document(true);var before=document.Clone();
        Assert.Throws<InvalidDataException>(()=>HpglFormat.Prepare(document));
        Assert.True(DocumentSession.ContentEquals(before,document));
    }

    [Theory]
    [InlineData(JobFormat.Dxf)] [InlineData(JobFormat.Hpgl)]
    public void SimulatorCannotCreateAnOutputOrJobFromGrainlineSource(JobFormat format)
    {
        var root=DirectoryForTest();var spool=Path.Combine(root,"spool");var queue=new SimulationQueue(spool);
        var document=Document(true);var before=document.Clone();var profile=new MachineProfile("Yerel",40,2);
        Assert.Throws<InvalidDataException>(()=>queue.Prepare(document,profile,format));
        Assert.Empty(queue.Jobs);Assert.True(DocumentSession.ContentEquals(before,document));
        Assert.Empty(Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories));
    }

    [Fact]
    public void SimulatorHpglReviewRejectsGrainlineWithoutCreatingJob()
    {
        var root=DirectoryForTest();var queue=new SimulationQueue(Path.Combine(root,"spool"));var document=Document(true);
        Assert.Throws<InvalidDataException>(()=>queue.PrepareHpglReview(document,new MachineProfile("Yerel",40,2)));
        Assert.Empty(queue.Jobs);Assert.Empty(Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("dxf")] [InlineData("svg")] [InlineData("hpgl")]
    public void UnmarkedDocumentStillExportsNormally(string format)
    {
        var path=Path.Combine(DirectoryForTest(),"normal."+format);var document=Document(false);
        switch(format)
        {
            case "dxf": DxfFormat.Export(document,path);break;
            case "svg": SvgFormat.Export(document,path);break;
            default: HpglFormat.Export(document,path);break;
        }
        Assert.True(File.Exists(path));Assert.NotEmpty(File.ReadAllBytes(path));
    }
}
