using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class PlotterCalibrationRecordTests
{
    [Fact]
    public void RecordRoundTripsAndDetectsChangedOrMissingReferenceWithoutOverwriting()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"tcad-calibration-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var referencePath=Path.Combine(directory,"reference.plt");
            var notePath=Path.Combine(directory,"measurements"+PlotterCalibrationRecordStorage.Extension);
            var reference=PlotterCalibrationSquare.PrepareWithOrientationMarker(40,1);
            PlotterCalibrationSquare.WriteNew(reference,referencePath);
            var note=PlotterCalibrationRecordStorage.Create(referencePath,reference.ContentSha256,40,true,101,99,142,141);
            Assert.Equal(1,note.XDeviation);Assert.Equal(-1,note.YDeviation);Assert.Equal(1,note.DiagonalDifference);
            PlotterCalibrationRecordStorage.SaveNew(notePath,note);
            Assert.Equal(note,PlotterCalibrationRecordStorage.Load(notePath));
            Assert.True(PlotterCalibrationRecordStorage.ReferenceMatches(note));
            var bytes=File.ReadAllBytes(notePath);
            Assert.ThrowsAny<IOException>(()=>PlotterCalibrationRecordStorage.SaveNew(notePath,note));
            Assert.Equal(bytes,File.ReadAllBytes(notePath));
            File.AppendAllText(referencePath,"changed");
            Assert.False(PlotterCalibrationRecordStorage.ReferenceMatches(PlotterCalibrationRecordStorage.Load(notePath)));
            File.Delete(referencePath);
            Assert.False(PlotterCalibrationRecordStorage.ReferenceMatches(note));
            Assert.Equal(bytes,File.ReadAllBytes(notePath));
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public void RejectsInconsistentReferenceAndMeasurements()
    {
        var reference=PlotterCalibrationSquare.Prepare(40,1);
        var path=Path.GetFullPath("reference.plt");
        Assert.Throws<InvalidDataException>(()=>PlotterCalibrationRecordStorage.Create(path,reference.ContentSha256,40,true,100,100,null,null));
        Assert.Throws<InvalidDataException>(()=>PlotterCalibrationRecordStorage.Create(path,reference.ContentSha256,40,false,100,100,140,null));
        Assert.Throws<InvalidDataException>(()=>PlotterCalibrationRecordStorage.Create(path,reference.ContentSha256,40,false,double.NaN,100,null,null));
    }
}
