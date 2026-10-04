using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.ExceptionServices;
using NineToOne.Accounts;

/// <summary>Real durable canonical membership/role reads with synthetic verified
/// account IDs. This does not prove real CAKE credentials or a native Admin UI.</summary>
public static class OrganisationAdministrationSpecs
{
    public static void Run()
    {
        RunCase(StableBoundedPagesAcrossRestart);
        RunCase(CurrentPermissionAndMemberRevocation);
        RunCase(CursorCannotChangeActorOrganisationCollectionOrRevision);
        RunCase(CancellationMalformedCursorAndDetachedCollections);
        Console.WriteLine("PASS: canonical Admin members and roles, scoped pagination, current revocation and detached snapshots");
    }

    private static void StableBoundedPagesAcrossRestart(Fixture f)
    {
        var first = f.Service.ListMembers(f.Owner, f.OrgID, limit: 2);
        Check(first.Items.Count == 2 && first.TotalCount == 5 && first.NextCursor is not null,
            "first bounded page observes the real canonical member count");
        var restart = new OrganisationService(f.StatePath, f.Profiles);
        var second = restart.ListMembers(f.Owner, f.OrgID, first.NextCursor, 2);
        var third = restart.ListMembers(f.Owner, f.OrgID, second.NextCursor, 2);
        var members = first.Items.Concat(second.Items).Concat(third.Items).ToArray();
        Check(third.Items.Count == 1 && third.NextCursor is null && members.Select(m => m.MembershipID).Distinct().Count() == 5,
            "stable pages survive restart without duplicates or omitted canonical members");
        Check(members.Select(m => m.MembershipID).SequenceEqual(f.Current.Members.OrderBy(m => m.MembershipID).Select(m => m.MembershipID)),
            "all source MembershipID, AccountID and actual role identities are retained");
        Check(members.All(m => f.Current.Members.Any(original => original.MembershipID == m.MembershipID &&
            original.AccountID == m.AccountID && original.RoleIDs.SequenceEqual(m.RoleIDs))), "pages do not invent membership identities");
        var roles = f.Service.ListRoles(f.Owner, f.OrgID, limit: 1);
        Check(roles.Items.Count == 1 && roles.TotalCount == 2 && roles.NextCursor is not null,
            "roles use an independently bounded canonical collection");
        Check(f.StateBytes.SequenceEqual(f.OriginalStateBytes) && File.ReadAllBytes(f.ProfilePath).SequenceEqual(f.OriginalProfileBytes),
            "listing and restart make no organisation, audit, billing or personal profile mutation");
    }

    private static void CurrentPermissionAndMemberRevocation(Fixture f)
    {
        Throws<OrganisationAccessException>(() => f.Service.ListMembers(f.Stranger, f.OrgID));
        Throws<OrganisationAccessException>(() => f.Service.ListRoles(f.Stranger, f.OrgID));
        var current = f.Current;
        current = f.Service.CreateRole(f.Owner, f.OrgID, current.Revision, "list-delegation", "People observer",
            new HashSet<string> { "Admin.Members.List" }, new HashSet<string>());
        var delegated = current.Roles.Single(r => r.Name == "People observer");
        current = f.Service.SetRoles(f.Owner, f.OrgID, current.Revision, "assign-list", f.Members[0], [delegated.RoleID]);
        var allowed = f.Service.ListMembers(f.Members[0], f.OrgID, limit: 1);
        Check(allowed.NextCursor is not null && allowed.Items.Count == 1, "current explicit delegated list capability permits only its collection");
        Throws<OrganisationAccessException>(() => f.Service.ListRoles(f.Members[0], f.OrgID));
        f.Service.SetMemberState(f.Owner, f.OrgID, current.Revision, "suspend-observer", f.Members[0], OrganisationMemberState.Suspended);
        Throws<OrganisationAccessException>(() => f.Service.ListMembers(f.Members[0], f.OrgID, allowed.NextCursor, 1));
        Check(File.ReadAllBytes(f.ProfilePath).SequenceEqual(f.OriginalProfileBytes), "organisation revocation preserves personal profiles");
    }

    private static void CursorCannotChangeActorOrganisationCollectionOrRevision(Fixture f)
    {
        var page = f.Service.ListMembers(f.Owner, f.OrgID, limit: 1);
        Check(page.NextCursor is not null, "real continuation cursor exists");
        Throws<ArgumentException>(() => f.Service.ListRoles(f.Owner, f.OrgID, page.NextCursor, 1));
        Throws<ArgumentException>(() => f.Service.ListMembers(f.Owner, f.OrgID, page.NextCursor, 2));
        var other = f.Service.CreateTrustedOrganisation(f.Owner, "Unrelated organisation", BusinessAddOnKind.Business,
            "synthetic-existing-funded-addon");
        Throws<ArgumentException>(() => f.Service.ListMembers(f.Owner, other.OrgID, page.NextCursor, 1));
        var current = f.Current;
        current = f.Service.CreateRole(f.Owner, f.OrgID, current.Revision, "another-list-role", "Another observer",
            new HashSet<string> { "Admin.Members.List" }, new HashSet<string>());
        var role = current.Roles.Single(r => r.Name == "Another observer");
        current = f.Service.SetRoles(f.Owner, f.OrgID, current.Revision, "assign-other-observer", f.Members[1], [role.RoleID]);
        var fresh = f.Service.ListMembers(f.Owner, f.OrgID, limit: 1);
        Throws<ArgumentException>(() => f.Service.ListMembers(f.Members[1], f.OrgID, fresh.NextCursor, 1));
        Throws<InvalidOperationException>(() => f.Service.ListMembers(f.Owner, f.OrgID, page.NextCursor, 1));
        var before = f.StateBytes;
        f.Service.PublishPolicy(f.Owner, f.OrgID, current.Revision, "deny-members-read",
            new HashSet<string> { "Admin.Members.List" }, new Dictionary<string, string>(), new Dictionary<string, string>());
        Throws<OrganisationAccessException>(() => f.Service.ListMembers(f.Owner, f.OrgID, fresh.NextCursor, 1));
        Check(!f.StateBytes.SequenceEqual(before), "the real policy changed; the copied cursor cannot bypass its current denial");
    }

