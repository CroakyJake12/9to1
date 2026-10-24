using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Host adapter for the SAME canonical Home Library and shared domain/index owner.
/// The host keeps page work/navigation custody; Notes and Home remain the canonical owners.</summary>
internal sealed class HomeLibraryDestinationPage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    private readonly HomeLibraryService _library;
    private readonly IHomeOriginalLibrarySource? _source;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly Func<HomeDeepLink, Task, bool> _queue;
    private readonly Func<HomeDeepLink, CancellationToken, Task>? _prepareNavigation;
    private readonly CuiControlLoader _loader = new();
    private readonly CuiViewModel _bindings = new();
    private readonly TextBox _search = new();
    private readonly DesktopOriginalWorkLifetime _work;
    private HomeLibraryPage? _page;
    private HomeLibraryQuery _query = new(Limit: 25);
    private DesktopOriginalWorkLifetime.Original? _opening;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private long _generation;
    private bool _active, _busy, _stopped;
    private string _status = "Browse authorised references from your apps.";

    internal HomeLibraryDestinationPage(Func<IHomeDeepLinkRouter, HomeLibraryService> create,
        Func<CancellationToken, Task> admit, Func<HomeDeepLink, Task, bool> queue,
        IHomeOriginalLibrarySource? ownedSource = null, CuiThemePreferenceContext? theme = null,
        Func<HomeDeepLink, CancellationToken, Task>? prepareNavigation = null)
    {
        Dispatcher.UIThread.VerifyAccess(); _admit = admit; _queue = queue; _source = ownedSource;
        _prepareNavigation = prepareNavigation;
        _work = new(StopAsync, CleanupAsync); _library = create(new Router(this));
        var document = HomeCuiSurface.LoadDefault().Document;
        var scope = document.Components.Single(component => component.IsThemeScope);
        var fragment = Descendants(scope).Single(component => component.AuthoredId == "library-page");
        var themeScope = new CuiComponent(scope.Type, scope.Name, scope.Classes, scope.Properties, scope.Actions,
            [fragment], scope.Condition, scope.List, scope.Span, scope.DefaultTheme, scope.Repeat,
            scope.ElseChildren, scope.Groups, scope.IsDefinition, scope.PropertyRegion);
        if (theme is not null) _loader.SetThemePreferenceContext(theme);
        _loader.SetActionDispatcher(this); _loader.SetBindingContext(_bindings);
        _loader.BindAuthoredControl("library-search", _ => _search);
        var loaded = _loader.TryLoad(document with { Components = [themeScope] });
        if (loaded.Root is null || loaded.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The canonical Home Library fragment could not be loaded.");
        _loader.WireBindings(loaded.Root); Content = loaded.Root; RefreshBindings();
    }
    internal bool IsAlive => !_work.IsRetiring;
    internal HomeLibraryPage? Current => _page;
    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    internal void InvokeOriginalSource(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; });
    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _active = true;
        return _work.RunAsync(original => SearchAsync(original, token, _query.Offset), actual => _initialization ??= actual);
    }
    internal void Deactivate() { Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation; }
    public bool? IsActionAvailable(string command) => IsAlive && _active && !_busy && (command switch
    {
        "SearchLibrary" or "RefreshLibrary" => true,
        "NextLibraryPage" => _page?.HasMore == true,
        "PreviousLibraryPage" => _query.Offset > 0,
        "OpenHomeLibraryArtifact" => _page?.Items.Count > 0,
        _ => false
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Library action is unavailable.");
        return new(_work.RunAsync(async original =>
        {
            var generation = _generation;
            original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (command != "OpenHomeLibraryArtifact")
                {
                    var offset = command == "NextLibraryPage" ? _query.Offset + _query.Limit :
                        command == "PreviousLibraryPage" ? Math.Max(0, _query.Offset - _query.Limit) : 0;
                    await SearchAsync(original, cancellationToken, offset); return;
                }
                var selected = _page?.Items.SingleOrDefault(item => ReferenceEquals(item.OpenLink, parameter));
                if (selected is null) throw new UnauthorizedAccessException("Choose an actual item from the current Library page.");
                _busy = true; RefreshBindings();
                await original.AwaitAsync(_admit(original.Token));
                if (!original.IsAcceptedPublicationCurrent) return;
                _opening = original;
                var opened = await original.AwaitAsync(_library.OpenAsync(selected.OwnerApp, selected.ArtifactId, original.Token));
                await original.AwaitAsync(_admit(original.Token));
                if (original.IsAcceptedPublicationCurrent)
                    original.RunAcceptedPublicationCallback(() =>
                    {
                        if (!opened.Succeeded) _page = null;
                        _status = opened.Succeeded ? selected.OwnerApp == "files"
                            ? selected.OpenLink.TargetRouteId == "files.preview" ? "Opening the original text/code preview in Files."
                                : "Opening the canonical folder in Files."
                            : "Opening the canonical notebook in Boards." : opened.Message;
                    });
            }
            catch (Exception cause) when (_source?.IsExpectedRefusal(cause) == true)
            { ClearUnavailable(cause.Message); }
            catch
            { if (original.IsAcceptedPublicationCurrent) ClearUnavailable("Library could not be refreshed. Its saved artifacts are preserved."); throw; }
            finally
            {
                _opening = null; _busy = false;
                if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(RefreshBindings);
            }
        }));
    }
    private async Task SearchAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller, int offset)
    {
        var generation = ++_generation;
        original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
        caller.ThrowIfCancellationRequested(); _busy = true; RefreshBindings();
        try
        {
            await original.AwaitAsync(_admit(original.Token));
            if (!original.IsAcceptedPublicationCurrent) return;
            var query = _query with { Offset = offset, Text = string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text };
            var page = await original.AwaitAsync(_library.SearchAsync(query, original.Token));
            await original.AwaitAsync(_admit(original.Token));
            caller.ThrowIfCancellationRequested();
            if (!original.IsAcceptedPublicationCurrent) return;
            original.RunAcceptedPublicationCallback(() =>
            { _query = query; _page = page.Succeeded ? page : null; _status = page.Message; });
        }
        catch (Exception cause) when (_source?.IsExpectedRefusal(cause) == true)
        { if (original.IsAcceptedPublicationCurrent) ClearUnavailable(cause.Message); }
        catch
        { if (original.IsAcceptedPublicationCurrent) ClearUnavailable("Library is unavailable. Refresh after resolving the original source."); throw; }
        finally
        { _busy = false; if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(RefreshBindings); }
    }
    private void ClearUnavailable(string status) { _page = null; _status = status; RefreshBindings(); }
    private void RefreshBindings()
    {
        var available = _active && IsAlive && !_busy;
        _bindings.Set("LibraryRows", NativeHomeArtifactLibrarySource.ProjectRows(_page));
        var notice = _page?.Succeeded == true ? _source?.AvailabilityNotice : null;
        _bindings.Set("LibrarySummary", string.IsNullOrWhiteSpace(notice) ? _status : _status + " " + notice);
        _bindings.Set("CanSearchLibrary", available);
        _bindings.Set("CanOpenHomeLibraryArtifact", available && _page?.Succeeded == true);
        _bindings.Set("CanNextLibraryPage", available && _page?.HasMore == true);
        _bindings.Set("CanPreviousLibraryPage", available && _query.Offset > 0);
        _bindings.Set("LibraryPageSummary", _page is { Items.Count: > 0 } page
            ? $"{_query.Offset + 1}–{_query.Offset + page.Items.Count}" : "No matching authorised artifacts.");
        _loader.RefreshBindings();
    }
    private sealed class Router(HomeLibraryDestinationPage owner) : IHomeDeepLinkRouter
    {
        public async Task<HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken cancellationToken = default)
        {
            var dispatch = Dispatcher.UIThread.InvokeAsync<Task<HomeFeatureNavigationResult>>(async () =>
            {
                var original = owner._opening ?? throw new InvalidOperationException("Retain the original Library open action.");
                cancellationToken.ThrowIfCancellationRequested(); original.DemandPublication();
                if (owner._prepareNavigation is { } prepare)
                    await original.AwaitAsync(prepare(link, cancellationToken)); // Actual async source resolution belongs to this open.
                await original.AwaitAsync(owner._admit(original.Token));
                cancellationToken.ThrowIfCancellationRequested(); original.DemandPublication();
                var accepted = false;
                original.RunAcceptedPublicationCallback(() => accepted = owner._queue(link,
                    Task.WhenAll(original.Task, owner._loader.WhenActionsIdleAsync())));
                var request = new HomeFeatureNavigationRequest(link.TargetRouteId!, link.OwnerApp, link.ObjectId, link.Action);
                return new HomeFeatureNavigationResult(accepted, accepted ? "Queued" : "NavigationRefused",
                    accepted ? "Opening in the owning app." : "The original browser no longer accepts this item.", request);
            }).GetTask();
            var actual = await dispatch.ConfigureAwait(false);
            return await actual.ConfigureAwait(false);
        }
    }
    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        _work.DemandExternalClose(); token.ThrowIfCancellationRequested();
        if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
        return _preparation = _work.RunAsync(_ => Task.FromResult(true));
    }
    internal Task CloseAndDrainAsync()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); return _work.CloseAndDrainAsync(); }
    private async Task StopAsync()
    {
        _active = false; ++_generation;
        var failures = new List<Exception>();
        async Task Join(Task task) { try { await task; } catch (Exception cause) { failures.Add(task.Exception ?? cause); } }
        await Join(_loader.WhenActionsIdleAsync());
        var disposed = false;
        try { _loader.Dispose(); disposed = true; } catch (Exception cause) { failures.Add(cause); }
        await Join(_loader.WhenActionsIdleAsync());
        _stopped = disposed;
        if (failures.Count > 0) throw new AggregateException("Original Library work failed while closing.", failures);
    }
    private async Task CleanupAsync()
    {
        if (!_stopped) throw new InvalidOperationException("Join the original Library loader before detachment.");
        // DesktopOriginalWorkLifetime invokes cleanup AFTER all admitted page originals,
        // including first activation, settle. Only then seal/join their private index adapter.
        // Sealing it in Stop would fault a delayed query's already-admitted authorization.
        try { if (_source is not null) await _source.DisposeAsync(); }
        finally { Content = null; }
    }
    private static IEnumerable<CuiComponent> Descendants(CuiComponent node) => new[] { node }
        .Concat(node.Children.SelectMany(Descendants)).Concat(node.ElseChildren.SelectMany(Descendants));
}
