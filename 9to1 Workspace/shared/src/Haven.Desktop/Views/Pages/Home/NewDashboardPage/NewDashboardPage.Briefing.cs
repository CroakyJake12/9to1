using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// Typed destination for a surfaced briefing row. The Dashboard never accepts an arbitrary URI:
/// a row can only be opened when it is re-validated against the current source snapshot (present,
/// current, well-formed canonical identity and permitted kind/action). Id/Kind/ActionKey alone are
/// NOT authorization; they are only the typed identity used to revalidate against live source state.
/// </summary>
public sealed record DashboardBriefingTarget(string Kind, string Id, string ActionKey);

/// <summary>One TODAY agenda row projected from a real DashboardAgendaItem.</summary>
public sealed record DashboardAgendaRow(
    string Id,
    string Kind,
    string Title,
    string Provenance,
    string WhenLabel,
    string StatusLabel,
    bool IsOverdue,
    DashboardBriefingTarget Target,
    bool CanOpen)
{
    /// <summary>Accessible action label so activation never relies on the visible title alone.</summary>
    public string OpenLabel => "Open " + Title;
}

/// <summary>One RECENT WORK row projected from a real DashboardWorkItem.</summary>
public sealed record DashboardWorkRow(
    string Id,
    string Kind,
    string Title,
    string Provenance,
    string UpdatedLabel,
    string IconKey,
    DashboardBriefingTarget Target,
    bool CanOpen)
{
    /// <summary>Accessible action label so activation never relies on the visible title alone.</summary>
    public string OpenLabel => "Open " + Title;
}

/// <summary>Truthful briefing lifecycle/availability states. Loading, Ready, Stale, Error and
/// Revoked are distinct; nothing presents fake zeros, old data as current, or a hidden failure.</summary>
public enum DashboardBriefingPhase { Loading, Ready, Stale, Error, Revoked }

