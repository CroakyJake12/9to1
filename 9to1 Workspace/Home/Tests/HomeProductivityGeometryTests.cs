using System.Text.Json;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeProductivityGeometryTests
{
    [Fact]
    public void Picture_clockwise_rotation_and_flip_match_actual_document_pixel_convention()
    {
        var clockwise = new HomeProductivityAffine(0, 1, -1, 0, 40, 0);
        Assert.Equal((40d, 0d), clockwise.Map(0, 0));
        Assert.Equal((0d, 100d), clockwise.Map(100, 40));
        var flip = new HomeProductivityAffine(-1, 0, 0, 1, 100, 0);
        Assert.Equal((100d, 0d), flip.Map(0, 0));
        Assert.Equal((0d, 40d), flip.Map(100, 40));
    }

    [Fact]
    public void Explicit_local_and_document_composition_produce_different_correct_placements()
    {
        var source = Project(new(2, 0, 0, 2, 10, 20));
        var translate = HomeProductivityAffine.Identity with { Dx = 5, Dy = 3 };
        var local = HomeProductivityGeometryOperations.Transform(source, translate, HomeProductivityTransformSpace.Local, 0, 0);
        var document = HomeProductivityGeometryOperations.Transform(source, translate, HomeProductivityTransformSpace.Document, 0, 0);
        Assert.Equal((20d, 26d), HomeProductivityGeometryOperations.Read(local).LocalToDocument.Map(0, 0));
        Assert.Equal((15d, 23d), HomeProductivityGeometryOperations.Read(document).LocalToDocument.Map(0, 0));
        Assert.Equal((10d, 20d), HomeProductivityGeometryOperations.Read(source).LocalToDocument.Map(0, 0));
    }

    [Fact]
    public void Explicit_pivot_is_fixed_and_unknown_properties_identity_content_and_bounds_survive()
    {
        var source = Project(HomeProductivityAffine.Identity);
        var extended = JsonSerializer.SerializeToElement(new
        {
            ownerUnknown = new { retained = true },
            sharedGeometry = new { schemaVersion = 1, unit = "document-pixel", width = 100, height = 40,
                localToDocument = new { m11 = 1, m12 = 0, m21 = 0, m22 = 1, dx = 0, dy = 0, futureMatrix = "retain" }, futureField = new[] { 3, 4 } }
        });
        source = source with { Layout = extended };
        var changed = HomeProductivityGeometryOperations.Transform(source, new(0, 1, -1, 0, 0, 0),
            HomeProductivityTransformSpace.Document, 50, 20);
        var geometry = HomeProductivityGeometryOperations.Read(changed);
        Assert.Equal((50d, 20d), geometry.LocalToDocument.Map(50, 20));
        Assert.Equal((70d, -30d), geometry.LocalToDocument.Map(0, 0));
        Assert.Equal(source.ObjectId, changed.ObjectId);
        Assert.Equal(source.Content.GetRawText(), changed.Content.GetRawText());
        Assert.Equal(100, geometry.Width);
        Assert.Equal("[3,4]", changed.Layout.GetProperty("sharedGeometry").GetProperty("futureField").GetRawText());
        Assert.True(changed.Layout.GetProperty("ownerUnknown").GetProperty("retained").GetBoolean());
        Assert.Equal("retain", changed.Layout.GetProperty("sharedGeometry").GetProperty("localToDocument").GetProperty("futureMatrix").GetString());
    }

    [Fact]
    public void Unknown_geometry_versions_and_implicit_geometry_are_not_reinterpreted()
    {
        var source = NewObject();
        Assert.Throws<NotSupportedException>(() => HomeProductivityGeometryOperations.Read(source));
        source = source with { Layout = JsonSerializer.SerializeToElement(new { sharedGeometry = new { schemaVersion = 2 } }) };
        Assert.Throws<NotSupportedException>(() => HomeProductivityGeometryOperations.Read(source));
        Assert.Throws<InvalidOperationException>(() => HomeProductivityGeometryOperations.Project(source, new(1, "document-pixel", 100, 40, HomeProductivityAffine.Identity)));
    }

    [Fact]
    public void Nonfinite_and_unbounded_transform_values_are_rejected_before_mutation()
    {
        var source = Project(HomeProductivityAffine.Identity);
        Assert.Throws<InvalidDataException>(() => HomeProductivityGeometryOperations.Transform(source,
            HomeProductivityAffine.Identity with { Dx = double.NaN }, HomeProductivityTransformSpace.Document, 0, 0));
        Assert.Throws<InvalidDataException>(() => HomeProductivityGeometryOperations.Transform(source,
            HomeProductivityAffine.Identity, HomeProductivityTransformSpace.Document, double.PositiveInfinity, 0));
        Assert.Throws<InvalidDataException>(() => new HomeProductivityAffine(1e12, 0, 0, 1, 0, 0).Compose(new(1e12, 0, 0, 1, 0, 0)));
        Assert.Equal(HomeProductivityAffine.Identity, HomeProductivityGeometryOperations.Read(source).LocalToDocument);
    }

    private static HomeProductivityObject Project(HomeProductivityAffine transform) =>
        HomeProductivityGeometryOperations.Project(NewObject(), new(1, "document-pixel", 100, 40, transform));
    private static HomeProductivityObject NewObject() => new(Guid.NewGuid(), "media.image", 1,
        JsonSerializer.SerializeToElement(new { canonicalOwnerReference = "preserved" }),
        JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [],
        JsonSerializer.SerializeToElement(new { ownerUnknown = 7 }));
}
