using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using NineToOne.Accounts;

/// <summary>Real canonical durable owner operations with synthetic verified accounts
/// and original local PKCE-issued sessions. No remote Worker or native UI proof.</summary>
public static class OrganisationRoleAndJobSpecs
{
    public static void Run()
    {
        RunCase(RoleMutationReplayRestartAndDelegation);
        RunCase(ProtectedOwnerAndRoleInUse);
        RunCase(EffectiveCandidatesAreCurrentDetachedMetadata);
        RunCase(ActualExportPagesAndRestart);
        RunCase(HistoricalExportOutputRetiresWithCurrentAuthority);
        RunCase(SignOutAfterClaimRetainsOriginalTerminalFailure);
        RunCase(ExpiryAfterClaimRetainsOriginalTerminalFailure);
        RunCase(ConcurrentOrganisationRevisionRefusesOutput);
        RunCase(PendingCancellationAndOriginalCancellation);
        RunCase(ConcurrentRoleMutationHasOneCanonicalWinner);
        RunCase(CompletedSaveRetiresPayloadWithoutReplayingOriginal);
        RunCase(ActualSerializationRetiresPayloadAfterKnownCommit);
        Console.WriteLine("PASS: canonical Admin roles, bounded original export jobs, current sessions and terminal custody (12 scenarios)");
    }

    private static void RoleMutationReplayRestartAndDelegation(Fixture f)
    {
        var before = f.Current;
        var result = f.Service.UpdateRole(f.Owner, f.OrgID, f.RoleID, before.Revision, "role-update", "Edited member",
            Set("Admin.Organisations.Get", "Admin.Members.List"), Set("Files.Delete"));
        Check(!result.Replayed && result.Role?.RoleID == f.RoleID && result.OrganisationRevision == before.Revision + 1,
            "same canonical role ID changes once under actual organisation CAS");
        var after = f.StateBytes;
        var restart = f.Restart();
        var replay = restart.UpdateRole(f.Owner, f.OrgID, f.RoleID, before.Revision, "role-update", "Edited member",
            Set("Admin.Members.List", "Admin.Organisations.Get"), Set("Files.Delete"));
        Check(replay.Replayed && replay.Receipt == result.Receipt && f.StateBytes.SequenceEqual(after),
            "sorted exact request identity survives restart without duplicate audit or effect");
        Throws<InvalidOperationException>(() => restart.UpdateRole(f.Owner, f.OrgID, f.RoleID, before.Revision,
            "role-update", "Other request", Set("Admin.Members.List"), Set()));
        Throws<UnauthorizedAccessException>(() => restart.UpdateRole(f.Owner, f.OrgID, f.RoleID, f.Current.Revision,
            "wildcard-escalation", "Wildcard", Set("*"), Set()));
        var org = f.Current;
        org = f.Service.CreateRole(f.Owner, f.OrgID, org.Revision, "limited-editor", "Limited editor",
            Set("Admin.Roles.Update", "Admin.Organisations.Get", "Admin.Members.List"), Set());
        var delegated = org.Roles.Single(r => r.Name == "Limited editor");
        f.Service.SetRoles(f.Owner, f.OrgID, org.Revision, "assign-editor", f.Member, [delegated.RoleID]);
        var protectedBytes = f.StateBytes;
        Throws<OrganisationAccessException>(() => f.Service.UpdateRole(f.Member, f.OrgID, f.RoleID, f.Current.Revision,
            "outside-delegation", "Escalation", Set("Admin.Organisations.Get", "Files.Delete"), Set()));
        Check(f.StateBytes.SequenceEqual(protectedBytes), "current delegation refuses newly granted authority atomically");
        Check(File.ReadAllBytes(f.ProfilePath).SequenceEqual(f.OriginalProfiles), "role edits preserve personal profiles");
    }

