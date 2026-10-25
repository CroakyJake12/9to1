using Haven.Application;
using Haven.Core;
using HavenOS.Home;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Read-only original request source over the existing Home actor, verified local
/// settings owner and SAME Space registry. It creates no store identity, ownership or permission.</summary>
internal sealed partial class OriginalSpaceRequestPolicySource(IServiceProvider services) : IOriginalSpaceRequestPolicySource
{
    private readonly IServiceProvider _services = services;
    private sealed record Owners(HomeNativeWindowsComposition Home, SpaceRegistry Spaces, IVersionedSettingsStore Settings);
    private sealed record Authority(AuthenticatedResourceActor Actor, SettingsStoreIdentity Store,
        VerifiedResourceStoreOwnership Ownership, long ServicesRevision);
    private sealed record Input(OriginalSpaceRequestPolicySource Owner, Owners Owners, Authority Authority,
        Guid ConversationId, Guid SpaceId, OriginalSpacePolicySnapshot Snapshot, ContextCustody? Context) : IOriginalSpaceRequestPolicyInput;

    private Owners CurrentOwners()
    {
        var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
        var spaces = _services.GetRequiredService<SpaceRegistry>();
        var settings = _services.GetRequiredService<IVersionedSettingsStore>();
        if (!spaces.HasOriginalSettingsStore(settings)) throw Block("SPACE_POLICY_STORE_MISMATCH", "The original Space registry and settings store do not match.");
        return new(home, spaces, settings);
    }