    private static void CancellationMalformedCursorAndDetachedCollections(Fixture f)
    {
        var before = f.StateBytes;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Throws<OperationCanceledException>(() => f.Service.ListMembers(f.Owner, f.OrgID, cancellationToken: canceled.Token));
        Throws<ArgumentOutOfRangeException>(() => f.Service.ListRoles(f.Owner, f.OrgID, limit: 201));
        Throws<ArgumentException>(() => f.Service.ListMembers(f.Owner, f.OrgID, "a", 1));
        Throws<ArgumentException>(() => f.Service.ListMembers(f.Owner, f.OrgID, new string('A', 513), 1));
        var page = f.Service.ListMembers(f.Owner, f.OrgID, limit: 1);
        var cursor = JsonNode.Parse(Convert.FromBase64String(Pad(page.NextCursor!)))!;
        cursor["SchemaVersion"] = 99;
        Throws<ArgumentException>(() => f.Service.ListMembers(f.Owner, f.OrgID, Encode(cursor.ToJsonString()), 1));
        cursor["SchemaVersion"] = 1; cursor["CopiedRoleGrant"] = "*";
        Throws<ArgumentException>(() => f.Service.ListMembers(f.Owner, f.OrgID, Encode(cursor.ToJsonString()), 1));
        var duplicate = "{\"SchemaVersion\":1,\"SchemaVersion\":1,\"OrgID\":\"" + f.OrgID + "\"}";
        Throws<ArgumentException>(() => f.Service.ListMembers(f.Owner, f.OrgID, Encode(duplicate), 1));
        var roles = f.Service.ListRoles(f.Owner, f.OrgID);
        Check(roles.Items.All(r => f.Current.Roles.Any(o => o.RoleID == r.RoleID &&
            r.Grants.SetEquals(o.Grants) && r.Denials.SetEquals(o.Denials))),
            "returned role capabilities and denials retain the exact canonical values");
        Throws<NotSupportedException>(() => ((IList<OrganisationRole>)roles.Items).Clear());
        Throws<NotSupportedException>(() => ((IList<Guid>)page.Items[0].RoleIDs).Clear());
        Check(f.StateBytes.SequenceEqual(before), "all read refusal and client collection mutation attempts preserve exact durable bytes");
    }

    private static string Pad(string encoded)
    { encoded = encoded.Replace('-', '+').Replace('_', '/'); return encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '='); }
    private static string Encode(string json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

    private static void RunCase(Action<Fixture> test)
    {
        var fixture = new Fixture(); Exception? primary = null;
        try { fixture.Initialize(); test(fixture); }
        catch (Exception error) { primary = error; }
        var errors = new List<Exception>();
        if (primary is not null) errors.Add(primary);
        try { fixture.Dispose(); } catch (Exception error) { if (!errors.Any(e => ReferenceEquals(e, error))) errors.Add(error); }
        if (errors.Count > 1) throw new AggregateException("AdminCollectionFixtureFailed", errors);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
    }

    private sealed class Fixture : IDisposable
    {
        private string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-admin-collection-" + Guid.NewGuid().ToString("N"));
        public string StatePath => Path.Combine(Root, "organisation.json");
        public string ProfilePath => Path.Combine(Root, "profiles.json");
        public Guid Owner { get; } = Guid.NewGuid(); public Guid Stranger { get; } = Guid.NewGuid();
        public Guid[] Members { get; } = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        public ProfileService Profiles { get; private set; } = null!;
        public OrganisationService Service { get; private set; } = null!;
        public Guid OrgID { get; private set; }
        public byte[] StateBytes => File.ReadAllBytes(StatePath);
        public byte[] OriginalStateBytes { get; private set; } = [];
        public byte[] OriginalProfileBytes { get; private set; } = [];
        public Organisation Current => Service.Get(Owner, OrgID);
        public void Initialize()
        {
            Profiles = new(ProfilePath, null);
            foreach (var account in Members.Append(Owner).Append(Stranger))
                Profiles.Update(account, 0, JsonSerializer.SerializeToElement(new { name = "Synthetic verified account", username = "synthetic-" + account.ToString("N") }));
            Service = new(StatePath, Profiles);
            var org = Service.CreateTrustedOrganisation(Owner, "Canonical organisation", BusinessAddOnKind.Business, "synthetic-existing-funded-addon");
            OrgID = org.OrgID;
            org = Service.CreateRole(Owner, OrgID, org.Revision, "member-role", "Member",
                new HashSet<string> { "Admin.Organisations.Get" }, new HashSet<string>());
            var memberRole = org.Roles.Single(r => r.Name == "Member");
            foreach (var account in Members)
            {
                var invitation = Service.Invite(Owner, OrgID, account, [memberRole.RoleID], DateTimeOffset.UtcNow.AddHours(1));
                Service.AcceptInvitation(account, invitation.Token);
            }
            OriginalStateBytes = StateBytes; OriginalProfileBytes = File.ReadAllBytes(ProfilePath);
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