    private static void ProtectedOwnerAndRoleInUse(Fixture f)
    {
        var org = f.Current;
        var ownerRole = org.Roles.Single(r => r.IsOwner);
        Throws<UnauthorizedAccessException>(() => f.Service.DeleteRole(f.Owner, f.OrgID, ownerRole.RoleID, org.Revision, "delete-owner"));
        Throws<UnauthorizedAccessException>(() => f.Service.UpdateRole(f.Owner, f.OrgID, ownerRole.RoleID, org.Revision,
            "edit-owner", "Changed owner", Set(), Set()));
        Throws<InvalidOperationException>(() => f.Service.DeleteRole(f.Owner, f.OrgID, f.RoleID, org.Revision, "delete-in-use"));
        org = f.Service.CreateRole(f.Owner, f.OrgID, org.Revision, "unused", "Unused", Set("Admin.Organisations.Get"), Set());
        var unused = org.Roles.Single(r => r.Name == "Unused");
        var deleted = f.Service.DeleteRole(f.Owner, f.OrgID, unused.RoleID, org.Revision, "delete-unused");
        Check(deleted.Role is null && !f.Current.Roles.Any(r => r.RoleID == unused.RoleID), "unused role actually removed");
        var bytes = f.StateBytes;
        var replay = f.Restart().DeleteRole(f.Owner, f.OrgID, unused.RoleID, org.Revision, "delete-unused");
        Check(replay.Replayed && replay.Receipt == deleted.Receipt && f.StateBytes.SequenceEqual(bytes), "deletion receipt preserves original effect");
    }

    private static void EffectiveCandidatesAreCurrentDetachedMetadata(Fixture f)
    {
        var observed = f.Service.GetEffectiveRolePermissions(f.Owner, f.OrgID, f.Member);
        Check(observed.CandidateCapabilities.SetEquals(Set("Admin.Organisations.Get")) && !observed.HasWildcardCandidate &&
            observed.RequiresCurrentOwningObjectAdmission, "role candidates explicitly require original owning-object admission");
        Check(f.Service.GetEffectiveRolePermissions(f.Owner, f.OrgID, f.Owner).HasWildcardCandidate,
            "owner wildcard is metadata rather than a new blanket object grant");
        var org = f.Current;
        f.Service.PublishPolicy(f.Owner, f.OrgID, org.Revision, "block-candidate", Set("Admin.Organisations.Get"), new Dictionary<string,string>(), new Dictionary<string,string>());
        var changed = f.Service.GetEffectiveRolePermissions(f.Owner, f.OrgID, f.Member);
        Check(changed.CandidateCapabilities.Count == 0 && changed.DeniedCapabilities.Contains("Admin.Organisations.Get") &&
            observed.CandidateCapabilities.Contains("Admin.Organisations.Get"), "current policy intersects detached former observation");
        Throws<OrganisationAccessException>(() => f.Service.GetEffectiveRolePermissions(f.Stranger, f.OrgID, f.Owner));
    }

