using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class LegacyFormatGateTests
{
    [Fact] public void UnknownAllocationFailsClosedAndVersionComesFromLoadedBytes()
    {
        var source=Path.GetTempFileName();var destination=Path.GetTempFileName();
        try
        {
            File.WriteAllText(source,PhysicalPhotoCompatibilityTests.Photo8().ToJsonString());
            File.WriteAllText(destination,"preserve");var original=File.ReadAllBytes(source);
            var loaded=ProjectStorage.LoadWithVersion(source);Assert.Equal(8,loaded.FormatVersion);
            Assert.False(ProjectStorage.Version10WriteAvailable);
            Assert.Throws<InvalidOperationException>(()=>ProjectStorage.Save(loaded.Project,destination));
            Assert.Throws<InvalidOperationException>(()=>RecoveryEnvelope.Serialize(loaded.Project,[]));
            Assert.Equal("preserve",File.ReadAllText(destination));Assert.Equal(original,File.ReadAllBytes(source));
            using(ProjectStorage.EnableVersion10ForVerification())
            {
                Assert.True(ProjectStorage.Version10WriteAvailable);
                ProjectStorage.Save(loaded.Project,destination);
                Assert.Equal(10,ProjectStorage.LoadWithVersion(destination).FormatVersion);
            }
            Assert.False(ProjectStorage.Version10WriteAvailable);Assert.Equal(original,File.ReadAllBytes(source));
            File.WriteAllText(source,PhysicalPhotoCompatibilityTests.Plane9().ToJsonString());var planeBytes=File.ReadAllBytes(source);
            Assert.Equal(9,ProjectStorage.LoadWithVersion(source).FormatVersion);Assert.Equal(planeBytes,File.ReadAllBytes(source));
        }
        finally { File.Delete(source);File.Delete(destination); }
    }
}
