using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("excluded")]
    [InlineData("denied")]
    [InlineData("unknown")]
    [InlineData("retrieval-zero")]
    [InlineData("outside-item-limit")]
    public async Task Space_graph_context_activation_rejects_unusable_candidate_sources_without_displacing_active_or_saved_draft(string defect)
    {
        var token = TestContext.Current.CancellationToken;
        using var files = new SpacePolicyFiles();
        var current = await files.Registry.CreateAsync("Context graph activation", cancellationToken: token);
        current = await files.Registry.SetLayoutAsync(current.Id, UserMessageLayout(3), token);
        var reference = GraphContextReference();
        var configured = defect switch
        {
            "excluded" => reference with { IncludedAutomatically = false },
            "denied" => reference with { Permission = SpaceContextPermission.Denied },
            "unknown" => reference with { Permission = SpaceContextPermission.Unknown },
            _ => reference
        };
        var draft = SpaceConfigurationDocument.From(current) with
        {
            LayoutDocument = CapturedContextLayout(reference.ContextId),
            ContextReferences = defect == "missing" ? [] : defect == "outside-item-limit"
                ? [GraphContextReference() with { AddedAt = DateTimeOffset.UnixEpoch }, configured] : [configured],
            ContextPolicy = defect == "retrieval-zero" ? new SpaceContextPolicy(MaximumRetrievedItems: 0)
                : defect == "outside-item-limit" ? new SpaceContextPolicy(MaximumRetrievedItems: 1) : current.ContextPolicy
        };
        var saved = await files.Registry.SaveDraftAsync(current.Id, draft, current.Revision,
            current.DraftConfigurationRevision, current.ActiveConfigurationRevision, token);
        var before = JsonSerializer.Serialize(saved);
        var error = await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => files.Registry.ActivateDraftAsync(saved.Id,
            saved.Revision, saved.DraftConfigurationRevision, saved.ActiveConfigurationRevision, token));
        Assert.Equal("SPACE_USER_MESSAGE_GRAPH_INVALID", error.Code);
        var after = await files.Registry.ReadExistingAsync(saved.Id, token);
        Assert.Equal(before, JsonSerializer.Serialize(after));
        Assert.Equal(current.LayoutDocument!.GraphId, after!.LayoutDocument!.GraphId);
        Assert.Equal(draft.LayoutDocument!.GraphId, after.DraftConfiguration!.LayoutDocument!.GraphId);
        files.Passed = true;
    }

    [Theory]
    [InlineData(SpaceContextPermission.Read)]
    [InlineData(SpaceContextPermission.ReadWrite)]
    public async Task Space_graph_context_activation_publishes_valid_read_intent_once_without_claiming_a_body_read_or_permission_grant(SpaceContextPermission permission)
    {
        var token = TestContext.Current.CancellationToken;
        using var files = new SpacePolicyFiles();
        var current = await files.Registry.CreateAsync("Valid context graph", cancellationToken: token);
        var reference = GraphContextReference() with { Permission = permission };
        var layout = CapturedContextLayout(reference.ContextId);
        var draft = SpaceConfigurationDocument.From(current) with { ContextReferences = [reference], LayoutDocument = layout };
        var saved = await files.Registry.SaveDraftAsync(current.Id, draft, current.Revision,
            current.DraftConfigurationRevision, current.ActiveConfigurationRevision, token);
        var active = await files.Registry.ActivateDraftAsync(saved.Id, saved.Revision,
            saved.DraftConfigurationRevision, saved.ActiveConfigurationRevision, token);
        Assert.Equal(current.Revision + 1, active.Revision);
        Assert.Equal(current.ActiveConfigurationRevision + 1, active.ActiveConfigurationRevision);
        Assert.Null(active.DraftConfiguration); Assert.Equal(SpaceLayoutRevisionState.Active, active.LayoutDocument!.State);
        Assert.Equal(reference, Assert.Single(active.ContextReferences!));
        var policy = await new SpacePolicyResolver(files.Registry).ResolveAsync(active.Id, token);
        var graph = SpaceUserMessageGraph.Compile(active.Id, active.Revision, active.LayoutDocument);
        graph.ValidateContextDependencies(policy);
        Assert.Equal(reference.ContextId, Assert.Single(graph.ReferencedContextIds));
        // Publishing read intent supplies no original body witness to this pure preview.
        await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => graph.ExecuteAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User, "sample", null, null, null, DateTimeOffset.UtcNow), token));
        files.Passed = true;
    }

    [Theory]
    [InlineData("read")]
    [InlineData("inheritance-off")]
    [InlineData("excluded-child")]
    [InlineData("denied-child")]
    [InlineData("unknown-child")]
    public async Task Space_graph_context_activation_uses_effective_active_ancestry_and_nearest_source_intent(string intent)
    {
        var token = TestContext.Current.CancellationToken;
        using var files = new SpacePolicyFiles();
        var reference = GraphContextReference();
        var parent = await files.Registry.CreateAsync("Context parent", cancellationToken: token);
        parent = await files.Registry.UpdateAsync(parent with { ContextReferences = [reference] }, parent.Revision, token);
        var child = await files.Registry.CreateAsync("Context child", description: null, parentSpaceId: parent.Id, cancellationToken: token);
        var childReference = intent switch
        {
            "excluded-child" => reference with { IncludedAutomatically = false },
            "denied-child" => reference with { Permission = SpaceContextPermission.Denied },
            "unknown-child" => reference with { Permission = SpaceContextPermission.Unknown },
            _ => null
        };
        var draft = SpaceConfigurationDocument.From(child) with
        {
            Inheritance = new SpaceInheritancePolicy(Sources: intent != "inheritance-off"),
            ContextReferences = childReference is null ? [] : [childReference],
            LayoutDocument = CapturedContextLayout(reference.ContextId)
        };
        var saved = await files.Registry.SaveDraftAsync(child.Id, draft, child.Revision,
            child.DraftConfigurationRevision, child.ActiveConfigurationRevision, token);
        var before = JsonSerializer.Serialize(saved);
        var validator = new GraphContextReadOnlyActivationValidator(files.Registry);
        if (intent == "read")
        {
            var active = await files.Registry.ActivateDraftAsync(saved.Id, saved.Revision,
                saved.DraftConfigurationRevision, saved.ActiveConfigurationRevision, validator, action => action(), _ => { }, token);
            Assert.Empty(active.ContextReferences!); // Inheritance does not clone the parent source into the child.
            var effective = await new SpacePolicyResolver(files.Registry).ResolveAsync(active.Id, token);
            Assert.Equal(reference.ContextId, Assert.Single(effective.Sources).ContextId);
            SpaceUserMessageGraph.Compile(active.Id, active.Revision, active.LayoutDocument!).ValidateContextDependencies(effective);
        }
        else
        {
            var error = await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => files.Registry.ActivateDraftAsync(saved.Id,
                saved.Revision, saved.DraftConfigurationRevision, saved.ActiveConfigurationRevision, validator, action => action(), _ => { }, token));
            Assert.Equal("SPACE_USER_MESSAGE_GRAPH_INVALID", error.Code);
            Assert.Equal(before, JsonSerializer.Serialize(await files.Registry.ReadExistingAsync(saved.Id, token)));
        }
        files.Passed = true;
    }

    [Fact]
    public async Task Space_graph_context_activation_rechecks_parent_revision_and_ignores_unpublished_parent_sources()
    {
        var token = TestContext.Current.CancellationToken;
        using var files = new SpacePolicyFiles();
        var reference = GraphContextReference();
        var parent = await files.Registry.CreateAsync("Active source parent", cancellationToken: token);
        var child = await files.Registry.CreateAsync("Inherited source child", description: null, parentSpaceId: parent.Id, cancellationToken: token);
        parent = await files.Registry.SaveDraftAsync(parent.Id, SpaceConfigurationDocument.From(parent) with { ContextReferences = [reference] },
            parent.Revision, parent.DraftConfigurationRevision, parent.ActiveConfigurationRevision, token);
        var draft = SpaceConfigurationDocument.From(child) with
        { Inheritance = new SpaceInheritancePolicy(Sources: true), LayoutDocument = CapturedContextLayout(reference.ContextId) };
        var saved = await files.Registry.SaveDraftAsync(child.Id, draft, child.Revision,
            child.DraftConfigurationRevision, child.ActiveConfigurationRevision, token);
        // External metadata validation deliberately returns a current ancestry receipt
        // without checking graph dependencies; the authoritative registry must still refuse.
        var metadataOnly = new GraphContextReadOnlyActivationValidator(files.Registry);
        await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => files.Registry.ActivateDraftAsync(saved.Id,
            saved.Revision, saved.DraftConfigurationRevision, saved.ActiveConfigurationRevision, metadataOnly, action => action(), _ => { }, token));
        parent = await files.Registry.UpdateAsync(parent with { ContextReferences = [reference] }, parent.Revision, token);
        var racing = new GraphContextReadOnlyActivationValidator(files.Registry, async () =>
        {
            _ = await files.Registry.UpdateAsync(parent with { ContextReferences = [] }, parent.Revision, token);
        });
        var before = JsonSerializer.Serialize(await files.Registry.ReadExistingAsync(saved.Id, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => files.Registry.ActivateDraftAsync(saved.Id,
            saved.Revision, saved.DraftConfigurationRevision, saved.ActiveConfigurationRevision, racing, action => action(), _ => { }, token));
        Assert.Equal(before, JsonSerializer.Serialize(await files.Registry.ReadExistingAsync(saved.Id, token)));
        files.Passed = true;
    }

    [Theory]
    [InlineData(SpaceContextPermission.Denied)]
    [InlineData(SpaceContextPermission.Unknown)]
    public async Task Space_graph_context_runtime_policy_does_not_resurrect_an_inherited_source_after_child_exclusion(SpaceContextPermission permission)
    {
        var token = TestContext.Current.CancellationToken;
        using var files = new SpacePolicyFiles();
        var reference = GraphContextReference();
        var parent = await files.Registry.CreateAsync("Runtime context parent", cancellationToken: token);
        parent = await files.Registry.UpdateAsync(parent with { ContextReferences = [reference] }, parent.Revision, token);
        var child = await files.Registry.CreateAsync("Runtime context child", description: null, parentSpaceId: parent.Id, cancellationToken: token);
        child = await files.Registry.UpdateAsync(child with
        {
            Inheritance = new SpaceInheritancePolicy(Sources: true),
            ContextReferences = [reference with { Permission = permission }]
        }, child.Revision, token);
        var originals = new List<Task>();
        var snapshot = await new SpacePolicyResolver(files.Registry).ResolveExistingAsync(child.Id, action => action(), originals.Add, token);
        Assert.Empty(snapshot.Policy.Sources);
        Assert.All(originals, original => Assert.True(original.IsCompletedSuccessfully));
        var graph = SpaceUserMessageGraph.Compile(child.Id, child.Revision, CapturedContextLayout(reference.ContextId));
        Assert.Throws<SpaceRequestPolicyBlockedException>(() => graph.ValidateContextDependencies(snapshot.Policy));
        files.Passed = true;
    }

    /// <summary>Actual read-only ancestry resolver, with an explicit optional race injection.
    /// It issues no body witness, resource grant, model dispatch or alternate registry state.</summary>
    private sealed class GraphContextReadOnlyActivationValidator(SpaceRegistry registry, Func<Task>? afterRead = null) : ISpaceDraftActivationValidator
    {
        public async Task<SpaceDraftActivationValidation> ValidateAsync(SpaceDefinition current, SpaceConfigurationDocument draft,
            Action<Action> acquireOriginal, Action<Task> retainOriginal, CancellationToken cancellationToken = default)
        {
            var resolved = await new SpacePolicyResolver(registry).ResolveDraftAsync(current, draft, acquireOriginal, retainOriginal, cancellationToken);
            if (afterRead is not null) await afterRead();
            return new(current.Id, current.Revision, current.DraftConfigurationRevision, current.ActiveConfigurationRevision, resolved.Ancestry);
        }
    }
}