/// <summary>
/// The current briefing projection the authored Dashboard.cui reads: the real bounded agenda and
/// recent rows, plus honest loading/empty/stale/error/revoked state and the latest visible refusal.
/// State never invents a current result. Phase/count ordering is explicit so stale/revoked/error
/// explanations always win over populated counts.
/// </summary>
public sealed record DashboardBriefingViewState(
    DashboardBriefingPhase Phase,
    string Message,
    DateTimeOffset? CapturedAt,
    IReadOnlyList<DashboardAgendaRow> Agenda,
    IReadOnlyList<DashboardWorkRow> RecentWork,
    string Refusal = "")
{
    /// <summary>The state before the first bounded source read completes.</summary>
    public static DashboardBriefingViewState Loading { get; } =
        new(DashboardBriefingPhase.Loading, "Loading your brief…", null, [], []);

    public bool HasAgenda => Agenda.Count > 0;
    public bool NoAgenda => Agenda.Count == 0 && Phase is DashboardBriefingPhase.Ready or DashboardBriefingPhase.Stale;
    public bool HasRecent => RecentWork.Count > 0;
    public bool NoRecent => RecentWork.Count == 0 && Phase is DashboardBriefingPhase.Ready or DashboardBriefingPhase.Stale;
    public bool ShowRetry => Phase is DashboardBriefingPhase.Error or DashboardBriefingPhase.Stale or DashboardBriefingPhase.Revoked;
    public bool HasOpenable => Agenda.Any(row => row.CanOpen) || RecentWork.Any(row => row.CanOpen);
    public bool HasRefusal => Refusal.Length > 0;

    public string AgendaStatus => (Phase, Agenda.Count) switch
    {
        (DashboardBriefingPhase.Loading, _) => "Loading today's brief…",
        (DashboardBriefingPhase.Error, _) => Message,
        (DashboardBriefingPhase.Revoked, _) => Message,
        (DashboardBriefingPhase.Stale, _) => Message,
        (_, 1) => "1 item in today's agenda.",
        (_, > 1) => $"{Agenda.Count} items in today's agenda.",
        (_, 0) => "Nothing is scheduled for today."
    };

    public string RecentStatus => (Phase, RecentWork.Count) switch
    {
        (DashboardBriefingPhase.Loading, _) => "Loading recent work…",
        (DashboardBriefingPhase.Error, _) => Message,
        (DashboardBriefingPhase.Revoked, _) => Message,
        (DashboardBriefingPhase.Stale, _) => Message,
        (_, 1) => "1 recently changed item.",
        (_, > 1) => $"{RecentWork.Count} recently changed items.",
        (_, 0) => "No recent work to show yet."
    };

    public string AgendaEmpty => Phase switch
    {
        DashboardBriefingPhase.Stale => "Nothing is due or scheduled for today. Items are stale until refreshed.",
        DashboardBriefingPhase.Revoked => "Today's agenda is unavailable because its source is no longer authorized.",
        _ => "Nothing is due or scheduled for today."
    };

    public string RecentEmpty => Phase switch
    {
        DashboardBriefingPhase.Stale => "No recent work yet. Items are stale until refreshed.",
        DashboardBriefingPhase.Revoked => "Recent work is unavailable because its source is no longer authorized.",
        _ => "Recent chats, projects and calls will appear here."
    };

    /// <summary>
    /// Projects a bounded snapshot (or a truthful error/revoked state when the source is unavailable).
    /// A null snapshot yields Error/Revoked with NO rows (unauthorized/sensitive content is purged,
    /// never retained). A successfully read but time-stale snapshot is surfaced only as stale and
    /// non-openable, never as current.
    /// </summary>
    public static DashboardBriefingViewState FromSnapshot(DashboardSnapshot? snapshot, bool stale, string? error)
    {
        if (snapshot is null)
            return new(DashboardBriefingPhase.Error, error ?? "Dashboard data is unavailable.", null, [], []);
        var agenda = snapshot.Agenda.Select(item => MapAgenda(item, stale)).ToArray();
        var recent = snapshot.RecentWork.Select(item => MapWork(item, stale)).ToArray();
        var phase = stale ? DashboardBriefingPhase.Stale : DashboardBriefingPhase.Ready;
        var message = stale
            ? $"Data is from {snapshot.CapturedAt.ToLocalTime():t} and is stale; items can't be opened until refreshed."
            : $"Updated {snapshot.CapturedAt.ToLocalTime():t}.";
        if (error is not null) message += " " + error;
        return new(phase, message, snapshot.CapturedAt, agenda, recent);
    }

    /// <summary>Revoked state: no rows are retained because the source is denied/revoked/deleted.</summary>
    public static DashboardBriefingViewState Revoked(string? detail) =>
        new(DashboardBriefingPhase.Revoked, detail ?? "This area is no longer authorized. Content has been cleared.", null, [], []);

    private static DashboardAgendaRow MapAgenda(DashboardAgendaItem item, bool stale) => new(
        item.Id, item.Kind, item.Title, Provenance(item.Kind),
        AgendaWhenLabel(item), item.IsOverdue ? "Overdue" : string.Empty,
        item.IsOverdue,
        new DashboardBriefingTarget(item.Kind, item.Id, item.ActionKey),
        CanOpen(item.Kind, item.ActionKey, item.Id, stale));

    private static DashboardWorkRow MapWork(DashboardWorkItem item, bool stale) => new(
        item.Id, item.Kind, item.Title, WorkProvenance(item),
        WorkWhenLabel(item), item.IconKey,
        new DashboardBriefingTarget(item.Kind, item.Id, item.ActionKey),
        CanOpen(item.Kind, item.ActionKey, item.Id, stale));

    private static string Provenance(string kind) => (kind ?? string.Empty).ToLowerInvariant() switch
    {
        "task" => "Planner task",
        "event" => "Calendar event",
        _ => kind ?? string.Empty
    };

    private static string WorkProvenance(DashboardWorkItem item) =>
        (item.Kind ?? string.Empty).ToLowerInvariant() switch
        {
            "group" => "Chat group",
            "subject" => "Study subject",
            "project" => "Studio project",
            "call" => "Call",
            "chat" => "Chat conversation",
            _ => item.Detail
        };

    private static string AgendaWhenLabel(DashboardAgendaItem item) =>
        item.StartsAt is { } start ? start.ToLocalTime().ToString("HH:mm") : string.Empty;

    private static string WorkWhenLabel(DashboardWorkItem item)
    {
        var local = item.UpdatedAt.ToLocalTime();
        return local.Date == System.DateTime.Now.Date
            ? "Updated " + local.ToString("HH:mm")
            : "Updated " + local.ToString("d MMM");
    }

    /// <summary>
    /// A row is openable only when its snapshot is current (not stale/error) and its source identity
    /// is a well-formed canonical entity of an allowed kind/action. This is the typed guard used by
    /// the projection; the page re-validates against the live source before any navigation.
    /// </summary>
    private static bool CanOpen(string? kind, string? actionKey, string? id, bool stale)
    {
        if (stale || string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _)) return false;
        return (kind?.ToLowerInvariant(), actionKey?.ToLowerInvariant()) switch
        {
            ("task", "plan") or ("event", "plan") => true,
            ("chat", "chat") => true,
            ("group", "chat") => true,
            ("subject", "study") => true,
            ("project", "studio") => true,
            ("call", "call") => true,
            _ => false
        };
    }
}

