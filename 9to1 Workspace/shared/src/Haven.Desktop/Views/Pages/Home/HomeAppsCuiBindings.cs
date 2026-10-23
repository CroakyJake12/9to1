using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>One launcher entry, projected from the registered mode record. The identity, name,
/// description and icon key all come from stored product state; nothing here is illustrative.
/// A tile may be a group-navigation tile (<see cref="IsGroup"/>) but a group is never a new
/// installable app, package or runtime owner. Real canonical identity (the stored mode GUID and
/// source) is carried so group membership is resolved by actual identity, not by a name-lookalike
/// key match.</summary>
public sealed record HomeAppItem(string Key, string Name, string Description, string IconKey,
    bool IsGroup = false, Guid? CanonicalId = null, ModeSource? Source = null)
{
    /// <summary>The accessible action a tile performs, so the launcher never relies on the visible
    /// name alone to say what activation does.</summary>
    public string OpenLabel => "Open " + Name;
}

/// <summary>A single member of a Home group. Real canonical members are projected from their actual
/// mode records with their canonical identity (GUID + source). A referenced identity without a
/// native ModeDefinition/GUID in this composition is surfaced truthfully as unavailable
/// (IsInstalled=false) with an explanation; it is never fabricated as installed, minted a synthetic
/// GUID, or routed to another engine.</summary>
public sealed record HomeGroupMember(string Key, string Name, string Description, string IconKey,
    bool IsInstalled, string AvailabilityNote, Guid? CanonicalId = null, ModeSource? Source = null)
{
    /// <summary>Unavailable members open no app; their accessible name states the unavailable state.</summary>
    public string OpenLabel => (IsInstalled ? "Open " : "Unavailable: ") + Name;
}

/// <summary>A Home group: canonical navigation over real members (e.g. Create → Picture/Motion/Wave).
/// It is group navigation, not an installable ModeDefinition; no ID/package/engine is minted.</summary>
public sealed record HomeAppGroup(string Key, string Name, string IconKey, string Description,
    IReadOnlyList<HomeGroupMember> Members)
{
    public bool HasMembers => Members.Count > 0;
}

/// <summary>
/// Canonical Create-group metadata and projection helper. Membership is resolved by REAL canonical
/// built-in identity (GUID + source) for Picture (imagine GUID …0008) and Motion (motion GUID …0025);
/// Wave (audio creation) is a canonical built-in identity with no native ModeDefinition/GUID in this
/// composition and is resolved by built-in source + key, surfaced truthfully unavailable unless a
/// genuine built-in Wave record is present. A custom app whose key/name merely resembles a member is
/// never captured. Home groups are navigation, never installable apps, so they contribute no count and
/// mint no IDs.
/// </summary>
public static class HomeCreateGroup
{
    public const string Key = "create";
    public const string Name = "Create";
    public const string IconKey = "palette";
    public const string Description = "Open Picture, Motion and Wave";

    private sealed record MemberSpec(string Key, string DisplayName, string IconKey, Guid? CanonicalId);

    private static readonly MemberSpec[] MemberSpecs =
    [
        new("imagine", "Picture", "palette", Guid.Parse("a0000000-0000-0000-0000-000000000008")),
        new("motion", "Motion", "play", Guid.Parse("a0000000-0000-0000-0000-000000000025")),
        // Wave is a canonical built-in product identity (audio creation). There is no Wave native
        // ModeDefinition GUID or registration/factory in this composition, so its canonical identity is
        // null rather than a fabricated GUID. If a genuine built-in Wave record later appears in the
        // catalogue it is resolved here by identity (built-in source + key) exactly like Picture/Motion.
        new("wave", "Wave", "wave", null)
    ];

    /// <summary>True when the item is one of the real canonical built-in members, matched by stored
    /// mode identity — GUID + built-in source for Picture/Motion; built-in source + key for Wave —
    /// never by visible name alone.</summary>
    public static bool IsCanonicalMember(HomeAppItem item) =>
        item.Source == ModeSource.BuiltIn && MemberSpecs.Any(spec => MatchesSpec(spec, item));

