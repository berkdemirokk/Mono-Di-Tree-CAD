using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DesignRequestStorageTests
{
    [Fact]
    public void RequestRoundTripsUnicodeMeasurementsAndExactReferenceBytes()
    {
        byte[] image=[137,80,78,71,13,10,26,10,1,2,3];
        var original=new DesignRequest("Bol kollu gömlek 👗", "Göğüs 96 cm\nKol 62,5 cm",new("örnek.png","image/png",image));
        var restored=DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(original));
        Assert.Equal(original.Prompt,restored.Prompt);Assert.Equal(original.Measurements,restored.Measurements);
        Assert.Equal(original.Reference!.Name,restored.Reference!.Name);Assert.Equal(image,restored.Reference.Data);
    }
    [Fact]
    public void MissingDuplicateUnknownAndContradictoryFieldsAreRejected()
    {
        var valid=DesignRequestStorage.Serialize(new("etek", "", null));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(valid.Replace("\"version\":1", "\"version\":1,\"version\":1")));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(valid.Replace("\"version\":1", "\"extra\":1,\"version\":1")));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(valid.Replace("\"version\":1,", "")));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Serialize(new("", "göğüs 80 cm",null)));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Serialize(new("", "",new("a.png","image/jpeg",[137,80,78,71,13,10,26,10]))));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Serialize(new(new string('a',DesignRequestStorage.MaximumTextLength+1),"",null)));
    }
    [Fact]
    public void NewFileRoundTripsAndNeverOverwritesExistingTarget()
    {
        var directory=Path.Combine(Path.GetTempPath(),"tcad-request-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"model.tcad-request.json");var first=new DesignRequest("elbise", "boy: 90 cm",null);
            DesignRequestStorage.SaveNew(path,first);var bytes=File.ReadAllBytes(path);
            Assert.Equal(first,DesignRequestStorage.Load(path));
            Assert.ThrowsAny<Exception>(()=>DesignRequestStorage.SaveNew(path,new("farklı", "",null)));
            Assert.Equal(bytes,File.ReadAllBytes(path));
            Assert.Throws<InvalidOperationException>(()=>DesignRequestStorage.SaveNew(Path.Combine(directory,"model.tcad"),first));
        }
        finally { Directory.Delete(directory,true); }
    }
}
