using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Cross-platform smoke contract for the small unversioned contour
/// document emitted by the iPhone sketch studio. This is not an industrial
/// DXF/AAMA/ASTM certificate or a test of the iOS share sheet.</summary>
public sealed class IPhoneContourExportCompatibilityTests
{
    private static string MobileFixture => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "mobile-simple.tcad");

    [Fact]
    public void MobileContourEnvelopeLoadsAndRoundTripsWithoutChangingGeometry()
    {
        var folder=Path.Combine(Path.GetTempPath(), "tcad-mobile-contract-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var input = MobileFixture;
            var output=Path.Combine(folder,"desktop.tcad");
            var imported=ProjectStorage.Load(input);
            Assert.Equal("Mobil ön çizim", imported.Name);
            Assert.Equal(2,imported.Pieces.Count);
            Assert.Empty(imported.GradingRules);
            Assert.Null(imported.Layers);
            Assert.Null(imported.NestingSnapshot);
            Assert.Equal(new Point2(100,150), imported.Pieces[0].Points[2]);
            Assert.Equal(new Point2(250,40), imported.Pieces[1].Points[1]);
            Assert.True(imported.Pieces[0].Closed);
            Assert.False(imported.Pieces[1].Closed);
            Assert.All(imported.Pieces,p=>Assert.NotEqual(Guid.Empty,p.Id));

            ProjectStorage.Save(imported, output);
            var reopened=ProjectStorage.Load(output);
            Assert.Equal(imported.Name,reopened.Name);
            Assert.Equal(imported.Pieces.Count,reopened.Pieces.Count);
            for(var i=0;i<imported.Pieces.Count;i++)
            {
                Assert.Equal(imported.Pieces[i].Id,reopened.Pieces[i].Id);
                Assert.Equal(imported.Pieces[i].Name,reopened.Pieces[i].Name);
                Assert.Equal(imported.Pieces[i].Closed,reopened.Pieces[i].Closed);
                Assert.Equal(imported.Pieces[i].Points,reopened.Pieces[i].Points);
            }
        }
        finally { Directory.Delete(folder,recursive:true); }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("bad-guid")]
    public void InvalidMobilePieceIdentityIsRejectedInsteadOfRepairedSilently(string invalidId)
    {
        var json = File.ReadAllText(MobileFixture).Replace("11111111-1111-4111-8111-111111111111",invalidId,StringComparison.Ordinal);
        var folder=Path.Combine(Path.GetTempPath(), "tcad-mobile-reject-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"invalid-mobile.tcad");
            File.WriteAllText(file,json,new UTF8Encoding(false,true));
            Assert.ThrowsAny<Exception>(()=>ProjectStorage.Load(file));
        }
        finally { Directory.Delete(folder,recursive:true); }
    }
}
