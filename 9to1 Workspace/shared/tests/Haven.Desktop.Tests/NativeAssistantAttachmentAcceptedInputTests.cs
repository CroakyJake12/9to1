#if !ANDROID
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Real_imported_saved_input_reopens_with_fresh_source_while_empty_model_owner_keeps_Chat_and_Task_dispatch_unavailable()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            Rig? rig = null; NativeFilesWorkspaceService? files = null; AttachmentGraph? attachments = null;
            TaskProgressGraph? taskGraph = null; AttachmentInputModelGraph? models = null; ChatSessionService? chat = null;
            var roots = new List<object>(); var errors = new List<Exception>();
            rig = new Rig((_, profiles) => files = new(rig!.OriginalStateStore, profiles),
                originalAdditionalPolicy: new AttachmentFixturePolicies(),
                configuredTaskFactory: actual => { taskGraph = new(actual); roots.Add(taskGraph); return taskGraph.Tasks; },
                closeConfiguredTasks: async () =>
                {
                    if (taskGraph is not null) { var close = taskGraph.CloseAsync(); roots.Add(close); await close; }
                    if (models is not null) { var close = models.CloseAsync(); roots.Add(close); await close; }
                },
                configuredModelFactory: profiles => new AssistantOriginalModelSelectionOwner(models!.Registry,
                    models.Privacy, models.Permissions, profiles, models.Routing.ToCompatibilityDescriptor),
                configuredChatFactory: (actual, conversations, tasks) =>
                {
                    models = new(actual); roots.Add(models);
                    chat = new(conversations, models.Routing, new CapabilityPreflightService(), new Safety(),
                        new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()),
                        modelPermissions: models.Permissions, taskCoordinator: tasks);
                    roots.Add(chat); return chat;
                },
                configuredAttachmentFactory: (actual, production, paths) =>
                {
                    attachments = new(actual, files!, production, paths); roots.Add(attachments);
                    chat!.BindOriginalAttachmentInputSource(attachments.Source);
                    attachments.Source.BindOriginalInputOwners(chat, taskGraph!.Tasks); return attachments.Source;
                }, closeConfiguredAttachments: () => attachments?.CloseAsync() ?? Task.CompletedTask);
            RetainedAttachmentGraphs.Add([rig, roots, errors]);
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                Assert.True(attachments!.Source.HasOriginalInputComposition(chat!, taskGraph!.Tasks));
                var path = await attachments.RegisterExistingTextAsync("accepted-input.txt", "Actual approved document input.\nNo model was invoked.");
                var originalBytes = await File.ReadAllBytesAsync(path, Token);
                var definition = await rig.Bridge.CreateAsync(ConfiguredIdentityKind.Assistant,
                    new() { Name = "Saved document input", Memory = new(false) }, Guid.NewGuid(), Token);
                AssistantConversationBinding? chatBinding = null; IChatOriginalAttachmentInput? previousInput = null; Guid previousId = Guid.Empty;
                foreach (var kind in new[] { AssistantConversationKind.Chat, AssistantConversationKind.Task })
                {
                    var binding = await rig.Bridge.CreateConversationAsync(definition.Identity, definition.Revision, Guid.NewGuid(),
                        "Real selected " + kind + " input", Guid.NewGuid(), Token, kind);
                    var data = await rig.Bridge.ReadConversationAsync(binding, Token);
                    var branch = Assert.Single(data.Branches, row => row.IsCurrent).Id;
                    await rig.Bridge.SaveConversationDraftAsync(binding, branch, "Read my approved document.", [], Token);
                    var importing = rig.Bridge.ImportAttachmentOriginalAsync(binding, path, branch, Token); attachments.Retain(importing);
                    var read = await DecideAttachment(rig, importing, HomeCanonicalAssistantAttachmentReadSource.ReadAction, HomeApprovalChoice.Accept);
                    var write = await DecideAttachment(rig, importing, HomeCanonicalAssistantAttachmentImportSource.ImportAction, HomeApprovalChoice.Accept);
                    Assert.NotEqual(read, write); var attachment = await importing;
                    var preparing = attachments.Source.PrepareOriginalAttachmentInputWithinSourceAsync(binding,
                        "Read my approved document.", [attachment.Id], Scope, attachments.Retain, Token); attachments.Retain(preparing);
                    var input = await preparing;
                    Assert.True(attachments.Source.IsIssuedOriginalAttachmentInput(input));
                    var lineage = attachments.Source.ObserveOriginalAttachmentLineage(input);
                    Assert.Equal(binding.Conversation.Id, lineage.ConversationId); Assert.Equal(branch, lineage.BranchId);
                    Assert.Equal(attachment.Id, Assert.Single(lineage.AttachmentIds)); Assert.Equal(64, lineage.SnapshotSha256.Length);
                    Assert.Empty(await rig.Bridge.ListAvailableModelsAsync(binding, Token));
                    // This intentionally unlisted request is a negative input, never
                    // an invented available model or provider permission.
                    var unavailable = new ModelDescriptor("not-configured", 0, "", "", "", new HashSet<ToolCapability>(), DateTimeOffset.UnixEpoch);
                    var requested = new AssistantTaskInput("Read my approved document.", unavailable, EffortLevel.Medium, [attachment.Id], "not-configured");
                    var attachmentCommands = attachments.ObserveActualAttachmentCommands();
                    Task refused = kind == AssistantConversationKind.Chat
                        ? rig.Bridge.SendConversationOriginalAsync(binding, requested, Token)
                        : rig.Bridge.StartOriginalTaskAsync(binding, requested, Token);
                    attachments.Retain(refused);
                    var cause = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => refused);
                    Assert.Equal("The selected provider/model is no longer uniquely available under the effective canonical policy.", cause.Message);
                    Assert.Equal(attachmentCommands, attachments.ObserveActualAttachmentCommands());
                    attachments.ExpectKnownBridgeRefusal(refused, cause, requireAttachmentSource: false);
                    data = await rig.Bridge.ReadConversationAsync(binding, Token);
                    Assert.Empty(data.Messages); Assert.Null(data.CanonicalTask);
                    Assert.Equal(attachment.Id, Assert.Single(JsonSerializer.Deserialize<Guid[]>(data.Draft!.AttachmentIdsJson)!));
                    Assert.Equal("Read my approved document.", data.Draft.Content);
                    if (kind == AssistantConversationKind.Chat) { chatBinding = binding; previousInput = input; previousId = attachment.Id; }
                }
                var savedChat = Assert.IsType<AssistantConversationBinding>(chatBinding);
                await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
                {
                    await OpenActualSavedMemoryConversation(surface, definition.Identity, savedChat.Conversation.Id, window);
                    AssertAttachmentText(window, "accepted-input.txt");
                    var input = AssertOriginalComposer(window); Assert.Equal("Read my approved document.", input.Text);
                    Assert.False(surface.Bindings.IsActionAvailable("assistants.send"));
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible &&
                        item.Text?.Contains("No model", StringComparison.OrdinalIgnoreCase) == true);
                    Assert.Empty(controller.Snapshot.Conversation!.Messages);
                });
                await rig.ReopenAsync();
                var reopened = await rig.Bridge.OpenConversationAsync(definition.Identity, savedChat.Conversation.Id, Token);
                Assert.False(attachments!.Source.IsIssuedOriginalAttachmentInput(previousInput!));
                var freshTask = attachments.Source.PrepareOriginalAttachmentInputWithinSourceAsync(reopened,
                    "Read my approved document.", [previousId], Scope, attachments.Retain, Token); attachments.Retain(freshTask);
                var fresh = await freshTask; Assert.NotSame(previousInput, fresh);
                Assert.True(attachments.Source.IsIssuedOriginalAttachmentInput(fresh));
                Assert.Equal(previousId, Assert.Single(attachments.Source.ObserveOriginalAttachmentLineage(fresh).AttachmentIds));
                var reopenedFileBytes = await File.ReadAllBytesAsync(path, Token);
                Assert.True(originalBytes.SequenceEqual(reopenedFileBytes));
                Assert.Empty((await rig.Bridge.ReadConversationAsync(reopened, Token)).Messages);
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                try { attachments?.RequestWithdrawal(); } catch (Exception cause) { errors.Add(cause); }
                Task? close = null;
                try { close = rig.CloseAsync(); roots.Add(close); await close; }
                catch (Exception cause) { errors.Add(close?.Exception ?? cause); }
                // Rig's actual attachment dependency barrier preserves stores if
                // this source fails. Independently observe the SAME cached graph.
                if (attachments is not null)
                    try { close = attachments.CloseAsync(); roots.Add(close); await close; }
                    catch (Exception cause) { errors.Add(close?.Exception ?? cause); }
            }
            if (errors.Count != 0) throw new AggregateException("Actual accepted-input preparation and all owner receipts remain retained.", errors);
            return true;
        }, Token));
    }
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Changed_real_Den_membership_refuses_attachment_READ_before_new_file_review() =>
        RunAttachmentBoundaryControl(async control =>
        {
            var home = await control.Rig.Home.OpenAsync(Token);
            var session = (await home.Den.GetAsync<SessionRecord>(control.Binding.Definition.Identity.NamespaceId,
                OriginalAttachmentBindingProperty<string>(control.Binding, "DenSessionId"), Token))!;
            var changed = await home.Den.SaveAsync(session with { ActiveBranchId = Guid.NewGuid().ToString("D") },
                session.Revision, "test.attachment.den-membership." + Guid.NewGuid().ToString("N"), Token);
            Assert.True(changed.Revision > OriginalAttachmentBindingProperty<long>(control.Binding, "DenSessionRevision"));
            var actual = control.Graph.Source.ImportOriginalAsync(control.Binding, control.Path, null, Token); control.Retain(actual);
            var observed = await Record.ExceptionAsync(() => actual);
            control.ExpectOnly(observed, typeof(AssistantCommandRefusedException),
                "The exact current Den definition/session membership changed before protected canonical READ.");
            Assert.Empty((await control.Rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests);
            Assert.Equal(control.Binding.Conversation, await control.Rig.OriginalConversations.GetAsync(control.Binding.Conversation.Id, Token));
            var unchangedFileBytes = await File.ReadAllBytesAsync(control.Path, Token);
            Assert.True(control.Bytes.SequenceEqual(unchangedFileBytes));
        });

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Changed_real_conversation_scope_refuses_attachment_READ_under_the_original_SQL_lease() =>
        RunAttachmentBoundaryControl(async control =>
        {
            var changed = control.Binding.Conversation with { SpaceId = Guid.NewGuid() };
            await control.Rig.OriginalConversations.UpsertConversationAsync(changed, Token);
            var actual = control.Graph.Source.ImportOriginalAsync(control.Binding, control.Path, null, Token); control.Retain(actual);
            var observed = await Record.ExceptionAsync(() => actual);
            control.ExpectOnly(observed, typeof(UnauthorizedAccessException), "The actual conversation or selected current branch changed.");
            Assert.Empty((await control.Rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests);
            Assert.Equal(changed, await control.Rig.OriginalConversations.GetAsync(changed.Id, Token));
            var home = await control.Rig.Home.OpenAsync(Token);
            Assert.Equal(OriginalAttachmentBindingProperty<long>(control.Binding, "DenSessionRevision"), (await home.Den.GetAsync<SessionRecord>(
                control.Binding.Definition.Identity.NamespaceId, OriginalAttachmentBindingProperty<string>(control.Binding, "DenSessionId"), Token))!.Revision);
            var unchangedFileBytes = await File.ReadAllBytesAsync(control.Path, Token);
            Assert.True(control.Bytes.SequenceEqual(unchangedFileBytes));
        });

    private static T OriginalAttachmentBindingProperty<T>(AssistantConversationBinding binding, string name)
    {
        const System.Reflection.BindingFlags properties = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        // Observe the actual private issued binding for this corruption control; no new binding or authority is created.
        return Assert.IsType<T>(typeof(AssistantConversationBinding).GetProperty(name, properties)!.GetValue(binding));
    }

    private static async Task RunAttachmentBoundaryControl(Func<AttachmentBoundaryControl, Task> body)
    {
        var control = new AttachmentBoundaryControl(); var errors = new List<Exception>();
        RetainedAttachmentGraphs.Add([control, errors]);
        try { await control.Initialize(); await body(control); } catch (Exception cause) { errors.Add(cause); }
        try { await control.Close(); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual attachment boundary control and retained original owners.", errors);
    }
    private sealed class AttachmentBoundaryControl
    {
        internal readonly Rig Rig;
        internal AttachmentGraph Graph = null!;
        internal AssistantConversationBinding Binding = null!;
        internal string Path = "";
        internal byte[] Bytes = [];
        private NativeFilesWorkspaceService? _files;
        private ChatSessionService? _chat;
        private TaskExecutionCoordinator? _tasks;
        private readonly List<Task> _raw = [];
        private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        internal AttachmentBoundaryControl()
        {
            Rig = new Rig((_, profiles) => _files = new(Rig!.OriginalStateStore, profiles),
                originalAdditionalPolicy: new AttachmentFixturePolicies(),
                configuredChatFactory: (_, conversations, tasks) =>
                {
                    _tasks = tasks;
                    return _chat = new(conversations, new NoModelCalls(), new CapabilityPreflightService(), new Safety(),
                        new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()), taskCoordinator: tasks);
                },
                configuredAttachmentFactory: (actual, production, paths) =>
                {
                    Graph = new(actual, _files!, production, paths);
                    _chat!.BindOriginalAttachmentInputSource(Graph.Source);
                    Graph.Source.BindOriginalInputOwners(_chat, _tasks!); return Graph.Source;
                }, closeConfiguredAttachments: () => Graph?.CloseAsync() ?? Task.CompletedTask);
        }
        internal void Retain(Task raw) { _raw.Add(raw); Graph?.Retain(raw); }
        internal async Task Initialize()
        {
            await Rig.InitializeAsync(true, importMemory: false);
            Path = await Graph.RegisterExistingTextAsync("protected-boundary.txt", "Actual Home-approved bounded input.");
            Bytes = await File.ReadAllBytesAsync(Path, Token);
            var definition = await Rig.Bridge.CreateAsync(ConfiguredIdentityKind.Assistant,
                new() { Name = "Actual attachment source boundary", Memory = new(false) }, Guid.NewGuid(), Token);
            Binding = await Rig.Bridge.CreateConversationAsync(definition.Identity, definition.Revision, Guid.NewGuid(),
                "Protected document input", Guid.NewGuid(), Token, AssistantConversationKind.Chat);
            var rootBranch = Graph.EnsureActualRootBranchAsync(Binding); Retain(rootBranch);
            await rootBranch; rootBranch.GetAwaiter().GetResult();
            var data = await Rig.Bridge.ReadConversationAsync(Binding, Token);
            var branch = Assert.Single(data.Branches, row => row.IsCurrent).Id;
            await Rig.Bridge.SaveConversationDraftAsync(Binding, branch, "Inspect this approved input.", [], Token);
            var import = Rig.Bridge.ImportAttachmentOriginalAsync(Binding, Path, branch, Token); Retain(import);
            var read = await DecideAttachment(Rig, import, HomeCanonicalAssistantAttachmentReadSource.ReadAction, HomeApprovalChoice.Accept);
            var write = await DecideAttachment(Rig, import, HomeCanonicalAssistantAttachmentImportSource.ImportAction, HomeApprovalChoice.Accept);
            Assert.NotEqual(read, write); await import;
        }
        internal void ExpectOnly(Exception? observed, Type type, string message)
        {
            Assert.NotNull(observed); var leaves = new List<Exception>();
            Collect(observed!, leaves); var same = Assert.Single(leaves.Distinct<Exception>(ReferenceEqualityComparer.Instance));
            Assert.Equal(type, same.GetType()); Assert.Equal(message, same.Message); _expected.Add(same);
        }
        private static void Collect(Exception cause, List<Exception> leaves)
        {
            if (cause is AggregateException { InnerExceptions.Count: > 0 } group)
            { foreach (var child in group.InnerExceptions) Collect(child, leaves); }
            else leaves.Add(cause);
        }
        private void DemandOnlyExpected(Exception cause)
        {
            if (_expected.Contains(cause)) return;
            if (cause is AggregateException { InnerExceptions.Count: > 0 } group)
            { foreach (var child in group.InnerExceptions) DemandOnlyExpected(child); return; }
            throw new InvalidOperationException("An unexpected source or cleanup cause remains owned by this fixture.", cause);
        }
        internal async Task Close()
        {
            var errors = new List<Exception>();
            async Task Join(Func<Task> factory, bool requireKnownFailure = false)
            {
                Task? actual = null;
                try
                {
                    actual = factory(); _raw.Add(actual); await actual;
                    if (requireKnownFailure) errors.Add(new InvalidOperationException("The actual failed source close was incorrectly healthy."));
                }
                catch (Exception cause)
                {
                    try { DemandOnlyExpected(actual?.Exception ?? cause); } catch (Exception unknown) { errors.Add(unknown); }
                }
            }
            if (Graph is not null)
            {
                await Join(Graph.Source.CloseAndDrainAsync, _expected.Count != 0);
                var same = Graph.Source.OriginalClose;
                if (same is not null)
                    try { Assert.Same(same, Graph.Source.CloseAndDrainAsync()); } catch (Exception cause) { errors.Add(cause); }
                await Join(Graph.CloseAsync, _expected.Count != 0);
            }
            await Join(Rig.CloseAsync, _expected.Count != 0);
            foreach (var actual in _raw.ToArray())
                try { await actual; } catch (Exception cause)
                { try { DemandOnlyExpected(actual.Exception ?? cause); } catch (Exception unknown) { errors.Add(unknown); } }
            if (errors.Count != 0) throw new AggregateException("Actual independent attachment boundary retirement.", errors);
        }
    }

    private sealed class AttachmentInputModelGraph
    {
        internal ModelProviderRegistry Registry { get; } = new(Array.Empty<IModelProvider>());
        internal PrivacyPreferenceStore Privacy { get; }
        internal ModelPermissionEvaluator Permissions { get; }
        internal ProviderRoutingModelClient Routing { get; }
        private Task? _close;
        internal AttachmentInputModelGraph(Rig rig)
        {
            var paths = new Paths(rig.Root); Privacy = new(paths);
            Permissions = new(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(paths)));
            Routing = new(new NoModelCalls(), Registry, Privacy);
        }
        internal Task CloseAsync() => _close ??= Registry.CloseOriginalCataloguesAndDrainAsync();
    }
}
#endif
