using System.Text.Json.Nodes;

namespace HavenOS.Files.CUI.Tests;

/// <summary>Canonical Files persistence/authority boundary tests. Fixture guards model trusted host
/// observations; they are not evidence of a real authenticated CAKE/Home recipient adapter or native UI.</summary>
internal static class ScopedEmailSharingTests
{
    public static async Task RunAllAsync()
    {
        await ViewerScopeSurvivesRestart();
        await EditorScopeRevocationAndReplay();
        await ExistingRecipientRoleReplacementSurvivesRestart();
        await ExistingRecipientExpiryReplacementSurvivesRestart();
        await EffectiveGrantsAndMovePreviewPreserveDirectAndInheritedScopes();
        await ExpiryAndLastPublicationFence();
        await LegacyAndCorruptStateArePreserved();
        await MissingOrReplacedStoreCannotBeRecreated();
        await InheritedSharingCannotQualifyAsPersonalProject();
        Console.WriteLine("Files scoped-sharing domain checks passed; real recipient resolution, email delivery and native/CUI acceptance are NOT RUN.");
    }

    private static async Task ViewerScopeSurvivesRestart()
    {
        using var f = new Fixture(); await f.Initialize();
        var request = await f.Request(FilesAccessRole.Viewer);
        var granted = await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard);
        Require(granted.IsSuccess, "owner grants the exact recipient/item scope");
        var share = granted.Value!;
        Require(share.Share.PrincipalId == f.Recipient && share.RecipientEmail == "recipient@example.test", "email is address metadata, stable account principal is retained");
        Require(share.Share.ItemId == f.Root.Id && share.Share.Role == FilesAccessRole.Viewer, "canonical FileID and requested Viewer role persist");
        var restarted = f.Restart();
        var opened = await restarted.OpenSharedViewAsync(share.Share.Id, f.RecipientGuard);
        Require(opened.IsSuccess, "the same authenticated principal reopens the grant after provider restart");
        var view = opened.Value!;
        Require(view.Location.Id == f.Location && view.Location.Capabilities.Supports(FilesProviderCapabilities.Read), "shared view uses canonical location");
        Require(!view.Location.Capabilities.Supports(FilesProviderCapabilities.Write) &&
            !view.Location.Capabilities.Supports(FilesProviderCapabilities.Rename) &&
            !view.Location.Capabilities.Supports(FilesProviderCapabilities.ChangeFeed), "Viewer exposes no mutation or unfiltered owner history capabilities");
        var top = await view.ListAsync(null, null, null, default);
        Require(top.Items.Count == 1 && top.Items[0].Id == f.Root.Id && top.Items[0].ParentId is null, "scope root is navigable without exposing its parent");
        var children = await view.ListAsync(f.Root.Id, null, null, default);
        Require(children.Items.Count == 1 && children.Items[0].Id == f.Child.Id, "folder descendants refer to the same canonical items");
        Require((await view.GetAsync(f.Child.Id, default)).Value!.Id == f.Child.Id, "child is accessible by canonical identity");
        Require((await view.GetAsync(f.Grandchild.Id, default)).IsSuccess, "folder inheritance reaches deeper descendants");
        Denied(await view.GetAsync(f.Sibling.Id, default), "known sibling ID remains inaccessible");
        await Throws<UnauthorizedAccessException>(() => view.ListAsync(f.Sibling.Id, null, null, default));
        await Throws<UnauthorizedAccessException>(() => view.ListAsync(f.Root.Id, new("", ParentId: f.Sibling.Id), null, default));
        Denied(await view.MutateAsync(f.Rename(f.Child), "forbidden", default), "forged Viewer rename is denied");
        Denied(await restarted.OpenSharedViewAsync(share.Share.Id, f.WrongGuard), "a different authenticated principal cannot use the share ID or known email");
        Denied(await restarted.OpenSharedViewAsync(share.Share.Id, new(f.Recipient, _ => ValueTask.FromResult(false))), "stale authentication cannot open the grant");
        Denied(await restarted.GrantEmailAccessAsync(await f.Request(FilesAccessRole.Editor), f.RecipientGuard), "recipient cannot delegate or enlarge the grant");
        var rejected = await restarted.GrantEmailAccessAsync(await f.Request(FilesAccessRole.Owner), f.OwnerGuard);
        Require(rejected.Error?.Code == FilesErrorCode.InvalidState, "Owner privilege is not delegable through email sharing");
        rejected = await restarted.GrantEmailAccessAsync((await f.Request(FilesAccessRole.Viewer)) with { RecipientPrincipalId = "email:recipient@example.test" }, f.OwnerGuard);
        Require(rejected.Error?.Code == FilesErrorCode.InvalidState, "an address is never minted into a principal");
        rejected = await restarted.GrantEmailAccessAsync((await f.Request(FilesAccessRole.Viewer)) with { RecipientEmail = "Name <recipient@example.test>" }, f.OwnerGuard);
        Require(rejected.Error?.Code == FilesErrorCode.InvalidState, "recipient address input rejects display-name/header syntax");
        var grants = await restarted.GetEmailSharesAsync(f.Root.Id, null, f.OwnerGuard);
        Require(grants.Value!.Items.Single().Share.Id == share.Share.Id, "owner enumerates canonical persisted grant");
        Denied(await restarted.GetEmailSharesAsync(f.Root.Id, null, f.RecipientGuard), "recipient cannot enumerate other grant recipients");
        Require(Directory.EnumerateFiles(f.Directory).All(path => path == f.Path || path == f.Path + ".lock"), "no second private ACL or invitation store is created");
    }

    private static async Task EditorScopeRevocationAndReplay()
    {
        using var f = new Fixture(); await f.Initialize();
        var request = await f.Request(FilesAccessRole.Editor);
        var grant = (await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard)).Value!;
        var events = (await f.Provider.GetChangesAsync(null, 100, default)).Items.Count;
        var duplicate = await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard);
        Require(duplicate.Value!.Share.Id == grant.Share.Id && (await f.Provider.GetChangesAsync(null, 100, default)).Items.Count == events, "grant replay does not duplicate a permission mutation/event");
        var changedReplay = await f.Provider.GrantEmailAccessAsync(request with { Role = FilesAccessRole.Owner }, f.OwnerGuard);
        Require(!changedReplay.IsSuccess, "replay cannot widen permission");
        changedReplay = await f.Provider.GrantEmailAccessAsync(request with { RecipientPrincipalId = f.WrongPrincipal }, f.OwnerGuard);
        Require(changedReplay.Error?.Code == FilesErrorCode.InvalidState, "operation ID cannot be rebound to another recipient");
        var view = (await f.Provider.OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard)).Value!;
        var rename = f.Rename(f.Child);
        var renamed = await view.MutateAsync(rename, "Renamed canonical child", default);
        Require(renamed.IsSuccess, "Editor performs an authorized scoped rename");
        var canonical = (await f.Restart().GetAsync(f.Child.Id, default)).Value!;
        Require(canonical.Id == f.Child.Id && canonical.OwnerPrincipalId == f.Owner && canonical.Name == "Renamed canonical child", "edit changes the original durable object without cloning or transferring ownership");
        var replay = await view.MutateAsync(rename, "Renamed canonical child", default);
        Require(replay.Value == renamed.Value, "rename uses canonical operation idempotency");
        Denied(await view.MutateAsync(f.Rename(f.Sibling), "escape", default), "Editor cannot rename a sibling");
        Denied(await view.MutateAsync(f.Rename(canonical) with { ActorId = f.Owner }, "impersonate", default), "operation actor cannot impersonate the owner");
        foreach (var action in new[] { "Move", "Delete", "CreateFolder", "CommitDataCell", "WriteContent", "Share.Grant" })
            Denied(await view.MutateAsync(f.Rename(canonical) with { Operation = action }, "escape", default), "metadata sharing cannot bypass " + action + " admission");
        var root = (await f.Provider.GetAsync(f.Root.Id, default)).Value!;
        var revokeId = new FilesOperationId(Guid.NewGuid());
        Denied(await f.Provider.RevokeEmailAccessAsync(grant.Share.Id, root.CurrentRevisionId!.Value, revokeId, f.RecipientGuard), "recipient cannot revoke/manage owner ACLs");
        var revoked = await f.Provider.RevokeEmailAccessAsync(grant.Share.Id, root.CurrentRevisionId!.Value, revokeId, f.OwnerGuard);
        Require(revoked.Value!.Share.IsRevoked, "owner revocation commits durably");
        var replayRevoke = await f.Restart().RevokeEmailAccessAsync(grant.Share.Id, root.CurrentRevisionId!.Value, revokeId, f.OwnerGuard);
        Require(replayRevoke.Value!.Share.IsRevoked, "exact revoke replay survives restart");
        Denied(await view.GetAsync(f.Child.Id, default), "an existing view loses future read access immediately");
        Denied(await view.MutateAsync(rename, "Renamed canonical child", default), "revocation also denies an otherwise idempotent editor replay");
        await Throws<UnauthorizedAccessException>(() => view.ListAsync(null, null, null, default));
        Denied(await f.Restart().OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard), "restart does not restore revoked access");
        var oldGrantReplay = await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard);
        Require(oldGrantReplay.Value!.Share.IsRevoked, "replaying the original grant cannot revive it or report it currently active");
        Require((await f.Provider.GetAsync(f.Sibling.Id, default)).Value!.Name == f.Sibling.Name, "unrelated canonical data remains unchanged");
    }

    private static async Task ExistingRecipientRoleReplacementSurvivesRestart()
    {
        using var f = new Fixture(); await f.Initialize();
        var initial = (await f.Request(FilesAccessRole.Viewer)) with { ExpiresAt = f.Clock.Now.AddHours(1) };
        var viewer = (await f.Provider.GrantEmailAccessAsync(initial, f.OwnerGuard)).Value!;
        var oldViewer = (await f.Provider.OpenSharedViewAsync(viewer.Share.Id, f.RecipientGuard)).Value!;
        var root = (await f.Provider.GetAsync(f.Root.Id, default)).Value!;
        var upgrade = initial with { ExpectedRevision = root.CurrentRevisionId!.Value,
            Role = FilesAccessRole.Editor, OperationId = new(Guid.NewGuid()) };
        var editorResult = await f.Restart().GrantEmailAccessAsync(upgrade, f.OwnerGuard);
        Require(editorResult.IsSuccess, "an explicit role decision replaces the existing recipient grant");
        var editor = editorResult.Value!;
        Require(editor.Share.Id != viewer.Share.Id && editor.Share.Role == FilesAccessRole.Editor && !editor.Share.IsRevoked,
            "replacement has a new active ShareID and the reviewed Editor role");
        Require(editor.Share.PrincipalId == viewer.Share.PrincipalId && editor.RecipientEmail == viewer.RecipientEmail
            && editor.Share.ExpiresAt == viewer.Share.ExpiresAt, "role changes retain principal, address and expiry");
        Denied(await oldViewer.GetAsync(f.Child.Id, default), "a retained Viewer grant does not become an Editor capability");
        var restarted = f.Restart();
        var grants = (await restarted.GetEmailSharesAsync(f.Root.Id, null, f.OwnerGuard)).Value!.Items;
        Require(grants.Single(item => item.Share.Id == viewer.Share.Id).Share.IsRevoked, "old grant revocation survives restart");
        Require(grants.Single(item => item.Share.Id == editor.Share.Id).Share.Role == FilesAccessRole.Editor,
            "the replacement role survives restart on the same canonical item");
        var editorView = (await restarted.OpenSharedViewAsync(editor.Share.Id, f.RecipientGuard)).Value!;
        Require((await editorView.MutateAsync(f.Rename(f.Child), "Shared metadata edit", default)).IsSuccess,
            "the accepted Editor scope permits its implemented metadata rename");
        var before = (await restarted.GetChangesAsync(null, 100, default)).Items.Count;
        var replay = await restarted.GrantEmailAccessAsync(upgrade, f.OwnerGuard);
        Require(replay.Value!.Share.Id == editor.Share.Id && (await restarted.GetChangesAsync(null, 100, default)).Items.Count == before,
            "repeating the exact role-change operation cannot append a second grant or event");
        root = (await restarted.GetAsync(f.Root.Id, default)).Value!;
        var downgrade = upgrade with { ExpectedRevision = root.CurrentRevisionId!.Value,
            Role = FilesAccessRole.Viewer, OperationId = new(Guid.NewGuid()) };
        var currentViewer = (await restarted.GrantEmailAccessAsync(downgrade, f.OwnerGuard)).Value!;
        Require(currentViewer.Share.Id != editor.Share.Id && currentViewer.Share.Role == FilesAccessRole.Viewer,
            "downgrade records an independent reviewed Viewer grant");
        Denied(await editorView.GetAsync(f.Child.Id, default), "retained Editor views lose future access after replacement");
        Denied(await editorView.MutateAsync(f.Rename(f.Child), "must not write", default), "old Editor cannot keep mutation capability");
        var activeViewer = (await f.Restart().OpenSharedViewAsync(currentViewer.Share.Id, f.RecipientGuard)).Value!;
        Require(!activeViewer.Location.Capabilities.Supports(FilesProviderCapabilities.Rename), "reopened Viewer advertises no rename capability");
        var oldReplay = await restarted.GrantEmailAccessAsync(upgrade, f.OwnerGuard);
        Require(oldReplay.Value!.Share.IsRevoked, "replaying an obsolete upgrade cannot revive Editor access");
        var active = (await restarted.GetEmailSharesAsync(f.Root.Id, null, f.OwnerGuard)).Value!.Items.Where(item => !item.Share.IsRevoked).ToArray();
        Require(active.Length == 1 && active[0].Share.Id == currentViewer.Share.Id,
            "only the latest explicit direct scope remains active, without union or cloned files");
    }

    private static async Task ExistingRecipientExpiryReplacementSurvivesRestart()
    {
        using var f = new Fixture(); await f.Initialize();
        var initial = await f.Request(FilesAccessRole.Viewer);
        var original = (await f.Provider.GrantEmailAccessAsync(initial, f.OwnerGuard)).Value!;
        var oldView = (await f.Provider.OpenSharedViewAsync(original.Share.Id, f.RecipientGuard)).Value!;
        var root = (await f.Provider.GetAsync(f.Root.Id, default)).Value!;
        var expires = f.Clock.Now.AddMinutes(30);
        var changed = initial with { ExpectedRevision = root.CurrentRevisionId!.Value, ExpiresAt = expires,
            OperationId = new(Guid.NewGuid()) };
        var result = await f.Restart().GrantEmailAccessAsync(changed, f.OwnerGuard);
        Require(result.IsSuccess, "same-role expiry update commits against the actual current revision");
        var current = result.Value!;
        Require(current.Share.PrincipalId == original.Share.PrincipalId && current.RecipientEmail == original.RecipientEmail &&
            current.Share.Role == original.Share.Role && current.Share.ExpiresAt == expires, "recipient/role preserved; reviewed expiry retained");
        Denied(await oldView.GetAsync(f.Child.Id, default), "old grant does not survive a replacement expiry decision");
        var restarted = f.Restart();
        var view = (await restarted.OpenSharedViewAsync(current.Share.Id, f.RecipientGuard)).Value!;
        Require((await view.GetAsync(f.Child.Id, default)).IsSuccess, "new direct expiry scope reaches its canonical descendants before expiry");
        f.Clock.Now = expires;
        Denied(await view.GetAsync(f.Child.Id, default), "already-open recipient view stops future reads at exact new expiry");
        Denied(await f.Restart().OpenSharedViewAsync(current.Share.Id, f.RecipientGuard), "expiry remains enforced after restart");
        var replay = await f.Provider.GrantEmailAccessAsync(changed, f.OwnerGuard);
        Require(replay.IsSuccess && replay.Value!.Share.Id == current.Share.Id, "exact operation replay cannot create another grant");
    }

    private static async Task EffectiveGrantsAndMovePreviewPreserveDirectAndInheritedScopes()
    {
        using var f = new Fixture(); await f.Initialize();
        var rootGrant = await f.Request(FilesAccessRole.Viewer);
        Require((await f.Provider.GrantEmailAccessAsync(rootGrant, f.OwnerGuard)).IsSuccess, "ancestor Viewer grant commits");
        var child = (await f.Provider.GetAsync(f.Child.Id, default)).Value!;
        var childGrant = await f.Request(FilesAccessRole.Editor);
        childGrant = childGrant with
        {
            ItemId = f.Child.Id,
            ExpectedRevision = child.CurrentRevisionId!.Value,
            RecipientPrincipalId = f.WrongPrincipal,
        };
        Require((await f.Provider.GrantEmailAccessAsync(childGrant, f.OwnerGuard)).IsSuccess, "child Editor grant commits independently");

        var grants = await f.Provider.GetGrantsAsync(f.Child.Id, null);
        Require(grants.Items.Count == 3, "grant listing includes owner, direct and inherited scopes");
        var owner = grants.Items.Single(grant => grant.Role == FilesAccessRole.Owner);
        var inherited = grants.Items.Single(grant => grant.PrincipalId == f.Recipient);
        var direct = grants.Items.Single(grant => grant.PrincipalId == f.WrongPrincipal);
        Require(owner.PrincipalId == f.Owner && owner.Origin == FilesGrantOrigin.Direct &&
            owner.GrantedOnItemId == f.Child.Id, "canonical item owner is explicit in the grant projection");
        Require(inherited.Role == FilesAccessRole.Viewer && inherited.Origin == FilesGrantOrigin.Inherited &&
            inherited.GrantedOnItemId == f.Root.Id, "ancestor grant is identified as inherited from its canonical folder");
        Require(direct.Role == FilesAccessRole.Editor && direct.Origin == FilesGrantOrigin.Direct &&
            direct.GrantedOnItemId == f.Child.Id, "item grant remains a direct Editor scope");

        var preview = await f.Provider.PreviewMovePermissionsAsync(f.Child.Id, f.Sibling.Id,
            FilesPermissionInheritancePolicy.RequireApprovalOnChange);
        Require(preview.CurrentEffectiveGrants.Any(grant => grant.PrincipalId == f.Recipient && grant.Origin == FilesGrantOrigin.Inherited) &&
            !preview.ResultingEffectiveGrants.Any(grant => grant.PrincipalId == f.Recipient),
            "moving under a private folder removes only the source ancestor's inherited grant");
        Require(preview.ResultingEffectiveGrants.Any(grant => grant.PrincipalId == f.WrongPrincipal && grant.Origin == FilesGrantOrigin.Direct),
            "moving preserves an explicit direct grant on the canonical item");
        Require(preview.ChangedPrincipalIds.SequenceEqual(new[] { f.Recipient }) && preview.RequiresApproval,
            "the preview identifies the affected principal and marks policy-required review");
        Require(preview.AffectedItems!.Select(change => change.ItemId).OrderBy(id => id.Value)
                .SequenceEqual(new[] { f.Child.Id, f.Grandchild.Id }.OrderBy(id => id.Value)) &&
            preview.AffectedItems.All(change => !change.ResultingEffectiveGrants.Any(grant => grant.PrincipalId == f.Recipient)),
            "folder move preview includes inherited permission effects for every descendant");
        Require((await f.Provider.GetAsync(f.Child.Id, default)).Value!.ParentId == f.Root.Id,
            "preview does not move the item or mutate its ACL");

        var move = new FilesOperation(new(Guid.NewGuid()), f.Owner, f.Child.Id, f.Root.Id, f.Sibling.Id,
            "Move", child.CurrentRevisionId, null, FilesOperationState.Pending, f.Clock.Now, f.Clock.Now, null, null);
        var policyRequired = await f.Provider.MutateAsync(move, null, default);
        Require(policyRequired.Error?.Code == FilesErrorCode.PermissionRequired,
            "a move with changed access cannot commit before the caller selects an explicit policy");
        var preserved = await f.Provider.MutateAsync(move with
        {
            PermissionInheritancePolicy = FilesPermissionInheritancePolicy.PreserveExplicitGrants,
        }, null, default);
        Require(preserved.IsSuccess && preserved.Value!.PermissionInheritancePolicy == FilesPermissionInheritancePolicy.PreserveExplicitGrants,
            "the selected preserve policy is recorded on the durable move");
        var shares = (await f.Provider.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items;
        var preservedRecipient = shares.Single(grant => grant.Share.PrincipalId == f.Recipient);
        Require(!preservedRecipient.Share.IsRevoked && preservedRecipient.Share.Role == FilesAccessRole.Viewer,
            "the move atomically converts lost inherited access into an explicit direct grant on the moved folder");
        Require((await f.Provider.OpenSharedViewAsync(preservedRecipient.Share.Id, f.RecipientGuard)).IsSuccess,
            "the same recipient retains access to the canonical moved folder after restartable publication");
    }

    private static async Task ExpiryAndLastPublicationFence()
    {
        using var f = new Fixture(); await f.Initialize();
        var initial = await f.Request(FilesAccessRole.Editor);
        var request = initial with { ExpiresAt = f.Clock.Now.AddMinutes(5) };
        var stale = await f.Provider.GrantEmailAccessAsync(request with { StoreId = Guid.NewGuid() }, f.OwnerGuard);
        Denied(stale, "grant is bound to the actual store UUID");
        var before = await File.ReadAllBytesAsync(f.Path);
        var calls = 0;
        var retiredOwner = new FilesCommitAuthorityGuard(f.Owner, _ => ValueTask.FromResult(++calls < 2));
        Denied(await f.Provider.GrantEmailAccessAsync(request, retiredOwner), "Home withdrawal at final publication denies the grant");
        byte[] afterRetiredGrant = await File.ReadAllBytesAsync(f.Path);
        Require(before.SequenceEqual(afterRetiredGrant), "retired-owner grant leaves canonical state bytes unchanged");
        var grant = (await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard)).Value!;
        var recipientCalls = 0; var armed = false;
        var crossesExpiry = new FilesCommitAuthorityGuard(f.Recipient, _ =>
        {
            if (armed && ++recipientCalls == 2) f.Clock.Now = request.ExpiresAt!.Value;
            return ValueTask.FromResult(true);
        });
        var view = (await f.Provider.OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard)).Value!;
        var guardedView = (await f.Provider.OpenSharedViewAsync(grant.Share.Id, crossesExpiry)).Value!;
        armed = true;
        Denied(await guardedView.GetAsync(f.Child.Id, default), "expiry at the last read check denies already opened view access");
        Denied(await view.MutateAsync(f.Rename(f.Child), "expired", default), "expired Editor cannot mutate");
        f.Clock.Now = request.ExpiresAt!.Value.AddMinutes(-1);
        recipientCalls = 0;
        before = await File.ReadAllBytesAsync(f.Path);
        Denied(await guardedView.MutateAsync(f.Rename(f.Child), "expires during commit", default), "expiry at the last commit check prevents publication");
        byte[] afterExpiringMutation = await File.ReadAllBytesAsync(f.Path);
        Require(before.SequenceEqual(afterExpiringMutation), "expiry during serialization preserves canonical state bytes");
        Denied(await f.Restart().OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard), "expiry survives restart");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Throws<OperationCanceledException>(() => f.Provider.GrantEmailAccessAsync(awaitableRequest(), f.OwnerGuard, canceled.Token));
        byte[] afterCanceledGrant = await File.ReadAllBytesAsync(f.Path);
        Require(before.SequenceEqual(afterCanceledGrant), "cancellation cannot create another grant");
        FilesEmailShareRequest awaitableRequest() => request with { OperationId = new(Guid.NewGuid()) };
    }

    private static async Task LegacyAndCorruptStateArePreserved()
    {
        using var f = new Fixture(); await f.Initialize();
        var document = JsonNode.Parse(await File.ReadAllTextAsync(f.Path))!;
        var state = document["state"]!.AsObject(); state.Remove("shares"); state.Remove("shareJournal");
        await File.WriteAllTextAsync(f.Path, document.ToJsonString());
        var before = await File.ReadAllBytesAsync(f.Path);
        var legacy = f.Restart();
        Require((await legacy.GetAsync(f.Root.Id, default)).IsSuccess, "legacy canonical items remain readable");
        Require((await legacy.GetEmailSharesAsync(f.Root.Id, null, f.OwnerGuard)).Value!.Items.Count == 0, "legacy state adds no implicit grants");
        byte[] afterLegacyRead = await File.ReadAllBytesAsync(f.Path);
        Require(before.SequenceEqual(afterLegacyRead), "shared read does not adopt, migrate or rewrite legacy state");
        var request = await f.Request(FilesAccessRole.Viewer);
        state["items"]![0]!["metadata"]!["isShared"] = true;
        await File.WriteAllTextAsync(f.Path, document.ToJsonString());
        before = await File.ReadAllBytesAsync(f.Path);
        Require((await f.Provider.GetAsync(f.Child.Id, default)).Value!.IsShared, "untracked ancestor sharing remains effective");
        Require((await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard)).Error?.Code == FilesErrorCode.PermissionDenied, "unknown prior sharing cannot be overwritten");
        byte[] afterUnknownSharing = await File.ReadAllBytesAsync(f.Path);
        Require(before.SequenceEqual(afterUnknownSharing), "unknown sharing remains intact");
        state["shares"] = null;
        await File.WriteAllTextAsync(f.Path, document.ToJsonString());
        before = await File.ReadAllBytesAsync(f.Path);
        await Throws<InvalidDataException>(() => f.Restart().OpenSharedViewAsync(new(Guid.NewGuid()), f.RecipientGuard));
        byte[] afterCorruptRead = await File.ReadAllBytesAsync(f.Path);
        Require(before.SequenceEqual(afterCorruptRead), "corrupt sharing state is preserved and fails closed");
    }

    private static async Task MissingOrReplacedStoreCannotBeRecreated()
    {
        using var f = new Fixture(); await f.Initialize();
        var request = await f.Request(FilesAccessRole.Editor);
        var grant = (await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard)).Value!;
        var view = (await f.Provider.OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard)).Value!;
        var document = JsonNode.Parse(await File.ReadAllTextAsync(f.Path))!;
        document["state"]!["storeId"] = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(f.Path, document.ToJsonString());
        var before = await File.ReadAllBytesAsync(f.Path);
        Denied(await view.GetAsync(f.Child.Id, default), "existing view is bound to its original store UUID");
        Denied(await view.MutateAsync(f.Rename(f.Child), "replacement", default), "store replacement cannot redirect an old scoped commit");
        byte[] afterReplacementRefusal = await File.ReadAllBytesAsync(f.Path);
        Require(before.SequenceEqual(afterReplacementRefusal), "replacement-store refusal changes no state");
        File.Delete(f.Path);
        Require((await f.Provider.OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard)).Error?.Code == FilesErrorCode.StorageUnavailable, "missing state is explicitly unavailable");
        Require((await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard)).Error?.Code == FilesErrorCode.StorageUnavailable, "grant never creates a replacement empty canonical store");
        Require((await f.Provider.RevokeEmailAccessAsync(grant.Share.Id, request.ExpectedRevision, new(Guid.NewGuid()), f.OwnerGuard)).Error?.Code == FilesErrorCode.StorageUnavailable, "revoke never recreates missing state");
        Denied(await view.MutateAsync(f.Rename(f.Child), "missing", default), "old view cannot recreate missing state through a mutation");
        Require(!File.Exists(f.Path), "all denied missing-store paths preserve its absence");
    }

    private static async Task InheritedSharingCannotQualifyAsPersonalProject()
    {
        var profile = Guid.NewGuid();
        using var f = new Fixture("local-profile:" + profile.ToString("D")); await f.Initialize();
        var root = System.IO.Path.Combine(f.Directory, "materialized");
        var project = System.IO.Path.Combine(root, "project"); System.IO.Directory.CreateDirectory(project);
        var resolver = new FilesWorkspaceDirectoryResolver(System.IO.Path.Combine(f.Directory, "bindings.json"),
            _ => null, id => id == profile ? f.Provider : null);
        const string app = "dev.project.sharing-regression";
        Require((await resolver.RegisterProfileAsync(profile, f.Child.Id, app, project)).IsSuccess, "real project registration");
        var store = (await f.Provider.GetStoreEvidenceAsync()).StoreId;
        var retained = new List<Task>();
        await Observe(false);
        var request = (await f.Request(FilesAccessRole.Viewer)) with { ExpiresAt = f.Clock.Now.AddMinutes(5) };
        Require((await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard)).IsSuccess, "ancestor grant");
        await Observe(true);
        var bytes = await File.ReadAllBytesAsync(f.Path);
        f.Clock.Now = request.ExpiresAt!.Value;
        await Observe(false);
        byte[] afterExpiryProjection = await File.ReadAllBytesAsync(f.Path);
        Require(bytes.SequenceEqual(afterExpiryProjection), "expiry projection does not rewrite canonical state");
        var parent = (await f.Provider.GrantEmailAccessAsync(await f.Request(FilesAccessRole.Viewer), f.OwnerGuard)).Value!;
        var childRequest = (await f.Request(FilesAccessRole.Viewer)) with { ItemId = f.Child.Id,
            ExpectedRevision = (await f.Provider.GetAsync(f.Child.Id, default)).Value!.CurrentRevisionId!.Value };
        var child = (await f.Provider.GrantEmailAccessAsync(childRequest, f.OwnerGuard)).Value!;
        await Revoke(parent); await Observe(true); // Direct child scope survives ancestor revocation.
        await Revoke(child); await Observe(false);
        await Task.WhenAll(retained);

        async Task Revoke(FilesEmailShare grant)
        {
            var item = (await f.Provider.GetAsync(grant.Share.ItemId, default)).Value!;
            Require((await f.Provider.RevokeEmailAccessAsync(grant.Share.Id, item.CurrentRevisionId!.Value,
                new(Guid.NewGuid()), f.OwnerGuard)).IsSuccess, "exact canonical revoke");
        }
        async Task Observe(bool shared)
        {
            var ordinary = (await f.Provider.GetAsync(f.Child.Id, default)).Value!;
            var original = (await f.Provider.GetForOriginalStoreAsync(store, f.Child.Id, default)).Value!;
            var callback = (await f.Provider.GetForOriginalStoreAsync(store, f.Child.Id, default, a => a(), retained.Add)).Value!;
            var revision = (await f.Provider.GetForOriginalStoreAtRevisionAsync(store, f.Child.Id, ordinary.CurrentRevisionId!.Value)).Value!;
            var listed = (await f.Provider.ListAsync(f.Root.Id, null, null, default)).Items.Single();
            Require(new[] { ordinary, original, callback, revision, listed }.All(item => item.IsShared == shared), "all owner/original reads expose effective sharing");
            Task Current() => resolver.ObserveOriginalCurrentProjectRegistrationAsync(profile, app, f.Provider, store, root, a => a(), retained.Add, default);
            Task Execution() => resolver.ObserveOriginalExecutionRegistrationAsync(profile, app, f.Child.Id, f.Provider, store, project, a => a(), retained.Add, default);
            if (shared) { await Throws<AggregateException>(Current); await Throws<AggregateException>(Execution); }
            else { await Current(); await Execution(); }
        }
    }

    private static void Denied<T>(FilesResult<T> result, string message) => Require(result.Error?.Code == FilesErrorCode.PermissionDenied, message);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "files-sharing-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "files.json");
        public string Owner { get; } = "account:" + Guid.NewGuid().ToString("D");
        public string Recipient { get; } = "account:" + Guid.NewGuid().ToString("D");
        public string WrongPrincipal { get; } = "account:" + Guid.NewGuid().ToString("D");
        public FilesLocationId Location { get; } = new(Guid.NewGuid());
        public Clock Clock { get; } = new();
        public DurableDriveProvider Provider { get; }
        public HostedItemMetadata Root = null!, Child = null!, Grandchild = null!, Sibling = null!;
        public FilesCommitAuthorityGuard OwnerGuard => new(Owner, _ => ValueTask.FromResult(true));
        public FilesCommitAuthorityGuard RecipientGuard => new(Recipient, _ => ValueTask.FromResult(true));
        public FilesCommitAuthorityGuard WrongGuard => new(WrongPrincipal, _ => ValueTask.FromResult(true));
        public Fixture(string? owner = null) { if (owner is not null) Owner = owner; System.IO.Directory.CreateDirectory(Directory); Provider = Restart(); }
        public DurableDriveProvider Restart() => new(Path, Location, Owner, Clock);
        public async Task Initialize() { Root = await Create(null, "Shared folder"); Child = await Create(Root.Id, "Canonical child"); Grandchild = await Create(Child.Id, "Nested child"); Sibling = await Create(null, "Private sibling"); }
        public async Task<FilesEmailShareRequest> Request(FilesAccessRole role)
        {
            var evidence = await Provider.GetStoreEvidenceAsync(); var root = (await Provider.GetAsync(Root.Id, default)).Value!;
            return new(evidence.StoreId, Root.Id, root.CurrentRevisionId!.Value, Recipient, "recipient@example.test", role, null, new(Guid.NewGuid()));
        }
        public FilesOperation Rename(HostedItemMetadata item) => new(new(Guid.NewGuid()), Recipient, item.Id,
            item.ParentId, null, "Rename", item.CurrentRevisionId, null, FilesOperationState.Pending, Clock.Now, Clock.Now, null, null);
        private async Task<HostedItemMetadata> Create(HostedItemId? parent, string name)
        {
            var operation = new FilesOperation(new(Guid.NewGuid()), Owner, HostedItemId.New(), null, parent, "CreateFolder",
                null, null, FilesOperationState.Pending, Clock.Now, Clock.Now, null, null);
            Require((await Provider.MutateAsync(operation, name, default)).IsSuccess, "fixture canonical folder creation");
            return (await Provider.GetAsync(operation.ItemId, default)).Value!;
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
