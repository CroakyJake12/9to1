using Haven.Application;
using Haven.Core;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.Desktop.Views.Pages.WorkspaceEditor;
using HavenOS.Apps.Spaces.Builder;

namespace Haven.Desktop.Tests;

public sealed class WorkspaceEditorLanguageServerTests
{
    [Fact]
    public void Completion_prefers_protocol_replace_range_and_applies_additional_edits()
    {
        const string original = "using X;\nConso";
        var completion = new LanguageServerCompletion(
            "Console",
            "System.Console",
            "Console",
            new CodeRange(new CodePosition(1, 5), new CodePosition(1, 5)),
            new CodeRange(new CodePosition(1, 0), new CodePosition(1, 5)),
            [new LanguageServerTextEdit(new CodeRange(new CodePosition(0, 6), new CodePosition(0, 7)), "System")]);

        var updated = WorkspaceEditorPage.ApplyCompletionToText(original, original.Length, completion);

        Assert.Equal("using System;\nConsole", updated);
    }

    [Fact]
    public void Completion_rejects_overlapping_protocol_edits()
    {
        const string original = "alpha beta";
        var completion = new LanguageServerCompletion(
            "replacement", null, "replacement", null,
            new CodeRange(new CodePosition(0, 0), new CodePosition(0, 5)),
            [new LanguageServerTextEdit(new CodeRange(new CodePosition(0, 3), new CodePosition(0, 6)), "other")]);

        Assert.Throws<InvalidOperationException>(() => WorkspaceEditorPage.ApplyCompletionToText(original, 5, completion));
    }
}

