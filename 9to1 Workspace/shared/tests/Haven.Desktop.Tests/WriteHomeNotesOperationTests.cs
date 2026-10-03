using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Tests;

public sealed class WriteHomeNotesOperationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_rich_Notes_action_requires_exact_Home_approval_and_commits_same_moved_Write_identity_once(bool failAudit)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-write-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var actors = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = (await actors.GetCurrentAsync(token))!;
            var policies = new WriteNativeActionPolicies();
            var permissions = new HomePermissionTrustService(new AuditFailureStore(store, failAudit), policies.TryGet);
            var files = new NativeFilesWorkspaceService(store, actors);
            var ownership = new HomeLocalStoreOwnership(store, actors, new HomeLocalStoreEvidenceRegistry([files]), permissions);
            var receipt = new HomeResourceStoreOwnershipAuthority(ownership, actors);
            var chosen = Path.Combine(root, "files"); Directory.CreateDirectory(chosen);
            var workspace = await files.ConfigureNewAsync(chosen, ownership, token);
            var authority = new NativeFilesWorkspaceAuthority(files, actors, receipt);
            workspace = (await authority.GetCurrentAsync(token))!;
            var resources = new ResourceAuthorizationService(actors,
                [new FilesArtifactResourceResolver(async (current, ct) =>
                    (await authority.GetCurrentAsync(ct))?.Actor == current ? workspace.Provider : null,
                    async (current, app, ct) => (await authority.GetCurrentAsync(ct))?.Actor == current &&
                        workspace.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null)]);
            var bridge = new WriteFilesArtifactBridge(actors, _ => workspace.Provider, workspace.Directories,
                new WriteNativeDocumentPackageStore(), resources, () => AppAiAccessMode.Write,
                (current, provider, ct) => authority.CaptureCommitAuthorityAsync(current, provider, () => true, ct));
            var document = new NotesDocument { Title = "Canonical shared Write" };
            var block = NotesBlock.CreateParagraph();
            var run = new NotesTextRun { Text = "Retained rich paragraph", Italic = true, Bold = false };
            block.Runs = [run]; block.StyleId = document.Styles[0].Id;
            var other = NotesBlock.CreateParagraph("Untargeted retained paragraph");
            document.Sections[0].Pages[0].Blocks = [block, other];
            var fileId = HostedItemId.New();
            Assert.True((await workspace.Provider.RegisterArtifactAsync(new("write", document.Id.ToString("N"), fileId,
                workspace.Configuration.AppFolders["write"], nameof(FilesArtifactType.WriteDocument), "Rich.9to1w"), actor.ActorId, token)).IsSuccess);
            var initial = await bridge.SaveAsync(fileId, document, null, token);
            var now = DateTimeOffset.UtcNow;
            var move = await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, fileId,
                workspace.Configuration.AppFolders["write"], workspace.Configuration.AppFolders["boards"], "Move", initial.Id,
                null, FilesOperationState.Pending, now, now, null, null), null, token);
            Assert.True(move.IsSuccess);
            var structural = move.Value!.ResultRevisionId!.Value;
            var owning = Guid.Parse(initial.OwningAppRevisionId!);
            var action = new HomeProductivityAction("text.run.bold", 1, "text.paragraph", [block.Id],
                JsonSerializer.SerializeToElement(new { runId = run.Id, value = true }), 0)
                { ExpectedArtifactRevision = new(VersionId: owning) };
            var captured = await bridge.OpenAsync(fileId, token);
            var intent = WriteNotesWriteIntent.Capture(fileId, structural, owning, captured, action);
            captured.Title = "Caller replacement after preparation";
            captured.Sections[0].Pages[0].Blocks[0].Runs[0].Text = "Caller replacement";
            var broker = new HomeResourceOperationBroker(resources, permissions);
            var pending = await broker.AuthorizeAsync("write", "write.file.save", intent.Scopes, intent.Arguments,
                "Make the selected canonical rich-text run bold", null, "actual-write-owner-test", token);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.Null(await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, token));
            Assert.Equal(structural, (await workspace.Provider.GetAsync(fileId, token)).Value!.CurrentRevisionId);
            Assert.False((await bridge.OpenAsync(fileId, token)).Sections[0].Pages[0].Blocks[0].Runs[0].Bold);
            Assert.True((await permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            var capability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, token));
            var operation = new WriteHomeNotesOperation(bridge, new NativeFilesArtifactContentReader(authority, actors, resources), broker);
            var context = new HomeProductivityContext("write", document.Id.ToString("N"), 0, [block.Id], new HashSet<string> { "text.paragraph" })
                { ArtifactRevision = new(VersionId: owning) };
            var provider = new WriteHomeNotesActionProvider(operation, intent, capability);
            var engine = new HomeProductivityEngine(artifactActions: [provider]);
            var changed = await engine.ApplyActionAsync(context,
                action with { Arguments = JsonSerializer.SerializeToElement(new { runId = run.Id, value = false }) }, token);
            Assert.False(changed.Succeeded); Assert.Equal("ActionBindingMismatch", changed.Code);
            var foreign = new WriteHomeNotesOperation(bridge, new NativeFilesArtifactContentReader(authority, actors, resources),
                new HomeResourceOperationBroker(resources, permissions));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.ExecuteAsync(intent, capability, token));
            var unboundBridge = new WriteFilesArtifactBridge(actors, _ => workspace.Provider, workspace.Directories,
                new WriteNativeDocumentPackageStore(), resources, () => AppAiAccessMode.Write);
            var unbound = new WriteHomeNotesOperation(unboundBridge, new NativeFilesArtifactContentReader(authority, actors, resources), broker);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unbound.ExecuteAsync(intent, capability, token));
            Assert.Equal(structural, (await workspace.Provider.GetAsync(fileId, token)).Value!.CurrentRevisionId);
            var result = await engine.ApplyActionAsync(context, action, token);
            Assert.True(result.Succeeded); Assert.Equal(HomeProductivityArtifactOutcome.Committed, result.Outcome);
            Assert.Equal(failAudit ? "CommittedAuditPending" : "Committed", result.Code);
            var audit = await permissions.GetSnapshotAsync(cancellationToken: token);
            var completions = audit.RecentAuditEvents.Where(entry => entry.RequestId == pending.RequestId && entry.ResultCode == "WRITE_COMMITTED").ToArray();
            if (failAudit) Assert.Empty(completions);
            else
            {
                var completion = Assert.Single(completions);
                Assert.Equal(HomePermissionRequestState.Succeeded, completion.RequestState);
                Assert.Contains(completion.AffectedObjects, item => item == new HomeObjectReference("files.item", fileId.ToString()));
            }
            var opened = await bridge.OpenAsync(fileId, token);
            Assert.Equal(document.Id, opened.Id); Assert.Equal(document.Title, opened.Title);
            var saved = opened.Sections[0].Pages[0].Blocks[0];
            Assert.Equal(block.Id, saved.Id); Assert.Equal(block.StyleId, saved.StyleId);
            Assert.Equal(run.Id, saved.Runs[0].Id); Assert.Equal(run.Text, saved.Runs[0].Text);
            Assert.True(saved.Runs[0].Bold); Assert.True(saved.Runs[0].Italic);
            Assert.Equal(JsonSerializer.Serialize(other), JsonSerializer.Serialize(opened.Sections[0].Pages[0].Blocks[1]));
            Assert.Equal(JsonSerializer.Serialize(document.Styles), JsonSerializer.Serialize(opened.Styles));
            var committed = (await workspace.Provider.GetCurrentArtifactContentAsync(fileId, token)).Value!.Revision;
            Assert.Equal(Guid.Parse(committed.OwningAppRevisionId!), result.ArtifactRevision!.VersionId);
            Assert.NotEqual(initial.Id, committed.Id);
            Assert.Equal(workspace.Configuration.AppFolders["boards"], (await workspace.Provider.GetAsync(fileId, token)).Value!.ParentId);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(chosen, "Boards")));
            var bytes = await File.ReadAllBytesAsync(Path.Combine(chosen, ".9to1-files", "drive.json"), token);
            var replay = await engine.ApplyActionAsync(context, action, token);
            Assert.False(replay.Succeeded);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(chosen, ".9to1-files", "drive.json"), token));
        }
        finally { Directory.Delete(root, true); }
    }
    // Only the final audit write fails; identity, authority and Files persistence use the real stores.
    private sealed class AuditFailureStore(IHomeCoreStateStore inner, bool failAudit) : IHomeCoreStateStore
    {
        public Task<HomeStateReadResult> ReadAsync(CancellationToken token = default) => inner.ReadAsync(token);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision, CancellationToken token = default)
        {
            if (failAudit && record.Payload.GetRawText().Contains("WRITE_COMMITTED", StringComparison.Ordinal))
                throw new IOException("Injected final audit storage failure.");
            return inner.WriteAsync(record, expectedRecordRevision, token);
        }
    }

}
