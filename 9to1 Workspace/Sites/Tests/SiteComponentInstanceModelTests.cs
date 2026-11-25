using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

/// <summary>ED-SITES-001: component instances distinguish linked reusable definitions, explicit overrides and detached copies; a definition edit
/// previews affected instances; detaching creates a declared independent tree. Proves linked inherits, override wins without altering the
/// definition, detach yields an independent tree unaffected by later definition edits, and affected-instance preview lists only linked/overridden.</summary>
public sealed class SiteComponentInstanceModelTests
{
    private static readonly Guid DefinitionId = Guid.NewGuid();
    private static readonly Guid RootId = Guid.NewGuid();

    private static JsonElement Value(string text) => JsonDocument.Parse("\"" + text + "\"").RootElement.Clone();

    private static SiteReusableComponent Definition() => new(DefinitionId, "Card", RootId,
        new Dictionary<string, JsonElement> { ["title"] = Value("Default") }, new Dictionary<string, IReadOnlyList<Guid>>(), 1);

    private static SiteComponent Instance(Guid id, Guid? definitionId, Dictionary<string, JsonElement>? overrides = null, Dictionary<string, JsonElement>? properties = null) =>
        new(id, "Card", properties ?? new Dictionary<string, JsonElement>(), [], new Dictionary<string, IReadOnlyList<Guid>>(),
            new(SiteLayoutMode.Flow, new Dictionary<string, JsonElement>(), new Dictionary<string, SiteLayoutOverride>()),
            new Dictionary<string, string>(), new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(), null, definitionId, overrides ?? new Dictionary<string, JsonElement>(), 1);

    [Fact]
    public void LinkedInstanceInheritsDefinitionValues()
    {
        var known = new[] { DefinitionId };
        var linked = Instance(Guid.NewGuid(), DefinitionId);
        Assert.Equal(SiteInstanceKind.Linked, SiteComponentInstanceModel.Classify(linked, known));

        var effective = SiteComponentInstanceModel.EffectiveProperties(Definition(), linked);
        Assert.Equal("Default", effective["title"].GetString());
    }

    [Fact]
    public void ExplicitOverrideWinsWithoutAlteringTheDefinition()
    {
        var definition = Definition();
        var overridden = Instance(Guid.NewGuid(), DefinitionId, new Dictionary<string, JsonElement> { ["title"] = Value("Custom") });
        Assert.Equal(SiteInstanceKind.Overridden, SiteComponentInstanceModel.Classify(overridden, new[] { DefinitionId }));

        var effective = SiteComponentInstanceModel.EffectiveProperties(definition, overridden);
        Assert.Equal("Custom", effective["title"].GetString());
        // The definition is untouched.
        Assert.Equal("Default", definition.DeclaredProperties["title"].GetString());
    }

    [Fact]
    public void DetachYieldsIndependentTreeUnaffectedByLaterDefinitionEdits()
    {
        var detached = SiteComponentInstanceModel.Detach(Definition(), Instance(Guid.NewGuid(), DefinitionId, new Dictionary<string, JsonElement> { ["title"] = Value("Custom") }));
        // The detached copy keeps its effective properties but no longer tracks the definition.
        Assert.Null(detached.ReusableDefinitionId);
        Assert.Equal("Custom", detached.Properties["title"].GetString());
        Assert.Equal(SiteInstanceKind.Detached, SiteComponentInstanceModel.Classify(detached, new[] { DefinitionId }));

        // A later definition edit does not affect the detached tree.
        var edited = Definition() with { DeclaredProperties = new Dictionary<string, JsonElement> { ["title"] = Value("Changed") } };
        Assert.Equal("Custom", SiteComponentInstanceModel.EffectiveProperties(edited, detached)["title"].GetString());
    }

    [Fact]
    public void AffectedInstancesListsOnlyLinkedAndOverridden()
    {
        var known = new[] { DefinitionId };
        var linked = Instance(Guid.NewGuid(), DefinitionId);
        var overridden = Instance(Guid.NewGuid(), DefinitionId, new Dictionary<string, JsonElement> { ["title"] = Value("X") });
        var standalone = Instance(Guid.NewGuid(), null);
        var detached = SiteComponentInstanceModel.Detach(Definition(), Instance(Guid.NewGuid(), DefinitionId));

        var affected = SiteComponentInstanceModel.AffectedInstances(DefinitionId, [linked, overridden, standalone, detached], known);
        Assert.Contains(linked.ComponentId, affected);
        Assert.Contains(overridden.ComponentId, affected);
        Assert.DoesNotContain(standalone.ComponentId, affected);
        Assert.DoesNotContain(detached.ComponentId, affected);
    }

    [Fact]
    public void ResetToInheritedRemovesOnlyThatOverride()
    {
        var overridden = Instance(Guid.NewGuid(), DefinitionId, new Dictionary<string, JsonElement>
        {
            ["title"] = Value("Custom"),
            ["subtitle"] = Value("Sub")
        });
        var reset = SiteComponentInstanceModel.ResetToInherited(overridden, "title");

        Assert.False(reset.Overrides.ContainsKey("title"));
        // The other override and the definition remain intact.
        Assert.True(reset.Overrides.ContainsKey("subtitle"));
        Assert.Equal("Default", SiteComponentInstanceModel.EffectiveProperties(Definition(), reset)["title"].GetString());
    }

    [Fact]
    public void ResetOfNonOverriddenPropertyIsRejected()
    {
        var linked = Instance(Guid.NewGuid(), DefinitionId);
        Assert.Throws<InvalidOperationException>(() => SiteComponentInstanceModel.ResetToInherited(linked, "title"));
    }
}
