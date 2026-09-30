using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeArtifactReferenceObjectsTests
{
    private static ProductivityArtifactReferenceSource Source() => new(Guid.NewGuid(), 2,
        new(Guid.NewGuid(), SpaceContextReferenceKind.WriteArtifact, "write", Guid.NewGuid().ToString("D"),
            "opaque-owning-token", SpaceContextPermission.Read, default, false, DateTimeOffset.UtcNow, Guid.NewGuid()), Guid.NewGuid());

    [Fact]
    public async Task Display_requires_exact_acquired_reference_preserves_unknowns_and_cannot_clone()
    {
        var reference = Source();
        using var prepared = await HomePreparedArtifactReference.PrepareAsync(new Resolver(reference), reference);
        var handler = new HomeArtifactReferenceObjectHandler(prepared);
        var engine = new HomeProductivityEngine(handlers: [handler]);
        var json = JsonNode.Parse(HomeArtifactReferenceObjectHandler.ReferenceContent(reference).GetRawText())!;
        json["future"] = true; json["reference"]!["context"]!["futureContext"] = 7;
        var value = engine.CreateObject("artifact.reference", reference.Context.ContextId, JsonSerializer.SerializeToElement(json));
        var render = engine.RenderObject(value);
        Assert.Contains("A &amp; B", render.CuiSource);
        Assert.Contains("content.future", render.RetainedUnsupportedProperties);
        Assert.Contains("content.reference.context.futureContext", render.RetainedUnsupportedProperties);
        Assert.Equal(7, value.Content.GetProperty("reference").GetProperty("context").GetProperty("futureContext").GetInt32());
        Assert.Throws<NotSupportedException>(() => handler.CloneForPaste(value, Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => handler.Create(Guid.NewGuid(), value.Content));
        var changed = reference with { FilesRevisionId = Guid.NewGuid() };
        Assert.Throws<UnauthorizedAccessException>(() => handler.Render(value with { Content = HomeArtifactReferenceObjectHandler.ReferenceContent(changed) }));
        prepared.Dispose();
        Assert.Throws<ObjectDisposedException>(() => handler.Render(value));
    }

    [Fact]
    public async Task Preparation_rejects_different_owner_result_and_canceled_acquisition()
    {
        var source = Source();
        await Assert.ThrowsAsync<InvalidDataException>(() => HomePreparedArtifactReference.PrepareAsync(
            new Resolver(source with { SpaceRevision = 3 }), source));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HomePreparedArtifactReference.PrepareAsync(new Resolver(source), source, canceled.Token));
    }

    [Fact]
    public void Unattached_object_cannot_fabricate_a_backed_artifact_reference()
    {
        var engine = new HomeProductivityEngine();
        var paragraph = engine.CreateObject("text.paragraph", Guid.NewGuid(), JsonSerializer.SerializeToElement(new { text = "Content" }));
        Assert.Throws<InvalidDataException>(() => engine.Convert(paragraph, "artifact.reference", JsonSerializer.SerializeToElement(new { })));
        Assert.Equal(JsonSerializer.Serialize(paragraph), JsonSerializer.Serialize(engine.Convert(paragraph, "text.paragraph", JsonSerializer.SerializeToElement(new { }))));
    }

    private sealed class Resolver(ProductivityArtifactReferenceSource result) : IProductivityArtifactReferenceResolver
    {
        public Task<ResolvedProductivityArtifactReference> ResolveAsync(ProductivityArtifactReferenceSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResolvedProductivityArtifactReference(result, "A & B", "WriteDocument"));
    }
}
