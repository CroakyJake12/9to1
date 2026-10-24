using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using HavenOS.Home;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>A native adapter for the SAME authored Home Events fragment and domain service.
/// It borrows source owners; it creates neither an activity store nor another app.</summary>
internal sealed class HomeEventsDestinationPage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    private readonly HomeEventsService _events;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly IHomeDeepLinkRouter? _links;
    private readonly HomeCuiSurface _surface = HomeCuiSurface.LoadDefault();
    private readonly CuiControlLoader _loader = new();
    private readonly CuiViewModel _bindings = new();
    private readonly DesktopOriginalWorkLifetime _work;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private long _generation;
    private bool _active;

    internal HomeEventsDestinationPage(HomeEventsService events, Func<CancellationToken, Task> admit,
        IHomeDeepLinkRouter? links = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _links = links;
        _work = new(StopAsync, CleanupAsync);
        // Select authored content from the canonical Home document. No copied document, donor
        // page, control template or second implementation owns this destination's presentation.
        var document = _surface.Document;
        var theme = document.Components.Single(component => component.IsThemeScope);
        var fragment = Descendants(theme).Single(component => component.AuthoredId == "events-page");
        var scope = new CuiComponent(theme.Type, theme.Name, theme.Classes, theme.Properties, theme.Actions,
            [fragment], theme.Condition, theme.List, theme.Span, theme.DefaultTheme, theme.Repeat,
            theme.ElseChildren, theme.Groups, theme.IsDefinition, theme.PropertyRegion);
        _loader.SetBindingContext(_bindings);
        _loader.SetActionDispatcher(this);
        var loaded = _loader.TryLoad(document with { Components = [scope] });
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The canonical Home Events fragment could not be rendered.");
        _loader.WireBindings(loaded.Root);
        Content = loaded.Root;
        RefreshBindings();
    }

    internal HomeEventsPage? Current => _surface.EventsPage;
    internal string? SelectedEventId => _surface.SelectedEventObject?.EventId;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal bool IsAlive => !_work.IsRetiring;

    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        _active = true;
        return _work.RunAsync(original => RefreshAsync(original, token), task => _initialization ??= task);
    }

    internal void Deactivate() { Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation; }

    public bool? IsActionAvailable(string command) => IsAlive && _active && (command switch
    {
        "RefreshEvents" or "SelectEventObject" => true,
        "OpenEventObject" => _links is not null && _surface.SelectedEventObject is not null,
        _ => false
    });

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Home Events action is unavailable.");
        return new(_work.RunAsync(async original =>
        {
            var generation = _generation;
            original.BindPublicationGuard(() => IsAlive && _active && generation == _generation);
            try
            {
                CheckTokens(original, cancellationToken);
                if (command == "RefreshEvents") { await RefreshAsync(original, cancellationToken); return; }
                if (command == "SelectEventObject")
                {
                    await original.AwaitAsync(_admit(original.Token));
                    CheckTokens(original, cancellationToken);
                    if (!original.IsAcceptedPublicationCurrent) return;
                    original.DemandPublication();
                    if (parameter is string eventId && _surface.SelectEventObject(eventId, 0))
                        _surface.SetNavigationStatus("Selected activity: " + _surface.EventItems.Single(item => item.EventId == eventId).Summary);
                    else
                    {
                        if (_surface.EventsPage is { } current) _surface.ApplyEventsPage(current);
                        _surface.SetNavigationStatus("This activity has no available linked object.");
                    }
                    RefreshBindings();
                    return;
                }
                var selected = _surface.SelectedEventObject ?? throw new InvalidOperationException("Select an activity item first.");
                await original.AwaitAsync(_admit(original.Token));
                var opened = await original.AwaitAsync(_events.OpenObjectAsync(selected.EventId, selected.ObjectIndex,
                    _links!, original.Token));
                await original.AwaitAsync(_admit(original.Token));
                original.DemandPublication();
                _surface.SetNavigationStatus(opened.Message);
                RefreshBindings();
            }
            catch
            {
                if (IsAlive && _active && generation == _generation) ClearUnavailable();
                throw;
            }
        }));
    }

    private async Task RefreshAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        CheckTokens(original, token);
        var generation = ++_generation;
        original.BindPublicationGuard(() => IsAlive && _active && generation == _generation);
        var selectedId = _surface.SelectedEventObject?.EventId;
        try
        {
            await original.AwaitAsync(_admit(original.Token));
            var page = await original.AwaitAsync(_events.ListAsync(new HomeEventsQuery(Limit: 200), original.Token));
            await original.AwaitAsync(_admit(original.Token));
            CheckTokens(original, token);
            if (!original.IsAcceptedPublicationCurrent) return;
            original.RunAcceptedPublicationCallback(() =>
            {
                _surface.ApplyEventsPage(page);
                if (selectedId is not null) _surface.SelectEventObject(selectedId, 0);
                RefreshBindings();
            });
        }
        catch
        {
            // A revoked/current-profile refusal must not leave previously authorised rows usable.
            if (IsAlive && generation == _generation) ClearUnavailable();
            throw;
        }
    }

    private void ClearUnavailable()
    {
        _surface.ApplyEventsPage(new([], 0, 200, false, Code: "Unavailable",
            Message: "Activity is unavailable. Reopen Home for the current profile."));
        RefreshBindings();
    }

    private void RefreshBindings()
    {
        _bindings.Set("EventsSummary", _surface.EventsSummary);
        _bindings.Set("EventItems", _surface.EventItems.Select(item => new Dictionary<string, object?>
        {
            ["EventId"] = item.EventId, ["Timestamp"] = item.Timestamp.ToString("g"),
            ["SourceApp"] = item.SourceApp, ["EventType"] = item.EventType,
            ["ActorDisplayName"] = item.ActorDisplayName ?? item.ActorIdentity ?? "Unknown actor",
            ["Severity"] = item.Severity, ["Category"] = item.Category, ["Summary"] = item.Summary,
            ["IsSelected"] = _surface.SelectedEventObject?.EventId == item.EventId
        }).ToArray());
        _bindings.Set("NavigationStatus", _surface.NavigationStatus);
        _bindings.Set("CanOpenEventObject", _active && _links is not null && _surface.SelectedEventObject is not null);
        _bindings.Set("CanSelectEventObject", _active && IsAlive);
        _bindings.Set("CanRefreshEvents", _active && IsAlive);
        _loader.RefreshBindings();
    }
    private static void CheckTokens(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        // Sources receive the exact retained page-owner token. Caller cancellation is observed
        // at boundaries; it never detaches a source or turns cooperative close into a foreign OCE.
        original.Token.ThrowIfCancellationRequested(); caller.ThrowIfCancellationRequested();
    }

    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _work.DemandExternalClose();
        if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
        token.ThrowIfCancellationRequested();
        return _preparation = _work.RunAsync(original => Task.FromResult(true));
    }
    internal Task CloseAndDrainAsync() => _work.CloseAndDrainAsync();
    private async Task StopAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation;
        var failures = new List<Exception>();
        try { _loader.Dispose(); } catch (Exception error) { failures.Add(error); }
        try { await _loader.WhenActionsIdleAsync(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Home Events did not close cleanly.", failures);
    }
    private Task CleanupAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        Content = null;
        return Task.CompletedTask;
    }
    private static IEnumerable<CuiComponent> Descendants(CuiComponent node) =>
        new[] { node }.Concat(node.Children.SelectMany(Descendants)).Concat(node.ElseChildren.SelectMany(Descendants));
}
