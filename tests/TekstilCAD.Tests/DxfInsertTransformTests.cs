using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfInsertTransformTests
{
    [Fact]
    public void BasePointIsSubtractedBeforeNonUniformScaleRotationAndTranslation()
    {
        // (12,23)-(10,20)=(2,3); scale -> (4,9); rotate -> (-9,4).
        var transform = DxfInsertTransform.Create(new(10, 20), new(100, 200), 2, 3, 90);
        AssertPoint(new(91, 204), transform.Apply(new(12, 23)));
        AssertPoint(new(100, 200), transform.Apply(new(10, 20)));
        Assert.False(transform.IsReflection);
    }

    [Fact]
    public void NegativeScaleReflectsBeforeRotation()
    {
        // (2,3) -> (-4,9) -> (-9,-4) -> (91,196).
        var transform = DxfInsertTransform.Create(new(0, 0), new(100, 200), -2, 3, 90);
        AssertPoint(new(91, 196), transform.Apply(new(2, 3)));
        Assert.True(transform.IsReflection);
    }

    [Fact]
    public void TwoNegativeScalesAreNotAReflection()
    {
        var transform = DxfInsertTransform.Create(new(1, 1), new(5, 7), -2, -2, 0);
        AssertPoint(new(1, 1), transform.Apply(new(3, 4)));
        Assert.False(transform.IsReflection);
    }

    [Fact]
    public void NestedCompositionAppliesChildBeforeParent()
    {
        // Child: (4,6) -> (1,16). Parent: (1,16) -> (99,232).
        var child = DxfInsertTransform.Create(new(1, 2), new(5, 10), 2, 1, 90);
        var parent = DxfInsertTransform.Create(new(0, 0), new(100, 200), -1, 2, 0);
        var combined = child.Then(parent);
        AssertPoint(new(1, 16), child.Apply(new(4, 6)));
        AssertPoint(new(99, 232), combined.Apply(new(4, 6)));
        Assert.True(combined.IsReflection);
    }

    [Fact]
    public void CompositionOfTwoReflectionsIsNotAReflection()
    {
        var first = DxfInsertTransform.Create(new(0, 0), new(0, 0), -1, 1, 0);
        var second = DxfInsertTransform.Create(new(0, 0), new(0, 0), 1, -1, 0);
        var combined = first.Then(second);
        AssertPoint(new(-3, -4), combined.Apply(new(3, 4)));
        Assert.False(combined.IsReflection);
    }

    [Fact]
    public void InvalidInputsAndOutOfRangeResultsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfInsertTransform.Create(new(double.NaN, 0), new(0, 0), 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfInsertTransform.Create(new(0, 0), new(0, double.PositiveInfinity), 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfInsertTransform.Create(new(0, 0), new(0, 0), 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfInsertTransform.Create(new(0, 0), new(0, 0), 1, double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfInsertTransform.Create(new(0, 0), new(0, 0), 1, 1, double.NegativeInfinity));
        var identity = DxfInsertTransform.Create(new(0, 0), new(0, 0), 1, 1, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => identity.Apply(new(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => identity.ApplyMillimeters(new(100_000_001, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DxfInsertTransform.Create(new(0, 0), new(100_000_000, 0), 1, 1, 0).ApplyMillimeters(new(1, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DxfInsertTransform.Create(new(0, 0), new(0, 0), double.MaxValue, 1, 0).Apply(new(2, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DxfInsertTransform.Create(new(0, 0), new(0, 0), 1e200, 1, 0)
                .Then(DxfInsertTransform.Create(new(0, 0), new(0, 0), 1e200, 1, 0)));
    }

    [Fact]
    public void DomainLimitAppliesAfterConvertingSourceUnitsToMillimeters()
    {
        var identity = DxfInsertTransform.Create(new(200_000_000, 0), new(200_000_000, 0), 1, 1, 0);
        AssertPoint(new(20_000_000, 0), identity.ApplyMillimeters(new(200_000_000, 0), 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => identity.ApplyMillimeters(new(200_000_000, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => identity.ApplyMillimeters(new(1, 0), 0));
    }

    [Fact]
    public void LargeBlockOriginDoesNotEraseSmallInsertion()
    {
        var transform = DxfInsertTransform.Create(new(1e16, 0), new(1, 0), 1, 1, 0);
        AssertPoint(new(1, 0), transform.Apply(new(1e16, 0)));
        AssertPoint(new(3, 0), transform.Apply(new(1e16 + 2, 0)));
        var parent = DxfInsertTransform.Create(new(1, 0), new(4, 0), 2, 1, 0);
        AssertPoint(new(8, 0), transform.Then(parent).Apply(new(1e16 + 2, 0)));
    }

    [Fact]
    public void ProvenanceUsesTheEntireOccurrencePathAsValueIdentity()
    {
        DxfInsertOccurrence[] path = [new("A", 1), new("B", 2)];
        var first = new DxfInsertProvenance("snapshot", path, 3, 4);
        var same = new DxfInsertProvenance("snapshot", path.ToArray(), 3, 4);
        var otherOccurrence = new DxfInsertProvenance("snapshot", [new("A", 1), new("B", 3)], 3, 4);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, otherOccurrence);
        Assert.Contains(same, new HashSet<DxfInsertProvenance> { first });
    }

    [Fact]
    public void UninitializedTransformCannotBeApplied()
    {
        Assert.Throws<InvalidOperationException>(() => default(DxfInsertTransform).Apply(new(0, 0)));
    }

    private static void AssertPoint(Point2 expected, Point2 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }
}
