using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using NineToOne.Accounts;

/// <summary>Synthetic verified IDs exercise the real durable authority, not password/OIDC or cloud deployment.</summary>
public static class OrganisationLifecycleSpecs
{
    public static void Run()
    {
        TransferRequiresRecipientAndPreservesPersonalIdentity();
        TransferRaceRestartAndLastOwner();
        TransferExpiryAndCurrentOwnerRevocation();
        RequestExpiresWhileActualDurableLeaseIsHeld();
        ArchiveRestoreAndExactRequestReplay();
        DelegatedCapabilityDoesNotConferOwnership();
        FreeGrantCannotMoveToAnotherAccount();
        CurrentOwningAdmissionAndRevocationRace();
        RegisteredOwningCallerUsesActualSessionAndSerialisesSignOut();
        CancellationAndLegacyDomainSchema();
        CorruptTransferAndReceiptFailWithoutOverwrite();
        Console.WriteLine("PASS: organisation lifecycle, recipient transfer, durable replay, current owner, archive, admission and personal identity");
    }

    private static void TransferRequiresRecipientAndPreservesPersonalIdentity()
    {
        using var f = new Fixture();
        var personalBefore = File.ReadAllBytes(f.ProfilePath);
        var unrelated = f.Service.CreateTrustedOrganisation(f.Owner, "Unrelated personal company", BusinessAddOnKind.BusinessPlus, "synthetic-existing-funded-addon");
        var unrelatedBefore = JsonSerializer.Serialize(unrelated);
        var org = f.Current;
        var recipientMember = org.Members.Single(m => m.AccountID == f.Recipient);
        var ownerMember = org.Members.Single(m => m.AccountID == f.Owner);
        var expires = f.Clock.GetUtcNow().AddHours(1);
        var transfer = f.Service.RequestOwnershipTransfer(f.Owner, org.OrgID, org.Revision, "one-request", f.Recipient, expires);
        var requested = f.Current;
        var bytes = f.StateBytes;
        var replay = f.Service.RequestOwnershipTransfer(f.Owner, org.OrgID, org.Revision, "one-request", f.Recipient, expires);
        Check(replay == transfer && f.StateBytes.SequenceEqual(bytes), "request replay performs no duplicate write");
        Throws<InvalidOperationException>(() => f.Service.RequestOwnershipTransfer(f.Owner, org.OrgID, org.Revision, "one-request", f.Stranger, expires));
        Throws<UnauthorizedAccessException>(() => f.Service.AcceptOwnershipTransfer(f.Stranger, org.OrgID, transfer.TransferID, requested.Revision, "wrong-account"));
        Throws<InvalidOperationException>(() => f.Service.AcceptOwnershipTransfer(f.Recipient, org.OrgID, transfer.TransferID, requested.Revision - 1, "stale"));
        Throws<UnauthorizedAccessException>(() => f.Service.AcceptOwnershipTransfer(f.Recipient, unrelated.OrgID, transfer.TransferID, requested.Revision, "foreign-org"));
        Check(f.StateBytes.SequenceEqual(bytes), "all refused transfers preserve exact durable state");
        var accepted = f.Service.AcceptOwnershipTransfer(f.Recipient, org.OrgID, transfer.TransferID, requested.Revision, "recipient-accept");
        Check(accepted.Transfer.State == OrganisationOwnershipTransferState.Accepted && accepted.Organisation.Revision == requested.Revision + 1, "recipient accepts exactly one canonical change");
        var after = accepted.Organisation;
        Check(after.OrgID == org.OrgID && after.AddOn == org.AddOn && JsonSerializer.Serialize(after.Policy) == JsonSerializer.Serialize(org.Policy), "no billing or policy replacement");
        Check(after.Members.Single(m => m.AccountID == f.Recipient).MembershipID == recipientMember.MembershipID &&
            after.Members.Single(m => m.AccountID == f.Owner).MembershipID == ownerMember.MembershipID, "membership and personal account identities preserved");
        Check(!IsOwner(after, f.Owner) && IsOwner(after, f.Recipient), "owner roles transfer from current owner to intended active recipient");
        Check(File.ReadAllBytes(f.ProfilePath).SequenceEqual(personalBefore), "no personal profile change");
        Check(JsonSerializer.Serialize(f.Service.Get(f.Owner, unrelated.OrgID)) == unrelatedBefore, "unrelated organisation is byte-equivalent domain state");
        var audit = f.Service.ListAudit(f.Recipient, org.OrgID);
        Check(audit.Count(a => a.Action == "Admin.Organisations.TransferOwnership.Accepted" && a.ActorID == f.Recipient && a.TargetID == transfer.TransferID) == 1, "audit binds actual recipient and exact transfer");
    }

