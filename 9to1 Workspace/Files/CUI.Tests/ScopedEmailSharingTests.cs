using System.Text.Json.Nodes;

namespace HavenOS.Files.CUI.Tests;

/// <summary>Canonical Files persistence/authority boundary tests. Fixture guards model trusted host
/// observations; they are not evidence of a real authenticated CAKE/Home recipient adapter or native UI.</summary>
internal static class ScopedEmailSharingTests
{
    public static async Task RunAllAsync()
    {
        await ViewerScopeSurvivesRestart();
        await AncestorShareListingDistinguishesInheritedGrants();
        await InheritedGrantExpiryAndRevocationSurviveRestart();
        await CorruptAncestorChainFailsClosedForAccessReview();
        await UnknownAncestorAclDoesNotReturnPartialKnownGrants();
        await SharePagesKeepOrderAndRejectChangedAclSnapshots();
        await EditorScopeRevocationAndReplay();
        await ExistingRecipientRoleReplacementSurvivesRestart();
        await ExistingRecipientExpiryReplacementSurvivesRestart();
        await ExpiryAndLastPublicationFence();
        await LegacyAndCorruptStateArePreserved();
        await MissingOrReplacedStoreCannotBeRecreated();
        await InheritedSharingCannotQualifyAsPersonalProject();
        Console.WriteLine("Files scoped-sharing domain checks passed; real recipient resolution, email delivery and native/CUI acceptance are NOT RUN.");
    }

