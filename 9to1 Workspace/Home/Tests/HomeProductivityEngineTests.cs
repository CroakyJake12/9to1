using System.Text.Json;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeProductivityEngineTests
{
    [Fact]
    public async Task Owner_dispatch_rejects_unsupported_surface_and_partial_atomic_success_preserves_observed_outcome()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var owner = new ReportingOwner(new(true, "Committed", "Committed", 8, [ids[0]])
        { ArtifactRevision = new(Sequence: 8), Outcome = HomeProductivityArtifactOutcome.Committed });
        var engine = new HomeProductivityEngine(artifactActions: [owner]);
        var context = new HomeProductivityContext("write", "doc", 7, ids, new HashSet<string>(["text.paragraph"]));
        var action = new HomeProductivityAction("format.bold", 1, "text.paragraph", ids, JsonSerializer.SerializeToElement(new { value = true }), 7);
        var unsupported = await engine.ApplyActionAsync(context with { SupportedObjectTypes = new HashSet<string>() }, action);
        Assert.Equal("SurfaceObjectTypeUnsupported", unsupported.Code); Assert.Equal(0, owner.Calls);
        var result = await engine.ApplyActionAsync(context, action);
        Assert.False(result.Succeeded); Assert.Equal(HomeProductivityArtifactOutcome.NeedsRecovery, result.Outcome);
        Assert.Equal("ArtifactOutcomeNeedsRecovery", result.Code);
        Assert.Equal(8, result.Revision); Assert.Equal(new(Sequence: 8), result.ArtifactRevision);
        Assert.Equal(ids[0], Assert.Single(result.AffectedObjectIds));
        owner.Result = owner.Result with { AffectedObjectIds = ids };
        var complete = await engine.ApplyActionAsync(context, action);
        Assert.True(complete.Succeeded); Assert.Equal(HomeProductivityArtifactOutcome.Committed, complete.Outcome);
        owner.Result = owner.Result with { ArtifactRevision = null };
        Assert.Equal(HomeProductivityArtifactOutcome.NeedsRecovery, (await engine.ApplyActionAsync(context, action)).Outcome);
    }
    private sealed class ReportingOwner(HomeProductivityActionResult result) : IHomeProductivityArtifactActionProvider
    {
        public string AppId => "write";
        public int Calls { get; private set; }
        public HomeProductivityActionResult Result { get; set; } = result;
        public ValueTask<HomeProductivityActionResult> ApplyAsync(HomeProductivityContext context, HomeProductivityAction action,
            Func<HomeProductivityObject, HomeProductivityObject> sharedTransformation, CancellationToken cancellationToken)
        { Calls++; return ValueTask.FromResult(Result); }
    }
    [Fact]
    public void Selection_and_paste_detach_unknown_extensions_and_asset_lists()
    {
        var engine = new HomeProductivityEngine(); var id = Guid.NewGuid();
        var assets = new List<string> { "asset:allowed" };
        var extensions = new Dictionary<string, JsonElement> { ["future"] = JsonSerializer.SerializeToElement(new { value = "preserved" }) };
        var item = engine.CreateObject("text.paragraph", id, JsonSerializer.SerializeToElement(new { text = "Hello" })) with
        { AssetReferences = assets, Extensions = extensions };
        var context = new HomeProductivityContext("write", "doc", 1, [id], new HashSet<string>(["text.paragraph"]));
        var bundle = engine.SerializeSelection(context, [item]);
        assets[0] = "asset:injected"; extensions.Clear();
        Assert.Equal("asset:allowed", Assert.Single(bundle.Objects[0].AssetReferences));
        Assert.Equal("preserved", bundle.Objects[0].Extensions!["future"].GetProperty("value").GetString());
        var pasted = engine.Paste(bundle, context)[0];
        bundle.Objects[0].Extensions!.Clear();
        Assert.Equal("preserved", pasted.Extensions!["future"].GetProperty("value").GetString());
        Assert.NotEqual(id, pasted.ObjectId);
    }

    [Fact]
    public void Opaque_owner_revisions_are_compared_without_synthetic_sequence_conversion()
    {
        var engine = new HomeProductivityEngine(); var id = Guid.NewGuid(); var revision = Guid.NewGuid();
        var context = new HomeProductivityContext("canvas", "artifact", 0, [id], new HashSet<string>(["text.paragraph"]))
        { ArtifactRevision = new(VersionId: revision) };
        var action = new HomeProductivityAction("format.bold", 1, "text.paragraph", [id], JsonSerializer.SerializeToElement(new { value = true }), 0)
        { ExpectedArtifactRevision = new(VersionId: revision) };
        Assert.Equal("ArtifactExecutionRequired", engine.ApplyAction(context, action).Code);
        Assert.Equal("RevisionConflict", engine.ApplyAction(context, action with { ExpectedArtifactRevision = new(VersionId: Guid.NewGuid()) }).Code);
        Assert.Equal("RevisionConflict", engine.ApplyAction(context, action with { ExpectedArtifactRevision = null }).Code);
        Assert.Equal("RevisionConflict", engine.ApplyAction(context, action with { ExpectedArtifactRevision = new(Sequence: 0, VersionId: revision) }).Code);
    }

    [Fact]
    public async Task Concrete_shared_formatting_requires_artifact_execution_and_preserves_optional_properties()
    {
        var engine = new HomeProductivityEngine();
        var id = Guid.NewGuid();
        var item = engine.CreateObject("text.paragraph", id, JsonSerializer.SerializeToElement(new { text = "<Hello & world>", future = 42 }));
        var context = new HomeProductivityContext("write", "doc-1", 7, [id], new HashSet<string>(["text.paragraph"]));
        var action = new HomeProductivityAction("format.bold", 1, "text.paragraph", [id], JsonSerializer.SerializeToElement(new { value = true }), 7);
        var result = await engine.ApplyActionAsync(context, action);
        Assert.False(result.Succeeded); Assert.Equal("ArtifactExecutorUnavailable", result.Code); Assert.Equal(7, result.Revision);
        var transformed = new HomeParagraphObjectHandler().Transform(item, action);
        Assert.True(transformed.Formatting.GetProperty("bold").GetBoolean());
        Assert.Equal(42, transformed.Content.GetProperty("future").GetInt32());
        Assert.False(item.Formatting.TryGetProperty("bold", out _));
        var rendered = engine.RenderObject(transformed);
        var document = System.Xml.Linq.XDocument.Parse(rendered.CuiSource);
        Assert.Equal("<Hello & world>", document.Root!.Element("TextBlock")!.Attribute("text")!.Value);
        Assert.Equal("Bold", document.Root.Element("TextBlock")!.Attribute("font-weight")!.Value);
        Assert.Contains("content.future", rendered.RetainedUnsupportedProperties);
        Assert.Throws<NotSupportedException>(() => engine.CreateObject("drawing.ink", Guid.NewGuid(), JsonSerializer.SerializeToElement(new { })));
        Assert.False(engine.GetCompatibility("canvas", "1", ["drawing.ink"]).Compatible);
    }

    [Fact]
    public void Bundle_round_trip_preserves_unknown_optional_properties_and_stable_styles()
    {
        var engine = new HomeProductivityEngine();
        var extension = JsonDocument.Parse("{\"future\":\"keep\"}").RootElement.Clone();
        var content = JsonDocument.Parse("{\"text\":\"Hello\"}").RootElement.Clone();
        var item = new HomeProductivityObject(Guid.NewGuid(), "text.paragraph", 1, content,
            JsonDocument.Parse("{}").RootElement.Clone(), JsonDocument.Parse("{}").RootElement.Clone(), [],
            JsonDocument.Parse("{}").RootElement.Clone(), new Dictionary<string, JsonElement> { ["x-extra"] = extension });
        var context = new HomeProductivityContext("write", "doc-1", 4, [item.ObjectId], new HashSet<string>(["text.paragraph"]));
        var parsed = engine.ParseBundle(engine.SerializeBundle(engine.SerializeSelection(context, [item])));
        Assert.Equal("keep", parsed.Objects.Single().Extensions!["x-extra"].GetProperty("future").GetString());
        Assert.True(engine.CanPaste(parsed, context, out var code));
        Assert.Equal("CanPaste", code);
        Assert.NotEqual(item.ObjectId, engine.Paste(parsed, context).Single().ObjectId);
        var blocked = context with { SupportedObjectTypes = new HashSet<string>(["artifact.reference"]) };
        Assert.False(engine.CanPaste(parsed, blocked, out var blockedCode));
        Assert.Equal("OfferArtifactEmbed", blockedCode);
        Assert.Throws<InvalidDataException>(() => engine.Paste(parsed, blocked));
    }

    [Fact]
    public void Compatibility_and_typed_actions_report_conflicts_without_silent_loss()
    {
        var engine = new HomeProductivityEngine();
        var compatibility = engine.GetCompatibility("write", "1.0", ["text.paragraph", "future.required"]);
        Assert.False(compatibility.Compatible);
        Assert.Equal("NeedsAttention", compatibility.State);
        var id = Guid.NewGuid();
        var context = new HomeProductivityContext("write", "doc-1", 7, [id], new HashSet<string>(["text.paragraph"]));
        var action = new HomeProductivityAction("format.bold", 1, "text.paragraph", [id],
            JsonDocument.Parse("{}").RootElement.Clone(), 6);
        Assert.Equal("RevisionConflict", engine.ApplyAction(context, action).Code);
        var unsupported = new HomeProductivityObject(id, "unknown.required", 2,
            JsonDocument.Parse("{}").RootElement.Clone(), JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse("{}").RootElement.Clone(), [], JsonDocument.Parse("{}").RootElement.Clone());
        Assert.Throws<InvalidDataException>(() => engine.ParseBundle(engine.SerializeBundle(new HomeProductivityObjectBundle(
            1, [unsupported], [], [], JsonDocument.Parse("{}").RootElement.Clone(), JsonDocument.Parse("{}").RootElement.Clone()))));
    }
}