    private static void TransferRaceRestartAndLastOwner()
    {
        using var f = new Fixture(); var org = f.Current;
        var transfer = f.Service.RequestOwnershipTransfer(f.Owner, org.OrgID, org.Revision, "race-request", f.Recipient, f.Clock.GetUtcNow().AddHours(1));
        var requestedRevision = f.Current.Revision; int accepted = 0;
        Parallel.For(0, 16, i =>
        {
            try { f.Service.AcceptOwnershipTransfer(f.Recipient, org.OrgID, transfer.TransferID, requestedRevision, "race-accept-" + i); Interlocked.Increment(ref accepted); }
            catch (InvalidOperationException error) when (error.Message == "idempotency_conflict") { }
        });
        Check(accepted == 1 && f.Service.Get(f.Recipient, org.OrgID).Revision == requestedRevision + 1, "concurrent acceptance commits exactly once");
        var state = JsonNode.Parse(f.StateText)!["State"]!;
        var key = state["OwnershipTransfers"]!.AsArray().Single()!["AcceptedIdempotencyKey"]!.GetValue<string>();
        var before = f.StateBytes; var restarted = f.Restart();
        var receipt = restarted.AcceptOwnershipTransfer(f.Recipient, org.OrgID, transfer.TransferID, requestedRevision, key);
        Check(receipt.Transfer.State == OrganisationOwnershipTransferState.Accepted && f.StateBytes.SequenceEqual(before), "accepted replay after restart has no write or audit duplicate");
        Throws<InvalidOperationException>(() => restarted.SetMemberState(f.Recipient, org.OrgID, receipt.Organisation.Revision, "last-owner-remove", f.Recipient, OrganisationMemberState.Removed));
        Throws<InvalidOperationException>(() => restarted.SetMemberState(f.Recipient, org.OrgID, receipt.Organisation.Revision, "last-owner-suspend", f.Recipient, OrganisationMemberState.Suspended));
        Check(f.StateBytes.SequenceEqual(before), "last owner protection is durable and atomic");
    }

    private static void TransferExpiryAndCurrentOwnerRevocation()
    {
        using var f = new Fixture(); var org = f.Current;
        var transfer = f.Service.RequestOwnershipTransfer(f.Owner, org.OrgID, org.Revision, "expires", f.Recipient, f.Clock.GetUtcNow().AddMinutes(1));
        var revision = f.Current.Revision; f.Clock.Advance(transfer.ExpiresAt); var before = f.StateBytes;
        Throws<UnauthorizedAccessException>(() => f.Service.AcceptOwnershipTransfer(f.Recipient, org.OrgID, transfer.TransferID, revision, "late"));
        Check(f.StateBytes.SequenceEqual(before), "expired transfer grants no authority");
        using var revoked = new Fixture(); var current = revoked.Current;
        var pending = revoked.Service.RequestOwnershipTransfer(revoked.Owner, current.OrgID, current.Revision, "pending-owner-revocation", revoked.Recipient, revoked.Clock.GetUtcNow().AddHours(1));
        current = revoked.Current; revision = current.Revision;
        revoked.Service.PublishPolicy(revoked.Owner, current.OrgID, current.Revision, "revoke-transfer", new HashSet<string> { "Admin.Organisations.TransferOwnership" }, new Dictionary<string, string>(), new Dictionary<string, string>());
        before = revoked.StateBytes;
        Throws<OrganisationAccessException>(() => revoked.Service.AcceptOwnershipTransfer(revoked.Recipient, current.OrgID, pending.TransferID, revision, "after-owner-policy-revocation"));
        Check(revoked.StateBytes.SequenceEqual(before), "current owner policy must still authorise acceptance");
    }

