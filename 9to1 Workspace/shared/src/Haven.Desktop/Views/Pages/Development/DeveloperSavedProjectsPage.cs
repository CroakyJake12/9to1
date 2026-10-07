using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Spaces.Development;

namespace Haven.Desktop.Views.Pages.Development;

/// <summary>Native existing-reference picker. Borrowed Home readiness and Task/Dev services
/// remain global owners; this page owns only its original metadata/open tasks and CUI host.
/// It creates no task, project, actor, permission, provider or Ready/frame certificate.</summary>
public sealed class DeveloperSavedProjectsPage : UserControl, IActivatablePage, ICuiBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
{
    private readonly SpaceDevelopmentReferenceCatalog? _catalog;
    private readonly SpaceRegistry? _spaces;
    private readonly ICuiSceneReadiness? _originalHomeReadiness;
    private readonly Action? _demandOriginalHomeJoin;
    private readonly Func<SpaceDeveloperView, Task>? _openExistingTask;
    private readonly Func<Guid, Task>? _openExistingSpace;
    private readonly CancellationToken _connectionLifetime, _windowLifetime;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    [ThreadStatic] private static List<DeveloperSavedProjectsPage>? _sourceCallbacks;
    private CuiSceneHost? _host;
    private Task? _hostClose, _detach;
    private SpaceDevelopmentReferencePage? _page;
    private bool _active, _metadataAvailable;
    private long _generation, _readSequence;
    private string _status = "Refresh to observe saved projects.";

