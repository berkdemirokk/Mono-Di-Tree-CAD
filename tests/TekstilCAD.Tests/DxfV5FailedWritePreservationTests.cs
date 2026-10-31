using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfV5FailedWritePreservationTests
{
    [Fact]
    public void InvalidHistoryDoesNotReplaceProjectOrRecoverySnapshot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName,"work","v5-failed-write",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,"project.tcad");
        var piece = new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(0,10)] };
        var document = new ProjectDocument { Pieces=[piece],ImportProvenance=new DxfImportProvenance {
            SourceFileName="kaynak.dxf",SourceSha256=new string('A',64),SourceByteLength=53,
            MillimetersPerUnit=1,Warnings=["Önceki uyarı"],PieceIds=[piece.Id] } };

        ProjectStorage.Save(document,path);
        var original = File.ReadAllBytes(path);
        var recoveryDirectory = Path.Combine(directory,"recovery");
        string recoveryPath;
        using (var recovery = new RecoveryStore(recoveryDirectory))
        {
            recovery.Save(document);
            recoveryPath = recovery.RecoveryPath;
            var recoveryBytes = File.ReadAllBytes(recoveryPath);
            document.ImportProvenance!.Warnings.Add(new string('x',4097));

            Assert.Throws<InvalidDataException>(() => ProjectStorage.Save(document,path));
            Assert.Throws<InvalidDataException>(() => recovery.Save(document));
            Assert.Equal(original,File.ReadAllBytes(path));
            Assert.Equal(recoveryBytes,File.ReadAllBytes(recoveryPath));
        }
        Assert.Equal("Önceki uyarı",Assert.Single(ProjectStorage.Load(path).ImportProvenance!.Warnings));
        using var reader = new RecoveryStore(recoveryDirectory);
        var restored = reader.LoadCandidate(recoveryPath).Document;
        Assert.Equal("Önceki uyarı",Assert.Single(restored.ImportProvenance!.Warnings));
        Assert.Equal(piece.Id,Assert.Single(restored.ImportProvenance.PieceIds));
    }
}
