using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class FactoryAcceptanceDxfWorkflowTests
{
    [Fact]
    public void SyntheticInchPatternIsReimportedAfterSourceChangeThenSavedAndExportedInMillimeters()
    {
        // Synthetic fixture: this checks a workflow, not a real factory drawing or cutter.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var folder = Path.Combine(root!.FullName, "work", "factory-acceptance-dxf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source.dxf");
        var exported = Path.Combine(folder, "export.dxf");
        var first = Encoding.UTF8.GetBytes(Drawing("2"));
        var changed = Encoding.UTF8.GetBytes(Drawing("3"));
        Assert.Equal(first.Length, changed.Length);
        try
        {
            File.WriteAllBytes(source, first);
            var reviewed = DxfFormat.Import(source, 25.4);
            Assert.True(reviewed.Units.IsReliable);
            Assert.Equal(1, reviewed.Units.Code);
            Assert.Equal(DxfSourceSnapshotCheck.Status.MatchesReadBytes, DxfSourceSnapshotCheck.Check(source, reviewed).Status);

            // Same-length replacement must invalidate the reviewed snapshot as well.
            File.WriteAllBytes(source, changed);
            Assert.Equal(DxfSourceSnapshotCheck.Status.Changed, DxfSourceSnapshotCheck.Check(source, reviewed).Status);
            var current = DxfFormat.Import(source, 25.4);
            Assert.Equal(DxfSourceSnapshotCheck.Status.MatchesReadBytes, DxfSourceSnapshotCheck.Check(source, current).Status);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(changed)), current.SourceSha256);
            var piece = Assert.Single(current.Project.Pieces);
            Assert.True(piece.Closed);
            Assert.Equal(76.2, piece.Points.Max(p => p.X), 8);
            Assert.Equal(25.4, piece.Points.Max(p => p.Y), 8);

            // Model the accepted import history, then verify project serialization and DXF output.
            current.Project.ImportProvenance = new DxfImportProvenance {
                SourceFileName = Path.GetFileName(source), SourceSha256 = current.SourceSha256!,
                SourceByteLength = current.SourceByteLength!.Value, MillimetersPerUnit = 25.4,
                Warnings = current.Warnings.ToList(), PieceIds = [piece.Id]
            };
            ProjectStorage.Validate(current.Project);
            var loaded = ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(current.Project)));
            Assert.True(DocumentSession.ContentEquals(current.Project, loaded));
            Assert.Equal(current.SourceSha256, loaded.ImportProvenance!.SourceSha256);
            Assert.Equal(piece.Id, Assert.Single(loaded.ImportProvenance.PieceIds));
            DxfFormat.Export(loaded, exported);
            var reimported = DxfFormat.Import(exported, 1);
            Assert.True(reimported.Units.IsReliable);
            Assert.Equal(4, reimported.Units.Code);
            var output = Assert.Single(reimported.Project.Pieces);
            Assert.True(output.Closed);
            Assert.Equal(piece.Points, output.Points);
            Assert.Equal(changed, File.ReadAllBytes(source));
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(exported)) File.Delete(exported);
            Directory.Delete(folder);
        }
    }

    private static string Drawing(string width) =>
        "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n" +
        "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\nCUT\n90\n4\n70\n1\n" +
        $"10\n0\n20\n0\n10\n{width}\n20\n0\n10\n{width}\n20\n1\n10\n0\n20\n1\n" +
        "0\nENDSEC\n0\nEOF\n";
}