    public DeveloperSavedProjectsPage(SpaceDevelopmentReferenceCatalog originalCatalog, SpaceRegistry originalSpaces,
        ICuiSceneReadiness originalHomeReadiness, Action demandOriginalHomeJoin,
        Func<SpaceDeveloperView, Task> openExistingTask, Func<Guid, Task> openExistingSpace,
        CancellationToken originalConnectionLifetime, CancellationToken originalWindowLifetime,
        Action<DeveloperSavedProjectsPage> retainActualPartial)
    {
        ArgumentNullException.ThrowIfNull(originalCatalog); ArgumentNullException.ThrowIfNull(originalSpaces);
        ArgumentNullException.ThrowIfNull(originalHomeReadiness); ArgumentNullException.ThrowIfNull(demandOriginalHomeJoin);
        ArgumentNullException.ThrowIfNull(openExistingTask); ArgumentNullException.ThrowIfNull(openExistingSpace);
        ArgumentNullException.ThrowIfNull(retainActualPartial);
        if (!originalConnectionLifetime.CanBeCanceled || !originalWindowLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Home connection and native window lifetimes.");
        _catalog = originalCatalog; _spaces = originalSpaces; _originalHomeReadiness = originalHomeReadiness;
        _demandOriginalHomeJoin = demandOriginalHomeJoin; _openExistingTask = openExistingTask; _openExistingSpace = openExistingSpace;
        _connectionLifetime = originalConnectionLifetime; _windowLifetime = originalWindowLifetime;
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        retainActualPartial(this); // Original shell captures this SAME page before any host/document/publication.
        _work.DemandAdmission();
        _host = new CuiSceneHost();
        _work.DemandAdmission();
        Document = SpaceDevelopmentReferencesCuiDocument.Load();
        AutomationProperties.SetAutomationId(this, "DeveloperSavedProjects");
        AutomationProperties.SetName(this, "Saved development projects and existing tasks");
        Content = _host;
    }

    private DeveloperSavedProjectsPage(string setupRequired, Action<DeveloperSavedProjectsPage> retainActualPartial)
    {
        ArgumentNullException.ThrowIfNull(retainActualPartial);
        _status = setupRequired;
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        retainActualPartial(this);
        _work.DemandAdmission();
        AutomationProperties.SetAutomationId(this, "DeveloperSavedProjectsSetupRequired");
        AutomationProperties.SetName(this, "Dev setup required");
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Dev setup required", FontFamily = new FontFamily("Montserrat"), FontSize = 24 },
                new TextBlock { Text = setupRequired, FontFamily = new FontFamily("Montserrat"), TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    public static DeveloperSavedProjectsPage CreateSetupRequired(Action<DeveloperSavedProjectsPage> retainActualPartial) =>
        new("Connect Home and sign in to open saved development projects. Your existing tasks and project access will be checked when you open a project.", retainActualPartial);

    public CuiDocument? Document { get; }
    public new event PropertyChangedEventHandler? PropertyChanged;
    private bool IsCurrent(long generation)
    { lock (_gate) return _active && !_work.IsRetiring && _generation == generation && !_windowLifetime.IsCancellationRequested && !_connectionLifetime.IsCancellationRequested; }

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        long generation; lock (_gate) { _work.DemandAdmission(); _active = true; generation = ++_generation; }
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            if (_catalog is null) { original.DemandPublication(); return; } // Honest setup state, no producer or Ready fallback.
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken, _connectionLifetime, _windowLifetime);
            var refresh = RefreshOriginalAsync(original, scope.Token);
            try { await original.AwaitAsync(refresh).ConfigureAwait(false); }
            catch (Exception error) { original.Capture(refresh, error); }
            Task? show = null;
            var dispatcher = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                show = InvokeOriginalSource(() => _host!.ShowAsync(new("dev.saved-projects", "Dev", "Dev",
                    Document!, this, this, _originalHomeReadiness!) { IsPublicationCurrent = () => IsCurrent(generation) }, scope.Token));
            }).GetTask());
            try { await original.AwaitAsync(dispatcher).ConfigureAwait(false); }
            catch (Exception error) { original.Capture(dispatcher, error); }
            if (show is not null)
                try { await original.AwaitAsync(show).ConfigureAwait(false); }
                catch (Exception error) { original.Capture(show, error); }
            else original.Retain(new InvalidOperationException("No original saved-project host Show was acquired."));
            original.ThrowRetained();
        });
    }
    public void Deactivate() { lock (_gate) { _active = false; _generation++; _metadataAvailable = false; } }

    private async Task<CuiSceneAvailability> ObserveOriginalHomeAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        original.DemandPublication(); token.ThrowIfCancellationRequested();
        var actual = InvokeOriginalSource(() => _originalHomeReadiness!.CheckAsync(token).AsTask()); // SAME actual ValueTask once.
        var observed = await original.AwaitAsync(actual).ConfigureAwait(false);
        original.DemandPublication(); token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(observed.State) || string.IsNullOrWhiteSpace(observed.Code) || string.IsNullOrWhiteSpace(observed.Message))
            throw new InvalidDataException("The actual borrowed Home readiness returned invalid observation metadata.");
        return observed; // SAME actual observation; successful unrelated tasks never become Ready.
    }

    private async Task RefreshOriginalAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var read = ReadAndPublishOriginalAsync(original, token);
        try { await original.AwaitAsync(read).ConfigureAwait(false); }
        catch (Exception error)
        {
            original.Capture(read, error);
            // Preserve the failed original and show an honest unavailable state if this
            // page is still current. The host continues to consult actual Home readiness.
            Task? unavailable = null;
            try
            {
                unavailable = PublishAsync(original, () =>
                {
                    _page = null; _metadataAvailable = false;
                    _status = "Saved projects could not be read. Refresh to try again.";
                });
                await original.AwaitAsync(unavailable).ConfigureAwait(false);
            }
            catch (Exception publicationError) { original.Capture(unavailable, publicationError); }
            original.ThrowRetained();
        }
    }

    private async Task ReadAndPublishOriginalAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        long sequence; lock (_gate) sequence = ++_readSequence;
        await PublishAsync(original, () => { _page = null; _metadataAvailable = false; _status = "Reading saved projects…"; }).ConfigureAwait(false);
        var before = await ObserveOriginalHomeAsync(original, token).ConfigureAwait(false);
        if (before.State != CuiSceneAvailabilityState.Ready)
        { await PublishAsync(original, () => _status = before.Message).ConfigureAwait(false); return; }
        var stored = await original.AwaitAsync(InvokeOriginalSource(() => _spaces!.ReadExistingPageAsync(
            SpaceDevelopmentReferenceCatalog.MaximumSelectedSpaces, token))).ConfigureAwait(false);
        var page = await original.AwaitAsync(InvokeOriginalSource(() => _catalog!.ReadAsync(stored.Select(space => space.Id).ToArray(),
            SpaceDevelopmentReferenceCatalog.MaximumSavedReferences, token, OwnNestedOriginalSource))).ConfigureAwait(false);
        var after = await ObserveOriginalHomeAsync(original, token).ConfigureAwait(false);
        await PublishAsync(original, () =>
        {
            if (_readSequence != sequence) return;
            if (after.State != CuiSceneAvailabilityState.Ready) { _status = after.Message; return; }
            _page = page; _metadataAvailable = true;
            _status = "Choose the existing task, then open its saved project in Dev. Each open checks the current saved reference again.";
        }).ConfigureAwait(false);
    }

    public bool TryGetValue(string path, out object? value)
    {
        lock (_gate)
        {
            value = path switch
            {
                "Projects" => (_page?.Rows ?? []).Select(row => new ProjectItem(row, _metadataAvailable)).ToArray(),
                "Coverage" => _page?.Coverage ?? "Connect Home and refresh to view your saved projects.",
                "Status" => _status,
                "CanRefresh" => IsActionAvailable("dev.references.refresh"),
                "CanOpenSpace" => IsActionAvailable("dev.references.open-space"),
                _ => null
            };
        }
        return path is "Projects" or "Coverage" or "Status" or "CanRefresh" or "CanOpenSpace";
    }
    public bool? IsActionAvailable(string command)
    {
        lock (_gate)
        {
            if (!_active || _work.IsRetiring || _catalog is null || !IsCurrent(_generation)) return false;
            return command switch
            {
                "dev.references.refresh" => true,
                "dev.references.open-task" => _metadataAvailable && _page is { Rows.Count: > 0 },
                "dev.references.open-space" => _metadataAvailable && _page?.Rows.Select(row => row.SpaceId).Distinct().Count() == 1,
                _ => false
            };
        }
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This saved-project action is unavailable.");
        long generation; SpaceDevelopmentReferenceRow? captured = null;
        lock (_gate)
        {
            generation = _generation;
            if (command == "dev.references.open-task")
                captured = _page?.Rows.SingleOrDefault(row => row.SelectionKey == parameter as string)
                    ?? throw new InvalidOperationException("Select an actual observed saved project reference.");
            if (command == "dev.references.open-space") captured = _page!.Rows[0];
        }
        return new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken, _connectionLifetime, _windowLifetime);
            if (command == "dev.references.refresh")
            { await RefreshOriginalAsync(original, scope.Token).ConfigureAwait(false); return; }
            var home = await ObserveOriginalHomeAsync(original, scope.Token).ConfigureAwait(false);
            if (home.State != CuiSceneAvailabilityState.Ready)
            { await PublishAsync(original, () => { _page = null; _metadataAvailable = false; _status = home.Message; }).ConfigureAwait(false); return; }
            var current = await original.AwaitAsync(InvokeOriginalSource(() => _catalog!.OpenAsync(captured!, scope.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
            var after = await ObserveOriginalHomeAsync(original, scope.Token).ConfigureAwait(false);
            if (after.State != CuiSceneAvailabilityState.Ready)
            { await PublishAsync(original, () => { _page = null; _metadataAvailable = false; _status = after.Message; }).ConfigureAwait(false); return; }
            original.DemandPublication();
            var opened = InvokeOriginalSource(() => command == "dev.references.open-task"
                ? _openExistingTask!(current) : _openExistingSpace!(current.Space.Id));
            await original.AwaitAsync(opened).ConfigureAwait(false); // Borrowed shell joins its genuine existing Task frame acquisition.
        }));
    }

    private sealed record ProjectItem(SpaceDevelopmentReferenceRow Original, bool CanOpen)
    {
        public string SelectionKey => Original.SelectionKey;
        public string Title => Original.Title;
        public string Summary => Original.Summary;
        public string TaskLabel => Original.TaskLabel;
        public string CheckpointLabel => Original.CheckpointLabel;
    }
    private Task PublishAsync(DesktopOriginalWorkLifetime.Original original, Action publish)
    {
        var actual = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
        {
            original.DemandPublication();
            InvokeOriginalSource(() => { lock (_gate) publish(); return true; });
            foreach (var callback in PropertyChanged?.GetInvocationList() ?? [])
            {
                original.DemandPublication();
                try { InvokeOriginalSource(() => { ((PropertyChangedEventHandler)callback)(this, new(null)); return true; }); }
                catch (Exception error) { original.Retain(error); }
            }
            original.ThrowRetained();
        }).GetTask());
        return original.AwaitAsync(actual);
    }
    private void OwnNestedOriginalSource(Action callback) => InvokeOriginalSource(() => { callback(); return true; });
    private T InvokeOriginalSource<T>(Func<T> callback)
    {
        var owners = _sourceCallbacks ??= []; owners.Add(this);
        try { return callback(); }
        catch (OperationCanceledException fault) { throw new AggregateException("A synchronous saved-project source callback faulted.", fault); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_sourceCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual saved-project callback cannot join its encompassing page close.");
        _work.DemandExternalClose(); _catalog?.DemandExternalOriginalRetirementJoin(); _demandOriginalHomeJoin?.Invoke();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalPresentationAsync()
    {
        lock (_gate) { _active = false; _generation++; _metadataAvailable = false; }
        return _hostClose ??= _host?.CloseOriginalAsync() ?? Task.CompletedTask;
    }
    private async Task DetachOriginalPresentationAsync()
    {
        if (_hostClose is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual saved-project host has not acknowledged close.");
        _detach ??= InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            InvokeOriginalSource(() => { Content = null; return true; })).GetTask());
        try { await _detach.ConfigureAwait(false); }
        catch { if (_detach.IsFaulted) throw _detach.Exception!; throw; }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
