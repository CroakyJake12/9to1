#if !ANDROID
using System.Collections;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

// Authored controls only. Uses the maintained actual Linux Home/SQLite rig and
// real original creator, schema catalogue and editor; no mock successful writer.
public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual Linux native custody fixture; installed platform acceptance remains separate.")]
    public Task Canonical_graph_CUI_edits_same_draft_with_typed_ports_and_preserves_saved_state_and_navigation_guards() => Run(async rig =>
    {
        var (source, _) = rig.CreateOriginalAutomationLibrary();
        var graph = new ActualGraphEditingLibrary(rig, Assert.IsType<CanonicalAutomationLibraryOriginalReadOwner>(source));
        var failures = new List<Exception>();
        try
        {
            using var app = new CancellationTokenSource(); using var native = new CancellationTokenSource();
            var window = new Window { Width = 1200, Height = 1000 }; window.Show();
            OriginalAutomationLibraryDesktopPage? page = null; var healthy = false;
            try
            {
                page = OriginalAutomationLibraryDesktopPage.BindOriginal(graph.Library, rig.Home.Profiles, window,
                    app.Token, native.Token, actual => page = actual, graph.Writer);
                window.Content = page; await rig.Keep(page.InitializeAsync(rig.Token));
                Assert.True(page.Bindings.TrySetValue("NewAutomationName", "Actual typed graph editor"));
                var creation = rig.Keep(page.Bindings.DispatchAsync("create-draft", null, rig.Token).AsTask());
                await graph.AcceptNext(creation, "automations.graph.save-draft");
                await graph.AcceptNext(creation, HomeCanonicalAutomationDefinitionWriteSource.CreateAction);
                await creation;
                await rig.Keep(page.Bindings.DispatchAsync("refresh", null, rig.Token).AsTask());
                var saved = Assert.Single(SavedLibraryRows(page));
                await rig.Keep(page.Bindings.DispatchAsync("select", saved.Target, rig.Token).AsTask());
                var homeBefore = await File.ReadAllBytesAsync(Path.Combine(rig.Root, "Home", "state.json"), rig.Token);
                var sqliteBefore = await rig.ReadOriginalAutomationDescriptors(source);
                await rig.Keep(page.Bindings.DispatchAsync("open-graph-editor", null, rig.Token).AsTask());
                Assert.True(BindingValue<bool>(page, "HasGraphEditor"));
                var trigger = DraftRow(page, "GraphEditorTypes", "Title", CanonicalAutomationCoreNodes.ManualType);
                var staleAdd = DraftTarget(page, trigger);
                await rig.Keep(page.Bindings.DispatchAsync("add-graph-node", staleAdd, rig.Token).AsTask());
                Assert.Throws<InvalidOperationException>(() => { _ = page.Bindings.DispatchAsync("add-graph-node", staleAdd, rig.Token); });
                var emit = DraftRow(page, "GraphEditorTypes", "Title", CanonicalAutomationCoreNodes.EmitType);
                await rig.Keep(page.Bindings.DispatchAsync("add-graph-node", DraftTarget(page, emit), rig.Token).AsTask());
                Assert.Equal(2, DraftRows(page, "GraphEditorNodes").Length);
                Assert.False(page.Bindings.IsActionAvailable("refresh"));
                Assert.False(page.Bindings.IsActionAvailable("archives"));
                Assert.False(page.Bindings.IsActionAvailable("select"));
                Assert.False(page.Bindings.IsActionAvailable("create-draft"));
                Assert.False(page.Bindings.TrySetValue("EditName", "Must not invalidate dirty graph"));
                Assert.False(page.Bindings.TrySetValue("Search", "Do not lose draft"));
                var manualNode = DraftRow(page, "GraphEditorNodes", "Title", CanonicalAutomationCoreNodes.ManualType);
                await page.Bindings.DispatchAsync("select-edit-node", DraftTarget(page, manualNode), rig.Token);
                var output = DraftRow(page, "GraphEditorPorts", "Title", "Output: out");
                await page.Bindings.DispatchAsync("select-graph-port", DraftTarget(page, output), rig.Token);
                var emitNode = DraftRow(page, "GraphEditorNodes", "Title", CanonicalAutomationCoreNodes.EmitType);
                await page.Bindings.DispatchAsync("select-edit-node", DraftTarget(page, emitNode), rig.Token);
                var input = DraftRow(page, "GraphEditorPorts", "Title", "Input: in");
                await page.Bindings.DispatchAsync("select-graph-port", DraftTarget(page, input), rig.Token);
                Assert.True(page.Bindings.IsActionAvailable("connect-graph-ports"));
                await rig.Keep(page.Bindings.DispatchAsync("connect-graph-ports", null, rig.Token).AsTask());
                Assert.Single(DraftRows(page, "GraphEditorConnections"));
                Assert.True(page.Bindings.TrySetValue("GraphEditorValue", "literal local value"));
                await rig.Keep(page.Bindings.DispatchAsync("apply-graph-value", null, rig.Token).AsTask());
                Assert.True(page.Bindings.TrySetValue("GraphEditorValue", "{{input}}"));
                await rig.Keep(page.Bindings.DispatchAsync("apply-graph-value", null, rig.Token).AsTask());
                Assert.Equal("{{input}}", BindingValue<string>(page, "GraphEditorValue"));
                Assert.Contains("graph.validation.failed", BindingValue<string>(page, "GraphEditorStatus"));
                Assert.True(page.Bindings.TrySetValue("GraphEditorValue", "literal local value"));
                var connection = Assert.Single(DraftRows(page, "GraphEditorConnections"));
                await rig.Keep(page.Bindings.DispatchAsync("disconnect-graph-link", DraftTarget(page, connection), rig.Token).AsTask());
                Assert.Empty(DraftRows(page, "GraphEditorConnections"));
                await rig.Keep(page.Bindings.DispatchAsync("undo-graph-edit", null, rig.Token).AsTask());
                Assert.Single(DraftRows(page, "GraphEditorConnections"));
                await rig.Keep(page.Bindings.DispatchAsync("redo-graph-edit", null, rig.Token).AsTask());
                Assert.Empty(DraftRows(page, "GraphEditorConnections"));
                Assert.False(page.Bindings.TryGetItemValue(new object(), "Target", out _));
                Assert.Equal(homeBefore, await File.ReadAllBytesAsync(Path.Combine(rig.Root, "Home", "state.json"), rig.Token));
                Assert.Equal(sqliteBefore, await rig.ReadOriginalAutomationDescriptors(source));
                Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
                await page.Bindings.DispatchAsync("discard-graph-edits", null, rig.Token);
                Assert.False(BindingValue<bool>(page, "HasGraphEditor"));
                Assert.True(page.Bindings.IsActionAvailable("refresh"));
                await PumpSavedLibrary(window);
            }
            catch (Exception cause) { failures.Add(cause); }
            finally
            {
                if (page is not null) try { await rig.Keep(page.CloseAndDrainAsync()); healthy = true; }
                    catch (Exception cause) { failures.Add(cause); }
                else healthy = true;
                if (healthy) window.Close();
            }
        }
        catch (Exception cause) { failures.Add(cause); }
        foreach (var close in new Func<Task>[] { graph.Writer.CloseAndDrainOriginalAsync, graph.Writes.CloseAndDrainOriginalAsync })
            try { await rig.Keep(close()); } catch (Exception cause) { failures.Add(cause); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual graph UI/body/owner close failed; original evidence retained.", failures);
    });
    private static object[] DraftRows(OriginalAutomationLibraryDesktopPage page, string path)
    { Assert.True(page.Bindings.TryGetValue(path, out var value)); return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object>().ToArray(); }
    private static object DraftRow(OriginalAutomationLibraryDesktopPage page, string collection, string field, string text) =>
        Assert.Single(DraftRows(page, collection), row => page.Bindings.TryGetItemValue(row, field, out var value) && Equals(value, text));
    private static object DraftTarget(OriginalAutomationLibraryDesktopPage page, object row)
    { Assert.True(page.Bindings.TryGetItemValue(row, "Target", out var value)); Assert.NotNull(value); return value; }
    private sealed class ActualGraphEditingLibrary
    {
        private readonly Rig _rig;
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Library;
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Writer;
        internal readonly HomeCanonicalAutomationDefinitionWriteSource Writes;
        internal readonly HomePermissionTrustService Permissions;
        internal ActualGraphEditingLibrary(Rig rig, CanonicalAutomationLibraryOriginalReadOwner library)
        {
            _rig = rig; Library = library; Writer = new(library);
            var sqlPolicy = new HomeCanonicalAutomationDefinitionActionPolicySource();
            var graphPolicy = new HomeAutomationCreationGraphActionPolicySource();
            Permissions = new(rig.Home.StateStore, (app, action) => sqlPolicy.TryGet(app, action) ?? graphPolicy.TryGet(app, action));
            HomeCanonicalAutomationDefinitionWriteSource? writes = null;
            var sqlResources = new HomeCanonicalAutomationDefinitionResourceResolver(() => writes ?? throw new InvalidOperationException("Actual SQL review owner unavailable."));
            var graphResources = new HomeGraphPublicationResourceRegistry();
            var resources = new ResourceAuthorizationService(rig.Home.Profiles, [sqlResources, graphResources]);
            var broker = new HomeResourceOperationBroker(resources, Permissions);
            Writes = writes = new(rig.Home.StateStore, rig.Home.Profiles, resources, broker, Permissions, Writer);
            Writer.BindOriginalHomeWriteSource(Writes);
            var schemas = CanonicalAutomationCoreNodes.CreateRegistry();
            var repository = new HomeVersionedNodeGraphRepository(rig.Home.StateStore, schemas);
            var publisher = new HomeGraphPublicationOwner(rig.Home.StateStore, rig.Home.Profiles, rig.Home.Ownership,
                repository, broker, Permissions, [Writer], graphResources);
            var association = new HomeGraphCreationAssociationSource(rig.Home.StateStore, rig.Home.Profiles,
                rig.Home.Ownership, repository, publisher, broker, Writes, Writer);
            Writer.BindOriginalCreationGraphSource(association, schemas);
        }
        internal async Task AcceptNext(Task sameActual, string action)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(15))
            {
                var all = (await _rig.Keep(Permissions.GetSnapshotAsync(cancellationToken: _rig.Token))).PendingRequests;
                var matching = all.Where(request => request.Scope.ActionName == action).ToArray();
                if (matching.Length != 0)
                {
                    var request = Assert.Single(matching); Assert.True(request.Policy.RequiresPerActionApproval);
                    Assert.True((await _rig.Keep(Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                        cancellationToken: _rig.Token))).Succeeded); return;
                }
                if (sameActual.IsCompleted) { await sameActual; throw new InvalidOperationException("The genuine creation completed without its next Home review."); }
                await Task.Delay(10, _rig.Token);
            }
            throw new TimeoutException("No actual original graph/definition review arrived.");
        }
    }
}
#endif