    private static void ActualExportPagesAndRestart(Fixture f)
    {
        var org = f.Current;
        f.Service.PublishPolicy(f.Owner, f.OrgID, org.Revision, "private-policy-value", Set(),
            new Dictionary<string,string>{{"credential-dependent-setting","DO_NOT_EXPORT_PRIVATE_VALUE"}}, new Dictionary<string,string>());
        var current = f.Current; string? cursor = null; var all = new List<OrganisationExportRecord>(); var pageNumber = 0;
        do
        {
            var requested = f.Admit(actor => f.Service.RequestOrganisationExport(actor, f.OrgID, current.Revision,
                "export-page-" + pageNumber++, cursor, 1));
            var bytes = f.StateBytes;
            var replay = f.Admit(actor => f.Restart().RequestOrganisationExport(actor, f.OrgID, current.Revision,
                requested.IdempotencyKey, cursor, 1));
            Check(replay.JobID == requested.JobID && f.StateBytes.SequenceEqual(bytes), "pending export page replay survives real restart");
            var original = f.Service.ExecuteOrganisationExportAsync(f.Session, f.OrgID, requested.JobID, requested.Revision);
            var result = original.GetAwaiter().GetResult();
            Check(result.State == AdminJobState.Succeeded && result.OutputIsCurrent && result.RunID is not null,
                "actual original owning export task produced durable current output");
            var page = JsonSerializer.Deserialize<OrganisationConfigurationExportPage>(result.ExportDocument!)!;
            Check(page.Records.Count == 1 && page.OrganisationRevision == current.Revision &&
                !result.ExportDocument!.Contains("DO_NOT_EXPORT_PRIVATE_VALUE", StringComparison.Ordinal) &&
                !result.ExportDocument.Contains(f.AccessToken, StringComparison.Ordinal), "bounded metadata export excludes private values and session credentials");
            all.AddRange(page.Records); cursor = page.NextCursor;
        } while (cursor is not null);
        Check(all.Count == current.Roles.Count + current.Members.Count + 1 && all.Select(r => r.CanonicalID).Distinct().Count() == all.Count,
            "complete bounded pages retain exact role/member/policy identities without duplicates");
        Check(f.Service.ListAdminJobs(f.Owner, f.OrgID).Items.All(j => j.State == AdminJobState.Succeeded), "real durable jobs list exposes actual terminal states");
    }

    private static void HistoricalExportOutputRetiresWithCurrentAuthority(Fixture f)
    {
        var requested = f.Admit(actor => f.Service.RequestOrganisationExport(actor, f.OrgID, f.Current.Revision, "export-retire"));
        var complete = f.Service.ExecuteOrganisationExportAsync(f.Session, f.OrgID, requested.JobID, requested.Revision).GetAwaiter().GetResult();
        Check(complete.ExportDocument is not null, "real export succeeded before authority retirement");
        var org = f.Current;
        f.Service.PublishPolicy(f.Owner, f.OrgID, org.Revision, "revoke-export", Set("Admin.Organisations.Export"), new Dictionary<string,string>(), new Dictionary<string,string>());
        var retired = f.Service.GetAdminJob(f.Owner, f.OrgID, complete.JobID);
        Check(retired.State == AdminJobState.Succeeded && retired.ExportDocument is null && !retired.OutputIsCurrent,
            "truthful old success persists but current denied/stale output is purged");
    }

    private static void SignOutAfterClaimRetainsOriginalTerminalFailure(Fixture f) =>
        RunHeldExport(f, () => f.Identity.SignOut(f.AccessToken), typeof(UnauthorizedAccessException), "PermissionDenied");

    private static void ExpiryAfterClaimRetainsOriginalTerminalFailure(Fixture f) =>
        RunHeldExport(f, () =>
        {
            // Real fresh fixture issuer row only: no replacement authentication provider.
            var state = JsonNode.Parse(File.ReadAllText(f.IdentityPath))!;
            var row = state["State"]!["Sessions"]!.AsArray().Single(n => n!["SessionID"]!.GetValue<Guid>() == f.Issued.Session.SessionID)!;
            var expires = DateTimeOffset.UtcNow.AddSeconds(1);
            row["ExpiresAt"] = expires; File.WriteAllText(f.IdentityPath, state.ToJsonString());
            using var elapsed = new ManualResetEventSlim(); elapsed.Wait(TimeSpan.FromSeconds(1.1));
        }, typeof(UnauthorizedAccessException), "PermissionDenied");

    private static void ConcurrentOrganisationRevisionRefusesOutput(Fixture f) =>
        RunHeldExport(f, () =>
        {
            var current = f.Current;
            f.Service.UpdateIdentity(f.Owner, f.OrgID, current.Revision, "intervening-owner-edit", "Intervening organisation");
        }, typeof(InvalidOperationException), "RevisionOrOutputConflict");