    public Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, Action<Action> acquireOriginal, CancellationToken token) =>
        CaptureAsync(conversation, OriginalSpaceContextBudget.Default, acquireOriginal, token);

    public Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, OriginalSpaceContextBudget budget,
        Action<Action> acquireOriginal, CancellationToken token) =>
        Run(acquireOriginal, async originals =>
        {
            if (conversation.Id == Guid.Empty || conversation.SpaceId is not { } id || id == Guid.Empty)
                throw Block("SPACE_POLICY_IDENTITY_INVALID", "The actual conversation and Space identities are required.");
            var owners = originals.Invoke(CurrentOwners);
            var authority = await ReadAuthorityAsync(owners, originals, token).ConfigureAwait(false);
            var snapshot = await ReadPolicyAsync(owners, id, originals, token).ConfigureAwait(false);
            await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
            await DemandVersionPinsAsync(snapshot, originals, token).ConfigureAwait(false);
            var context = await CaptureContextAsync(owners, authority, conversation, snapshot, budget, originals, token).ConfigureAwait(false);
            await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
            return (IOriginalSpaceRequestPolicyInput)new Input(this, owners, authority, conversation.Id, id, context.Snapshot, context.Custody);
        });

    public Task<OriginalSpacePolicySnapshot> ReadAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation,
        Action<Action> acquireOriginal, CancellationToken token) => Run(acquireOriginal, async originals =>
        { var actual = RequireInput(input, conversation); await ValidateCoreAsync(actual, originals, token).ConfigureAwait(false); return actual.Snapshot; });

    public Task ValidateAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquireOriginal, CancellationToken token) =>
        Run(acquireOriginal, async originals => { await ValidateCoreAsync(RequireInput(input, conversation), originals, token).ConfigureAwait(false); return true; });

    private Input RequireInput(IOriginalSpaceRequestPolicyInput input, Conversation conversation)
    {
        if (input is not Input actual || !ReferenceEquals(actual.Owner, this) || conversation.Id != actual.ConversationId ||
            conversation.SpaceId != actual.SpaceId || conversation.IsArchived)
            throw Block("SPACE_POLICY_INPUT_NOT_ORIGINAL", "This request does not retain its original Space policy input.");
        return actual;
    }
    private async Task ValidateCoreAsync(Input input, CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        await DemandAuthorityAsync(input.Owners, input.Authority, originals, token).ConfigureAwait(false);
        var current = await ReadPolicyAsync(input.Owners, input.SpaceId, originals, token).ConfigureAwait(false);
        if (!current.Lineage.Equals(input.Snapshot.Lineage)) throw Block("SPACE_POLICY_CHANGED", "The Space or an ancestor changed before dispatch.");
        await DemandVersionPinsAsync(current, originals, token).ConfigureAwait(false);
        if (input.Context is { } context)
            await context.RevalidateAsync(originals, token).ConfigureAwait(false);
        await DemandAuthorityAsync(input.Owners, input.Authority, originals, token).ConfigureAwait(false);
    }
    private Task<OriginalSpacePolicySnapshot> ReadPolicyAsync(Owners owners, Guid id, CloudflareOriginalTaskLedger originals, CancellationToken token) =>
        originals.AwaitAsync(originals.Invoke(() => new SpacePolicyResolver(owners.Spaces).ResolveExistingAsync(id,
            body => originals.Invoke(() => { body(); return true; }), task => originals.Track(task), token)));

    private async Task<Authority> ReadAuthorityAsync(Owners owners, CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var actor = await originals.AwaitAsync(originals.Invoke(() => owners.Home.Profiles.GetCurrentWithinOriginalSourceAsync(body => originals.Invoke(() => { body(); return true; }), task => originals.Track(task), token))).ConfigureAwait(false)
            ?? throw Block("SPACE_POLICY_PROFILE_UNAVAILABLE", "The original authenticated Home profile is unavailable.");
        if (actor.AccountId is not null || actor.OrganisationId is not null)
            throw Block("SPACE_POLICY_SCOPE_UNAVAILABLE", "This original policy source supports the currently verified local profile scope only.");
        var runtime = originals.Invoke(() => owners.Home.Runtime.Current);
        foreach (var id in new[] { "home.core", "home.state", "permissions.trust" })
        {
            var service = runtime.Services.SingleOrDefault(row => row.ServiceId == id);
            if (service is null || !service.IsAvailable || service.State != HomeServiceLifecycleState.Ready)
                throw Block("SPACE_POLICY_HOME_UNAVAILABLE", "An original Home service required by this request is unavailable.");
        }
        // Export is non-seeding: GetStoreIdentityAsync would persist a missing identity.
        var state = await originals.AwaitAsync(originals.Invoke(() => owners.Settings.ExportAsync(token))).ConfigureAwait(false);
        if (state.StoreIdentity is not { SchemaVersion: 1 } identity || identity.StoreId == Guid.Empty)
            throw Block("SPACE_POLICY_STORE_IDENTITY_UNAVAILABLE", "This existing Space store needs its actual Home-owned identity before requests can use it.");
        var ownership = await originals.AwaitAsync(originals.Invoke(() => owners.Home.Ownership.GetVerifiedWithinOriginalSourceAsync("spaces", identity.StoreId.ToString("D"), body => originals.Invoke(() => { body(); return true; }), task => originals.Track(task), token))).ConfigureAwait(false);
        if (ownership?.Receipt is null || ownership.ProfileId != actor.ProfileId || ownership.ResourceKind != "spaces" || ownership.StoreId != identity.StoreId.ToString("D") ||
            !await originals.AwaitAsync(originals.Invoke(() => owners.Home.Ownership.IsCurrentWithinOriginalSourceAsync(ownership, actor, body => originals.Invoke(() => { body(); return true; }), task => originals.Track(task), token))).ConfigureAwait(false))
            throw Block("SPACE_POLICY_STORE_OWNERSHIP_UNAVAILABLE", "Home has not verified this profile's current Space store ownership.");
        return new(actor, identity, ownership, runtime.Revision);
    }
    private async Task DemandAuthorityAsync(Owners expected, Authority captured, CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var actual = originals.Invoke(CurrentOwners);
        if (!ReferenceEquals(actual.Home, expected.Home) || !ReferenceEquals(actual.Spaces, expected.Spaces) || !ReferenceEquals(actual.Settings, expected.Settings))
            throw Block("SPACE_POLICY_OWNER_CHANGED", "The original Home or Space owner changed.");
        var now = await ReadAuthorityAsync(actual, originals, token).ConfigureAwait(false);
        if (now.Actor != captured.Actor || now.Store != captured.Store || now.ServicesRevision != captured.ServicesRevision ||
            now.Ownership.Receipt != captured.Ownership.Receipt || now.Ownership.ProfileId != captured.Ownership.ProfileId ||
            !await originals.AwaitAsync(originals.Invoke(() => actual.Home.Ownership.IsCurrentWithinOriginalSourceAsync(captured.Ownership, captured.Actor, body => originals.Invoke(() => { body(); return true; }), task => originals.Track(task), token))).ConfigureAwait(false))
            throw Block("SPACE_POLICY_AUTHORITY_CHANGED", "The original Space profile, store or Home ownership changed before dispatch.");
    }
    private async Task DemandVersionPinsAsync(OriginalSpacePolicySnapshot snapshot, CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var pins = snapshot.Policy.Capabilities.Where(row => row.Override != SpaceCapabilityOverride.Disabled && !string.IsNullOrWhiteSpace(row.VersionPin)).ToArray();
        if (pins.Length > 128) throw Block("SPACE_POLICY_PIN_BOUND", "The pinned capability set exceeds this request's explicit verification bound.");
        foreach (var pin in pins)
        {
            var extensions = originals.Invoke(() => _services.GetRequiredService<ExtensionManager>());
            if (pin.Kind == SpaceCapabilityKind.Plugin)
            {
                var package = await originals.AwaitAsync(originals.Invoke(() => extensions.GetPackageAsync(pin.CapabilityId, token))).ConfigureAwait(false);
                if (package is null || !package.IsEnabled || package.Manifest.Version != pin.VersionPin)
                    throw Block("SPACE_POLICY_PIN_UNAVAILABLE", "A pinned Plugin version is unavailable or no longer enabled.");
            }
            else if (pin.Kind == SpaceCapabilityKind.Skill)
            {
                var skills = await originals.AwaitAsync(originals.Invoke(() => extensions.ListSkillsAsync(token))).ConfigureAwait(false);
                if (!skills.Any(row => row.SkillId == pin.CapabilityId && row.IsAvailable && row.PackageVersion == pin.VersionPin))
                    throw Block("SPACE_POLICY_PIN_UNAVAILABLE", "A pinned Skill version is unavailable or no longer enabled.");
            }
            else throw Block("SPACE_POLICY_PIN_OWNER_UNAVAILABLE", "This pinned capability needs its actual version owner before it can be used.");
        }
    }
    private static Task<T> Run<T>(Action<Action> acquireOriginal, Func<CloudflareOriginalTaskLedger, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(acquireOriginal);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalCallerCallback(acquireOriginal);
        return originals.RunToOriginalSettlementAsync(() => body(originals));
    }
    private static SpaceRequestPolicyBlockedException Block(string code, string message) => new(code, message);
}
