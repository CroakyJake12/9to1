using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Contract.Tests;

public sealed class HavenRichSectionIconGeometryTests
{
    [Fact]
    public void Typed_shapes_round_trip_as_discriminated_geometry()
    {
        var icon = ValidIcon();
        HavenRichSectionIconGeometry.Validate(icon);

        var json = JsonSerializer.Serialize(icon, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(json), 1, HavenRichSectionIconGeometry.MaximumSerializedBytes);
        var restored = JsonSerializer.Deserialize<HavenRichSectionGeneratedIcon>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(restored);
        Assert.Equal(icon.Shapes.Count, restored.Shapes.Count);
        Assert.IsType<HavenRichSectionIconRoundRect>(restored.Shapes[0]);
        Assert.IsType<HavenRichSectionIconCircle>(restored.Shapes[1]);
        var polygon = Assert.IsType<HavenRichSectionIconPolygon>(restored.Shapes[2]);
        Assert.True(polygon.IsCutout);
        Assert.Equal(((HavenRichSectionIconPolygon)icon.Shapes[2]).Points, polygon.Points);
    }

    [Fact]
    public void Geometry_enforces_shape_vertex_viewbox_and_fill_limits()
    {
        var maxGeometry = new HavenRichSectionGeneratedIcon
        {
            Shapes = Enumerable.Range(0, HavenRichSectionIconGeometry.MaximumShapes)
                .Select(index => (HavenRichSectionIconShape)new HavenRichSectionIconCircle(
                    3 + (index % 6) * 3.5, 3 + (index / 6) * 8, 1))
                .ToList()
        };
        HavenRichSectionIconGeometry.Validate(maxGeometry);
        var maximumVertices = new HavenRichSectionGeneratedIcon
        {
            Shapes = Enumerable.Range(0, 8).Select(_ =>
            {
                var points = Enumerable.Range(0, 8).Select(index =>
                {
                    var angle = 2 * Math.PI * index / 8;
                    return new HavenRichSectionIconPoint(12 + 10 * Math.Cos(angle), 12 + 10 * Math.Sin(angle));
                }).ToList();
                return (HavenRichSectionIconShape)new HavenRichSectionIconPolygon(points);
            }).ToList()
        };
        HavenRichSectionIconGeometry.Validate(maximumVertices);

        Assert.Throws<InvalidOperationException>(() => HavenRichSectionIconGeometry.Validate(new()
        {
            Shapes = Enumerable.Range(0, HavenRichSectionIconGeometry.MaximumShapes + 1)
                .Select(_ => (HavenRichSectionIconShape)new HavenRichSectionIconCircle(12, 12, 1)).ToList()
        }));
        Assert.Throws<InvalidOperationException>(() => HavenRichSectionIconGeometry.Validate(new()
        {
            Shapes = [new HavenRichSectionIconRoundRect(0, 0, 25, 3, 1)]
        }));
        Assert.Throws<InvalidOperationException>(() => HavenRichSectionIconGeometry.Validate(new()
        {
            Shapes = [new HavenRichSectionIconCircle(1, 1, 2)]
        }));
        Assert.Throws<InvalidOperationException>(() => HavenRichSectionIconGeometry.Validate(new()
        {
            Shapes = [new HavenRichSectionIconPolygon(
                [new(0, 0), new(4, 4), new(0, 4), new(4, 0), new(5, 1)])]
        }));
        Assert.Throws<InvalidOperationException>(() => HavenRichSectionIconGeometry.Validate(new()
        {
            Shapes =
            [
                new HavenRichSectionIconCircle(12, 12, 1),
                .. Enumerable.Range(0, 6).Select(_ => (HavenRichSectionIconShape)new HavenRichSectionIconPolygon(
                    Enumerable.Range(0, 11).Select(index =>
                    {
                        var angle = 2 * Math.PI * index / 11;
                        return new HavenRichSectionIconPoint(12 + 10 * Math.Cos(angle), 12 + 10 * Math.Sin(angle));
                    }).ToList()))
            ]
        }));
        Assert.Throws<InvalidOperationException>(() => HavenRichSectionIconGeometry.Validate(new()
        {
            Shapes = [new HavenRichSectionIconPolygon([new(0, 0), new(1, 1), new(2, 2)], IsCutout: true)]
        }));
    }

    [Fact]
    public void Section_discriminator_requires_geometry_and_builtin_icon_rejects_sidecar_geometry()
    {
        var notes = HavenRichNotes.Create();
        var section = Assert.Single(notes.Sections);
        section.IconKey = HavenRichSectionIcons.Generated;
        Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(notes));

        section.GeneratedIcon = ValidIcon();
        HavenRichNotesValidator.Validate(notes);
        section.IconKey = HavenRichSectionIcons.Default;
        Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(notes));
    }

    [Fact]
    public void Copy_is_detached_and_value_comparison_is_structural()
    {
        var source = ValidIcon();
        var copy = HavenRichSectionIconGeometry.Copy(source);
        Assert.True(HavenRichSectionIconGeometry.AreEqual(source, copy));
        Assert.NotSame(source.Shapes, copy.Shapes);
        Assert.NotSame(((HavenRichSectionIconPolygon)source.Shapes[2]).Points,
            ((HavenRichSectionIconPolygon)copy.Shapes[2]).Points);

        ((HavenRichSectionIconPolygon)copy.Shapes[2]).Points[0] = new(3, 3);
        Assert.False(HavenRichSectionIconGeometry.AreEqual(source, copy));
    }

    private static HavenRichSectionGeneratedIcon ValidIcon() => new()
    {
        Shapes =
        [
            new HavenRichSectionIconRoundRect(2, 2, 20, 20, 3),
            new HavenRichSectionIconCircle(12, 12, 5),
            new HavenRichSectionIconPolygon([new(8, 8), new(16, 8), new(12, 16)], IsCutout: true)
        ]
    };
}
