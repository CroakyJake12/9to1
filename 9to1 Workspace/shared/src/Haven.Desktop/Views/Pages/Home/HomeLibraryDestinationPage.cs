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
using System.ComponentModel;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Host adapter for the SAME canonical Home Library and shared domain/index owner.
/// The host keeps page work/navigation custody; Notes and Home remain the canonical owners.</summary>
internal sealed class HomeLibraryDestinationPage : UserControl, ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private sealed record LibraryRow(string Key, string DisplayName, string OwnerApp, string ArtifactType,
        string LastModified, string? Location, string? Owner, string? SharedBy, bool IsPinned, HomeDeepLink Target);

    private readonly HomeLibraryService _library;
    private readonly IHomeOriginalLibrarySource? _source;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly Func<HomeDeepLink, Task, bool> _queue;
    private readonly Func<HomeDeepLink, CancellationToken, Task>? _prepareNavigation;
    private readonly Func<HomeArtifactReference, CancellationToken, Task<BoardEmbeddedArtifactReference?>>? _selectArtifact;
    private Func<HomeDeepLink, CancellationToken, Task<HomeDeepLink?>>? _selectBoardFolder;
    private readonly TaskCompletionSource<BoardEmbeddedArtifactReference?> _selection = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<HomeDeepLink?> _folderSelection = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CuiControlLoader _loader = new();
    private readonly CuiViewModel _bindings = new();
    private readonly TextBox _search = new() { MaxLength = 256 };
    private readonly TextBox _boardCreateName = new() { MaxLength = 120, Text = "New board" };
    private readonly TextBox _ownerFilter = new() { MaxLength = 256 };
    private readonly TextBox _typeFilter = new() { MaxLength = 256 };
    private readonly ComboBox _sort = new() { ItemsSource = new[] { "Recently modified", "Oldest first", "Name", "Type" }, SelectedIndex = 0 };
    private readonly ComboBox _pinnedFilter = new() { ItemsSource = new[] { "Any pin state", "Pinned", "Not pinned" }, SelectedIndex = 0 };
    private readonly ComboBox _sharedFilter = new() { ItemsSource = new[] { "Any sharing", "Shared", "Not shared" }, SelectedIndex = 0 };
    private readonly ComboBox _generatedFilter = new() { ItemsSource = new[] { "Any origin", "Generated", "Not generated" }, SelectedIndex = 0 };
    private readonly DesktopOriginalWorkLifetime _work;
    // Activation callers may stop waiting as soon as the page retires, while the actual
    // admitted query remains owned by _work and is still joined before the page detaches.
    private readonly CancellationTokenSource _activationObservers = new();
    private HomeLibraryPage? _page;
    private LibraryRow[] _rows = [];
    private HomeLibraryQuery _query = new(Limit: 25);
    private DesktopOriginalWorkLifetime.Original? _opening;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private long _generation;
    private bool _active, _busy, _stopped;
    private string _status = "Browse authorised references from your apps.";
    internal string? SelectedBoardCreateDisplayName { get; private set; }

    private event PropertyChangedEventHandler? _bindingPropertyChanged;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _bindingPropertyChanged += value;
        remove => _bindingPropertyChanged -= value;
    }

    internal HomeLibraryDestinationPage(Func<IHomeDeepLinkRouter, HomeLibraryService> create,
        Func<CancellationToken, Task> admit, Func<HomeDeepLink, Task, bool> queue,
        IHomeOriginalLibrarySource? ownedSource = null, CuiThemePreferenceContext? theme = null,
        Func<HomeDeepLink, CancellationToken, Task>? prepareNavigation = null,
        Func<HomeArtifactReference, CancellationToken, Task<BoardEmbeddedArtifactReference?>>? selectArtifact = null)
    {
        Dispatcher.UIThread.VerifyAccess(); _admit = admit; _queue = queue; _source = ownedSource;
        _prepareNavigation = prepareNavigation; _selectArtifact = selectArtifact;
        _bindings.PropertyChanged += (_, args) => _bindingPropertyChanged?.Invoke(this, args);
        if (_selectArtifact is not null) _status = "Choose a Write document to embed. The selected reference does not grant content access.";
        _work = new(StopAsync, CleanupAsync); _library = create(new Router(this));
        var document = HomeCuiSurface.LoadDefault().Document;
        var scope = document.Components.Single(component => component.IsThemeScope);
        var fragment = Descendants(scope).Single(component => component.AuthoredId == "library-page");
        var themeScope = new CuiComponent(scope.Type, scope.Name, scope.Classes, scope.Properties, scope.Actions,
            [fragment], scope.Condition, scope.List, scope.Span, scope.DefaultTheme, scope.Repeat,
            scope.ElseChildren, scope.Groups, scope.IsDefinition, scope.PropertyRegion);
        if (theme is not null) _loader.SetThemePreferenceContext(theme);
        _loader.SetActionDispatcher(this); _loader.SetBindingContext(this);
        _loader.BindAuthoredControl("library-search", _ => _search);
        _loader.BindAuthoredControl("library-board-create-name", _ => _boardCreateName);
        _loader.BindAuthoredControl("library-owner-filter", _ => _ownerFilter);
        _loader.BindAuthoredControl("library-type-filter", _ => _typeFilter);
        _loader.BindAuthoredControl("library-sort", _ => _sort);
        _loader.BindAuthoredControl("library-pinned-filter", _ => _pinnedFilter);
        _loader.BindAuthoredControl("library-shared-filter", _ => _sharedFilter);
        _loader.BindAuthoredControl("library-generated-filter", _ => _generatedFilter);
        _search.TextChanged += (_, _) => RefreshBindings();
        _ownerFilter.TextChanged += (_, _) => RefreshBindings(); _typeFilter.TextChanged += (_, _) => RefreshBindings();
        _sort.SelectionChanged += (_, _) => RefreshBindings(); _pinnedFilter.SelectionChanged += (_, _) => RefreshBindings();
        _sharedFilter.SelectionChanged += (_, _) => RefreshBindings(); _generatedFilter.SelectionChanged += (_, _) => RefreshBindings();
        var loaded = _loader.TryLoad(document with { Components = [themeScope] });
        if (loaded.Root is null || loaded.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The canonical Home Library fragment could not be loaded.");
        _loader.WireBindings(loaded.Root); Content = loaded.Root; RefreshBindings();
    }
    internal bool IsAlive => !_work.IsRetiring;
    internal HomeLibraryPage? Current => _page;
    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal Task<BoardEmbeddedArtifactReference?> Selection => _selection.Task;
    internal Task<HomeDeepLink?> FolderSelection => _folderSelection.Task;
    internal bool IsSelectionMode => _selectArtifact is not null || _selectBoardFolder is not null;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    internal void InvokeOriginalSource(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; });
    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _active = true;
        var actual = _work.RunAsync(original => SearchAsync(original, token, _query.Offset), actualTask => _initialization ??= actualTask);
        return ObserveActivationAsync(actual, _activationObservers.Token);
    }
    internal void SetInitialQuery(string ownerApp, string artifactType)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerApp);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactType);
        if (_active || _initialization is not null || _page is not null)
            throw new InvalidOperationException("Set the initial Library filters before its first activation.");
        _ownerFilter.Text = ownerApp;
        _typeFilter.Text = artifactType;
    }
    internal void SetFolderSelectionMode(Func<HomeDeepLink, CancellationToken, Task<HomeDeepLink?>> selectCurrentFolder)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(selectCurrentFolder);
        if (_active || _initialization is not null || _page is not null || _selectArtifact is not null || _selectBoardFolder is not null)
            throw new InvalidOperationException("Choose folder selection mode before the Library page is activated.");
        _selectBoardFolder = selectCurrentFolder;
        _bindings.Set("IsBoardFolderSelection", true);
        _status = "Choose the configured Boards folder. The folder row is checked again by its original Files owner.";
        SetInitialQuery("files", "Files folder");
        RefreshBindings();
    }
    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) => _bindings.TrySetValue(path, value);
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        // The CUI Repeat may retain an item scope while bindings are refreshed (for example
        // when the page enters/leaves its busy state). Accept a re-materialized row only when
        // it still carries the same current Home link object and stable identity; the action
        // dispatcher independently resolves that exact link against the current page.
        if (item is not LibraryRow row || !_rows.Any(current =>
            StringComparer.Ordinal.Equals(current.Key, row.Key) && ReferenceEquals(current.Target, row.Target))) return false;
        value = path.ToLowerInvariant() switch
        {
            "key" => row.Key,
            "displayname" => row.DisplayName,
            "ownerapp" => row.OwnerApp,
            "artifacttype" => row.ArtifactType,
            "lastmodified" => row.LastModified,
            "location" => row.Location,
            "owner" => row.Owner,
            "sharedby" => row.SharedBy,
            "ispinned" => row.IsPinned,
            "target" => row.Target,
            _ => null
        };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    private static async Task ObserveActivationAsync(Task actual, CancellationToken retirement) =>
        await actual.WaitAsync(retirement).ConfigureAwait(false);
    internal void Deactivate() { Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation; }
    public bool? IsActionAvailable(string command) => IsAlive && _active && !_busy && (command switch
    {
        "SearchLibrary" or "RefreshLibrary" => true,
        "NextLibraryPage" => _page?.HasMore == true && QueryInputsMatchCurrentPage(),
        "PreviousLibraryPage" => _query.Offset > 0 && QueryInputsMatchCurrentPage(),
        "OpenHomeLibraryArtifact" => _page?.Items.Count > 0,
        "ToggleHomeLibraryPin" => _selectArtifact is null && _selectBoardFolder is null && _page?.Items.Count > 0,
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
                if (command is "NextLibraryPage" or "PreviousLibraryPage" or "SearchLibrary" or "RefreshLibrary")
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
                if (_selectArtifact is { } select)
                {
                    var reference = await original.AwaitAsync(select(selected, original.Token));
                    await original.AwaitAsync(_admit(original.Token));
                    if (!original.IsAcceptedPublicationCurrent) return;
                    if (reference is not null)
                    {
                        reference.Validate();
                        if (reference.ApplicationId != selected.OwnerApp || reference.ArtifactId.ToString("D") != selected.ArtifactId)
                            throw new UnauthorizedAccessException("The selected reference does not match this Home Library row.");
                        original.RunAcceptedPublicationCallback(() => _selection.TrySetResult(reference));
                    }
                    return;
                }
                if (_selectBoardFolder is { } selectFolder)
                {
                    if (selected.OwnerApp != "files" || selected.ArtifactType != "Files folder" ||
                        selected.OpenLink.TargetRouteId != "files.folder")
                        throw new UnauthorizedAccessException("Choose a folder from the current Files Library results.");
                    var selectedFolder = await original.AwaitAsync(selectFolder(selected.OpenLink, original.Token));
                    await original.AwaitAsync(_admit(original.Token));
                    if (!original.IsAcceptedPublicationCurrent) return;
                    if (selectedFolder is not null)
                    {
                        if (!ReferenceEquals(selectedFolder, selected.OpenLink) || selectedFolder.OwnerApp != "files" ||
                            selectedFolder.TargetRouteId != "files.folder")
                            throw new UnauthorizedAccessException("The original Files folder selection no longer matches this Library row.");
                        var displayName = _boardCreateName.Text?.Trim();
                        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120 ||
                            displayName is "." or ".." || displayName.IndexOfAny(['/', '\\', '\0']) >= 0 ||
                            displayName.Any(char.IsControl))
                        {
                            original.RunAcceptedPublicationCallback(() => _status = "Enter a board file name of 1–120 characters without path separators.");
                            return;
                        }
                        original.RunAcceptedPublicationCallback(() =>
                        {
                            if (_folderSelection.TrySetResult(selectedFolder))
                                SelectedBoardCreateDisplayName = displayName;
                        });
                    }
                    else if (original.IsAcceptedPublicationCurrent)
                        original.RunAcceptedPublicationCallback(() => _status = "This row is not the currently configured Boards folder. Choose the registered Boards folder.");
                    return;
                }
                if (command == "ToggleHomeLibraryPin")
                {
                    var result = await original.AwaitAsync(_library.SetPinnedAsync(selected.OwnerApp, selected.ArtifactId,
                        !selected.IsPinned, original.Token));
                    await original.AwaitAsync(_admit(original.Token));
                    if (!original.IsAcceptedPublicationCurrent) return;
                    if (result.Succeeded)
                    {
                        await SearchAsync(original, cancellationToken, _query.Offset);
                        if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(() => _status = result.Message);
                    }
                    else if (original.IsAcceptedPublicationCurrent)
                        original.RunAcceptedPublicationCallback(() => _status = result.Message);
                    return;
                }
                _opening = original;
                var opened = await original.AwaitAsync(_library.OpenAsync(selected.OwnerApp, selected.ArtifactId, original.Token));
                await original.AwaitAsync(_admit(original.Token));
                if (original.IsAcceptedPublicationCurrent)
                    original.RunAcceptedPublicationCallback(() =>
                    {
                        if (!opened.Succeeded) _page = null;
                        _status = opened.Succeeded ? selected.OwnerApp == "files"
                            ? selected.OpenLink.TargetRouteId switch
                            {
                                "files.board" => "Opening this .9to1board as a read-only rich Boards document from the selected Files revision.",
                                "files.preview" => "Opening the original text/code preview in Files.",
                                _ => "Opening the canonical folder in Files."
                            }
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
            var query = CurrentQuery(offset);
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
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool? ReadFilter(int selectedIndex) => selectedIndex switch { 1 => true, 2 => false, _ => null };
    private HomeLibraryQuery CurrentQuery(int offset) => _query with
    {
        Offset = offset,
        Text = Normalize(_search.Text),
        OwnerApp = Normalize(_ownerFilter.Text),
        ArtifactType = Normalize(_typeFilter.Text),
        Pinned = ReadFilter(_pinnedFilter.SelectedIndex),
        Shared = ReadFilter(_sharedFilter.SelectedIndex),
        Generated = ReadFilter(_generatedFilter.SelectedIndex),
        Sort = _sort.SelectedIndex switch { 1 => "oldest", 2 => "name", 3 => "type", _ => "recent" }
    };
    private bool QueryInputsMatchCurrentPage() => _page is not null && CurrentQuery(_query.Offset) == _query;
    private void ClearUnavailable(string status) { _page = null; _status = status; RefreshBindings(); }
    private void RefreshBindings()
    {
        var available = _active && IsAlive && !_busy;
        var queryMatches = QueryInputsMatchCurrentPage();
        _rows = _page?.Items.Select(item => new LibraryRow(item.OwnerApp + "/" + item.ArtifactId,
            item.DisplayName, item.OwnerApp, item.ArtifactType, item.LastModified.ToLocalTime().ToString("g"),
            item.Location, item.Owner, item.SharedBy, item.IsPinned, item.OpenLink)).ToArray() ?? [];
        _bindings.Set("LibraryRows", _rows);
        _bindings.Set("IsBoardFolderSelection", _selectBoardFolder is not null);
        var notice = _page?.Succeeded == true ? _source?.AvailabilityNotice : null;
        var summary = string.IsNullOrWhiteSpace(notice) ? _status : _status + " " + notice;
        if (_page is not null && !queryMatches) summary += " Search to apply the changed filters.";
        _bindings.Set("LibrarySummary", summary);
        _bindings.Set("CanSearchLibrary", available);
        _bindings.Set("CanOpenHomeLibraryArtifact", available && _page?.Succeeded == true);
        _bindings.Set("CanToggleHomeLibraryPin", _selectArtifact is null && _selectBoardFolder is null && available && _page?.Succeeded == true);
        _bindings.Set("CanNextLibraryPage", available && queryMatches && _page?.HasMore == true);
        _bindings.Set("CanPreviousLibraryPage", available && queryMatches && _query.Offset > 0);
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
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        _selection.TrySetResult(null);
        _folderSelection.TrySetResult(null);
        return _work.CloseAndDrainAsync();
    }
    private async Task StopAsync()
    {
        _activationObservers.Cancel();
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
        finally { _activationObservers.Dispose(); Content = null; }
    }
    private static IEnumerable<CuiComponent> Descendants(CuiComponent node) => new[] { node }
        .Concat(node.Children.SelectMany(Descendants)).Concat(node.ElseChildren.SelectMany(Descendants));
}
