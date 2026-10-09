using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Core.Tests;

public sealed class DocumentVectorPrimitiveTests
{
    [Fact]
    public void Built_ins_use_valid_shared_editable_geometry_and_fresh_stable_ids()
    {
        foreach (var primitive in Enum.GetValues<DocumentVectorPrimitive>())
        {
            var first = DocumentVectorPrimitives.Create(primitive, 7, 200, 80);
            var second = DocumentVectorPrimitives.Create(primitive, 7, 200, 80);
            Assert.True(DocumentVectorShapeValidator.Validate(first).IsValid);
            Assert.Equal(DocumentShapeSourceKind.BuiltIn, first.SourceKind);
            Assert.Equal(200, first.ViewBox.Width); Assert.Equal(80, first.ViewBox.Height);
            Assert.Equal(primitive.ToString(), first.Metadata["creation.primitive"]);
            var nodes = first.Paths[0].Subpaths[0].Nodes;
            Assert.Equal(nodes.Count, nodes.Select(node => node.Id).Distinct().Count());
            Assert.NotEqual(first.Id, second.Id);
            Assert.Empty(nodes.Select(node => node.Id).Intersect(second.Paths[0].Subpaths[0].Nodes.Select(node => node.Id)));
            var clone = DocumentVectorShapes.Clone(first);
            Assert.Equal(first.Id, clone.Id); Assert.Equal(nodes.Select(node => node.Id), clone.Paths[0].Subpaths[0].Nodes.Select(node => node.Id));
            if (primitive == DocumentVectorPrimitive.Ellipse)
            {
                Assert.Equal(5, nodes.Count); Assert.Equal(nodes[0].Point, nodes[^1].Point);
                Assert.All(nodes.Skip(1), node => { Assert.Equal(DocumentVectorSegmentKind.Cubic, node.IncomingSegment); Assert.NotNull(node.Control1); Assert.NotNull(node.Control2); });
            }
            if (primitive == DocumentVectorPrimitive.Polygon) Assert.Equal(7, nodes.Count);
            if (primitive == DocumentVectorPrimitive.Star) Assert.Equal(14, nodes.Count);
            if (primitive == DocumentVectorPrimitive.Line)
            {
                Assert.False(first.Paths[0].Subpaths[0].Closed); Assert.Equal(DocumentVectorFillKind.None, first.Paths[0].Fill.Kind);
                Assert.True(first.Paths[0].Stroke.Enabled); Assert.Equal(DocumentVectorLineCap.Round, first.Paths[0].Stroke.Cap);
            }
        }
    }

    [Fact]
    public void Invalid_creation_parameters_are_refused_and_existing_canonical_shapes_remain_untouched()
    {
        var original = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle);
        var originalId = original.Id; var originalNodes = original.Paths[0].Subpaths[0].Nodes.Select(node => node.Id).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create((DocumentVectorPrimitive)99));
        foreach (var points in new[] { 0, 2, 65, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Star, points));
        foreach (var dimension in new[] { -1d, 0, double.NaN, double.PositiveInfinity, 100001 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse, width: dimension));
            Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Line, height: dimension));
        }
        Assert.Equal(128, DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Star, 64).Paths[0].Subpaths[0].Nodes.Count);
        Assert.Equal(originalId, original.Id); Assert.Equal(originalNodes, original.Paths[0].Subpaths[0].Nodes.Select(node => node.Id));
    }
}
