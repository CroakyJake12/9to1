using System;
using System.Collections.Generic;
using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// Typed destination for a surfaced briefing row. The Dashboard never accepts an arbitrary URI:
/// a row can only be opened when it derives from a real bounded snapshot row whose Id/Kind/ActionKey
/// pass validation and whose source is current and permitted.
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

/// <summary>Truthful briefing lifecycle/availability states. Loading and Error/Stale are distinct;
/// nothing presents fake zeros or old data as current.</summary>
public enum DashboardBriefingPhase { Loading, Ready, Stale, Error }

/// <summary>
/// The current briefing projection the authored Dashboard.cui reads: the real bounded agenda and
/// recent rows, plus honest loading/empty/stale/error state. State never invents a current result.
/// </summary>
public sealed record DashboardBriefingViewState(
    DashboardBriefingPhase Phase,
    string Message,
    DateTimeOffset? CapturedAt,
    IReadOnlyList<DashboardAgendaRow> Agenda,
    IReadOnlyList<DashboardWorkRow> RecentWork)
{
    /// <summary>The state before the first bounded source read completes.</summary>
    public static DashboardBriefingViewState Loading { get; } =
        new(DashboardBriefingPhase.Loading, "Loading your brief…", null, [], []);

    public bool HasAgenda => Agenda.Count > 0;
    public bool NoAgenda => Agenda.Count == 0 && Phase is DashboardBriefingPhase.Ready or DashboardBriefingPhase.Stale;
    public bool HasRecent => RecentWork.Count > 0;
    public bool NoRecent => RecentWork.Count == 0 && Phase is DashboardBriefingPhase.Ready or DashboardBriefingPhase.Stale;
    public bool ShowRetry => Phase is DashboardBriefingPhase.Error or DashboardBriefingPhase.Stale;
    public bool HasOpenable => Agenda.Any(row => row.CanOpen) || RecentWork.Any(row => row.CanOpen);

    public string AgendaStatus => (Phase, Agenda.Count) switch
    {
        (DashboardBriefingPhase.Loading, _) => "Loading today's brief…",
        (DashboardBriefingPhase.Error, _) => Message,
        (_, 1) => "1 item in today's agenda.",
        (_, > 1) => $"{Agenda.Count} items in today's agenda.",
        (DashboardBriefingPhase.Stale, 0) => $"{Message} Nothing is scheduled for today.",
        (_, 0) => "Nothing is scheduled for today."
    };

    public string RecentStatus => (Phase, RecentWork.Count) switch
    {
        (DashboardBriefingPhase.Loading, _) => "Loading recent work…",
        (DashboardBriefingPhase.Error, _) => Message,
        (_, 1) => "1 recently changed item.",
        (_, > 1) => $"{RecentWork.Count} recently changed items.",
        (DashboardBriefingPhase.Stale, 0) => $"{Message} No recent work yet.",
        (_, 0) => "No recent work to show yet."
    };

    public string AgendaEmpty => Phase == DashboardBriefingPhase.Stale
        ? "Nothing is due or scheduled for today. Items are stale until refreshed."
        : "Nothing is due or scheduled for today.";

    public string RecentEmpty => Phase == DashboardBriefingPhase.Stale
        ? "No recent work yet. Items are stale until refreshed."
        : "Recent chats, projects and calls will appear here.";

    /// <summary>
    /// Projects a bounded snapshot (or a truthful error/stale state when the source is unavailable).
    /// A null snapshot with an error yields the Error state with no rows; a retained snapshot is
    /// always surfaced as stale and non-openable, never as current.
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
    /// is a well-formed canonical entity of an allowed kind/action. This is the typed guard that
    /// prevents arbitrary URI acceptance and keeps revoked/stale rows from being actionable.
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
    private DashboardSnapshot? _briefingLastSnapshot;
    private Func<DashboardBriefingTarget, bool>? _briefingNavigator;
    private string _lastBriefingRefusal = string.Empty;

    /// <summary>Host hook that routes a validated briefing target to the owning app/entity. It must
    /// return true only when it actually navigated; otherwise the page reports a truthful refusal.</summary>
    public Func<DashboardBriefingTarget, bool>? BriefingNavigator
    {
        get => _briefingNavigator;
        set => _briefingNavigator = value;
    }

    /// <summary>Human-readable reason for the most recent refused briefing navigation.</summary>
    public string LastBriefingRefusal => _lastBriefingRefusal;

    /// <summary>
    /// Typed, authority-checked briefing navigation. Only a target that is present in the current
    /// snapshot as an openable row is forwarded to the host navigator. Unknown, stale, revoked or
    /// unauthorized targets are refused without any forbidden read/open.
    /// </summary>
    public bool TryNavigateBriefing(DashboardBriefingTarget target)
    {
        if (target is null || !IsPresentOpenableTarget(target))
            return RefuseBriefing("Cannot open: this item is missing, stale or no longer authorized.");
        if (_briefingNavigator is not null && _briefingNavigator(target))
        {
            _lastBriefingRefusal = string.Empty;
            return true;
        }
        return RefuseBriefing("This item's owning app is not reachable from this dashboard right now.");
    }

    private bool IsPresentOpenableTarget(DashboardBriefingTarget target) =>
        _briefingState.Agenda.Any(row => row.CanOpen && row.Target == target)
        || _briefingState.RecentWork.Any(row => row.CanOpen && row.Target == target);

    private bool RefuseBriefing(string reason)
    {
        _lastBriefingRefusal = reason;
        return false;
    }

    private void UpdateBriefing(DashboardSnapshot? snapshot, bool stale, string? error)
    {
        if (snapshot is not null) _briefingLastSnapshot = snapshot;
        _briefingState = DashboardBriefingViewState.FromSnapshot(snapshot, stale, error);
        _briefingBindings?.NotifyChanged();
    }

    private ValueTask DispatchBriefingCommandAsync(string command, object? parameter, CancellationToken token)
    {
        if (command == NewDashboardPageBriefingBindings.RefreshCommand)
        {
            if (_dashboardActive && !_disposed && _widgetDashboard is not null)
                return new ValueTask(RunHomeEventAsync(RefreshWidgetSurfaceAsync));
            return ValueTask.CompletedTask;
        }
        if (command == NewDashboardPageBriefingBindings.OpenCommand && parameter is DashboardBriefingTarget target)
            TryNavigateBriefing(target);
        return ValueTask.CompletedTask;
    }
}