    private static void RequestExpiresWhileActualDurableLeaseIsHeld()
    {
        using var f = new Fixture(); var org = f.Current;
        var expiry = f.Clock.GetUtcNow().AddMinutes(1); f.Clock.ReadStarted.Reset();
        var canonical = Path.GetFullPath(f.StatePath); if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        using var lease = new Mutex(false, "9to1-state-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        lease.WaitOne(); Task<OrganisationOwnershipTransfer>? original = null; Exception? primary = null;
        try
        {
            original = Task.Run(() => f.Service.RequestOwnershipTransfer(f.Owner, org.OrgID, org.Revision, "lease-expired", f.Recipient, expiry));
            Check(f.Clock.ReadStarted.Wait(TimeSpan.FromSeconds(5)) && !original.IsCompleted, "actual named state lease delays request admission");
            f.Clock.Advance(expiry.AddSeconds(1));
        }
        catch (Exception error) { primary = error; }
        finally { lease.ReleaseMutex(); }
        Exception? outcome = null;
        try { original?.GetAwaiter().GetResult(); }
        catch (Exception error) { outcome = error; }
        if (primary is not null)
        {
            if (outcome is not null && !ReferenceEquals(primary, outcome)) throw new AggregateException(primary, outcome);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
        Check(original is not null, "original request task captured");
        Check(outcome is UnauthorizedAccessException, "original lease-delayed request rejects expired admission");
        Check(f.Current.Revision == org.Revision, "expiry during actual lease creates no transfer or organisation revision");
    }

    private static void ArchiveRestoreAndExactRequestReplay()
    {
        using var f = new Fixture(); var before = f.Current; var profiles = File.ReadAllBytes(f.ProfilePath);
        var archived = f.Service.Archive(f.Owner, before.OrgID, before.Revision, "archive");
        Check(archived.Organisation.Lifecycle == OrganisationLifecycleState.Archived && !archived.Replayed, "recoverable archive is an explicit canonical state");
        var bytes = f.StateBytes;
        var again = f.Restart().Archive(f.Owner, before.OrgID, before.Revision, "archive");
        Check(again.Replayed && again.Receipt == archived.Receipt && f.StateBytes.SequenceEqual(bytes), "archive retry after restart returns original receipt without mutation");
        var denied = f.Service.EvaluateAsync(f.Owner, before.OrgID, "Connect.Directory.Get", [f.Scope], null, default).GetAwaiter().GetResult();
        Check(!denied.Allowed && denied.Code == "OrganisationArchived", "archive stops owning content admission");
        Throws<OrganisationAccessException>(() => f.Service.PublishPolicy(f.Owner, before.OrgID, archived.Organisation.Revision, "archived-policy", new HashSet<string>(), new Dictionary<string, string>(), new Dictionary<string, string>()));
        Check(f.Service.Get(f.Owner, before.OrgID).Lifecycle == OrganisationLifecycleState.Archived && f.Service.ListAudit(f.Owner, before.OrgID).Count > 0, "authorised archive metadata and audit remain available");
        var restored = f.Service.Restore(f.Owner, before.OrgID, archived.Organisation.Revision, "restore");
        Check(restored.Organisation.Lifecycle == OrganisationLifecycleState.Active && restored.Organisation.AddOn == before.AddOn &&
            JsonSerializer.Serialize(restored.Organisation.Members) == JsonSerializer.Serialize(before.Members) &&
            JsonSerializer.Serialize(restored.Organisation.Roles) == JsonSerializer.Serialize(before.Roles), "restore preserves original accounts, membership, roles and resources");
        var renamed = f.Service.UpdateIdentity(f.Owner, before.OrgID, restored.Organisation.Revision, "rename", "Canonical renamed company");
        bytes = f.StateBytes;
        var renamedAgain = f.Restart().UpdateIdentity(f.Owner, before.OrgID, restored.Organisation.Revision, "rename", "Canonical renamed company");
        Check(renamedAgain.Replayed && renamedAgain.Receipt == renamed.Receipt && f.StateBytes.SequenceEqual(bytes), "metadata request replay is exact across restart");
        Throws<InvalidOperationException>(() => f.Service.UpdateIdentity(f.Owner, before.OrgID, restored.Organisation.Revision, "rename", "Conflicting requested name"));
        Check(f.StateBytes.SequenceEqual(bytes) && File.ReadAllBytes(f.ProfilePath).SequenceEqual(profiles), "conflicting replay and lifecycle cannot alter personal profiles");
        f.Service.PublishPolicy(f.Owner, before.OrgID, renamed.Organisation.Revision, "revoke-original-capability", new HashSet<string> { "Admin.Organisations.Update" }, new Dictionary<string, string>(), new Dictionary<string, string>());
        bytes = f.StateBytes;
        Throws<OrganisationAccessException>(() => f.Service.UpdateIdentity(f.Owner, before.OrgID, restored.Organisation.Revision, "rename", "Canonical renamed company"));
        Check(f.StateBytes.SequenceEqual(bytes), "idempotent receipt never supplies cached policy authority");
    }

    private static void DelegatedCapabilityDoesNotConferOwnership()
    {
        using var f = new Fixture(); var org = f.Current;
        org = f.Service.CreateRole(f.Owner, org.OrgID, org.Revision, "delegate-role", "Transfer delegate", new HashSet<string> { "Admin.Organisations.Get", "Admin.Organisations.TransferOwnership" }, new HashSet<string>());
        var role = org.Roles.Single(r => r.Name == "Transfer delegate");
        var invitation = f.Service.Invite(f.Owner, org.OrgID, f.Stranger, [role.RoleID], DateTimeOffset.UtcNow.AddHours(1));
        org = f.Service.AcceptInvitation(f.Stranger, invitation.Token); var before = f.StateBytes;
        Throws<UnauthorizedAccessException>(() => f.Service.RequestOwnershipTransfer(f.Stranger, org.OrgID, org.Revision, "delegate-is-not-owner", f.Recipient, f.Clock.GetUtcNow().AddHours(1)));
        Check(f.StateBytes.SequenceEqual(before), "delegated transfer capability cannot impersonate canonical owner");
    }

    private static void FreeGrantCannotMoveToAnotherAccount()
    {
        using var f = new Fixture();
        var free = f.Service.CreateTrustedOrganisation(f.Jacob, "Synthetic verified Jacob company", BusinessAddOnKind.BusinessPlus, "synthetic-account-bound-entitlement");
        free = f.Service.CreateRole(f.Jacob, free.OrgID, free.Revision, "free-member", "Member", new HashSet<string> { "Admin.Organisations.Get" }, new HashSet<string>());
        var role = free.Roles.Single(r => r.Name == "Member");
        var invitation = f.Service.Invite(f.Jacob, free.OrgID, f.Recipient, [role.RoleID], DateTimeOffset.UtcNow.AddHours(1));
        free = f.Service.AcceptInvitation(f.Recipient, invitation.Token); var before = f.StateBytes;
        Throws<InvalidOperationException>(() => f.Service.RequestOwnershipTransfer(f.Jacob, free.OrgID, free.Revision, "move-free-grant", f.Recipient, f.Clock.GetUtcNow().AddHours(1)));
        Check(f.StateBytes.SequenceEqual(before) && f.Profiles.HasAccountBoundFreeBusiness(f.Jacob) && !f.Profiles.HasAccountBoundFreeBusiness(f.Recipient), "free grant follows only verified account and no paid purchase is invented");
        f.Profiles.Update(f.Jacob, f.Profiles.Get(f.Jacob)!.Revision, JsonSerializer.SerializeToElement(new { username = "jacob-renamed" }));
        Check(f.Profiles.HasAccountBoundFreeBusiness(f.Jacob), "free grant survives mutable username change");
    }

    private static void CurrentOwningAdmissionAndRevocationRace()
    {
        using var f = new Fixture(); var org = f.Current; int admitted = 0;
        var value = f.Service.WithCurrentAuthority(f.Recipient, org.OrgID, "Connect.Directory.Get", [f.Scope], org.Policy.Revision, () => ++admitted, default);
        Check(value == 1, "current member and actual canonical scope admit owning synchronous operation");
        Throws<OrganisationAccessException>(() => f.Service.WithCurrentAuthority(f.Recipient, org.OrgID, "Connect.Directory.Get", ["foreign-resource"], org.Policy.Revision, () => ++admitted, default));
        Throws<OrganisationAccessException>(() => f.Service.WithCurrentAuthority(f.Recipient, org.OrgID, "Connect.Directory.Get", [], org.Policy.Revision, () => ++admitted, default));
        Throws<ArgumentException>(() => f.Service.WithCurrentAuthority(f.Recipient, org.OrgID, "Connect.Directory.Get", [f.Scope], org.Policy.Revision, () => Task.CompletedTask, default));
        Check(admitted == 1, "invalid foreign/absent scope and asynchronous result do not invoke owning commit");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Throws<OperationCanceledException>(() => f.Service.WithCurrentAuthority(f.Recipient, org.OrgID, "Connect.Directory.Get", [f.Scope], org.Policy.Revision, () => ++admitted, cancelled.Token));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var revokeStarted = new ManualResetEventSlim();
        var original = Task.Run(() => f.Service.WithCurrentAuthority(f.Recipient, org.OrgID, "Connect.Directory.Get", [f.Scope], org.Policy.Revision, () =>
        {
            entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("original owning admission release missing");
            return ++admitted;
        }, default));
        Task<Organisation>? revoke = null; Exception? primary = null;
        try
        {
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "original owning callback is entered under real organisation lease");
            revoke = Task.Run(() => { revokeStarted.Set(); return f.Service.SetMemberState(f.Owner, org.OrgID, org.Revision, "revoke-recipient", f.Recipient, OrganisationMemberState.Suspended); });
            Check(revokeStarted.Wait(TimeSpan.FromSeconds(5)) && !revoke.IsCompleted, "concurrent revocation is proposed while original organisation admission lease is held");
        }
        catch (Exception error) { primary = error; }
        finally { release.Set(); }
        var failures = new List<Exception>(); if (primary is not null) failures.Add(primary);
        var originalResult = 0; Organisation? revoked = null;
        try { originalResult = original.GetAwaiter().GetResult(); } catch (Exception error) { if (!failures.Any(e => ReferenceEquals(e, error))) failures.Add(error); }
        try { if (revoke is not null) revoked = revoke.GetAwaiter().GetResult(); } catch (Exception error) { if (!failures.Any(e => ReferenceEquals(e, error))) failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("original owning callback and revocation task failures retained", failures);
        Check(originalResult == 2, "original admitted commit returns actual outcome");
        Check(revoked is not null && revoked.Members.Single(m => m.AccountID == f.Recipient).State == OrganisationMemberState.Suspended, "revocation commits after original admitted operation settles");
        Throws<OrganisationAccessException>(() => f.Service.WithCurrentAuthority(f.Recipient, org.OrgID, "Connect.Directory.Get", [f.Scope], org.Policy.Revision, () => ++admitted, default));
        Check(admitted == 2, "revoked member cannot admit later owning work");
    }

    private static void RegisteredOwningCallerUsesActualSessionAndSerialisesSignOut()
    {
        using var f = new Fixture(); var org = f.Current;
        const string client = "synthetic-local-owning-client";
        const string redirect = "https://fixture.invalid/local-owner";
        var identity = new CakeIdentityService(Path.Combine(f.Root, "sessions.json"),
            new Dictionary<string, IReadOnlySet<string>> { [client] = new HashSet<string> { redirect } });
        IssuedSession Issue(Guid account)
        {
            var verifier = new string('r', 64);
            var code = identity.AuthorizeAuthenticatedAccount(new(account, "Synthetic trusted fixture"), client, redirect,
                CakeIdentityService.Challenge(verifier));
            return identity.Exchange(code, client, redirect, verifier, "Synthetic local owner");
        }
        var recipient = Issue(f.Recipient); var foreign = Issue(f.Stranger);
        var caller = new AuthenticatedOrganisationOwningOperations(identity, f.Service);
        int commits = 0;
        Action? retiredRecheck = null;
        caller.WithCurrentSessionAuthority(recipient.AccessToken, org.OrgID, "Connect.Directory.Get", [f.Scope],
            org.Policy.Revision, (_, recheck) => { retiredRecheck = recheck; recheck(); return 0; }, default);
        Check(retiredRecheck is not null, "original admission exposes only its synchronous current recheck");
        Throws<UnauthorizedAccessException>(() => retiredRecheck!());
        Guid Commit(Guid account, Action recheck) { recheck(); ++commits; return account; }
        Check(caller.WithCurrentSessionAuthority(recipient.AccessToken, org.OrgID, "Connect.Directory.Get", [f.Scope],
            org.Policy.Revision, Commit, default) == f.Recipient, "owning account is derived only from the original current authenticated session");
        Throws<OrganisationAccessException>(() => caller.WithCurrentSessionAuthority(foreign.AccessToken, org.OrgID,
            "Connect.Directory.Get", [f.Scope], org.Policy.Revision, Commit, default));
        Throws<UnauthorizedAccessException>(() => caller.WithCurrentSessionAuthority("copied-account-guid-" + f.Recipient,
            org.OrgID, "Connect.Directory.Get", [f.Scope], org.Policy.Revision, Commit, default));
        Check(commits == 1, "foreign authenticated membership and copied account claims never reach owning commit");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var signOutStarted = new ManualResetEventSlim();
        var original = Task.Run(() => caller.WithCurrentSessionAuthority(recipient.AccessToken, org.OrgID,
            "Connect.Directory.Get", [f.Scope], org.Policy.Revision, (account, recheck) =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("original session admission release missing");
                return Commit(account, recheck);
            }, default));
        Task? signOut = null; Exception? primary = null;
        try
        {
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "original owning callback holds actual session and organisation leases");
            signOut = Task.Run(() => { signOutStarted.Set(); identity.SignOut(recipient.AccessToken); });
            Check(signOutStarted.Wait(TimeSpan.FromSeconds(5)) && !signOut.IsCompleted,
                "actual session revocation waits for the already admitted synchronous operation");
        }
        catch (Exception error) { primary = error; }
        finally { release.Set(); }
        var failures = new List<Exception>(); if (primary is not null) failures.Add(primary);
        Guid originalActor = Guid.Empty;
        try { originalActor = original.GetAwaiter().GetResult(); }
        catch (Exception error) { if (!failures.Any(e => ReferenceEquals(e, error))) failures.Add(error); }
        try { signOut?.GetAwaiter().GetResult(); }
        catch (Exception error) { if (!failures.Any(e => ReferenceEquals(e, error))) failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("original session admission and sign-out failures retained", failures);
        Check(originalActor == f.Recipient && commits == 2, "original admitted effect and actor are preserved before revocation");
        Throws<UnauthorizedAccessException>(() => caller.WithCurrentSessionAuthority(recipient.AccessToken, org.OrgID,
            "Connect.Directory.Get", [f.Scope], org.Policy.Revision, Commit, default));
        Check(commits == 2, "revoked session cannot admit any later owning operation");

        // Synthetic issuer state has a genuinely time-bounded session. This changes
        // only fresh fixture data before launch, never a production account or token.
        var expiring = Issue(f.Recipient); var sessionPath = Path.Combine(f.Root, "sessions.json");
        var sessionState = JsonNode.Parse(File.ReadAllText(sessionPath))!;
        var expires = DateTimeOffset.UtcNow.AddSeconds(2);
        var row = sessionState["State"]!["Sessions"]!.AsArray().Single(n => n!["SessionID"]!.GetValue<Guid>() == expiring.Session.SessionID)!;
        row["ExpiresAt"] = expires; File.WriteAllText(sessionPath, sessionState.ToJsonString());
        object owningResourceGate = new(); using var waitingForResource = new ManualResetEventSlim();
        Monitor.Enter(owningResourceGate); Task<Guid>? delayed = null; primary = null;
        try
        {
            delayed = Task.Factory.StartNew(() => caller.WithCurrentSessionAuthority(expiring.AccessToken, org.OrgID,
                "Connect.Directory.Get", [f.Scope], org.Policy.Revision, (account, recheck) =>
                {
                    waitingForResource.Set();
                    lock (owningResourceGate) return Commit(account, recheck);
                }, default), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Check(waitingForResource.Wait(TimeSpan.FromSeconds(5)) && !delayed.IsCompleted,
                "original authenticated admission waits for its actual owning resource lock");
            var wait = expires.AddMilliseconds(50) - DateTimeOffset.UtcNow;
            Check(wait <= TimeSpan.FromSeconds(3), "actual synthetic expiry wait remains bounded");
            if (wait > TimeSpan.Zero) using (var elapsed = new ManualResetEventSlim()) elapsed.Wait(wait);
        }
        catch (Exception error) { primary = error; }
        finally { Monitor.Exit(owningResourceGate); }
        Exception? delayedOutcome = null;
        try { delayed?.GetAwaiter().GetResult(); } catch (Exception error) { delayedOutcome = error; }
        if (primary is not null)
        {
            if (delayedOutcome is not null && !ReferenceEquals(primary, delayedOutcome)) throw new AggregateException(primary, delayedOutcome);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
        Check(delayed is not null && delayedOutcome is UnauthorizedAccessException && commits == 2,
            "issuer-held recheck inside the actual owning resource lock refuses expiry without mutation");
    }

    private static void CancellationAndLegacyDomainSchema()
    {
        using var f = new Fixture(); var org = f.Current; var before = f.StateBytes;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Throws<OperationCanceledException>(() => f.Service.UpdateIdentity(f.Owner, org.OrgID, org.Revision, "cancel-update", "Cancelled name", cancelled.Token));
        Throws<OperationCanceledException>(() => f.Service.Archive(f.Owner, org.OrgID, org.Revision, "cancel-archive", cancelled.Token));
        Throws<OperationCanceledException>(() => f.Service.RequestOwnershipTransfer(f.Owner, org.OrgID, org.Revision, "cancel-transfer", f.Recipient, f.Clock.GetUtcNow().AddHours(1), cancelled.Token));
        Check(f.StateBytes.SequenceEqual(before), "cancelled lifecycle admission makes no state, receipt or audit write");
        var legacy = JsonNode.Parse(f.StateText)!; var record = legacy["State"]!["Organisations"]![0]!;
        record["SchemaVersion"] = 1; record.AsObject().Remove("Lifecycle");
        File.WriteAllText(f.StatePath, legacy.ToJsonString());
        var loaded = f.Restart().Get(f.Owner, org.OrgID);
        Check(loaded.SchemaVersion == 1 && loaded.Lifecycle == OrganisationLifecycleState.Active, "old active domain schema retains canonical identity");
        var archived = f.Restart().Archive(f.Owner, org.OrgID, loaded.Revision, "legacy-archive");
        Check(archived.Organisation.SchemaVersion == 2 && archived.Organisation.OrgID == org.OrgID, "recoverable lifecycle advances explicit domain schema without replacing identity");
    }

    private static void CorruptTransferAndReceiptFailWithoutOverwrite()
    {
        using var f = new Fixture(); var org = f.Current;
        f.Service.RequestOwnershipTransfer(f.Owner, org.OrgID, org.Revision, "corruption-transfer", f.Recipient, f.Clock.GetUtcNow().AddHours(1));
        org = f.Current; f.Service.UpdateIdentity(f.Owner, org.OrgID, org.Revision, "corruption-receipt", "Canonical company");
        var clean = f.StateText;
        foreach (var corruption in new[] { "foreign-transfer-recipient", "duplicate-transfer", "receipt-payload", "receipt-revision" })
        {
            var json = JsonNode.Parse(clean)!; var state = json["State"]!;
            if (corruption == "foreign-transfer-recipient") state["OwnershipTransfers"]![0]!["RecipientAccountID"] = Guid.NewGuid().ToString();
            else if (corruption == "duplicate-transfer") state["OwnershipTransfers"]!.AsArray().Add(state["OwnershipTransfers"]![0]!.DeepClone());
            else if (corruption == "receipt-payload") state["LifecycleReceipts"]![0]!["Lifecycle"] = 1;
            else state["LifecycleReceipts"]![0]!["ResultingRevision"] = long.MaxValue;
            var bad = json.ToJsonString(); File.WriteAllText(f.StatePath, bad);
            Throws<InvalidDataException>(() => f.Restart().Get(f.Owner, org.OrgID));
            Check(File.ReadAllText(f.StatePath) == bad, "corrupt canonical lifecycle state preserved: " + corruption);
        }
        File.WriteAllText(f.StatePath, clean);
        Check(f.Restart().Get(f.Owner, org.OrgID).OrgID == org.OrgID, "restored original durable state remains readable");
    }

    private static bool IsOwner(Organisation org, Guid account) => org.Members.Any(m => m.AccountID == account && m.State == OrganisationMemberState.Active && m.RoleIDs.Any(id => org.Roles.Any(r => r.RoleID == id && r.IsOwner)));
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Throws<T>(Action operation) where T : Exception
    { try { operation(); } catch (T) { return; } throw new Exception("Expected original " + typeof(T).Name + " refusal"); }

    private sealed class Clock(DateTimeOffset initial) : TimeProvider
    {
        private long ticks = initial.UtcTicks;
        public ManualResetEventSlim ReadStarted { get; } = new(false);
        public override DateTimeOffset GetUtcNow() { ReadStarted.Set(); return new(Interlocked.Read(ref ticks), TimeSpan.Zero); }
        public void Advance(DateTimeOffset now) => Interlocked.Exchange(ref ticks, now.UtcTicks);
    }
    private sealed class ScopeAuthority : IOrganisationObjectScopeResolver
    {
        public Guid OrgID { get; set; }
        public string Scope { get; set; } = "";
        public bool Allows(Guid account, Guid org, string action, string scope) => org == OrgID && scope == Scope;
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-org-lifecycle-" + Guid.NewGuid().ToString("N"));
        public Guid Owner { get; } = Guid.NewGuid(); public Guid Recipient { get; } = Guid.NewGuid();
        public Guid Stranger { get; } = Guid.NewGuid(); public Guid Jacob { get; } = Guid.NewGuid();
        public string StatePath => Path.Combine(Root, "organisation.json");
        public string ProfilePath => Path.Combine(Root, "profiles.json");
        public ProfileService Profiles { get; }
        public Clock Clock { get; } = new(DateTimeOffset.UtcNow);
        private ScopeAuthority Scopes { get; } = new();
        public OrganisationService Service { get; }
        public Guid OrgID { get; }
        public string Scope => Scopes.Scope;
        public Organisation Current => Service.Get(Owner, OrgID);
        public byte[] StateBytes => File.ReadAllBytes(StatePath);
        public string StateText => File.ReadAllText(StatePath);
        public Fixture()
        {
            Profiles = new(ProfilePath, Jacob);
            foreach (var account in new[] { Owner, Recipient, Stranger, Jacob })
                Profiles.Update(account, 0, JsonSerializer.SerializeToElement(new { name = "Synthetic account", username = "synthetic-" + account.ToString("N"), job = "Original optional job" }));
            Service = new(StatePath, Profiles, Scopes, Clock);
            var org = Service.CreateTrustedOrganisation(Owner, "Canonical company", BusinessAddOnKind.Business, "synthetic-existing-funded-addon");
            OrgID = org.OrgID; Scopes.OrgID = OrgID; Scopes.Scope = "fixture:contact/" + Guid.NewGuid().ToString("N");
            org = Service.CreateRole(Owner, OrgID, org.Revision, "member-role", "Member", new HashSet<string> { "Admin.Organisations.Get", "Connect.Directory.Get" }, new HashSet<string>());
            var role = org.Roles.Single(r => r.Name == "Member");
            var invitation = Service.Invite(Owner, OrgID, Recipient, [role.RoleID], DateTimeOffset.UtcNow.AddHours(1));
            Service.AcceptInvitation(Recipient, invitation.Token);
        }
        public OrganisationService Restart() => new(StatePath, Profiles, Scopes, Clock);
        public void Dispose() { Clock.ReadStarted.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
