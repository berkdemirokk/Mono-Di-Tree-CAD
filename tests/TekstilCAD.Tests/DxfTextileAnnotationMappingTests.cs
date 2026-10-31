using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfTextileAnnotationMappingTests
{
    private static DxfImportResult Read(string text="M",string type="TEXT",string fields="",string? labelState="70\n0\n62\n7\n")
    {
        var table=labelState is null ? "" : "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\nLABEL\n"+labelState+"0\nLAYER\n2\nCUT\n70\n0\n62\n7\n0\nENDTAB\n0\nENDSEC\n";
        var source=table+"0\nSECTION\n2\nENTITIES\n"+
            $"0\n{type}\n8\nLABEL\n1\n{text}\n{fields}"+
            "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n"+
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"textile");
    }

    [Fact]
    public void ExplicitTextSelectionCopiesValueWithoutChangingImport()
    {
        var import=Read();
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Size)],fingerprint);
        Assert.Equal("M",Assert.Single(result.Project.Pieces).Metadata!.Size);
        Assert.Null(piece.Metadata);
        Assert.Equal("M",Assert.Single(result.Assignments).Value);
        var targetSource=Assert.Single(result.Assignments).TargetSource;
        Assert.NotNull(targetSource);
        Assert.Equal(piece.Id,targetSource.PieceId);
        Assert.Contains(result.SourceGeometry!.Records,r=>targetSource.SourceOrdinals.Contains(r.SourceOrdinal));
        Assert.Same(import.SkippedSourceRecords,result.SkippedSourceRecords);
        Assert.Same(import.SkippedSourceDetails,result.SkippedSourceDetails);
        Assert.Equal(import.SourceSha256,result.SourceSha256);
        Assert.Equal(annotation.SourceOrdinal,Assert.Single(result.SourceAnnotations.Records).SourceOrdinal);
    }

    [Fact]
    public void ExplicitPieceNameRetainsPreviousLayerNameInAudit()
    {
        var import=Read("Front");
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.PieceName)],fingerprint);
        var mapped=Assert.Single(result.Project.Pieces);
        Assert.Equal("Front",mapped.Name);Assert.Null(mapped.Metadata);
        var audit=Assert.Single(result.Assignments);
        Assert.Equal("CUT",audit.PreviousValue);Assert.Equal("Front",audit.RawValue);
        Assert.Equal("CUT",piece.Name);
    }

    [Theory]
    [InlineData(" M ","TEXT")]
    [InlineData("\\U+00GG","TEXT")]
    [InlineData("\\P","TEXT")]
    [InlineData("\\U+D800","TEXT")]
    [InlineData("\\U+DC00","TEXT")]
    [InlineData("\\U+D83D\\U+0041","TEXT")]
    [InlineData("\\U+0020M","TEXT")]
    [InlineData("\\U+0025%","TEXT")]
    [InlineData("%\\U+0025","TEXT")]
    [InlineData("\\U+005C","TEXT")]
    [InlineData("A\tB","TEXT")]
    [InlineData("%%d","TEXT")]
    [InlineData("M","MTEXT")]
    public void UnsupportedOrLossyTextIsRejected(string text,string type)
    {
        var import=Read(text,type);
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Material)],fingerprint));
        Assert.Null(piece.Metadata);
    }

    [Fact]
    public void ChangedSourceIdentityOrProjectInvalidatesSelection()
    {
        var import=Read();
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var selection=new DxfTextileTextSelection(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Size);
        var altered=import with { SourceSha256=new string('0',64) };
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(altered,[selection],fingerprint));
        piece.Name="changed";
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(import,[selection],fingerprint));
    }

    [Fact]
    public void HexadecimalHashCaseDoesNotChangeSourceIdentity()
    {
        var original=Read();
        var import=original with { SourceSha256=original.SourceSha256!.ToLowerInvariant() };
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var token=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Size)],token);
        Assert.Equal("M",Assert.Single(result.Project.Pieces).Metadata!.Size);
        Assert.Null(piece.Metadata);
    }

    [Fact]
    public void DuplicateTargetFieldIsRejectedTransactionally()
    {
        var import=Read();
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var selection=new DxfTextileTextSelection(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Size);
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(import,[selection,selection],fingerprint));
        Assert.Null(piece.Metadata);
    }

    [Theory]
    [InlineData("67\n1\n")]
    [InlineData("410\nLayout1\n")]
    [InlineData("60\n1\n")]
    [InlineData("62\n-7\n")]
    [InlineData("62\n0\n")]
    [InlineData("440\n33554432\n")]
    public void HiddenOrNonModelTextCannotBeMapped(string fields)
    {
        var import=Read(fields:fields);
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Size)],fingerprint));
        Assert.Null(piece.Metadata);
    }

    [Theory]
    [InlineData("70\n1\n62\n7\n")]
    [InlineData("70\n0\n62\n-7\n")]
    [InlineData("70\n0\n")]
    [InlineData("70\n0\n62\n7\n440\n33554432\n")]
    [InlineData(null)]
    public void HiddenOrUnknownLayerCannotSupplyText(string? labelState)
    {
        var import=Read(labelState:labelState);
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Size)],fingerprint));
        Assert.Null(piece.Metadata);
    }

    [Theory]
    [InlineData("62\n257\n")]
    [InlineData("62\nnot-a-color\n")]
    [InlineData("62\n7\n62\n8\n")]
    public void InvalidTextColorIsRejectedAtMappingWithoutChangingImport(string fields)
    {
        // Generic import retains TEXT for review without interpreting its color.
        // The mapping boundary validates it before assigning a textile field.
        var import=Read(fields:fields);
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Size)],fingerprint));
        Assert.Null(piece.Metadata);
    }
    [Theory]
    [InlineData("1",1)]
    [InlineData("0002",2)]
    [InlineData("100000",100000)]
    public void ExplicitQuantityMappingPreservesSourceTextInAudit(string text,int expected)
    {
        var import=Read(text);
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Quantity)],fingerprint);
        Assert.Equal(expected,Assert.Single(result.Project.Pieces).Metadata!.Quantity);
        Assert.Equal(text,Assert.Single(result.Assignments).Value);
        Assert.Null(piece.Metadata);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100001")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("2.5")]
    [InlineData("2 adet")]
    public void InvalidQuantityDoesNotMutateSource(string text)
    {
        var import=Read(text);
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Quantity)],fingerprint));
        Assert.Null(piece.Metadata);
    }

    [Theory]
    [InlineData("\\U+004D","M")]
    [InlineData("\\U+D83D\\U+DE00","😀")]
    [InlineData("A\\U+00D6","AÖ")]
    public void UnicodeEscapesDecodeAndRawValueRemainsAuditable(string raw,string expected)
    {
        var import=Read(raw);
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Size)],fingerprint);
        Assert.Equal(expected,Assert.Single(result.Project.Pieces).Metadata!.Size);
        var audit=Assert.Single(result.Assignments);
        Assert.Equal(expected,audit.Value);Assert.Equal(raw,audit.RawValue);
        Assert.Null(piece.Metadata);
    }

    [Theory]
    [InlineData("100% Cotton","100% Cotton")]
    [InlineData("100\\U+0025 Cotton","100% Cotton")]
    public void LiteralPercentInMaterialIsPreserved(string raw,string expected)
    {
        var import=Read(raw);
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Material)],fingerprint);
        Assert.Equal(expected,Assert.Single(result.Project.Pieces).Metadata!.Material);
        Assert.Equal(raw,Assert.Single(result.Assignments).RawValue);
        Assert.Null(piece.Metadata);
    }

    [Fact]
    public void EscapedDigitCanBeExplicitlyMappedToQuantity()
    {
        var import=Read("\\U+0032");
        var piece=Assert.Single(import.Project.Pieces);
        var annotation=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileAnnotationMapping.Apply(import,[new(piece.Id,annotation.SourceOrdinal,DxfTextileTextField.Quantity)],fingerprint);
        Assert.Equal(2,Assert.Single(result.Project.Pieces).Metadata!.Quantity);
        Assert.Equal("\\U+0032",Assert.Single(result.Assignments).RawValue);
    }

    [Fact]
    public void DecodedTextLengthBoundaryIsEnforcedWithoutChangingSource()
    {
        var allowed=Read(string.Concat(Enumerable.Repeat("\\U+0041",128)));
        var piece=Assert.Single(allowed.Project.Pieces);
        var ordinal=Assert.Single(allowed.Annotations!.Records).SourceOrdinal;
        var token=DxfTextileGrainlineMapping.CaptureFingerprint(allowed);
        var mapped=DxfTextileAnnotationMapping.Apply(allowed,[new(piece.Id,ordinal,DxfTextileTextField.Size)],token);
        Assert.Equal(new string('A',128),Assert.Single(mapped.Project.Pieces).Metadata!.Size);
        Assert.Null(piece.Metadata);

        var tooLong=Read(new string('A',129));
        var other=Assert.Single(tooLong.Project.Pieces);
        var otherOrdinal=Assert.Single(tooLong.Annotations!.Records).SourceOrdinal;
        var otherToken=DxfTextileGrainlineMapping.CaptureFingerprint(tooLong);
        Assert.Throws<InvalidDataException>(()=>DxfTextileAnnotationMapping.Apply(tooLong,[new(other.Id,otherOrdinal,DxfTextileTextField.Size)],otherToken));
        Assert.Null(other.Metadata);
    }
}
