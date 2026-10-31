using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfGeneralImportLimitTests
{
    [Fact]
    public void GeneralImportStopsWhenAcceptedContoursExceedTotalPointLimit()
    {
        var dxf = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
        var fullContours = ProjectStorage.MaximumTotalPoints / ProjectStorage.MaximumPointsPerPiece;
        for (var contour = 0; contour < fullContours; contour++)
        {
            dxf.Append("0\nLWPOLYLINE\n6\nCONTINUOUS\n90\n")
                .Append(ProjectStorage.MaximumPointsPerPiece).Append('\n');
            for (var point = 0; point < ProjectStorage.MaximumPointsPerPiece; point++)
                dxf.Append("10\n").Append(point).Append("\n20\n0\n");
        }
        dxf.Append("0\nLWPOLYLINE\n6\nCONTINUOUS\n90\n2\n10\n0\n20\n0\n10\n1\n20\n0\n")
            // An invalid later contour proves rejection occurs at the limit,
            // before the reader tries to construct subsequent pieces.
            .Append("0\nLWPOLYLINE\n90\n2\n10\n0\n0\nENDSEC\n0\nEOF\n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dxf.ToString()));

        var error = Assert.Throws<InvalidDataException>(() => DxfFormat.Import(stream, "large"));
        Assert.Contains("ortak sınırını aşıyor", error.Message);
    }
}