    private static void PendingCancellationAndOriginalCancellation(Fixture f)
    {
        var request = f.Admit(actor => f.Service.RequestOrganisationExport(actor, f.OrgID, f.Current.Revision, "cancel-pending"));
        var cancelled = f.Admit(actor => f.Service.CancelAdminJob(actor, f.OrgID, request.JobID, request.Revision, "cancel-original"));
        Check(cancelled.State == AdminJobState.Cancelled && cancelled.RunID is null && cancelled.Targets[0].State == AdminJobTargetState.Skipped,
            "pending cancellation has a durable distinct terminal target result");
        var bytes = f.StateBytes;
        var replay = f.Admit(actor => f.Restart().CancelAdminJob(actor,f.OrgID,request.JobID,request.Revision,"cancel-original"));
        Check(replay.JobID == cancelled.JobID && replay.State == cancelled.State && replay.Revision == cancelled.Revision && f.StateBytes.SequenceEqual(bytes),"cancellation replay preserves exact original terminal observation and audit");
        Throws<InvalidOperationException>(() => f.Service.ExecuteOrganisationExportAsync(f.Session, f.OrgID, request.JobID, cancelled.Revision).GetAwaiter().GetResult());
        using var cancellation = new CancellationTokenSource();
        RunHeldExport(f, () => cancellation.Cancel(), typeof(OperationCanceledException), "OperationCancelled", cancellation.Token);
    }

    private static void ConcurrentRoleMutationHasOneCanonicalWinner(Fixture f)
    {
        var current = f.Current; var errors = new List<Exception>();
        Task<OrganisationRoleMutationResult> Start(string name) => Task.Run(() => f.Service.UpdateRole(f.Owner, f.OrgID,
            f.RoleID, current.Revision, name, name, Set("Admin.Organisations.Get"), Set()));
        var first = Start("first-original"); var second = Start("second-original"); var success = 0;
        foreach (var original in new[]{first,second})
        { try { original.GetAwaiter().GetResult(); success++; } catch(Exception error) { errors.Add(error); } }
        Check(success == 1 && errors.Count == 1 && errors[0] is InvalidOperationException && f.Current.Revision == current.Revision + 1,
            "both same original tasks settle with exactly one canonical CAS winner");
    }

