using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class LayerModelTests : IDisposable
{
    private readonly string directory;
    public LayerModelTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        directory = Path.Combine(root.FullName, "work", "layer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }
    public void Dispose()
    {
        try { Directory.Delete(directory, true); } catch (IOException) { }
    }
    private string FilePath(string name) => Path.Combine(directory, name);

    private static ProjectDocument Sample() => new()
    {
        Name = "Katmanlı proje",
        Pieces =
        [
            new PatternPiece { Name = "Ön", Closed = true, Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] },
            new PatternPiece { Name = "Arka", Closed = true, Points = [new(200, 0), new(300, 0), new(300, 100), new(200, 100)] }
        ]
    };

    [Fact]
    public void DocumentWithoutExplicitLayersKeepsOldBytesAndOneDefaultLayer()
    {
        var document = Sample();
        var text = ProjectStorage.SerializeProject(document);
        Assert.DoesNotContain("\"Layers\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Pieces\"", text, StringComparison.Ordinal);
        var layers = LayerTools.Layers(document);
        Assert.Single(layers);
        Assert.Equal(Layer.DefaultId, layers[0].Id);
        Assert.True(layers[0].Visible);
        Assert.False(layers[0].Locked);
        Assert.All(document.Pieces, piece => Assert.True(LayerTools.IsPieceVisible(document, piece)));
        Assert.All(document.Pieces, piece => Assert.False(LayerTools.IsPieceLocked(document, piece)));
    }

    [Fact]
    public void AddedLayerRoundTripsThroughVersionEightFile()
    {
        var document = Sample();
        var layer = LayerTools.Add(document, "Astar");
        LayerTools.Assign(document, document.Pieces[1], layer.Id);
        LayerTools.SetVisible(document, layer.Id, false);
        LayerTools.SetLocked(document, layer.Id, true);

        var path = FilePath("layers.tcad");
        ProjectStorage.Save(document, path);
        var text = File.ReadAllText(path);
        Assert.Matches(@"^\s*\[\s*8\s*,", text);
        Assert.Contains("\"Layers\"", text, StringComparison.Ordinal);

        var loaded = ProjectStorage.Load(path);
        var layers = LayerTools.Layers(loaded);
        Assert.Equal(2, layers.Count);
        Assert.Equal(Layer.DefaultId, layers[0].Id);
        Assert.Equal("Astar", layers[1].Name);
        Assert.False(layers[1].Visible);
        Assert.True(layers[1].Locked);
        Assert.Equal(layer.Id, loaded.Pieces[1].LayerId);
        Assert.Equal(Layer.DefaultId, loaded.Pieces[0].LayerId);
        Assert.True(DocumentSession.ContentEquals(document, loaded));
    }

    [Fact]
    public void DeletingALayerMovesItsPiecesToDefaultAndDropsTheExplicitList()
    {
        var document = Sample();
        var layer = LayerTools.Add(document, "Astar");
        LayerTools.Assign(document, document.Pieces[1], layer.Id);
        Assert.True(LayerTools.Delete(document, layer.Id));
        Assert.Null(document.Layers);
        Assert.All(document.Pieces, piece => Assert.Equal(Layer.DefaultId, piece.LayerId));
        Assert.Single(LayerTools.Layers(document));
        Assert.DoesNotContain("\"Layers\"", ProjectStorage.SerializeProject(document), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultLayerCannotBeDeleted()
    {
        var document = Sample();
        Assert.Throws<InvalidOperationException>(() => LayerTools.Delete(document, Layer.DefaultId));
        LayerTools.Add(document, "Astar");
        Assert.Throws<InvalidOperationException>(() => LayerTools.Delete(document, Layer.DefaultId));
        Assert.Equal(2, LayerTools.Layers(document).Count);
    }

    [Fact]
    public void ValidationRejectsMissingDefaultLayerDanglingPieceAndDuplicateIds()
    {
        var document = Sample();
        var layer = LayerTools.Add(document, "Astar");
        LayerTools.Assign(document, document.Pieces[0], layer.Id);

        var withoutDefault = document.Clone();
        withoutDefault.Layers!.RemoveAll(candidate => candidate.Id == Layer.DefaultId);
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(withoutDefault));

        var dangling = document.Clone();
        dangling.Pieces[0].LayerId = Guid.NewGuid();
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(dangling));

        var duplicate = document.Clone();
        duplicate.Layers!.Add(new Layer { Id = layer.Id, Name = "Kopya" });
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(duplicate));

        var emptyName = document.Clone();
        emptyName.Layers![1].Name = " ";
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(emptyName));
    }

    [Fact]
    public void LayerNamesAreTrimmedUniqueAndFreeOfControlCharacters()
    {
        var document = Sample();
        Assert.Throws<ArgumentException>(() => LayerTools.Add(document, "  Astar"));
        Assert.Throws<ArgumentException>(() => LayerTools.Add(document, ""));
        Assert.Throws<ArgumentException>(() => LayerTools.Add(document, "As\u0001tar"));
        Assert.Throws<ArgumentException>(() => LayerTools.Add(document, new string('a', LayerTools.MaximumNameLength + 1)));
        Assert.Throws<ArgumentException>(() => LayerTools.Add(document, "Katman 1"));
        var layer = LayerTools.Add(document, "Astar");
        Assert.Throws<ArgumentException>(() => LayerTools.Add(document, "Astar"));
        Assert.Throws<ArgumentException>(() => LayerTools.Rename(document, layer.Id, "Katman 1"));
        LayerTools.Rename(document, layer.Id, "Astarlık");
        Assert.Equal("Astarlık", LayerTools.Find(document, layer.Id)!.Name);
        Assert.Throws<ArgumentException>(() => LayerTools.Rename(document, Guid.NewGuid(), "Yeni"));
    }

    [Fact]
    public void SuggestedNameSkipsNamesInUse()
    {
        var document = Sample();
        Assert.Equal("Katman 2", LayerTools.SuggestName(document));
        LayerTools.Add(document, "Katman 2");
        Assert.Equal("Katman 3", LayerTools.SuggestName(document));
        LayerTools.Rename(document, LayerTools.Layers(document)[0].Id, "Gövde");
        Assert.Equal("Katman 1", LayerTools.SuggestName(document));
    }

    [Fact]
    public void ChangingTheImplicitDefaultLayerStoresItAndDeletingKeepsItsSettings()
    {
        var document = Sample();
        Assert.Null(document.Layers);
        LayerTools.SetVisible(document, Layer.DefaultId, false);
        Assert.NotNull(document.Layers);
        Assert.False(LayerTools.DefaultLayer(document).Visible);

        var path = FilePath("hidden-default.tcad");
        ProjectStorage.Save(document, path);
        var loaded = ProjectStorage.Load(path);
        Assert.False(LayerTools.DefaultLayer(loaded).Visible);
        Assert.All(loaded.Pieces, piece => Assert.False(LayerTools.IsPieceVisible(loaded, piece)));

        // Removing the only extra layer must not reset a customised default layer.
        var layer = LayerTools.Add(loaded, "Astar");
        Assert.True(LayerTools.Delete(loaded, layer.Id));
        Assert.NotNull(loaded.Layers);
        Assert.Single(loaded.Layers!);
        Assert.False(loaded.Layers![0].Visible);
    }

    [Fact]
    public void LayerOrderAndFlagsTakePartInDocumentEquality()
    {
        var left = Sample();
        var right = left.Clone();
        Assert.True(DocumentSession.ContentEquals(left, right));
        var layer = LayerTools.Add(left, "Astar");
        Assert.False(DocumentSession.ContentEquals(left, right));
        var mirror = LayerTools.Add(right, "Astar");
        mirror.Id = layer.Id;
        Assert.True(DocumentSession.ContentEquals(left, right));
        LayerTools.SetVisible(right, mirror.Id, false);
        Assert.False(DocumentSession.ContentEquals(left, right));
        LayerTools.SetVisible(right, mirror.Id, true);
        right.Pieces[0].LayerId = mirror.Id;
        Assert.False(DocumentSession.ContentEquals(left, right));
        right.Pieces[0].LayerId = left.Pieces[0].LayerId;
        Assert.True(DocumentSession.ContentEquals(left, right));
    }
}