    private static bool MatchesSpec(MemberSpec spec, HomeAppItem item)
    {
        if (item.Source != ModeSource.BuiltIn) return false;
        return spec.CanonicalId is { } id
            ? id == item.CanonicalId
            : string.Equals(item.Key, spec.Key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The top-level launcher items: the real visible app records minus the canonical members
    /// that move into Create, plus a single Create group-navigation tile only when at least one genuine
    /// canonical built-in member is present (an empty/no-member catalogue keeps its truthful no-app
    /// state rather than showing a group tile that would falsely appear installed).</summary>
    public static IReadOnlyList<HomeAppItem> BuildLauncher(IReadOnlyList<HomeAppItem> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var launcher = catalogue.Where(item => !IsCanonicalMember(item)).ToList();
        if (catalogue.Any(IsCanonicalMember))
            launcher.Add(new HomeAppItem(Key, Name, Description, IconKey, IsGroup: true));
        return launcher;
    }

    /// <summary>Projects the Create group from the real catalogue. Canonical members are resolved by
    /// their actual built-in identity; a canonical member whose record is absent is surfaced as
    /// unavailable rather than hidden or invented. Wave (no native GUID/factory here) is shown as a
    /// truthful, non-launchable reference unless a genuine built-in Wave record is present.</summary>
    public static HomeAppGroup Build(IReadOnlyList<HomeAppItem> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var members = new List<HomeGroupMember>();
        foreach (var spec in MemberSpecs)
        {
            var member = catalogue.FirstOrDefault(item => MatchesSpec(spec, item));
            members.Add(member is null
                ? new HomeGroupMember(spec.Key, spec.DisplayName, UnavailableDescriptionFor(spec.Key),
                    spec.IconKey, false, AvailabilityNoteFor(spec.Key, spec.DisplayName),
                    spec.CanonicalId, ModeSource.BuiltIn)
                : new HomeGroupMember(member.Key, member.Name, member.Description, member.IconKey, true,
                    string.Empty, member.CanonicalId, member.Source));
        }
        return new HomeAppGroup(Key, Name, IconKey, Description, members);
    }

    public static IReadOnlyList<HomeGroupMember> FilterMembers(IReadOnlyList<HomeGroupMember> members, string query) =>
        query.Length == 0
            ? members
            : members.Where(member => member.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || member.Key.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>Selects the members to show when the Create group is OPEN. Precedence: a query that
    /// matches the group's OWN key/name (e.g. "create") surfaces every member, so opening Create from
    /// its own tile never hides its children. Otherwise actual child key/name matches win and show
    /// only those members (e.g. "wave" shows only Wave). Only when NO child matches may a description-
    /// only match (e.g. "Open") surface all members; otherwise the surface is honestly empty.</summary>
    public static IReadOnlyList<HomeGroupMember> ResolveGroupVisible(HomeAppGroup group, string query)
    {
        if (query.Length == 0) return group.Members;
        if (Matches(group.Key, query) || Matches(group.Name, query))
            return group.Members;
        var childMatches = FilterMembers(group.Members, query);
        if (childMatches.Count > 0) return childMatches;
        if (Matches(group.Description, query)) return group.Members;
        return childMatches;
    }

    /// <summary>True when any Create member (key or name) matches the query. This keeps the Create
    /// group discoverable when searching for a child member (e.g. "imagine") even while the group is
    /// closed.</summary>
    public static bool MatchesAnyMember(HomeAppGroup group, string query)
    {
        if (query.Length == 0) return false;
        foreach (var member in group.Members)
            if (member.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || member.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Launcher filtering that is group-aware: normal text matches keep a tile; additionally,
    /// the Create navigation tile stays visible when the query matches the group's own key/name/
    /// description OR one of its child members, so searching "imagine" or "create" still surfaces
    /// Create.</summary>
    public static IReadOnlyList<HomeAppItem> FilterLauncher(
        IReadOnlyList<HomeAppItem> launcher, HomeAppGroup group, string query)
    {
        if (query.Length == 0) return launcher;
        var ownMatch = Matches(group.Key, query) || Matches(group.Name, query) || Matches(group.Description, query);
        var combined = ownMatch || MatchesAnyMember(group, query);
        return launcher.Where(item =>
            item.IsGroup ? combined
                : Matches(item.Key, query) || Matches(item.Name, query) || Matches(item.Description, query)).ToArray();
    }

    private static bool Matches(string text, string query) =>
        text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string UnavailableDescriptionFor(string key) => key switch
    {
        "wave" => "Create and edit audio locally",
        _ => string.Empty
    };

    private static string AvailabilityNoteFor(string key, string displayName) => key switch
    {
        "wave" => "Wave is not installed in this native composition.",
        _ => $"{displayName} is not available in this profile."
    };
}

/// <summary>The current launcher projection the authored document reads: the filtered catalogue, the
/// live query, the truthful availability/empty state, and the Create group. Whether the group is open
/// or closed is authored CUI state (a document Variable), not host presentation state; the group's
/// member data is always projected here.</summary>
public sealed record HomeAppsViewState(
    IReadOnlyList<HomeAppItem> All,
    IReadOnlyList<HomeAppItem> Visible,
    string Query,
    string Status,
    IReadOnlyList<HomeAppItem>? Launcher = null,
    HomeAppGroup? Group = null,
    IReadOnlyList<HomeGroupMember>? GroupVisible = null)
{
    public static HomeAppsViewState Empty { get; } =
        new([], [], string.Empty, "Loading the installed apps.");

    /// <summary>All real visible app records (count basis); group navigation and unavailable
    /// references are never counted as apps.</summary>
    public IReadOnlyList<HomeAppItem> AllRecords => All;

    /// <summary>The top-level items actually rendered (apps minus group members, plus group tiles).</summary>
    public IReadOnlyList<HomeAppItem> LauncherItems => Launcher ?? All;

    /// <summary>The group members actually rendered (filtered) when a group is open.</summary>
    public IReadOnlyList<HomeGroupMember> EffectiveGroupVisible => GroupVisible ?? [];

    public bool HasQuery => Query.Length > 0;

    /// <summary>True when the closed launcher surface has at least one visible top-level tile.</summary>
    public bool HasVisibleLauncher => Visible.Count > 0;
    public bool HasNoVisibleLauncher => !HasVisibleLauncher;

    /// <summary>True when the open group surface has at least one installed (launchable) member.</summary>
    public bool HasVisibleMembers => EffectiveGroupVisible.Any(member => member.IsInstalled);
    public bool HasNoVisibleMembers => !HasVisibleMembers;

    /// <summary>The number of member rows actually shown in the open group (regardless of installed /
    /// launchable status, so an unavailable Wave row still counts as a shown row).</summary>
    public int GroupRowCount => EffectiveGroupVisible.Count;
    public bool HasShownRows => GroupRowCount > 0;
    public bool HasNoShownRows => !HasShownRows;

    /// <summary>True when an actionable entry exists on either surface. Used only for action
    /// availability; presentation uses the honest per-surface projections above, never this union.</summary>
    public bool HasVisibleApps => HasVisibleLauncher || HasVisibleMembers;
    public bool HasNoVisibleApps => !HasVisibleApps;

    public string CountLabel => All.Count == 1 ? "1 app" : All.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) + " apps";

    /// <summary>Distinct, actionable empty states: an unmatched search is not the same condition as
    /// a profile with nothing installed, and the launcher never shows a blank region for either.</summary>
    public string EmptyTitle => HasQuery
        ? "No app matches “" + Query + "”."
        : "No apps are available in this profile.";

    public string EmptyDetail => HasQuery
        ? "Clear the search, or use the address bar to open an app by name."
        : "Install an app from Home, then open it here.";

    public static IReadOnlyList<HomeAppItem> Filter(IReadOnlyList<HomeAppItem> items, string query) =>
        query.Length == 0
            ? items
            : items.Where(app => app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || app.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                || app.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
}

/// <summary>
/// CUI binding owner for <c>UnifiedHomeApps.cui</c>.
///
/// The authored launcher only reads values and raises actions. This class publishes the current
/// registered-catalogue projection and dispatches every action back through the Home page, so the
/// CUI document never calls domain methods directly. Group open/closed state is authored in the CUI
/// document as a document Variable toggled by document Actions; this binding does not own that
/// presentation state.
/// </summary>
public sealed class HomeAppsCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged,
    IHomeBrandObservationCoordinator
{
    private static readonly IReadOnlySet<string> KnownCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "home.open-app", "home.set-app-query", "home.clear-app-query"
    };

    private readonly CuiViewModel _values = new();
    private readonly Func<HomeAppsViewState> _state;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private HomeAppsViewState _snapshot = HomeAppsViewState.Empty;
    private long _revision;
    private readonly ConditionalWeakTable<Control, BrandTargetObservation> _brandTargets = new();
    private readonly List<WeakReference<BrandTargetObservation>> _activeObservations = new();
    private long _generation;
    private bool _targetsRetired;

    public HomeAppsCuiBindings(
        Func<HomeAppsViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _values.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HomeAppsViewState Snapshot => _state();

    public void Refresh()
    {
        _snapshot = _state();
        _values.Set("Revision", _revision++);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>Permanently retires every per-target brand observation. No subsequent attach/detach or
    /// appearance change runs any callback — even after later teardown work fails. Called by the Home
    /// page before its loaders are disposed.</summary>
    internal void RetireAppearanceObservation()
    {
        _targetsRetired = true;
        foreach (var reference in _activeObservations)
            if (reference.TryGetTarget(out var observation))
                observation.Stop();
        _activeObservations.Clear();
        _brandTargets.Clear();
    }

    /// <summary>Reconciles the brand observation for an actual rendered image target. Called by the
    /// registered CUI resolver for EVERY resolved source (brand or not) before any branch, so stale
    /// branding ownership is dropped when a target moves to another brand or to a generic rail/catalog
    /// or malformed source. Only a VALID bounded brand token merits observation. Keeps one resolver
    /// registration; rail/catalog fallback never bypasses cleanup.</summary>
    void IHomeBrandObservationCoordinator.ReconcileTarget(Control target, string symbolicSource)
    {
        if (_targetsRetired) return;
        PruneActiveObservations();
        var isBrand = HomeAppBrandingIcons.TryParseBrandSource(symbolicSource, out _, out _);
        if (isBrand)
        {
            if (_brandTargets.TryGetValue(target, out var existing))
            {
                existing.Source = symbolicSource;
                return;
            }
            var observation = new BrandTargetObservation(this, target);
            // Publish into the weak-key table and the weak tracking list BEFORE starting the observation:
            // Start() may synchronously attach and refresh, which re-enters the resolver for this same
            // target; the table must already own the observation so that reentry updates it instead of
            // constructing another.
            _brandTargets.Add(target, observation);
            _activeObservations.Add(new WeakReference<BrandTargetObservation>(observation));
            observation.Start();
        }
        else if (_brandTargets.TryGetValue(target, out var removed))
        {
            removed.Stop();
            _brandTargets.Remove(target);
        }
    }

    private void PruneActiveObservations()
    {
        for (var i = _activeObservations.Count - 1; i >= 0; i--)
            if (!_activeObservations[i].TryGetTarget(out _))
                _activeObservations.RemoveAt(i);
    }

    public bool TryGetValue(string path, out object? value)
    {
        var snapshot = Snapshot;
        value = path switch
        {
            "apps.all" => snapshot.AllRecords,
            "apps.visible" => snapshot.Visible,
            "apps.launcher" => snapshot.LauncherItems,
            "apps.query" => snapshot.Query,
            "apps.hasQuery" => snapshot.HasQuery,
            "apps.hasVisibleApps" => snapshot.HasVisibleApps,
            "apps.hasNoVisibleApps" => snapshot.HasNoVisibleApps,
            "apps.hasVisibleLauncher" => snapshot.HasVisibleLauncher,
            "apps.hasNoVisibleLauncher" => snapshot.HasNoVisibleLauncher,
            "apps.hasVisibleMembers" => snapshot.HasVisibleMembers,
            "apps.hasNoVisibleMembers" => snapshot.HasNoVisibleMembers,
            "apps.countLabel" => snapshot.CountLabel,
            "apps.emptyTitle" => snapshot.EmptyTitle,
            "apps.emptyDetail" => snapshot.EmptyDetail,
            "apps.status" => snapshot.Status,
            "apps.group.name" => snapshot.Group?.Name,
            "apps.group.iconSource" => HomeAppBrandingIcons.SourceForGroup(snapshot.Group?.Key, snapshot.Group?.IconKey),
            "apps.group.members" => snapshot.EffectiveGroupVisible,
            "apps.group.hasMembers" => snapshot.EffectiveGroupVisible.Count > 0,
            "apps.group.visibleCount" => snapshot.GroupRowCount,
            "apps.group.hasShownRows" => snapshot.HasShownRows,
            "apps.group.hasNoShownRows" => snapshot.HasNoShownRows,
            "apps.group.backLabel" => "Back to Apps",
            _ => Unresolved
        };
        return !ReferenceEquals(value, Unresolved);
    }

    public bool TrySetValue(string path, object? value)
    {
        if (!path.Equals("apps.query", StringComparison.OrdinalIgnoreCase)) return false;
        _ = _dispatch("home.set-app-query", value, CancellationToken.None);
        return true;
    }

    public bool? IsActionAvailable(string command)
    {
        if (!KnownCommands.Contains(command)) return null;
        var snapshot = Snapshot;
        return command switch
        {
            // Clearing is only offered when a query is actually narrowing the catalogue.
            "home.clear-app-query" => snapshot.HasQuery,
            // Opening needs a real entry on the currently relevant surface (launcher or group).
            "home.open-app" => snapshot.HasVisibleApps,
            _ => true
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (!KnownCommands.Contains(command))
            throw new InvalidOperationException($"Home apps CUI command '{command}' is not owned by this binding.");
        await _dispatch(command, parameter, cancellationToken);
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        switch (item)
        {
            case HomeAppItem app:
                value = path switch
                {
                    _ when path.Equals("key", StringComparison.OrdinalIgnoreCase) => app.Key,
                    _ when path.Equals("name", StringComparison.OrdinalIgnoreCase) => app.Name,
                    _ when path.Equals("description", StringComparison.OrdinalIgnoreCase) => app.Description,
                    _ when path.Equals("openLabel", StringComparison.OrdinalIgnoreCase) => app.OpenLabel,
                    _ when path.Equals("iconSource", StringComparison.OrdinalIgnoreCase) =>
                        app.IsGroup
                            ? HomeAppBrandingIcons.SourceForGroup(app.Key, app.IconKey)
                            : HomeAppBrandingIcons.SourceFor(app.IconKey, app.CanonicalId, app.Source),
                    _ when path.Equals("isGroup", StringComparison.OrdinalIgnoreCase) => app.IsGroup,
                    _ when path.Equals("notGroup", StringComparison.OrdinalIgnoreCase) => !app.IsGroup,
                    _ => null
                };
                break;
            case HomeGroupMember member:
                value = path switch
                {
                    _ when path.Equals("key", StringComparison.OrdinalIgnoreCase) => member.Key,
                    _ when path.Equals("name", StringComparison.OrdinalIgnoreCase) => member.Name,
                    _ when path.Equals("description", StringComparison.OrdinalIgnoreCase) => member.Description,
                    _ when path.Equals("openLabel", StringComparison.OrdinalIgnoreCase) => member.OpenLabel,
                    _ when path.Equals("iconSource", StringComparison.OrdinalIgnoreCase) =>
                        HomeAppBrandingIcons.SourceForGroupMember(member),
                    _ when path.Equals("isInstalled", StringComparison.OrdinalIgnoreCase) => member.IsInstalled,
                    _ when path.Equals("notInstalled", StringComparison.OrdinalIgnoreCase) => !member.IsInstalled,
                    _ when path.Equals("availabilityNote", StringComparison.OrdinalIgnoreCase) => member.AvailabilityNote,
                    _ => null
                };
                break;
            default:
                return false;
        }
        return value is not null;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    private static string IconSource(string? key)
    {
        var symbolic = key?.Trim() ?? string.Empty;
        return symbolic.Length is > 0 and <= 64 && symbolic.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? "icon:" + symbolic : "icon:unknown";
    }

    private static readonly object Unresolved = new();

    /// <summary>One observed brand image target. Owns the subscription to the ACTUAL rendered target's
    /// "CuiAppearance" resource, subscribing on attach and disposing on detach. On EVERY attach
    /// (including the first) the current scope is applied through the existing typed binding
    /// invalidation, so an image resolved while detached under an earlier appearance is corrected at
    /// attach time. The synchronous initial resource emission is consumed as a baseline; the explicit
    /// attach-time refresh applies the current scope. The subscription is owned here so retirement and
    /// detach stop all callbacks. Each target is keyed by its Control, so reusing a control for
    /// another source reconciles (updates or removes) the observation instead of leaving stale ownership.</summary>
    private sealed class BrandTargetObservation
    {
        private readonly HomeAppsCuiBindings _owner;
        private readonly Control _target;
        private IDisposable? _subscription;
        private bool _attached;
        private long _generation;

        // Inert construction: only inert fields are assigned. No attach/subscribe/refresh/event
        // registration happens here, because the observation is not yet owned by the owner's weak-key
        // table; starting it here would re-enter the resolver before ownership exists and recursively
        // create another observation for the same target. Callers must publish (CWT + weak tracking
        // list) and then call <see cref="Start"/>.
        internal BrandTargetObservation(HomeAppsCuiBindings owner, Control target)
        {
            _owner = owner;
            _target = target;
        }

        /// <summary>Registers the attach/detach handlers and immediately observes when the target is
        /// already attached. Called ONLY after this observation has been published into the owner's
        /// conditional weak table and weak tracking list, so reentrant resolver calls during the initial
        /// attach-time refresh see this observation rather than constructing another.</summary>
        internal void Start()
        {
            if (_owner._targetsRetired) return;
            _target.AttachedToVisualTree += OnAttached;
            _target.DetachedFromVisualTree += OnDetached;
            if (_target.IsAttachedToVisualTree())
                Attach();
        }

        internal string Source { get; set; } = string.Empty;

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Attach();
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Detach();

        private void Attach()
        {
            if (_attached || _owner._targetsRetired) return;
            _attached = true;
            var generation = ++_owner._generation;
            _generation = generation;
            var subscription = _target.GetResourceObservable("CuiAppearance")
                .Subscribe(new InstanceObserver(this, generation));
            // Own the registration before the synchronous initial emission; if retirement/source/owner
            // generation changed during that emission, dispose the returned subscription instead of
            // retaining it.
            if (_owner._targetsRetired || _generation != generation || !_attached)
            {
                subscription.Dispose();
                _subscription = null;
                return;
            }
            _subscription = subscription;
            // Apply the ACTUAL current scope on EVERY attach, including the first. An image resolved
            // while detached under an earlier appearance, whose effective nearest scope changes before
            // its first attach, would otherwise never be re-resolved (the first current emission is the
            // baseline). Refreshing here re-runs the existing typed image binding invalidation on the
            // actual target; the registration above is already owned, so retirement/removal during it
            // cannot resurrect a subscription.
            _owner.Refresh();
        }

        private void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _subscription?.Dispose();
            _subscription = null;
            _generation++;
        }

        internal void Stop()
        {
            _target.AttachedToVisualTree -= OnAttached;
            _target.DetachedFromVisualTree -= OnDetached;
            _attached = false;
            _subscription?.Dispose();
            _subscription = null;
            _generation++;
        }

        private void OnEmitted(long generation)
        {
            if (_owner._targetsRetired || generation != _generation || !_attached) return;
            _owner.Refresh();
        }

        private sealed class InstanceObserver : IObserver<object?>
        {
            private readonly BrandTargetObservation _observation;
            private readonly long _generation;
            private bool _first = true;

            internal InstanceObserver(BrandTargetObservation observation, long generation)
            {
                _observation = observation;
                _generation = generation;
            }

            public void OnNext(object? value)
            {
                if (_first) { _first = false; return; }
                switch (value)
                {
                    case string text:
                        foreach (var name in Enum.GetNames<CuiAppearance>())
                            if (name.Equals(text, StringComparison.OrdinalIgnoreCase))
                            {
                                _observation.OnEmitted(_generation);
                                return;
                            }
                        break;
                    case CuiAppearance appearance when Enum.IsDefined(appearance):
                        _observation.OnEmitted(_generation);
                        break;
                }
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