    private static void RunHeldExport(Fixture f, Action intervene, Type expectedError, string expectedCode,
        CancellationToken cancellationToken = default)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = f.Restart(_ => { entered.TrySetResult(); return release.Task; });
        var request = f.Admit(actor => service.RequestOrganisationExport(actor, f.OrgID, f.Current.Revision,
            "held-" + Guid.NewGuid().ToString("N")));
        Task<AdminJob>? original = null; Exception? primary = null; Exception? outcome = null;
        try
        {
            original = service.ExecuteOrganisationExportAsync(f.Session, f.OrgID, request.JobID, request.Revision, cancellationToken);
            entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Check(!original.IsCompleted && service.GetAdminJob(f.Owner, f.OrgID, request.JobID).State == AdminJobState.Running,
                "same original owning task and durable RunID are held before publication");
            Throws<InvalidOperationException>(() => service.CancelAdminJob(f.Owner, f.OrgID, request.JobID, request.Revision + 1, "cannot-cancel-running"));
            intervene();
        }
        catch(Exception error) { primary = error; }
        // Release and settle the SAME original task on every assertion/error path.
        release.TrySetResult();
        try { original?.GetAwaiter().GetResult(); } catch(Exception error) { outcome = error; }
        if(primary is not null)
        { if(outcome is not null && !ReferenceEquals(primary,outcome)) throw new AggregateException(primary,outcome); ExceptionDispatchInfo.Capture(primary).Throw(); }
        Check(original is not null && outcome is not null && expectedError.IsAssignableFrom(outcome.GetType()), "original current-session/revision/cancellation refusal retained");
        var recorded = f.Restart().GetAdminJob(f.Owner, f.OrgID, request.JobID);
        Check(recorded.State == AdminJobState.Failed && recorded.Code == expectedCode && recorded.RunID is not null &&
            recorded.ExportDocument is null && recorded.Targets[0].State == AdminJobTargetState.Failed,
            "same admitted job independently records durable terminal failure without further output authority");
        Check(f.Service.ListAudit(f.Owner,f.OrgID).Any(a => a.Action == "Admin.Jobs.ExportFailed" && a.TargetID == request.JobID), "original failure audit retained");
    }

    private static void CompletedSaveRetiresPayloadWithoutReplayingOriginal(Fixture f)
    {
        var request=f.Admit(actor=>f.Service.RequestOrganisationExport(actor,f.OrgID,f.Current.Revision,"expire-after-save"));
        f.Service.ExportObservationCheckpoint=phase=>
        { if(phase==AdminExportObservationPhase.AfterCompletedSave)ExpireOriginalSession(f); };
        var original=f.Service.ExecuteOrganisationExportAsync(f.Session,f.OrgID,request.JobID,request.Revision);
        var outcome=original.GetAwaiter().GetResult();
        Check(outcome.State==AdminJobState.Succeeded&&outcome.JobID==request.JobID&&outcome.RunID is not null&&
            outcome.Revision==request.Revision+2&&outcome.ExportDocument is null&&!outcome.OutputIsCurrent&&
            outcome.OutputWithheldReason=="CurrentOriginalSessionAndPermissionRequired",
            "expiry after actual completed durable Save preserves known success but releases no retired payload");
        var recorded=f.Restart().GetAdminJob(f.Owner,f.OrgID,request.JobID);
        Check(recorded.State==AdminJobState.Succeeded&&recorded.RunID==outcome.RunID&&recorded.Revision==outcome.Revision,
            "same original committed result remains durably reconciliable without replay");
        var bytes=f.StateBytes;
        Throws<UnauthorizedAccessException>(()=>f.Service.ExecuteOrganisationExportAsync(f.Session,f.OrgID,request.JobID,outcome.Revision).GetAwaiter().GetResult());
        Check(f.StateBytes.SequenceEqual(bytes),"retired original cannot retry execution or mutate its known terminal result");
    }
    private static void ActualSerializationRetiresPayloadAfterKnownCommit(Fixture f)
    {
        var request=f.Admit(actor=>f.Service.RequestOrganisationExport(actor,f.OrgID,f.Current.Revision,"expire-after-serialization"));
        var original=f.Service.ExecuteOrganisationExportAsync(f.Session,f.OrgID,request.JobID,request.Revision);
        var outcome=original.GetAwaiter().GetResult();
        Check(outcome.ExportDocument is not null&&outcome.OutputIsCurrent,"actual original completed before serialization retirement");
        f.Service.ExportObservationCheckpoint=phase=>
        { if(phase==AdminExportObservationPhase.AfterOutputSerialization)ExpireOriginalSession(f); };
        // SAME public owning publication method is selected by actual AdminWebDomain;
        // hook retires the genuine fixture session AFTER its real JSON was built.
        var result=f.Service.SerializeOriginalExportOutcome(f.Session,outcome);
        Check(result.GetProperty("JobID").GetGuid()==outcome.JobID&&result.GetProperty("RunID").GetGuid()==outcome.RunID&&
            result.GetProperty("State").GetInt32()==(int)AdminJobState.Succeeded&&
            result.GetProperty("ExportDocument").ValueKind==JsonValueKind.Null&&!result.GetProperty("OutputIsCurrent").GetBoolean(),
            "exact original session recheck after actual serialization purges built payload and keeps original known IDs/status");
        Check(f.Restart().GetAdminJob(f.Owner,f.OrgID,request.JobID).State==AdminJobState.Succeeded,
            "serialization refusal never rewrites observed owning success as a failed job");
    }
    private static void ExpireOriginalSession(Fixture f)
    {
        var state=JsonNode.Parse(File.ReadAllText(f.IdentityPath))!;
        var row=state["State"]!["Sessions"]!.AsArray().Single(n=>n!["SessionID"]!.GetValue<Guid>()==f.Issued.Session.SessionID)!;
        var expiry=DateTimeOffset.UtcNow.AddTicks(-1);
        Check(expiry>f.Issued.Session.CreatedAt,"real original fixture session has a valid observed lifetime before retirement");
        row["ExpiresAt"]=expiry;File.WriteAllText(f.IdentityPath,state.ToJsonString());
    }

    private static HashSet<string> Set(params string[] values) => new(values,StringComparer.Ordinal);
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T:Exception
    { try { action(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void RunCase(Action<Fixture> scenario)
    {
        var fixture = new Fixture(); Exception? primary = null;
        try { fixture.Initialize(); scenario(fixture); } catch(Exception error) { primary = error; }
        try { fixture.Dispose(); } catch(Exception cleanup)
        { if(primary is null) throw; if(!ReferenceEquals(primary,cleanup)) throw new AggregateException(primary,cleanup); }
        if(primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"9to1-admin-owner-"+Guid.NewGuid().ToString("N"));
        public string StatePath => Path.Combine(Root,"organisations.json");
        public string ProfilePath => Path.Combine(Root,"profiles.json");
        public string IdentityPath => Path.Combine(Root,"sessions.json");
        public Guid Owner { get; } = Guid.NewGuid(); public Guid Member { get; } = Guid.NewGuid(); public Guid Stranger { get; } = Guid.NewGuid();
        public Guid OrgID { get; private set; } public Guid RoleID { get; private set; }
        public ProfileService Profiles { get; private set; } = null!;
        public OrganisationService Service { get; private set; } = null!;
        public CakeIdentityService Identity { get; private set; } = null!;
        public IssuedSession Issued { get; private set; } = null!;
        public CurrentAdminSession Session { get; private set; } = null!;
        public string AccessToken => Issued.AccessToken;
        public byte[] OriginalProfiles { get; private set; } = [];
        public byte[] StateBytes => File.ReadAllBytes(StatePath);
        public Organisation Current => Service.Get(Owner,OrgID);
        public OrganisationService Restart(Func<CancellationToken,Task>? checkpoint=null) => new(StatePath,Profiles,adminJobCheckpoint:checkpoint);
        public T Admit<T>(Func<Guid,T> action) => Service.WithCurrentAdministrationSession(Session,action);
        public void Initialize()
        {
            Profiles = new(ProfilePath,null);
            foreach(var account in new[]{Owner,Member,Stranger}) Profiles.Update(account,0,
                JsonSerializer.SerializeToElement(new{name="Synthetic verified account",username="synthetic-"+account.ToString("N")}));
            Service = Restart();
            var org = Service.CreateTrustedOrganisation(Owner,"Canonical export organisation",BusinessAddOnKind.Business,"synthetic-existing-funded-addon");
            OrgID=org.OrgID;
            org=Service.CreateRole(Owner,OrgID,org.Revision,"member","Member",Set("Admin.Organisations.Get"),Set());
            RoleID=org.Roles.Single(r=>r.Name=="Member").RoleID;
            var invitation=Service.Invite(Owner,OrgID,Member,[RoleID],DateTimeOffset.UtcNow.AddHours(1));
            Service.AcceptInvitation(Member,invitation.Token);
            Identity=new(IdentityPath,new Dictionary<string,IReadOnlySet<string>>{{"admin-synthetic",Set("http://127.0.0.1/admin-callback")}});
            var verifier=new string('a',43);
            var code=Identity.AuthorizeAuthenticatedAccount(new(Owner,"Synthetic authenticated owner"),"admin-synthetic","http://127.0.0.1/admin-callback",CakeIdentityService.Challenge(verifier));
            Issued=Identity.Exchange(code,"admin-synthetic","http://127.0.0.1/admin-callback",verifier,"original-admin-fixture");
            Session=new AuthenticatedAdminManagementOperations(Identity).Open(Issued.AccessToken);
            OriginalProfiles=File.ReadAllBytes(ProfilePath);
        }
        public void Dispose() { if(Directory.Exists(Root)) Directory.Delete(Root,true); }
    }
}
