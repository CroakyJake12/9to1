using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class GenUiOriginalInstanceActionTests
{
    [Fact]
    public async Task Two_live_custom_documents_keep_the_same_public_action_and_route_only_their_original_instance()
    {
        var instances = new GenUiInstanceStore(); var local = new GenUiLocalActionRegistry();
        var runtime = new CustomTemplateRuntime(local, instances);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), instances);
        var first = runtime.Create(Guid.NewGuid(), "assistants", Inputs("first"));
        var second = runtime.Create(Guid.NewGuid(), "assistants", Inputs("second"));
        instances.Register(first); instances.Register(second);
        var firstBinding = Find(first.Root, "button").Actions.Single();
        var secondBinding = Find(second.Root, "button").Actions.Single();
        Assert.Equal("set", firstBinding.ActionId); Assert.Equal(firstBinding.ActionId, secondBinding.ActionId);
        Assert.NotEqual(firstBinding.TargetKey, secondBinding.TargetKey);
        var firstResult = await router.RouteAsync(Event(first), firstBinding, CancellationToken.None);
        Assert.Equal(GenUiActionStatus.Completed, firstResult.Status);
        Assert.Equal("first", Find(instances.TryGet(first.Origin.InstanceId)!.Root, "value").Properties["text"].GetString());
        Assert.Equal("before", Find(instances.TryGet(second.Origin.InstanceId)!.Root, "value").Properties["text"].GetString());
        var secondResult = await router.RouteAsync(Event(second), secondBinding, CancellationToken.None);
        Assert.Equal(GenUiActionStatus.Completed, secondResult.Status);
        Assert.Equal("second", Find(instances.TryGet(second.Origin.InstanceId)!.Root, "value").Properties["text"].GetString());
        Assert.Equal(GenUiActionStatus.Denied, (await router.RouteAsync(Event(first), secondBinding, CancellationToken.None)).Status);
        Assert.Equal(GenUiActionStatus.Denied, (await local.HandleAsync(Event(second), firstBinding, CancellationToken.None)).Status);
        Assert.False(runtime.ReleaseOriginalInstanceActions(first with { }));
        Assert.True(runtime.ReleaseOriginalInstanceActions(first));
        Assert.False(local.CanHandle(firstBinding.TargetKey)); Assert.True(local.CanHandle(secondBinding.TargetKey));
        Assert.True(runtime.ReleaseOriginalInstanceActions(first));
        Assert.Equal(GenUiActionStatus.Unavailable, (await router.RouteAsync(Event(first), firstBinding, CancellationToken.None)).Status);
        Assert.Equal(GenUiActionStatus.Completed, (await router.RouteAsync(Event(second), secondBinding, CancellationToken.None)).Status);
        Assert.True(runtime.ReleaseOriginalInstanceActions(second));
    }

    [Fact]
    public void Static_custom_document_has_its_own_exact_issued_release_receipt_without_any_action_registration()
    {
        var instances = new GenUiInstanceStore(); var local = new GenUiLocalActionRegistry();
        var runtime = new CustomTemplateRuntime(local, instances);
        var document = runtime.Create(Guid.NewGuid(), "assistants", new Dictionary<string, JsonElement>());
        GenerativeUiContractValidator.ValidateAndThrow(document);
        instances.Register(document);
        Assert.Empty(document.Root.Actions);
        Assert.False(runtime.ReleaseOriginalInstanceActions(document with { }));
        Assert.True(runtime.ReleaseOriginalInstanceActions(document));
        Assert.True(runtime.ReleaseOriginalInstanceActions(document));
        Assert.Same(document, instances.TryGet(document.Origin.InstanceId));
    }

    private static IReadOnlyDictionary<string, JsonElement> Inputs(string value) => new Dictionary<string, JsonElement>
    {
        ["components"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { id = "value", type = "HavenText", props = new { text = "before" } },
            new { id = "button", type = "HavenButton", props = new { label = "Set" },
                actions = new[] { new { id = "set", patches = new[] { new { target = "value", path = "text", value } } } } }
        })
    };
    private static GenUiComponent Find(GenUiComponent root, string id) => root.ComponentId == id ? root :
        root.Children.Select(child => FindOrNull(child, id)).First(found => found is not null)!;
    private static GenUiComponent? FindOrNull(GenUiComponent root, string id) => root.ComponentId == id ? root :
        root.Children.Select(child => FindOrNull(child, id)).FirstOrDefault(found => found is not null);
    private static GenUiEvent Event(GenUiDocument document) => new(Guid.NewGuid(), GenUiEventType.ActionInvoked,
        DateTimeOffset.UtcNow, document.Origin, "button", "set", null, null, null,
        JsonSerializer.SerializeToElement(new { }), GenUiEventSource.User, "Actual declarative local action.");
}