public sealed class SpaceLayoutPersistenceTests
{
    [AvaloniaFact]
    public async Task Space_builder_authored_CUI_loads_and_reports_missing_capability_catalog()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("CUI host smoke");
        var page = new SpaceBuilderPage(registry, space.Id, new ReadyReadiness());
        try
        {
            await page.ActivateAsync();
            var show = page.Show;
            Assert.NotNull(show);
            Assert.Equal(CuiSceneAvailabilityState.Ready, (await show!).State);
            Assert.DoesNotContain(page.Host.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
            Assert.Contains("Installed Skills/Plugins are unavailable", page.State.StatusMessage);
        }
        finally
        {
            page.RequestRetirement();
            await page.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Space_builder_revalidates_a_skill_removed_after_the_catalog_was_shown()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Stale Skill selection");
        var installedPackage = CreateInstalledSkillPackage();
        var repository = new MutableExtensionRepository([installedPackage]);
        var extensions = new ExtensionManager(repository, null!, new ExtensionManifestValidator(), null!, null!);
        var page = new SpaceBuilderPage(registry, space.Id, new ReadyReadiness(), extensions: extensions);
        var window = new Window { Content = page, Width = 1280, Height = 900 };
        try
        {
            window.Show();
            await page.ActivateAsync();
            Assert.Equal(CuiSceneAvailabilityState.Ready, (await page.Show!).State);
            Assert.Contains("Loaded 1 installed Skills", page.State.StatusMessage);

            repository.InstalledPackages = [installedPackage with
                { Manifest = installedPackage.Manifest with { PackageType = ExtensionPackageType.Skill } }];
            var pluginAttach = Assert.Single(page.Host.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "space-builder-attach-plugin"));
            pluginAttach.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await repository.RevalidationReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForStatusAsync(page, "no longer installed as a Plugin");
            Assert.Empty(page.State.Draft.CapabilityBindings);

            repository.InstalledPackages = [];
            var skillAttach = Assert.Single(page.Host.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "space-builder-attach-skill"));
            skillAttach.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await WaitForStatusAsync(page, "This Skill is no longer installed");

            Assert.Empty(page.State.Draft.CapabilityBindings);
            Assert.Empty(page.State.Draft.ContextReferences);
        }
        finally
        {
            page.RequestRetirement();
            await page.CloseAndDrainAsync();
            window.Close();
        }
    }

    [Fact]
    public async Task Space_layout_save_rejects_stale_editor_without_replacing_latest_layout()
    {
        var settings = new MemorySettingsStore();
        var firstRegistry = new SpaceRegistry(settings);
        var space = await firstRegistry.CreateAsync("Concurrent layout");
        var staleSnapshot = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.NotNull(staleSnapshot);

        var resourceId = "file:canonical-not-a-clone";
        var placedResource = new SpaceLayoutNode(Guid.NewGuid(), "Resource", "Canonical file")
        {
            X = 165,
            Y = 90,
            Metadata = new Dictionary<string, string> { ["resourceId"] = resourceId }
        };
        var latestLayout = new SpaceLayoutDocument([placedResource], []);
        await firstRegistry.SetLayoutAsync(space.Id, latestLayout, CancellationToken.None, expectedRevision: space.Revision);

        var error = await Assert.ThrowsAsync<SpaceRevisionConflictException>(() =>
            new SpaceRegistry(settings).SetLayoutAsync(space.Id, SpaceLayoutDocument.Empty,
                CancellationToken.None, expectedRevision: staleSnapshot!.Revision));
        Assert.Equal(space.Id, error.SpaceId);
        Assert.Equal(staleSnapshot.Revision, error.ExpectedRevision);
        Assert.True(error.ActualRevision > error.ExpectedRevision);

        var persisted = await new SpaceRegistry(settings).GetAsync(space.Id);
        var retained = Assert.Single(persisted!.LayoutDocument!.Nodes);
        Assert.Equal(placedResource.Id, retained.Id);
        Assert.Equal(165d, retained.X);
        Assert.Equal(90d, retained.Y);
        Assert.Equal(resourceId, retained.Metadata["resourceId"]);
    }

    [Fact]
    public async Task Space_layout_rejects_invalid_graphs_without_replacing_valid_maximum_layout()
    {
        var settings = new MemorySettingsStore();
        var registry = new SpaceRegistry(settings);
        var space = await registry.CreateAsync("Invalid layout");
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SetLayoutAsync(
            SpaceRegistry.StudySpaceId, SpaceLayoutDocument.Empty, CancellationToken.None, expectedRevision: 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            registry.SetLayoutAsync(space.Id, SpaceLayoutDocument.Empty, CancellationToken.None, expectedRevision: 0));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([], []) { SchemaVersion = SpaceLayoutDocument.CurrentSchemaVersion + 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([], []) { ConnectionProfileId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([], []) { State = (SpaceLayoutRevisionState)999 }));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([], []) { Revision = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([new SpaceLayoutNode(Guid.NewGuid(), "Logic", "Invalid schema")
            { TypeId = " ", SchemaVersion = 1 }], [])));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([], []) { Groups = [new SpaceLayoutGroup(Guid.Empty, "", [])] }));
        var duplicatedMember = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([], [])
            {
                Groups = [new SpaceLayoutGroup(Guid.NewGuid(), "Duplicate members", [duplicatedMember, duplicatedMember])]
            }));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([], [])
            {
                Groups = [new SpaceLayoutGroup(Guid.NewGuid(), "Oversized group",
                    Enumerable.Range(0, SpaceLayoutDocument.MaximumNodes + 1).Select(_ => Guid.NewGuid()).ToArray())]
            }));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            new SpaceLayoutDocument([], []) { Comments = [new SpaceLayoutComment(Guid.NewGuid(), "bad geometry", 0, 0, double.NaN)] }));
        var maximumNodes = Enumerable.Range(0, SpaceLayoutDocument.MaximumNodes)
            .Select(index => new SpaceLayoutNode(Guid.NewGuid(), "Logic", $"Node {index}"))
            .ToArray();
        await registry.SetLayoutAsync(space.Id, new SpaceLayoutDocument(maximumNodes, []));
        var duplicateId = Guid.NewGuid();
        var invalid = new SpaceLayoutDocument(
        [
            new SpaceLayoutNode(duplicateId, "Resource", "First"),
            new SpaceLayoutNode(duplicateId, "Resource", "Duplicate")
        ], []);

        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id, invalid));
        var oversized = new SpaceLayoutDocument(
            Enumerable.Range(0, SpaceLayoutDocument.MaximumNodes + 1)
                .Select(index => new SpaceLayoutNode(Guid.NewGuid(), "Logic", $"Node {index}"))
                .ToArray(), []);
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id, oversized));
        var retainedNodes = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.NotNull(retainedNodes);
        Assert.Equal(maximumNodes.Select(node => node.Id), retainedNodes.LayoutDocument!.Nodes.Select(node => node.Id));
        const int outputCount = 9;
        const int inputCount = 250;
        var outputs = Enumerable.Range(0, outputCount)
            .Select(index => new SpaceLayoutNode(Guid.NewGuid(), "Logic", $"Source {index}")
            {
                Ports = [new SpaceLayoutPort("out", "Out", SpaceLayoutPortDirection.Output)]
            }).ToArray();
        var inputs = Enumerable.Range(0, inputCount)
            .Select(index => new SpaceLayoutNode(Guid.NewGuid(), "Logic", $"Target {index}")
            {
                Ports = [new SpaceLayoutPort("in", "In", SpaceLayoutPortDirection.Input)]
            }).ToArray();
        SpaceLayoutDocument EdgeLayout(int edgeCount) => new(
            [.. outputs, .. inputs],
            Enumerable.Range(0, edgeCount)
                .Select(index => new SpaceLayoutEdge(Guid.NewGuid(), outputs[index / inputCount].Id, "out",
                    inputs[index % inputCount].Id, "in"))
                .ToArray());
        var maximumEdges = EdgeLayout(SpaceLayoutDocument.MaximumEdges);
        await registry.SetLayoutAsync(space.Id, maximumEdges);
        var persistedMaximumEdges = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.NotNull(persistedMaximumEdges);
        Assert.Equal(SpaceLayoutDocument.MaximumEdges, persistedMaximumEdges.LayoutDocument!.Edges.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id,
            EdgeLayout(SpaceLayoutDocument.MaximumEdges + 1)));

        var persisted = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.NotNull(persisted);
        var persistedLayout = persisted.LayoutDocument;
        Assert.NotNull(persistedLayout);
        Assert.Equal(SpaceLayoutDocument.MaximumEdges, persistedLayout.Edges.Count);
        Assert.Equal(maximumEdges.Nodes.Select(node => node.Id), persistedLayout.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void Space_layout_adapter_round_trips_graph_and_resource_identity()
    {
        Assert.NotEqual(SpaceLayoutEditorAdapter.ToEditor(null).GraphId, SpaceLayoutEditorAdapter.ToEditor(null).GraphId);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<SpaceLayoutDocument>("{\"Nodes\":[],\"Edges\":[]}");
        Assert.NotNull(legacy);
        Assert.Equal(Guid.Empty, legacy.GraphId);
        Assert.Equal(SpaceLayoutDocument.CurrentSchemaVersion, legacy.SchemaVersion);
        Assert.Empty(legacy.Groups);
        Assert.Empty(legacy.Comments);
        var legacyEditor = SpaceLayoutEditorAdapter.ToEditor(legacy);
        Assert.NotEqual(Guid.Empty, legacyEditor.GraphId);
        Assert.Equal(legacyEditor.GraphId, SpaceLayoutEditorAdapter.FromEditor(legacyEditor).GraphId);
        var untyped = SpaceLayoutEditorAdapter.ToEditor(new SpaceLayoutDocument(
            [new SpaceLayoutNode(Guid.NewGuid(), "Logic", "Unregistered fixture")], []));
        var status = SpaceLayoutEditorPage.LayoutSummary("Loaded", untyped);
        Assert.Contains("Untyped nodes are not executable", status);
        Assert.Contains("does not activate or execute graphs", status);
        var canonicalResourceId = "worker28.fixture";
        var graphId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var commentId = Guid.NewGuid();
        var original = new SpaceLayoutDocument(
        [
            new SpaceLayoutNode(nodeId, "Resource", "Reviewed plugin")
            {
                TypeId = "fixture.unregistered-node",
                SchemaVersion = 2,
                X = 412.5,
                Y = 83.25,
                Metadata = new Dictionary<string, string> { ["resourceId"] = canonicalResourceId },
                Ports = [new SpaceLayoutPort("out", "Resource", SpaceLayoutPortDirection.Output, "resource")]
            }
        ], [])
        {
            GraphId = graphId,
            Revision = 7,
            State = SpaceLayoutRevisionState.Active,
            ConnectionProfileId = "fixture.unregistered-profile",
            Groups = [new SpaceLayoutGroup(groupId, "Reviewed resources", [nodeId], "accent")],
            Comments = [new SpaceLayoutComment(commentId, "Keep this placement", 30, 44, 240, 90)]
        };

        var restored = SpaceLayoutEditorAdapter.FromEditor(SpaceLayoutEditorAdapter.ToEditor(original));

        var node = Assert.Single(restored.Nodes);
        Assert.Contains("1 groups and 1 comments", SpaceLayoutEditorPage.LayoutSummary("Loaded", SpaceLayoutEditorAdapter.ToEditor(original)));
        Assert.Equal(original.Nodes[0].Id, node.Id);
        Assert.Equal("fixture.unregistered-node", node.TypeId);
        Assert.Equal(2, node.SchemaVersion);
        Assert.Equal(graphId, restored.GraphId);
        Assert.Equal(SpaceLayoutDocument.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(7L, restored.Revision);
        Assert.Equal(SpaceLayoutRevisionState.Active, restored.State);
        Assert.Equal("fixture.unregistered-profile", restored.ConnectionProfileId);
        Assert.Equal(groupId, Assert.Single(restored.Groups).Id);
        Assert.Equal(nodeId, Assert.Single(restored.Groups[0].NodeIds));
        Assert.Equal("accent", restored.Groups[0].Color);
        var comment = Assert.Single(restored.Comments);
        Assert.Equal(commentId, comment.Id);
        Assert.Equal("Keep this placement", comment.Text);
        Assert.Equal(30d, comment.X);
        Assert.Equal(44d, comment.Y);
        Assert.Equal(240d, comment.Width);
        Assert.Equal(90d, comment.Height);
        Assert.Equal(412.5, node.X);
        Assert.Equal(83.25, node.Y);
        Assert.Equal(canonicalResourceId, node.Metadata["resourceId"]);
        Assert.Equal("resource", Assert.Single(node.Ports).DataType);

        var jsonRestored = System.Text.Json.JsonSerializer.Deserialize<SpaceLayoutDocument>(
            System.Text.Json.JsonSerializer.Serialize(original));
        Assert.NotNull(jsonRestored);
        Assert.Equal(graphId, jsonRestored.GraphId);
        Assert.Equal(groupId, Assert.Single(jsonRestored.Groups).Id);
        Assert.Equal(commentId, Assert.Single(jsonRestored.Comments).Id);
    }

    [Fact]
    public void Space_layout_editor_refuses_to_replace_an_active_layout_without_publish_support()
    {
        Assert.False(SpaceLayoutEditorPage.CanSaveLayout(SpaceLayoutRevisionState.Active));
        Assert.True(SpaceLayoutEditorPage.CanSaveLayout(SpaceLayoutRevisionState.Draft));
        Assert.True(SpaceLayoutEditorPage.CanSaveLayout(null));
    }

    [Fact]
    public async Task Space_layout_save_detaches_mutable_caller_owned_placement_data()
    {
        var settings = new MemorySettingsStore();
        var registry = new SpaceRegistry(settings);
        var space = await registry.CreateAsync("Snapshot layout");
        var metadata = new Dictionary<string, string> { ["resourceId"] = "file:canonical" };
        var placedNode = new SpaceLayoutNode(Guid.NewGuid(), "Resource", "Original")
        {
            X = 32,
            Y = 64,
            Metadata = metadata
        };
        var callerNodes = new List<SpaceLayoutNode> { placedNode };
        var groupId = Guid.NewGuid();
        var callerGroupMembers = new List<Guid> { placedNode.Id };
        settings.HoldNextRegistryRead = true;
        var save = registry.SetLayoutAsync(space.Id, new SpaceLayoutDocument(callerNodes, [])
        {
            Groups = [new SpaceLayoutGroup(groupId, "Resource placements", callerGroupMembers)]
        }, CancellationToken.None);
        try
        {
            await settings.RegistryReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            metadata["resourceId"] = "file:changed-after-call";
            callerNodes[0] = placedNode with { Title = "Changed after call" };
            callerGroupMembers.Clear();
        }
        finally
        {
            settings.ReleaseRegistryRead.TrySetResult();
            await save.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var persisted = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.NotNull(persisted);
        var persistedLayout = persisted.LayoutDocument;
        Assert.NotNull(persistedLayout);
        Assert.NotEqual(Guid.Empty, persistedLayout.GraphId);
        var savedGraphId = persistedLayout.GraphId;
        var savedNode = Assert.Single(persistedLayout.Nodes);
        Assert.Equal("Original", savedNode.Title);
        Assert.Equal("file:canonical", savedNode.Metadata["resourceId"]);
        Assert.Equal(placedNode.Id, Assert.Single(persistedLayout.Groups.Single(group => group.Id == groupId).NodeIds));
        Assert.IsType<Dictionary<string, string>>(savedNode.Metadata)["resourceId"] = "mutated-read-result";
        var restoredAgain = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.Equal(savedGraphId, restoredAgain!.LayoutDocument!.GraphId);
        Assert.Equal("file:canonical", Assert.Single(restoredAgain!.LayoutDocument!.Nodes).Metadata["resourceId"]);
    }

    [Fact]
    public async Task Space_registry_persists_a_graph_id_migration_for_legacy_saved_layouts()
    {
        var settings = new MemorySettingsStore();
        var registry = new SpaceRegistry(settings);
        var space = await registry.CreateAsync("Legacy graph identity");
        var secondSpace = await registry.CreateAsync("Second legacy graph");
        var currentSpaces = await registry.GetAllAsync(includeArchived: true);
        var legacyLayout = System.Text.Json.JsonSerializer.Deserialize<SpaceLayoutDocument>("{\"Nodes\":[],\"Edges\":[]}");
        Assert.NotNull(legacyLayout);
        Assert.Equal(Guid.Empty, legacyLayout.GraphId);
        var legacySpace = space with { LayoutDocument = legacyLayout };
        var secondLegacySpace = secondSpace with { LayoutDocument = legacyLayout };
        var legacySpaces = currentSpaces.Select(item => item.Id == space.Id ? legacySpace :
            item.Id == secondSpace.Id ? secondLegacySpace : item).ToArray();
        settings.SeedJson("spaces.registry", System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = 3, // Current schema; only the missing GraphIDs should require reconciliation.
            Spaces = legacySpaces,
            Deletions = Array.Empty<object>()
        }));

        var readOnlySnapshot = await new SpaceRegistry(settings).ReadExistingAsync(space.Id);
        Assert.NotNull(readOnlySnapshot);
        var readOnlyLayout = readOnlySnapshot.LayoutDocument;
        Assert.NotNull(readOnlyLayout);
        Assert.NotEqual(Guid.Empty, readOnlyLayout.GraphId);
        Assert.True(settings.IsJson("spaces.registry"));
        var readOnlyGraphId = readOnlyLayout.GraphId;
        var readOnlyPage = await new SpaceRegistry(settings).ReadExistingPageAsync();
        var secondReadOnlyLayout = Assert.Single(readOnlyPage.Where(item => item.Id == secondSpace.Id)).LayoutDocument;
        Assert.NotNull(secondReadOnlyLayout);
        Assert.NotEqual(readOnlyGraphId, secondReadOnlyLayout.GraphId);
        Assert.True(settings.IsJson("spaces.registry"));
        var migrated = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.NotNull(migrated);
        Assert.False(settings.IsJson("spaces.registry"));
        var migratedLayout = migrated.LayoutDocument;
        Assert.NotNull(migratedLayout);
        Assert.Equal(readOnlyGraphId, migratedLayout.GraphId);
        var graphId = migratedLayout.GraphId;

        var reread = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.NotNull(reread);
        Assert.NotNull(reread.LayoutDocument);
        Assert.Equal(graphId, reread.LayoutDocument.GraphId);
        var rereadSecond = await new SpaceRegistry(settings).GetAsync(secondSpace.Id);
        Assert.Equal(readOnlyPage.Single(item => item.Id == secondSpace.Id).LayoutDocument!.GraphId,
            rereadSecond!.LayoutDocument!.GraphId);
    }

    [Fact]
    public async Task Space_builder_conflict_keeps_draft_until_user_explicitly_discards_and_reloads()
    {
        var settings = new MemorySettingsStore();
        var registry = new SpaceRegistry(settings);
        var original = await registry.CreateAsync("Conflict Space");
        var builder = new SpaceBuilderController(registry, original.Id);
        Assert.True(await builder.LoadAsync());
        Assert.True(builder.SetField(SpaceBuilderField.Name, "My unsaved name"));
        var binding = new SpaceCapabilityBinding("worker28.fixture", SpaceCapabilityKind.Plugin,
            SpaceCapabilityOverride.Inherit, "2.1");
        Assert.True(builder.AttachCapability(binding));

        await registry.UpdateAsync(original with { Description = "Newer external description" }, original.Revision);

        Assert.False(await builder.SaveAsync());
        Assert.True(builder.State.HasConflict);
        Assert.Equal("My unsaved name", builder.State.Draft.Name);
        Assert.Contains(binding, builder.State.Draft.CapabilityBindings);
        var persisted = await new SpaceRegistry(settings).GetAsync(original.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Conflict Space", persisted.Name);
        Assert.Equal("Newer external description", persisted.Description);

        Assert.True(await builder.DiscardChangesAsync());
        Assert.False(builder.State.HasConflict);
        Assert.Equal("Conflict Space", builder.State.Draft.Name);
    }

    [Fact]
    public async Task Space_builder_restores_canonical_source_and_preserves_layout_resource_placement()
    {
        var settings = new MemorySettingsStore();
        var registry = new SpaceRegistry(settings);
        var space = await registry.CreateAsync("Canonical source");
        var placedNode = new SpaceLayoutNode(Guid.NewGuid(), "Resource", "Canonical reference")
        {
            TypeId = "fixture.unregistered-node",
            X = 245,
            Y = 135,
            Metadata = new Dictionary<string, string> { ["resourceId"] = "file:stable-entity" }
        };
        var graphId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var commentId = Guid.NewGuid();
        await registry.SetLayoutAsync(space.Id, new SpaceLayoutDocument([placedNode], [])
        {
            GraphId = graphId,
            Revision = 4,
            State = SpaceLayoutRevisionState.Active,
            ConnectionProfileId = "fixture.unregistered-profile",
            Groups = [new SpaceLayoutGroup(groupId, "Canonical resources", [placedNode.Id])],
            Comments = [new SpaceLayoutComment(commentId, "Placement note", 12, 24)]
        });
        var activeSnapshot = await registry.GetAsync(space.Id);
        Assert.NotNull(activeSnapshot);
        Assert.NotNull(activeSnapshot.LayoutDocument);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SetLayoutAsync(space.Id,
            SpaceLayoutDocument.Empty, CancellationToken.None, activeSnapshot.Revision));
        var attemptedDraftUpdate = await registry.UpdateAsync(
            activeSnapshot with { LayoutDocument = SpaceLayoutDocument.Empty }, activeSnapshot.Revision);
        Assert.Equal(graphId, attemptedDraftUpdate.LayoutDocument!.GraphId);
        var builder = new SpaceBuilderController(registry, space.Id);
        Assert.True(await builder.LoadAsync());
        var entityId = Guid.NewGuid().ToString("D");
        var originalContextId = Guid.NewGuid();
        var original = new SpaceContextReference(originalContextId, SpaceContextReferenceKind.BoardPage,
            "boards", entityId, "7", SpaceContextPermission.Read, SpaceContextIndexState.Ready,
            true, DateTimeOffset.UnixEpoch);
        Assert.True(builder.AttachContextReference(original, builder.State.DraftRevision!.Value));

        var refreshed = original with { ContextId = Guid.NewGuid(), RevisionToken = "8" };
        Assert.True(builder.AttachContextReference(refreshed, builder.State.DraftRevision!.Value));
        Assert.Equal(originalContextId, Assert.Single(builder.State.Draft.ContextReferences).ContextId);
        Assert.True(await builder.SaveAsync());

        var restored = new SpaceBuilderController(new SpaceRegistry(settings), space.Id);
        Assert.True(await restored.LoadAsync());
        Assert.Equal(space.Id, restored.State.SpaceId);
        var reference = Assert.Single(restored.State.Draft.ContextReferences);
        Assert.Equal(originalContextId, reference.ContextId);
        Assert.Equal(entityId, reference.CanonicalEntityId);
        Assert.Equal("8", reference.RevisionToken);
        Assert.Equal("boards", reference.OwnerAppId);
        Assert.Equal(SpaceContextPermission.Read, reference.Permission);
        Assert.Equal(SpaceContextIndexState.Ready, reference.IndexState);
        Assert.True(reference.IncludedAutomatically);
        Assert.Equal(DateTimeOffset.UnixEpoch, reference.AddedAt);
        var persistedSpace = await new SpaceRegistry(settings).GetAsync(space.Id);
        Assert.NotNull(persistedSpace);
        var persistedLayout = persistedSpace.LayoutDocument;
        Assert.NotNull(persistedLayout);
        var persistedNode = Assert.Single(persistedLayout.Nodes);
        Assert.Equal(placedNode.Id, persistedNode.Id);
        Assert.Equal(245d, persistedNode.X);
        Assert.Equal(135d, persistedNode.Y);
        Assert.Equal("file:stable-entity", persistedNode.Metadata["resourceId"]);
        Assert.Equal("fixture.unregistered-node", persistedNode.TypeId);
        Assert.Equal(graphId, persistedLayout.GraphId);
        Assert.Equal(SpaceLayoutDocument.CurrentSchemaVersion, persistedLayout.SchemaVersion);
        Assert.Equal(4L, persistedLayout.Revision);
        Assert.Equal(SpaceLayoutRevisionState.Active, persistedLayout.State);
        Assert.Equal("fixture.unregistered-profile", persistedLayout.ConnectionProfileId);
        Assert.Equal(groupId, Assert.Single(persistedLayout.Groups).Id);
        Assert.Equal(placedNode.Id, Assert.Single(persistedLayout.Groups[0].NodeIds));
        var persistedComment = Assert.Single(persistedLayout.Comments);
        Assert.Equal(commentId, persistedComment.Id);
        Assert.Equal("Placement note", persistedComment.Text);
        Assert.Equal(12d, persistedComment.X);
        Assert.Equal(24d, persistedComment.Y);
    }

    [Fact]
    public async Task Space_builder_rejects_noncanonical_or_write_escalating_source_references()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Source validation");
        var builder = new SpaceBuilderController(registry, space.Id);
        Assert.True(await builder.LoadAsync());
        var revision = builder.State.DraftRevision!.Value;
        var malformed = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.BoardPage,
            "boards", "not-a-canonical-guid", "1", SpaceContextPermission.Read,
            SpaceContextIndexState.Ready, true, DateTimeOffset.UnixEpoch);
        var escalation = malformed with { CanonicalEntityId = Guid.NewGuid().ToString("D"), Permission = SpaceContextPermission.ReadWrite };

        Assert.Throws<ArgumentException>(() => builder.AttachContextReference(malformed, revision));
        Assert.Throws<ArgumentException>(() => builder.AttachContextReference(escalation, revision));
        Assert.Empty(builder.State.Draft.ContextReferences);
    }

    private static InstalledExtensionPackage CreateInstalledSkillPackage()
    {
        var skill = new ExtensionSkillManifest("summary", "Summary skill", "Test fixture only.", "skills/summary.md", true);
        var manifest = new ExtensionPackageManifest(
            "worker28.fixture", "package", "Fixture package", ExtensionPackageType.PluginAndSkills,
            "1.0.0", "*", "Test fixture only.", "Tests", "Tests", null, null,
            ExtensionPermission.None, [], [], [skill], null);
        return new InstalledExtensionPackage(Guid.NewGuid(), Guid.NewGuid(), manifest, "package",
            ExtensionPermission.None, ExtensionInstallState.Enabled, true, false, "fixture-hash",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }

    private static async Task WaitForStatusAsync(SpaceBuilderPage page, string statusFragment)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (page.State.StatusMessage.Contains(statusFragment, StringComparison.Ordinal)) return;
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.Contains(statusFragment, page.State.StatusMessage);
    }

    private sealed class MutableExtensionRepository(IReadOnlyList<InstalledExtensionPackage> packages) : IExtensionRepository
    {
        private int _installedReads;
        public IReadOnlyList<InstalledExtensionPackage> InstalledPackages { get; set; } = packages;
        public TaskCompletionSource RevalidationReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<ExtensionSource>> GetSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExtensionSource>>([]);
        public Task UpsertSourceAsync(ExtensionSource source, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteSourceAsync(Guid sourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertInstalledAsync(InstalledExtensionPackage package, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteInstalledAsync(Guid packageId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<InstalledExtensionPackage>> GetInstalledAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _installedReads) == 3) RevalidationReadEntered.TrySetResult();
            return Task.FromResult(InstalledPackages);
        }
    }

    // Local sequential fixture only; it does not simulate production compare-exchange.
    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        // Deterministically queues one in-process registry mutation; this is not cross-instance CAS.
        public bool HoldNextRegistryRead { get; set; }
        public TaskCompletionSource RegistryReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRegistryRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (key == "spaces.registry" && HoldNextRegistryRead)
            {
                HoldNextRegistryRead = false;
                RegistryReadStarted.TrySetResult();
                await ReleaseRegistryRead.Task.WaitAsync(cancellationToken);
            }
            if (!_values.TryGetValue(key, out var value)) return null;
            if (value is string json && typeof(T) != typeof(string))
                return System.Text.Json.JsonSerializer.Deserialize<T>(json);
            return (T?)value;
        }

        public void SeedJson(string key, string json) => _values[key] = json;
        public bool IsJson(string key) => _values.TryGetValue(key, out var value) && value is string;

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ReadyReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "test-ready", "Headless CUI host fixture."));
    }
}