    private static async Task AncestorShareListingDistinguishesInheritedGrants()
    {
        using var f = new Fixture(); await f.Initialize();
        var parent = (await f.Provider.GrantEmailAccessAsync(await f.Request(FilesAccessRole.Viewer), f.OwnerGuard)).Value!;
        var inherited = (await f.Provider.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items;
        var row = inherited.Single();
        Require(row.Share.Id == parent.Share.Id && row.Share.ItemId == f.Root.Id
            && row.Origin == FilesGrantOrigin.Inherited && row.InheritedFrom == f.Root.Id,
            "child access review identifies the original active ancestor grant without cloning or retargeting it");
        var directRequest = (await f.Request(FilesAccessRole.Editor)) with { ItemId = f.Child.Id,
            ExpectedRevision = (await f.Provider.GetAsync(f.Child.Id, default)).Value!.CurrentRevisionId!.Value };
        var direct = (await f.Provider.GrantEmailAccessAsync(directRequest, f.OwnerGuard)).Value!;
        var rows = (await f.Restart().GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items;
        Require(rows.Single(entry => entry.Share.Id == parent.Share.Id).Origin == FilesGrantOrigin.Inherited
            && rows.Single(entry => entry.Share.Id == direct.Share.Id).Origin == FilesGrantOrigin.Direct,
            "direct and inherited grants remain distinguishable after persisted provider restart");
        var deepRows = (await f.Provider.GetEmailSharesAsync(f.Grandchild.Id, null, f.OwnerGuard)).Value!.Items;
        Require(deepRows.Count == 2 && deepRows.All(entry => entry.Origin == FilesGrantOrigin.Inherited)
            && deepRows.Select(entry => entry.InheritedFrom).ToHashSet().SetEquals(new HostedItemId?[] { f.Root.Id, f.Child.Id })
            && deepRows.Select(entry => entry.Share.Id).ToHashSet().SetEquals(new[] { parent.Share.Id, direct.Share.Id }),
            "deep descendants retain each original ancestor ShareID and provenance without collapsing roles");
        var childRevision = (await f.Provider.GetAsync(f.Child.Id, default)).Value!.CurrentRevisionId!.Value;
        var revoked = await f.Provider.RevokeEmailAccessAsync(direct.Share.Id, childRevision, new(Guid.NewGuid()), f.OwnerGuard);
        Require(revoked.IsSuccess, "revoking a child direct grant leaves the parent ACL decision independent");
        var afterRevoke = (await f.Restart().GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items;
        Require(afterRevoke.Single(row => row.Share.Id == parent.Share.Id).Origin == FilesGrantOrigin.Inherited
            && afterRevoke.Single(row => row.Share.Id == direct.Share.Id).Origin == FilesGrantOrigin.Direct
            && afterRevoke.Single(row => row.Share.Id == direct.Share.Id).Share.IsRevoked,
            "direct revocation preserves inherited access and direct revocation history after restart");
        var inheritedView = (await f.Restart().OpenSharedViewAsync(parent.Share.Id, f.RecipientGuard)).Value!;
        Require((await inheritedView.GetAsync(f.Grandchild.Id, default)).IsSuccess,
            "parent grant remains independently usable for descendants after child direct revocation");
        var stored = JsonNode.Parse(await File.ReadAllTextAsync(f.Path))!["state"]!;
        Require(stored["shares"]!.AsArray().All(entry => !entry!.AsObject().ContainsKey("origin")
                && !entry!.AsObject().ContainsKey("inheritedFrom"))
            && stored["shareJournal"]!.AsArray().All(entry => !entry!["result"]!.AsObject().ContainsKey("origin")
                && !entry!["result"]!.AsObject().ContainsKey("inheritedFrom")),
            "read-time provenance never changes the persisted ACL or journal schema");
    }

    private static async Task InheritedGrantExpiryAndRevocationSurviveRestart()
    {
        using (var f = new Fixture())
        {
            await f.Initialize();
            var expires = f.Clock.Now.AddMinutes(5);
            var grant = (await f.Provider.GrantEmailAccessAsync(
                (await f.Request(FilesAccessRole.Viewer)) with { ExpiresAt = expires }, f.OwnerGuard)).Value!;
            var rows = (await f.Provider.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items;
            Require(rows.Single().Origin == FilesGrantOrigin.Inherited, "active parent grant appears on child access review");
            var open = (await f.Provider.OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard)).Value!;
            Require((await open.GetAsync(f.Child.Id, default)).IsSuccess, "inherited recipient can open the canonical child before expiry");
            var persisted = await File.ReadAllBytesAsync(f.Path);
            f.Clock.Now = expires;
            Denied(await open.GetAsync(f.Child.Id, default), "an already-open inherited view stops at exact grant expiry");
            var restarted = f.Restart();
            Require((await restarted.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items.Count == 0,
                "expired ancestor grant is omitted from inherited access after restart");
            var expiredOwnerHistory = (await restarted.GetEmailSharesAsync(f.Root.Id, null, f.OwnerGuard)).Value!.Items;
            Require(expiredOwnerHistory.Count == 1 && expiredOwnerHistory[0].Share.Id == grant.Share.Id
                && expiredOwnerHistory[0].Share.ExpiresAt == expires && !expiredOwnerHistory[0].Share.IsRevoked,
                "expired tracked direct grant remains distinguishable from missing/untracked ACL data");
            Denied(await restarted.OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard), "expired persisted grant cannot reopen after restart");
            Require(persisted.SequenceEqual(await File.ReadAllBytesAsync(f.Path)), "expiry projection and reopen refusal do not rewrite persisted grant state");
        }

        using (var f = new Fixture())
        {
            await f.Initialize();
            var grant = (await f.Provider.GrantEmailAccessAsync(await f.Request(FilesAccessRole.Editor), f.OwnerGuard)).Value!;
            var open = (await f.Provider.OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard)).Value!;
            Require((await open.GetAsync(f.Child.Id, default)).IsSuccess, "active inherited Editor opens the same canonical child");
            var folder = (await f.Provider.GetAsync(f.Root.Id, default)).Value!;
            var revoked = await f.Restart().RevokeEmailAccessAsync(grant.Share.Id, folder.CurrentRevisionId!.Value,
                new(Guid.NewGuid()), f.OwnerGuard);
            Require(revoked.IsSuccess && revoked.Value!.Share.IsRevoked, "owner revocation persists the exact ancestor ShareID");
            Denied(await open.GetAsync(f.Child.Id, default), "revocation immediately removes future access from an already-open inherited view");
            var restarted = f.Restart();
            Require((await restarted.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items.Count == 0,
                "revoked ancestor grant is absent from inherited listing after restart");
            Denied(await restarted.OpenSharedViewAsync(grant.Share.Id, f.RecipientGuard), "revoked persisted grant cannot reopen after restart");
            var ownerHistory = (await restarted.GetEmailSharesAsync(f.Root.Id, null, f.OwnerGuard)).Value!.Items.Single();
            Require(ownerHistory.Share.Id == grant.Share.Id && ownerHistory.Share.IsRevoked
                && ownerHistory.Origin == FilesGrantOrigin.Direct, "owner retains direct revocation history without an inherited duplicate");
        }
    }

    private static async Task CorruptAncestorChainFailsClosedForAccessReview()
    {
        using var f = new Fixture(); await f.Initialize();
        await f.Provider.GrantEmailAccessAsync(await f.Request(FilesAccessRole.Viewer), f.OwnerGuard);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(f.Path))!;
        var state = document["state"]!;
        var child = state["items"]!.AsArray().Single(entry =>
            entry!["metadata"]!["id"]!.GetValue<string>() == f.Child.Id.Value.ToString("D"));
        child!["metadata"]!["parentId"] = f.Child.Id.Value.ToString("D");
        await File.WriteAllTextAsync(f.Path, document.ToJsonString());
        var before = await File.ReadAllBytesAsync(f.Path);
        var result = await f.Restart().GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard);
        Require(result.Error?.Code == FilesErrorCode.InvalidState && result.Value is null,
            "a cyclic folder chain returns a structured refusal instead of partial inherited ACL data");
        Require(before.SequenceEqual(await File.ReadAllBytesAsync(f.Path)), "corrupt ancestry refusal preserves the original persisted state bytes");
    }

    private static async Task UnknownAncestorAclDoesNotReturnPartialKnownGrants()
    {
        using var f = new Fixture(); await f.Initialize();
        var child = await f.Provider.GetAsync(f.Child.Id, default);
        var request = (await f.Request(FilesAccessRole.Viewer)) with
        {
            ItemId = f.Child.Id,
            ExpectedRevision = child.Value!.CurrentRevisionId!.Value
        };
        var knownChildGrant = (await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard)).Value!;
        var document = JsonNode.Parse(await File.ReadAllTextAsync(f.Path))!;
        var state = document["state"]!;
        var root = state["items"]!.AsArray().Single(entry =>
            entry!["metadata"]!["id"]!.GetValue<string>() == f.Root.Id.Value.ToString("D"));
        root!["metadata"]!["isShared"] = true; // Legacy/untracked ancestor ACL, alongside a known child grant.
        await File.WriteAllTextAsync(f.Path, document.ToJsonString());
        var before = await File.ReadAllBytesAsync(f.Path);
        var restarted = f.Restart();
        var result = await restarted.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard);
        Require(result.Error?.Code == FilesErrorCode.InvalidState && result.Value is null,
            "unknown ancestor ACL refuses enumeration instead of returning only the known child grant");
        Require(knownChildGrant.Share.ItemId == f.Child.Id && before.SequenceEqual(await File.ReadAllBytesAsync(f.Path)),
            "unknown-ancestor refusal preserves known grants and exact stored bytes");
    }

    private static async Task SharePagesKeepOrderAndRejectChangedAclSnapshots()
    {
        using var f = new Fixture(); await f.Initialize();
        var expires = f.Clock.Now.AddMinutes(1);
        var direct = new List<FilesEmailShare>();
        var inherited = new List<FilesEmailShare>();
        for (var i = 0; i < 51; i++)
            direct.Add(await Grant(f.Child.Id, "direct", i, i == 0 ? expires : null));
        for (var i = 0; i < 52; i++)
            inherited.Add(await Grant(f.Root.Id, "parent", i, i == 0 ? expires : null));

        await Revoke(direct[1]);
        await Revoke(inherited[1]);
        var first = (await f.Provider.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!;
        Require(first.Items.Count == 100 && first.NextPageToken is not null,
            "the stable access list produces a continuation after its first hundred rows");
        var malformed = await f.Provider.GetEmailSharesAsync(f.Child.Id, "not-a-share-cursor", f.OwnerGuard);
        var oversized = await f.Provider.GetEmailSharesAsync(f.Child.Id, new string('x', 4096), f.OwnerGuard);
        Require(malformed.Error?.Code == FilesErrorCode.InvalidCursor && malformed.Value is null
            && oversized.Error?.Code == FilesErrorCode.InvalidCursor && oversized.Value is null,
            "malformed and oversized cursors fail structurally before enumeration");
        var wrongTarget = await f.Provider.GetEmailSharesAsync(f.Grandchild.Id, first.NextPageToken, f.OwnerGuard);
        Require(wrongTarget.Error?.Code == FilesErrorCode.InvalidCursor && wrongTarget.Value is null,
            "a cursor cannot be reused for another target even when its inherited source grants overlap");
        var second = (await f.Provider.GetEmailSharesAsync(f.Child.Id, first.NextPageToken, f.OwnerGuard)).Value!;
        var complete = first.Items.Concat(second.Items).ToArray();
        Require(second.NextPageToken is null && complete.Length == 102
            && complete.Select(entry => entry.Share.Id).Distinct().Count() == complete.Length,
            "unchanged two-page enumeration includes every direct/history and active inherited grant exactly once");
        var expectedIds = direct.Select(entry => entry.Share.Id)
            .Concat(inherited.Where(entry => entry.Share.Id != inherited[1].Share.Id).Select(entry => entry.Share.Id)).ToHashSet();
        Require(complete.Select(entry => entry.Share.Id).ToHashSet().SetEquals(expectedIds),
            "pagination preserves each original direct ShareID and every active inherited ShareID");
        Require(complete.Take(51).All(entry => entry.Origin == FilesGrantOrigin.Direct)
            && complete.Skip(51).All(entry => entry.Origin == FilesGrantOrigin.Inherited)
            && complete.SequenceEqual(complete.OrderBy(entry => entry.Origin).ThenBy(entry => entry.Share.CreatedAt)
                .ThenBy(entry => entry.Share.Id.Value)),
            "multi-page results retain complete deterministic direct-then-inherited ordering");
        Require(complete.Any(entry => entry.Share.Id == direct[1].Share.Id && entry.Share.IsRevoked)
            && !complete.Any(entry => entry.Share.Id == inherited[1].Share.Id),
            "direct revoked history remains visible while revoked ancestor grants are not inherited");

        var bytesBeforeExpiryRead = await File.ReadAllBytesAsync(f.Path);
        var expiryPage = (await f.Provider.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!;
        Require(expiryPage.NextPageToken is not null, "the expiry-race fixture spans multiple pages");
        f.Clock.Now = expires;
        var afterExpiry = await f.Provider.GetEmailSharesAsync(f.Child.Id, expiryPage.NextPageToken, f.OwnerGuard);
        Require(afterExpiry.Error?.Code == FilesErrorCode.InvalidCursor && afterExpiry.Value is null,
            "expiry between pages invalidates the old cursor instead of silently skipping rows");
        var afterExpiryPage = (await f.Provider.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!;
        Require(afterExpiryPage.NextPageToken is not null, "post-expiry access history still spans two pages");
        var afterExpiryRows = afterExpiryPage.Items.Concat(
            (await f.Provider.GetEmailSharesAsync(f.Child.Id, afterExpiryPage.NextPageToken, f.OwnerGuard)).Value!.Items).ToList();
        Require(afterExpiryRows.Count == 101
            && afterExpiryRows.Any(entry => entry.Share.Id == direct[0].Share.Id
                && entry.Origin == FilesGrantOrigin.Direct && entry.Share.ExpiresAt == expires)
            && !afterExpiryRows.Any(entry => entry.Share.Id == inherited[0].Share.Id),
            "direct expired history is retained while the expired ancestor grant is no longer inherited");
        Require(bytesBeforeExpiryRead.SequenceEqual(await File.ReadAllBytesAsync(f.Path)),
            "expiry and pagination reads do not rewrite canonical sharing state");

        var revokePage = (await f.Provider.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!;
        Require(revokePage.NextPageToken is not null, "the revocation-race fixture spans multiple pages");
        var activeAncestor = inherited.Skip(2).First();
        await Revoke(activeAncestor);
        var afterRevoke = await f.Provider.GetEmailSharesAsync(f.Child.Id, revokePage.NextPageToken, f.OwnerGuard);
        Require(afterRevoke.Error?.Code == FilesErrorCode.InvalidCursor && afterRevoke.Value is null,
            "revocation between pages invalidates the old cursor rather than skipping the changed row");
        var finalBytes = await File.ReadAllBytesAsync(f.Path);
        var finalRows = (await f.Provider.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items;
        Require(finalRows.Count == 100 && finalRows.Count(entry => entry.Origin == FilesGrantOrigin.Direct) == 51
            && finalRows.Count(entry => entry.Origin == FilesGrantOrigin.Inherited) == 49
            && finalRows.All(entry => entry.Origin != FilesGrantOrigin.Inherited || !entry.Share.IsRevoked
                && (entry.Share.ExpiresAt is null || entry.Share.ExpiresAt > f.Clock.Now)),
            "fresh post-revocation enumeration contains all direct history and only active inherited grants");
        Require(finalBytes.SequenceEqual(await File.ReadAllBytesAsync(f.Path)),
            "all pagination and stale-cursor reads preserve the final persisted ACL bytes");

        async Task<FilesEmailShare> Grant(HostedItemId itemId, string prefix, int index, DateTimeOffset? expiry)
        {
            var item = (await f.Provider.GetAsync(itemId, default)).Value!;
            var request = (await f.Request(FilesAccessRole.Viewer)) with
            {
                ItemId = itemId,
                ExpectedRevision = item.CurrentRevisionId!.Value,
                RecipientPrincipalId = $"account:page-{prefix}-{index:D2}",
                RecipientEmail = $"page-{prefix}-{index:D2}@example.test",
                ExpiresAt = expiry,
                OperationId = new(Guid.NewGuid())
            };
            var result = await f.Provider.GrantEmailAccessAsync(request, f.OwnerGuard);
            Require(result.IsSuccess, $"persisted page fixture grant {prefix}-{index:D2}");
            return result.Value!;
        }

        async Task Revoke(FilesEmailShare grant)
        {
            var item = (await f.Provider.GetAsync(grant.Share.ItemId, default)).Value!;
            var result = await f.Provider.RevokeEmailAccessAsync(grant.Share.Id, item.CurrentRevisionId!.Value,
                new(Guid.NewGuid()), f.OwnerGuard);
            Require(result.IsSuccess, $"persisted page fixture revoke {grant.Share.Id}");
        }
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
        var store = await restarted.GetStoreEvidenceAsync();
        var feedTail = await restarted.GetChangesAsync(null, 500, default);
        Require(feedTail.NextPageToken is null && store.ChangeCursor.Revision == feedTail.Items.Last().Cursor.Revision,
            "durable Files evidence carries the same current cursor as the retained ordered event feed");
        var opened = await restarted.OpenSharedViewAsync(share.Share.Id, f.RecipientGuard);
        Require(opened.IsSuccess, "the same authenticated principal reopens the grant after provider restart");
        var view = opened.Value!;
        Require(view.Location.Id == f.Location && view.Location.Capabilities.Supports(FilesProviderCapabilities.Read | FilesProviderCapabilities.Search), "shared view uses canonical location and supports scoped search");
        Require(!view.Location.Capabilities.Supports(FilesProviderCapabilities.Write) &&
            !view.Location.Capabilities.Supports(FilesProviderCapabilities.Rename) &&
            !view.Location.Capabilities.Supports(FilesProviderCapabilities.ChangeFeed), "Viewer exposes no mutation or unfiltered owner history capabilities");
        var top = await view.ListAsync(null, null, null, default);
        Require(top.Items.Count == 1 && top.Items[0].Id == f.Root.Id && top.Items[0].ParentId is null, "scope root is navigable without exposing its parent");
        var children = await view.ListAsync(f.Root.Id, null, null, default);
        Require(children.Items.Count == 1 && children.Items[0].Id == f.Child.Id, "folder descendants refer to the same canonical items");
        Require((await view.GetAsync(f.Child.Id, default)).Value!.Id == f.Child.Id, "child is accessible by canonical identity");
        Require((await view.GetAsync(f.Grandchild.Id, default)).IsSuccess, "folder inheritance reaches deeper descendants");
        var sharedSearch = await view.SearchAsync(new("Grandchild"), null, default);
        Require(sharedSearch.Items.Single().Id == f.Grandchild.Id,
            "shared-view search returns the same canonical descendant only inside the authenticated grant scope");
        Require((await view.SearchAsync(new("Sibling"), null, default)).Items.Count == 0,
            "shared-view search does not disclose a known item outside the grant scope");
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
        Require((await legacy.GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard)).Value!.Items.Count == 0,
            "clean legacy descendants enumerate empty when no item or ancestor is marked shared");
        byte[] afterLegacyRead = await File.ReadAllBytesAsync(f.Path);
        Require(before.SequenceEqual(afterLegacyRead), "shared read does not adopt, migrate or rewrite legacy state");
        var request = await f.Request(FilesAccessRole.Viewer);
        state["items"]![0]!["metadata"]!["isShared"] = true;
        await File.WriteAllTextAsync(f.Path, document.ToJsonString());
        before = await File.ReadAllBytesAsync(f.Path);
        Require((await f.Provider.GetAsync(f.Child.Id, default)).Value!.IsShared, "untracked ancestor sharing remains effective");
        var unknownTargetShares = await f.Restart().GetEmailSharesAsync(f.Root.Id, null, f.OwnerGuard);
        Require(unknownTargetShares.Error?.Code == FilesErrorCode.InvalidState && unknownTargetShares.Value is null,
            "an item marked shared without tracked direct grants is unavailable, not an empty ACL");
        var unknownAncestorShares = await f.Restart().GetEmailSharesAsync(f.Child.Id, null, f.OwnerGuard);
        Require(unknownAncestorShares.Error?.Code == FilesErrorCode.InvalidState && unknownAncestorShares.Value is null,
            "a child access review fails closed when its ancestor ACL is untracked");
        Require(before.SequenceEqual(await File.ReadAllBytesAsync(f.Path)), "unknown target/ancestor ACL reads and restart preserve exact stored bytes");
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
