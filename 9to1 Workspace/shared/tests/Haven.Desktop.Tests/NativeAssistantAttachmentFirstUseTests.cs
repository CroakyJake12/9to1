#if !ANDROID
using System.Collections;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Attachments;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    private static readonly List<object[]> RetainedAttachmentGraphs = [];
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Registered_DOCX_uses_same_manual_READ_and_import_then_renders_and_reopens_extracted_draft_text()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            Rig? rig = null; NativeFilesWorkspaceService? files = null; AttachmentGraph? graph = null;
            var graphs = new List<AttachmentGraph>(); var roots = new List<object>(); var failures = new List<Exception>();
            rig = new Rig((_, profiles) => files = new(rig!.OriginalStateStore, profiles), originalAdditionalPolicy: new AttachmentFixturePolicies(),
                configuredAttachmentFactory: (actual, production, paths) =>
                { graph = new(actual, files!, production, paths); graphs.Add(graph); return graph.Source; },
                closeConfiguredAttachments: () => graph?.CloseAsync() ?? Task.CompletedTask);
            RetainedAttachmentGraphs.Add([rig, graphs, roots, failures]);
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                var bytes = BuildRegisteredAttachmentDocx();
                var path = await graph!.RegisterExistingBytesAsync("registered-proof.docx", bytes,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
                var binding = await rig.CreateAsync(new() { Name = "Approved document", Memory = new(false) });
                var rootBranch = graph!.EnsureActualRootBranchAsync(binding); await rootBranch; rootBranch.GetAwaiter().GetResult();
                var identity = binding.Definition.Identity; var conversationId = binding.Conversation.Id;
                var initial = await rig.Bridge.ReadConversationAsync(binding, Token);
                var branch = initial.Branches.Single(row => row.IsCurrent).Id;
                await rig.Bridge.SaveConversationDraftAsync(binding, branch, "Keep this document with my draft.", [], Token);
                var imported = rig.Bridge.ImportAttachmentOriginalAsync(binding, path, branch, Token); graph.Retain(imported);
                var read = await DecideAttachment(rig, imported, HomeCanonicalAssistantAttachmentReadSource.ReadAction, HomeApprovalChoice.Accept);
                var write = await DecideAttachment(rig, imported, HomeCanonicalAssistantAttachmentImportSource.ImportAction, HomeApprovalChoice.Accept);
                Assert.NotEqual(read, write);
                var attachment = await imported;
                Assert.Equal(MessageAttachmentKind.Word, attachment.Kind);
                Assert.Equal(AttachmentProcessingState.Ready, attachment.ProcessingState);
                Assert.Contains("Proof from approved DOCX.", attachment.ExtractedText!, StringComparison.Ordinal);
                Assert.Contains("Same approved file.", attachment.ExtractedText!, StringComparison.Ordinal);
                var unchangedBytes1 = await File.ReadAllBytesAsync(path);
                Assert.True(bytes.SequenceEqual(unchangedBytes1));
                await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
                {
                    await OpenActualSavedMemoryConversation(surface, identity, conversationId, window);
                    AssertAttachmentText(window, "registered-proof.docx"); AssertAttachmentText(window, "Proof from approved DOCX.");
                    Assert.Null(controller.Snapshot.Conversation!.CanonicalTask);
                });
                await rig.ReopenAsync();
                var reopened = await rig.Bridge.OpenConversationAsync(identity, conversationId, Token);
                var actual = await rig.Bridge.ReadConversationAsync(reopened, Token);
                Assert.Equal(attachment.Id, Assert.Single(actual.Attachments).Id);
                Assert.Equal(attachment.ExtractedText, Assert.Single(actual.Attachments).ExtractedText);
                Assert.Equal("Keep this document with my draft.", actual.Draft!.Content);
                Assert.Equal(new[] { attachment.Id }, JsonSerializer.Deserialize<Guid[]>(actual.Draft.AttachmentIdsJson));
                var unchangedBytes2 = await File.ReadAllBytesAsync(path);
                Assert.True(bytes.SequenceEqual(unchangedBytes2));
                Assert.Null(actual.CanonicalTask);
            }
            catch (Exception cause) { failures.Add(cause); }
            finally
            {
                foreach (var actual in graphs) try { actual.RequestWithdrawal(); } catch (Exception cause) { failures.Add(cause); }
                Task? close = null;
                try { close = rig.CloseAsync(); roots.Add(close); await close; } catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
                foreach (var actual in graphs)
                    try { close = actual.CloseAsync(); roots.Add(close); await close; } catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
            }
            if (failures.Count != 0) throw new AggregateException("The actual approved document/draft/native source receipts remain retained.", failures);
            return true;
        }, Token));
    }
    private static byte[] BuildRegisteredAttachmentDocx()
    {
        using var bytes = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in new[] { ("word/document.xml", "Proof from approved DOCX."), ("word/header1.xml", "Same approved file.") })
            {
                using var entry = archive.CreateEntry(name).Open();
                using var writer = new StreamWriter(entry, new UTF8Encoding(false));
                writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:document>");
            }
        }
        return bytes.ToArray();
    }
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Individual_import_WRITE_decline_and_pending_owner_withdrawal_preserve_the_actual_draft_and_file()
    {
        foreach (var withdraw in new[] { false, true })
        {
            Rig? rig = null; NativeFilesWorkspaceService? files = null; AttachmentGraph? graph = null;
            var roots = new List<object>(); var failures = new List<Exception>();
            rig = new Rig((_, profiles) => files = new(rig!.OriginalStateStore, profiles), originalAdditionalPolicy: new AttachmentFixturePolicies(),
                configuredAttachmentFactory: (actual, production, paths) =>
                { graph = new(actual, files!, production, paths); roots.Add(graph); return graph.Source; },
                closeConfiguredAttachments: () => graph?.CloseAsync() ?? Task.CompletedTask);
            RetainedAttachmentGraphs.Add([rig, roots, failures]);
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                var path = await graph!.RegisterExistingTextAsync("write-declined.txt", "Keep the registered file.");
                var bytes = await File.ReadAllBytesAsync(path);
                var binding = await rig.CreateAsync(new() { Name = "Import WRITE control", Memory = new(false) });
                var rootBranch = graph!.EnsureActualRootBranchAsync(binding); await rootBranch; rootBranch.GetAwaiter().GetResult();
                var data = await rig.Bridge.ReadConversationAsync(binding, Token); var branch = data.Branches.Single(row => row.IsCurrent).Id;
                await rig.Bridge.SaveConversationDraftAsync(binding, branch, "Keep the saved draft", [], Token);
                var actual = rig.Bridge.ImportAttachmentOriginalAsync(binding, path, branch, Token); graph.Retain(actual);
                var read = await DecideAttachment(rig, actual, HomeCanonicalAssistantAttachmentReadSource.ReadAction, HomeApprovalChoice.Accept);
                var write = await DecideAttachment(rig, actual, HomeCanonicalAssistantAttachmentImportSource.ImportAction,
                    withdraw ? null : HomeApprovalChoice.Decline);
                Assert.NotEqual(read, write);
                Task? writeClose = withdraw ? graph.RequestImportRetirement() : null;
                var failure = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
                graph.ExpectKnownBridgeRefusal(actual, failure);
                if (writeClose is not null) await writeClose;
                var requestTask = rig.Permissions.ReadImportRequestWithinOriginalSourceAsync(write, body => body(), graph.Retain, Token);
                graph.Retain(requestTask);
                var request = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(await requestTask);
                Assert.Equal(withdraw ? HomePermissionRequestState.Cancelled : HomePermissionRequestState.Denied, request.State);
                data = await rig.Bridge.ReadConversationAsync(binding, Token);
                Assert.Empty(data.Attachments); Assert.Equal("Keep the saved draft", data.Draft!.Content); Assert.Equal("[]", data.Draft.AttachmentIdsJson);
                var unchangedBytes3 = await File.ReadAllBytesAsync(path);
                Assert.True(bytes.SequenceEqual(unchangedBytes3));
            }
            catch (Exception cause) { failures.Add(cause); }
            finally
            {
                try { graph?.RequestWithdrawal(); } catch (Exception cause) { failures.Add(cause); }
                Task? close = null;
                try { close = rig.CloseAsync(); roots.Add(close); await close; }
                catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
                if (graph is not null)
                    try { close = graph.CloseAsync(); await close; } catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
            }
            if (failures.Count != 0) throw new AggregateException("Actual import WRITE refusal/withdrawal and independent source closes remain retained.", failures);
        }
    }
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Registered_text_requires_distinct_READ_and_import_then_rendered_detach_preserves_file_and_reopened_draft()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            Rig? rig = null; NativeFilesWorkspaceService? files = null; AttachmentGraph? graph = null;
            var graphs = new List<AttachmentGraph>(); var failures = new List<Exception>();
            rig = new Rig((_, profiles) => files = new(rig!.OriginalStateStore, profiles),
                originalAdditionalPolicy: new AttachmentFixturePolicies(),
                configuredAttachmentFactory: (actual, production, paths) =>
                {
                    graph = new(actual, files!, production, paths); graphs.Add(graph); return graph.Source;
                }, closeConfiguredAttachments: () => graph?.CloseAsync() ?? Task.CompletedTask);
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                var original = graph!;
                var path = await original.RegisterExistingTextAsync("registered-note.txt", "A real registered text fixture.\nSecond line.");
                var before = await File.ReadAllBytesAsync(path);
                var binding = await rig.CreateAsync(new() { Name = "Attachment source control", Memory = new(false) });
                var rootBranch = original.EnsureActualRootBranchAsync(binding); await rootBranch; rootBranch.GetAwaiter().GetResult();
                var identity = binding.Definition.Identity; var conversationId = binding.Conversation.Id;
                Guid attachmentId = Guid.Empty;
                await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
                {
                    await OpenActualSavedMemoryConversation(surface, identity, conversationId, window);
                    Assert.True(surface.Bindings.TrySetValue("Prompt", "Keep this draft and selected text."));
                    await surface.Bindings.DispatchAsync("assistants.draft.save", null, Token);
                    // The real selected-file producer is exercised directly here. A
                    // headless fixture cannot certify the operating-system picker.
                    var imported = controller.ImportAttachmentAsync(path,
                        controller.Snapshot.Conversation!.Branches.Single(row => row.IsCurrent).Id, Token);
                    original.Retain(imported);
                    var readRequest = await DecideAttachment(rig, imported, HomeCanonicalAssistantAttachmentReadSource.ReadAction, HomeApprovalChoice.Accept);
                    var writeRequest = await DecideAttachment(rig, imported, "assistants.attachments.import", HomeApprovalChoice.Accept);
                    Assert.NotEqual(readRequest, writeRequest);
                    attachmentId = (await imported).Id;
                    await surface.Bindings.DispatchAsync("assistants.work.refresh", null, Token); await FlushNativeMemoryUi(window);
                    AssertAttachmentText(window, "registered-note.txt"); AssertAttachmentText(window, "Second line.");
                    var importedConversation = Assert.IsType<AssistantConversationData>(controller.Snapshot.Conversation);
                    var importedDraft = Assert.IsType<ConversationDraft>(importedConversation.Draft);
                    Assert.Equal("Keep this draft and selected text.", importedDraft.Content);
                    Assert.Equal(new[] { attachmentId }, JsonSerializer.Deserialize<Guid[]>(importedDraft.AttachmentIdsJson));
                    var unchangedBytes4 = await File.ReadAllBytesAsync(path);
                    Assert.True(before.SequenceEqual(unchangedBytes4));
                    Assert.Null(importedConversation.CanonicalTask);
                });
                await rig.ReopenAsync();
                await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
                {
                    await OpenActualSavedMemoryConversation(surface, identity, conversationId, window);
                    AssertAttachmentText(window, "registered-note.txt");
                    Assert.Equal(attachmentId, Assert.Single(controller.Snapshot.Conversation!.Attachments).Id);
                    var remove = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && Equals(button.Content, "Remove from draft"));
                    var detached = ClickCanonicalCaptureControl(window, surface, remove, () =>
                        surface.Bindings.TryGetValue("HasDraftAttachments", out var has) && Equals(has, false) &&
                        surface.Bindings.IsActionAvailable("assistants.draft.save") == true);
                    graph!.Retain(detached);
                    await DecideAttachment(rig, detached, "assistants.attachments.detach", HomeApprovalChoice.Accept);
                    await detached; await FlushNativeMemoryUi(window);
                    var detachedConversation = Assert.IsType<AssistantConversationData>(controller.Snapshot.Conversation);
                    Assert.Equal("[]", detachedConversation.Draft!.AttachmentIdsJson);
                    Assert.Equal(attachmentId, Assert.Single(detachedConversation.Attachments).Id);
                    var unchangedBytes5 = await File.ReadAllBytesAsync(path);
                    Assert.True(before.SequenceEqual(unchangedBytes5));
                });
                await rig.ReopenAsync();
                var reopened = await rig.Bridge.OpenConversationAsync(identity, conversationId, Token);
                var data = await rig.Bridge.ReadConversationAsync(reopened, Token);
                Assert.Equal("[]", data.Draft!.AttachmentIdsJson); Assert.Equal("Keep this draft and selected text.", data.Draft.Content);
                Assert.Equal(attachmentId, Assert.Single(data.Attachments).Id); var unchangedBytes6 = await File.ReadAllBytesAsync(path);
                Assert.True(before.SequenceEqual(unchangedBytes6));
            }
            catch (Exception cause) { failures.Add(cause); }
            finally
            {
                foreach (var original in graphs) try { original.RequestWithdrawal(); } catch (Exception cause) { failures.Add(cause); }
                Task? close = null;
                try { close = rig.CloseAsync(); await close; } catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
                foreach (var original in graphs)
                    try { close = original.CloseAsync(); await close; } catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
                RetainedAttachmentGraphs.Add([rig, graphs, failures]);
            }
            if (failures.Count != 0) throw new AggregateException("Actual attachment READ/import/draft/native originals remain retained.", failures);
            return true;
        }, Token));
    }

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Individual_READ_decline_preserves_registered_bytes_and_the_saved_draft_without_import_approval()
    {
        Rig? rig = null; NativeFilesWorkspaceService? files = null; AttachmentGraph? graph = null; var failures = new List<Exception>();
        rig = new Rig((_, profiles) => files = new(rig!.OriginalStateStore, profiles), originalAdditionalPolicy: new AttachmentFixturePolicies(),
            configuredAttachmentFactory: (actual, production, paths) => (graph = new(actual, files!, production, paths)).Source,
            closeConfiguredAttachments: () => graph?.CloseAsync() ?? Task.CompletedTask);
        try
        {
            await rig.InitializeAsync(true, importMemory: false);
            var path = await graph!.RegisterExistingTextAsync("read-declined.txt", "This real file remains unchanged.");
            var before = await File.ReadAllBytesAsync(path); var binding = await rig.CreateAsync(new() { Name = "READ decline control", Memory = new(false) });
            var rootBranch = graph!.EnsureActualRootBranchAsync(binding); await rootBranch; rootBranch.GetAwaiter().GetResult();
            var data = await rig.Bridge.ReadConversationAsync(binding, Token); var branch = data.Branches.Single(row => row.IsCurrent).Id;
            await rig.Bridge.SaveConversationDraftAsync(binding, branch, "Preserved original draft", [], Token);
            var actual = rig.Bridge.ImportAttachmentOriginalAsync(binding, path, branch, Token); graph.Retain(actual);
            await DecideAttachment(rig, actual, HomeCanonicalAssistantAttachmentReadSource.ReadAction, HomeApprovalChoice.Decline);
            var failure = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
            graph.AssertActualBridgeRefusal(actual, failure, requireAttachmentSource: true);
            graph.ExpectKnownBridgeRefusal(actual, failure);
            data = await rig.Bridge.ReadConversationAsync(binding, Token);
            Assert.Empty(data.Attachments); Assert.Equal("Preserved original draft", data.Draft!.Content); Assert.Equal("[]", data.Draft.AttachmentIdsJson);
            var unchangedBytes7 = await File.ReadAllBytesAsync(path);
            Assert.True(before.SequenceEqual(unchangedBytes7));
            Assert.DoesNotContain((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                request => request.Scope.ActionName == "assistants.attachments.import");
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            try { graph?.RequestWithdrawal(); } catch (Exception cause) { failures.Add(cause); }
            Task? close = null; try { close = rig.CloseAsync(); await close; } catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
            RetainedAttachmentGraphs.Add([rig, graph!, failures]);
        }
        if (failures.Count != 0) throw new AggregateException("Actual declined attachment source originals remain retained.", failures);
    }
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Unsupported_registered_format_is_declined_before_individual_READ_or_import_review()
    {
        Rig? rig = null; NativeFilesWorkspaceService? files = null; AttachmentGraph? graph = null; var failures = new List<Exception>();
        rig = new Rig((_, profiles) => files = new(rig!.OriginalStateStore, profiles), originalAdditionalPolicy: new AttachmentFixturePolicies(),
            configuredAttachmentFactory: (actual, production, paths) => (graph = new(actual, files!, production, paths)).Source,
            closeConfiguredAttachments: () => graph?.CloseAsync() ?? Task.CompletedTask);
        try
        {
            await rig.InitializeAsync(true, importMemory: false);
            var path = await graph!.RegisterExistingTextAsync("unsupported.bin", "The registered bytes remain unchanged.");
            var before = await File.ReadAllBytesAsync(path);
            var binding = await rig.CreateAsync(new() { Name = "Unsupported attachment control", Memory = new(false) });
            var rootBranch = graph!.EnsureActualRootBranchAsync(binding); await rootBranch; rootBranch.GetAwaiter().GetResult();
            var data = await rig.Bridge.ReadConversationAsync(binding, Token); var branch = data.Branches.Single(row => row.IsCurrent).Id;
            await rig.Bridge.SaveConversationDraftAsync(binding, branch, "Keep this draft", [], Token);
            var actual = rig.Bridge.ImportAttachmentOriginalAsync(binding, path, branch, Token); graph.Retain(actual);
            var failure = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
            Assert.Contains("format is not supported", failure.Message, StringComparison.Ordinal);
            graph.ExpectKnownBridgeRefusal(actual, failure);
            data = await rig.Bridge.ReadConversationAsync(binding, Token);
            Assert.Empty(data.Attachments); Assert.Equal("Keep this draft", data.Draft!.Content); Assert.Equal("[]", data.Draft.AttachmentIdsJson);
            var unchangedBytes8 = await File.ReadAllBytesAsync(path);
            Assert.True(before.SequenceEqual(unchangedBytes8));
            Assert.DoesNotContain((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                request => request.Scope.ActionName is "assistants.attachments.read" or "assistants.attachments.import" or "assistants.attachments.detach");
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            try { graph?.RequestWithdrawal(); } catch (Exception cause) { failures.Add(cause); }
            Task? close = null; try { close = rig.CloseAsync(); await close; } catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
            RetainedAttachmentGraphs.Add([rig, graph!, failures]);
        }
        if (failures.Count != 0) throw new AggregateException("Actual unsupported-file observations and source closes remain retained.", failures);
    }
    private static void AssertAttachmentText(Window window, string expected) => Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
        value => value.IsEffectivelyVisible && value.Text?.Contains(expected, StringComparison.Ordinal) == true);
    private static async Task<string> DecideAttachment(Rig rig, Task original, string action, HomeApprovalChoice? decision)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            var pending = (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests.Where(request =>
                request.Scope.ActionName == action && request.State == HomePermissionRequestState.PendingApproval).ToArray();
            if (pending.Length != 0)
            {
                var request = Assert.Single(pending); Assert.True(request.Policy.RequiresPerActionApproval);
                if (decision is { } choice) Assert.True((await rig.Permissions.DecideAsync(request.RequestId, choice, cancellationToken: Token)).Succeeded); return request.RequestId;
            }
            if (original.IsCompleted) { await original; throw new InvalidOperationException("The actual action ended without its separate Home approval: " + action); }
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The genuine attachment Home review was not observed: " + action);
    }
    private sealed class AttachmentFixturePolicies : IHomeActionPolicySource
    {
        public HomePermissionActionPolicy? TryGet(string app, string action) =>
            new HomeAssistantAttachmentReadActionPolicySource().TryGet(app, action) ?? new HomeAssistantAttachmentImportActionPolicySource().TryGet(app, action);
    }
    private sealed class AttachmentGraph
    {
        private readonly Rig _rig; private readonly NativeFilesWorkspaceService _files; private readonly NativeFilesWorkspaceAuthority _workspace;
        private readonly ConversationProductionRepository _production;
        private readonly FilesNativeBrowserService _browser; private readonly CanonicalAttachmentOriginalFileSource _content;
        private readonly HomeCanonicalAssistantAttachmentReadSource _read;
        private readonly HomeCanonicalAssistantAttachmentImportSource _write;
        private readonly List<Task> _raw = []; private readonly HashSet<Task> _known = new(ReferenceEqualityComparer.Instance);
        private Task? _close;
        internal AssistantOriginalAttachmentSource Source { get; }
        internal AttachmentGraph(Rig rig, NativeFilesWorkspaceService files, ConversationProductionRepository production, IAppPaths paths)
        {
            _rig = rig; _files = files; _production = production; _workspace = new(files, rig.Profiles, rig.Authority);
            HomeCanonicalAssistantAttachmentReadSource? read = null; HomeCanonicalAssistantAttachmentImportSource? write = null;
            var resources = new ResourceAuthorizationService(rig.Profiles, [new HomeAssistantAttachmentReadResourceResolver(() => read!),
                new HomeAssistantAttachmentImportResourceResolver(() => write!)]);
            var broker = new HomeResourceOperationBroker(resources, rig.Permissions);
            _browser = new(_workspace, rig.Profiles, resources, new FilesCompatibilityPackageContentSource(_workspace, rig.Profiles, resources));
            _content = new(_browser, rig.Profiles);
            var maintained = new MessageAttachmentService(paths, production, new LocalMediaToolLocator());
            var processing = new OdsAwareMessageAttachmentService(new SafeMessageAttachmentService(maintained, paths), paths,
                rig.Database, new RetrievalIndexService(rig.Database, new LocalHashEmbeddingService()));
            processing.BindOriginalContentSource(_content);
            Source = new(rig.Home, rig.OriginalConversations, production, rig.Store, rig.Profiles, rig.Authority, _browser, _content, processing);
            _read = read = new(rig.OriginalStateStore, rig.Profiles, resources, broker, rig.Permissions, _browser);
            _write = write = new(rig.OriginalStateStore, rig.Profiles, resources, broker, rig.Permissions, Source);
            Source.BindOriginalHomeSources(_read, _write);
        }
        internal void Retain(Task raw) { lock (_raw) _raw.Add(raw); }
        internal Task<ConversationBranch> EnsureActualRootBranchAsync(AssistantConversationBinding binding)
        {
            // These controls explicitly arrange a rooted empty draft through the
            // SAME configured canonical repository, without inventing a message.
            var raw = _production.EnsureRootBranchAsync(binding.Conversation.Id, Token);
            Retain(raw); return raw;
        }
        internal Task[] ObserveActualAttachmentCommands()
        {
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var originals = Source.GetType().GetField("_originals", fields)!.GetValue(Source)!;
            var gate = originals.GetType().GetField("_gate", fields)!.GetValue(originals)!;
            lock (gate) return Assert.IsAssignableFrom<IEnumerable<Task>>(
                originals.GetType().GetField("_commands", fields)!.GetValue(originals)).ToArray();
        }
        internal void ExpectKnownBridgeRefusal(Task raw, Exception cause, bool requireAttachmentSource = true)
        {
            AssertActualBridgeRefusal(raw, cause, requireAttachmentSource); _known.Add(raw);
        }
        internal void AssertActualBridgeRefusal(Task raw, Exception cause, bool requireAttachmentSource)
        {
            Assert.IsType<AssistantCommandRefusedException>(cause); Assert.Null(cause.InnerException);
            Assert.True(raw.IsFaulted); Assert.Same(cause, Assert.Single(raw.Exception!.InnerExceptions));
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var originals = _rig.Bridge.GetType().GetField("_originals", fields)!.GetValue(_rig.Bridge)!;
            var gate = originals.GetType().GetField("_gate", fields)!.GetValue(originals)!;
            Task[] sources;
            lock (gate)
            {
                var commands = Assert.IsAssignableFrom<IEnumerable<Task>>(originals.GetType().GetField("_commands", fields)!.GetValue(originals));
                Assert.Contains(commands, command => ReferenceEquals(command, raw));
                sources = Assert.IsAssignableFrom<IEnumerable<Task>>(originals.GetType().GetField("_sources", fields)!.GetValue(originals)).ToArray();
            }
            if (!requireAttachmentSource) return; // Model refusal occurs before the attachment source is invoked.
            var actualSource = Assert.Single(sources, source => source.Exception is { InnerExceptions.Count: 1 } group &&
                ReferenceEquals(group.InnerExceptions[0], cause) && Source.IsAcknowledgedOriginalCommandRefusal(source));
            Assert.NotSame(raw, actualSource);
            // The source issuer acknowledges its actual raw Task, not the outer bridge driver.
            try { actualSource.GetAwaiter().GetResult(); } catch (Exception observed) { Assert.Same(cause, observed); }
            Assert.True(Source.IsAcknowledgedOriginalCommandRefusal(actualSource));
            Retain(actualSource); _known.Add(actualSource);
        }
        internal Task<string> RegisterExistingTextAsync(string name, string text) =>
            RegisterExistingBytesAsync(name, Encoding.UTF8.GetBytes(text), "text/plain");
        internal async Task<string> RegisterExistingBytesAsync(string name, byte[] bytes, string mediaType)
        {
            var root = Path.Combine(_rig.Root, "registered-files"); Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var workspace = await _files.ConfigureNewAsync(root, _rig.Ownership, Token);
            var path = Path.Combine(root, name);
            await File.WriteAllBytesAsync(path, bytes, Token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var id = HostedItemId.New(); var revision = new FilesRevisionId(Guid.NewGuid()); var now = DateTimeOffset.UtcNow;
            // Arrange an existing registered Files item through maintained owner APIs;
            // never seed a JSON row or treat this fixture path as a READ grant.
            Assert.True((await workspace.Provider.CommitUploadedContentAsync(new(id, null, name, mediaType, revision, null,
                workspace.Actor.ActorId, now, bytes.Length, hash, "registered-fixture/" + id + "/" + revision), Token)).IsSuccess);
            await workspace.Materializations.RegisterValidatedAsync(path, new(id, revision, hash, bytes.Length, now), SyncAvailability.AvailableOffline, Token);
            return path;
        }
        internal void RequestWithdrawal()
        {
            _read.RequestOriginalPendingReviewWithdrawals(); _write.RequestOriginalPendingReviewWithdrawals();
            if (_read.OriginalPendingReviewWithdrawalTask is { } read) Retain(read);
            if (_write.OriginalPendingReviewWithdrawalTask is { } write) Retain(write);
        }
        internal Task RequestImportRetirement()
        {
            _write.RequestOriginalRetirement();
            if (_write.OriginalPendingReviewWithdrawalTask is { } withdrawal) Retain(withdrawal);
            var close = _write.CloseAndDrainOriginalAsync(); Retain(close);
            Assert.Same(close, _write.CloseAndDrainOriginalAsync()); return close;
        }
        internal Task CloseAsync()
        {
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseBody(start.Task); start.SetResult(); return _close;
        }
        private async Task CloseBody(Task start)
        {
            await start; var errors = new List<Exception>();
            try { RequestWithdrawal(); } catch (Exception cause) { errors.Add(cause); }
            async Task<bool> Join(Func<Task> acquire)
            {
                Task? actual = null;
                try { actual = acquire(); Retain(actual); await actual; return true; }
                catch (Exception cause) { errors.Add(actual?.Exception ?? cause); return false; }
            }
            // A failed borrower retains its dependent owners. Already accepted
            // originals below still settle independently and preserve all causes.
            var healthy = await Join(Source.CloseAndDrainAsync);
            if (healthy) healthy = await Join(_content.CloseAndDrainOriginalAsync);
            if (healthy) healthy = await Join(_read.CloseAndDrainOriginalAsync);
            if (healthy) healthy = await Join(_write.CloseAndDrainOriginalAsync);
            if (healthy) _ = await Join(_browser.CloseOriginalAttachmentSelectionsAndDrainAsync);
            Task[] raw; lock (_raw) raw = _raw.ToArray();
            foreach (var actual in raw)
                try { await actual; } catch (Exception cause) { if (!_known.Contains(actual)) errors.Add(actual.Exception ?? cause); }
            if (errors.Count != 0) throw new AggregateException("Actual attachment graph and source receipts are retained.", errors);
        }
    }
}
#endif