/// <summary>
/// CUI binding owner for the authored dashboard briefing subtree (Dashboard.cui). The briefing only
/// reads projected values and raises typed commands; it never opens an arbitrary target directly.
/// </summary>
public sealed class NewDashboardPageBriefingBindings
    : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability,
      ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    /// <summary>Command that opens a validated briefing row target.</summary>
    public const string OpenCommand = "dashboard.open-briefing-row";
    /// <summary>Command that triggers a bounded source refresh.</summary>
    public const string RefreshCommand = "dashboard.refresh-briefing";

    private static readonly object Unresolved = new();
    private readonly Func<DashboardBriefingViewState> _state;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly CuiViewModel _values = new();
    private long _revision;

    public NewDashboardPageBriefingBindings(
        Func<DashboardBriefingViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _values.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardBriefingViewState Snapshot => _state();

    /// <summary>Raise PropertyChanged so the loader re-renders the authored briefing subtree.</summary>
    public void NotifyChanged()
    {
        _values.Set("Revision", _revision++);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool TryGetValue(string path, out object? value)
    {
        var snapshot = Snapshot;
        value = path switch
        {
            "briefing.agendaStatus" => snapshot.AgendaStatus,
            "briefing.recentStatus" => snapshot.RecentStatus,
            "briefing.agendaEmpty" => snapshot.AgendaEmpty,
            "briefing.recentEmpty" => snapshot.RecentEmpty,
            "briefing.hasAgenda" => snapshot.HasAgenda,
            "briefing.noAgenda" => snapshot.NoAgenda,
            "briefing.hasRecent" => snapshot.HasRecent,
            "briefing.noRecent" => snapshot.NoRecent,
            "briefing.showRetry" => snapshot.ShowRetry,
            "briefing.agenda" => snapshot.Agenda,
            "briefing.recentWork" => snapshot.RecentWork,
            "briefing.status" => snapshot.Message,
            "briefing.refusal" => snapshot.Refusal,
            "briefing.hasRefusal" => snapshot.HasRefusal,
            _ => Unresolved
        };
        return !ReferenceEquals(value, Unresolved);
    }

    public bool TrySetValue(string path, object? value) => false;

    public bool? IsActionAvailable(string command)
    {
        return command switch
        {
            RefreshCommand => Snapshot.ShowRetry,
            OpenCommand => Snapshot.HasOpenable,
            _ => null
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case RefreshCommand:
                await _dispatch(command, null, cancellationToken);
                break;
            case OpenCommand:
                if (parameter is DashboardBriefingTarget target)
                    await _dispatch(command, target, cancellationToken);
                else
                    throw new InvalidOperationException("The briefing open action requires a typed target.");
                break;
            default:
                throw new InvalidOperationException($"Dashboard briefing command '{command}' is not owned by this binding.");
        }
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        switch (item)
        {
            case DashboardAgendaRow row:
                value = path.ToLowerInvariant() switch
                {
                    "id" => row.Id,
                    "kind" => row.Kind,
                    "title" => row.Title,
                    "provenance" => row.Provenance,
                    "whenlabel" => row.WhenLabel,
                    "statuslabel" => row.StatusLabel,
                    "isoverdue" => row.IsOverdue,
                    "canopen" => row.CanOpen,
                    "openlabel" => row.OpenLabel,
                    "detail" => row.Title,
                    "target" => row.Target,
                    _ => null
                };
                break;
            case DashboardWorkRow row:
                value = path.ToLowerInvariant() switch
                {
                    "id" => row.Id,
                    "kind" => row.Kind,
                    "title" => row.Title,
                    "provenance" => row.Provenance,
                    "whenlabel" => row.UpdatedLabel,
                    "statuslabel" => string.Empty,
                    "isoverdue" => false,
                    "canopen" => row.CanOpen,
                    "openlabel" => row.OpenLabel,
                    "detail" => row.Title,
                    "target" => row.Target,
                    _ => null
                };
                break;
        }
        return value is not null;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;
}

public sealed partial class NewDashboardPage
{
    private NewDashboardPageBriefingBindings? _briefingBindings;
    private DashboardBriefingViewState _briefingState = DashboardBriefingViewState.Loading;
    private Func<DashboardBriefingTarget, bool>? _briefingNavigator;
    private string _lastBriefingRefusal = string.Empty;

    /// <summary>Host hook that routes a page-validated target to the owning app/entity. It must
    /// return true only when it actually navigated; otherwise the page reports a truthful refusal.</summary>
    public Func<DashboardBriefingTarget, bool>? BriefingNavigator
    {
        get => _briefingNavigator;
        set => _briefingNavigator = value;
    }

    /// <summary>Human-readable reason for the most recent refused briefing navigation.</summary>
    public string LastBriefingRefusal => _lastBriefingRefusal;

    /// <summary>
    /// Reserved, authority-checked briefing navigation. The operation reserves the Home original work
    /// before any external read or callback, re-reads the current authorized source snapshot, and only
    /// then forwards a target that is present, current and permitted. Missing/revoked/stale/unknown
    /// targets are refused without any navigation, and the refusal is surfaced in the authored CUI.
    /// </summary>
    public Task<bool> TryNavigateBriefingAsync(DashboardBriefingTarget target)
    {
        if (_disposed || _homeWork.IsRetiring || !_dashboardActive)
            return Task.FromResult(RefuseBriefing("The dashboard is closing or inactive; this item cannot be opened now."));
        return _homeWork.Executing is { } current
            ? current.AwaitAsync(NavigateBriefingReservedAsync(current, target))
            : _homeWork.RunAsync(original => NavigateBriefingReservedAsync(original, target));
    }

    private async Task<bool> NavigateBriefingReservedAsync(DesktopOriginalWorkLifetime.Original original, DashboardBriefingTarget target)
    {
        Task<bool>? source = null;
        try
        {
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
            {
                source = NavigateBriefingCoreAsync(target, original.Token);
            }).GetTask()).ConfigureAwait(false);
            return await original.AwaitAsync(source ?? throw new InvalidOperationException("The Home navigation was not acquired.")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return RefuseBriefing("This item could not be opened: " + ex.Message);
        }
    }

    private async Task<bool> NavigateBriefingCoreAsync(DashboardBriefingTarget target, CancellationToken token)
    {
        if (_disposed || _homeWork.IsRetiring || !_dashboardActive)
            return RefuseBriefing("The dashboard is closing or inactive; this item cannot be opened now.");
        if (_widgetDashboard is null)
            return RefuseBriefing("No dashboard source is available to verify this item.");

        // Revalidate against the LIVE authorized source; the cached projection is never the authority.
        DashboardSnapshot snapshot;
        try
        {
            snapshot = await AwaitHomeSourceAsync(_widgetDashboard.GetSnapshotAsync(DateTimeOffset.UtcNow, token));
        }
        catch
        {
            return RefuseBriefing("This item could not be verified against its current source.");
        }
        token.ThrowIfCancellationRequested();

        var stale = DateTimeOffset.UtcNow - snapshot.CapturedAt > WidgetStaleAfter;
        var fresh = DashboardBriefingViewState.FromSnapshot(snapshot, stale: false, error: null);
        if (stale || !IsPresentOpenableTarget(fresh, target))
            return RefuseBriefing("Cannot open: this item is missing, stale or no longer authorized.");

        if (_briefingNavigator is null)
            return RefuseBriefing("This item's owning app is not reachable from this dashboard right now.");

        bool navigated;
        try
        {
            navigated = await AwaitHomeSourceAsync(Task.FromResult(_briefingNavigator(target)));
        }
        catch
        {
            navigated = false;
        }
        if (!navigated)
            return RefuseBriefing("This item's owning app is not reachable from this dashboard right now.");

        ClearBriefingRefusal();
        return true;
    }

    private static bool IsPresentOpenableTarget(DashboardBriefingViewState fresh, DashboardBriefingTarget? target) =>
        target is not null
        && (fresh.Agenda.Any(row => row.CanOpen && row.Target == target)
            || fresh.RecentWork.Any(row => row.CanOpen && row.Target == target));

    private bool RefuseBriefing(string reason)
    {
        _lastBriefingRefusal = reason;
        _briefingState = _briefingState with { Refusal = reason };
        _briefingBindings?.NotifyChanged();
        return false;
    }

    private void ClearBriefingRefusal()
    {
        if (_briefingState.Refusal.Length == 0) return;
        _briefingState = _briefingState with { Refusal = "" };
        _briefingBindings?.NotifyChanged();
    }

    private void UpdateBriefing(DashboardSnapshot? snapshot, bool stale, string? error)
    {
        // A retired/deactivated page must not publish a late-resolved source as current content.
        // The act source update is only admitted while the dashboard is active and mounted.
        if (_disposed || !_dashboardActive) return;
        _briefingState = DashboardBriefingViewState.FromSnapshot(snapshot, stale, error);
        _briefingBindings?.NotifyChanged();
    }

    private void RevokeBriefing(string? detail)
    {
        _briefingState = DashboardBriefingViewState.Revoked(detail);
        _briefingBindings?.NotifyChanged();
    }

    private async ValueTask DispatchBriefingCommandAsync(string command, object? parameter, CancellationToken token)
    {
        if (command == NewDashboardPageBriefingBindings.RefreshCommand)
        {
            if (_dashboardActive && !_disposed && _widgetDashboard is not null)
                await RunHomeEventAsync(RefreshWidgetSurfaceAsync);
            return;
        }
        if (command == NewDashboardPageBriefingBindings.OpenCommand && parameter is DashboardBriefingTarget target)
            await TryNavigateBriefingAsync(target);
    }
}
