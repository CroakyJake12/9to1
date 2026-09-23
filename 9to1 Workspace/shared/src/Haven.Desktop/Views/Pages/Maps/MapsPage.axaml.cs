using System.Globalization;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.Desktop.Services.Maps;

namespace Haven.Desktop.Views.Pages.Maps;

/// <summary>
/// Production adapter for the Haven.UI Maps scene. Owns provider calls, tile loading and
/// clipboard access; the visible surface remains the Haven-owned scene.
/// </summary>
public sealed partial class MapsPage : UserControl, IDisposable
{
    private const int SearchLimit = 12;

    private readonly IMapService _maps;
    private readonly ITileSource _tiles;
    private readonly IMapsSavedPlaceStore _savedPlaces;
    private readonly StructuredFormTemplateRuntime _structuredFormTemplate;
    private readonly GenerativeUiEventRouter _genUiRouter;
    private readonly GenUiInstanceStore _genUiInstances;
    private readonly MapsHavenScene _scene;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Guid _mapSessionId = Guid.NewGuid();
    private readonly HashSet<MapTileId> _requestedTiles = [];
    private readonly List<MapPlace> _results = [];
    private readonly List<SavedMapPlace> _saved = [];
    private readonly List<string> _recentSearches = [];
    private CancellationTokenSource? _tileLoading;
    private MapPlace? _startPlace;
    private MapPlace? _endPlace;
    private MapPlace? _selectedPlace;
    private MapPlace? _placeBeingSaved;
    private HavenGenUiSceneSurface? _saveFormSurface;
    private int _savingPlace;
    private bool _searching;
    private bool _disposed;

    public MapsPage(
        IMapService maps,
        ITileSource tiles,
        IMapsSavedPlaceStore savedPlaces,
        StructuredFormTemplateRuntime structuredFormTemplate,
        GenerativeUiEventRouter genUiRouter,
        GenUiInstanceStore genUiInstances)
    {
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _savedPlaces = savedPlaces ?? throw new ArgumentNullException(nameof(savedPlaces));
        _structuredFormTemplate = structuredFormTemplate ?? throw new ArgumentNullException(nameof(structuredFormTemplate));
        _genUiRouter = genUiRouter ?? throw new ArgumentNullException(nameof(genUiRouter));
        _genUiInstances = genUiInstances ?? throw new ArgumentNullException(nameof(genUiInstances));

        _scene = new MapsHavenScene();
        Scene = new HavenSceneControl(new MapTileImageResolver(tiles)) { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "HavenMapsPage");
        AutomationProperties.SetName(this, "Haven Maps");
        AutomationProperties.SetAutomationId(Scene, "HavenMapsScene");
        AutomationProperties.SetName(Scene, "Map search, directions and saved places");
        Content = Scene;
        InitializeComponent();

        WireEvents();
        SizeChanged += OnSizeChanged;
        _ = InitialiseAsync();
    }

    internal HavenSceneControl Scene { get; }
    internal MapsHavenScene HavenScene => _scene;
    public event Action<Guid>? DataWorkbookRequested;

    private void WireEvents()
    {
        _scene.SearchButton.Invoked += (_, _) => _ = RunSearchAsync();
        Scene.InputSubmitted += input =>
        {
            if (_saveFormSurface?.OwnsInput(input) == true)
                _ = _saveFormSurface.SubmitInputAsync(input, _lifetime.Token);
            else if (ReferenceEquals(input, _scene.SearchInput))
                _ = RunSearchAsync();
        };
        _scene.ResultsSelect.SelectionChanged += (_, _) => HandleResultSelected();
        _scene.RouteButton.Invoked += (_, _) => _ = RunRouteAsync();
        _scene.SavePlaceButton.Invoked += (_, _) => _ = SaveSelectedPlaceAsync();
        _scene.SaveFormCancelled += OnSaveFormCancelled;
        _scene.DataWorkbookRequested += OnOpenDataRequested;
        _scene.CopyCoordinatesButton.Invoked += (_, _) => _ = CopySelectedCoordinatesAsync();
        _scene.SavedPlacesSelect.SelectionChanged += (_, _) => HandleSavedPlaceSelected();
        _scene.RecentSearchesSelect.SelectionChanged += (_, _) => HandleRecentSearchSelected();
        _scene.ViewportChanged += (_, _) => _ = LoadVisibleTilesAsync();
    }

    private async Task InitialiseAsync()
    {
        if (_disposed) return;
        try
        {
            _saved.Clear();
            _saved.AddRange(await _savedPlaces.GetSavedPlacesAsync(_lifetime.Token));
            _recentSearches.Clear();
            _recentSearches.AddRange(await _savedPlaces.GetRecentSearchesAsync(_lifetime.Token));
            RefreshSavedPlacesList();
            RefreshRecentSearchesList();
            _scene.SetStatus($"Map data © OpenStreetMap contributors · tiles from {_tiles.Id}.");
            ScheduleTileRefresh();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _scene.SetStatus($"Saved places could not load: {failure.Message}");
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ScheduleTileRefresh();

    private void ScheduleTileRefresh()
    {
        if (_disposed) return;
        // Let Avalonia arrange the page before reading the Haven scene bounds.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) _scene.RefreshFromBounds();
        }, DispatcherPriority.Loaded);
    }

    private async Task LoadVisibleTilesAsync()
    {
        if (_disposed) return;
        var viewport = _scene.CurrentViewport();
        if (viewport is null) return;
        var loading = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var previous = Interlocked.Exchange(ref _tileLoading, loading);
        previous?.Cancel();
        var token = loading.Token;

        try
        {
            var pending = viewport.VisibleTiles().Where(tileId => !_requestedTiles.Contains(tileId)).ToArray();
            if (pending.Length == 0) return;
            _scene.SetStatus($"Loading {pending.Length} map tile{(pending.Length == 1 ? string.Empty : "s")}…");
            foreach (var tileId in pending)
            {
                token.ThrowIfCancellationRequested();
                var bytes = await _tiles.GetTileAsync(tileId.Zoom, tileId.X, tileId.Y, token);
                if (bytes is null) continue;
                _requestedTiles.Add(tileId);
                _scene.NotifyTileLoaded(tileId);
            }
            if (!_disposed && !token.IsCancellationRequested)
                _scene.SetStatus($"Map ready · {_requestedTiles.Count} tiles cached · {_tiles.Attribution}.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException or InvalidOperationException or UriFormatException)
        {
            if (!_disposed) _scene.SetStatus($"Map tiles are unavailable right now: {failure.Message}");
        }
        finally
        {
            Interlocked.CompareExchange(ref _tileLoading, null, loading);
            loading.Dispose();
        }
    }

    private async Task RunSearchAsync()
    {
        if (_disposed || _searching) return;
        var query = _scene.SearchInput.Text.Trim();
        if (query.Length == 0)
        {
            _scene.SetStatus("Enter a place name to search.");
            return;
        }

        _searching = true;
        try
        {
            _scene.SetStatus($"Searching for “{query}”…");
            var result = await _maps.SearchAsync(query, SearchLimit, _lifetime.Token);
            _results.Clear();
            _results.AddRange(result.Places);
            _scene.SetResults([.. _results.Select(Describe)]);
            if (_results.Count == 0)
            {
                _scene.SetStatus($"No places matched “{query}”. The search service may be unavailable.");
                return;
            }
            _scene.SetStatus($"{_results.Count} place{(_results.Count == 1 ? string.Empty : "s")} found for “{query}”.");
            await _savedPlaces.RecordSearchAsync(query, _lifetime.Token);
            _recentSearches.RemoveAll(item => item.Equals(query, StringComparison.OrdinalIgnoreCase));
            _recentSearches.Insert(0, query);
            RefreshRecentSearchesList();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException or InvalidOperationException or TimeoutException)
        {
            _scene.SetStatus($"The place search failed: {failure.Message}");
        }
        finally
        {
            _searching = false;
        }
    }

    private void HandleResultSelected()
    {
        var index = _scene.ResultsSelect.SelectedIndex;
        if (index < 0 || index >= _results.Count) return;
        ChoosePlace(_results[index]);
    }

    private void HandleSavedPlaceSelected()
    {
        var index = _scene.SavedPlacesSelect.SelectedIndex;
        if (index < 0 || index >= _saved.Count) return;
        var saved = _saved[index];
        ChoosePlace(new MapPlace(saved.Id, saved.DisplayName, saved.Note, saved.Location, "saved"));
    }

    private void HandleRecentSearchSelected()
    {
        var index = _scene.RecentSearchesSelect.SelectedIndex;
        if (index < 0 || index >= _recentSearches.Count) return;
        _scene.SearchInput.Text = _recentSearches[index];
        _ = RunSearchAsync();
    }

    private void ChoosePlace(MapPlace place)
    {
        if (_disposed) return;
        _selectedPlace = place;
        if (_startPlace is null)
        {
            _startPlace = place;
            _scene.SetStatus($"Start marker set: {place.DisplayName}. Choose another place as the destination.");
        }
        else
        {
            _endPlace = place;
            _scene.SetStatus($"Destination marker set: {place.DisplayName}. Select “Get directions” to route between the markers.");
        }
        _scene.SetMarkers(_startPlace?.Location, _startPlace?.DisplayName, _endPlace?.Location, _endPlace?.DisplayName);
        _scene.CentreOn(place.Location);
    }

    private async Task RunRouteAsync()
    {
        if (_disposed) return;
        if (_startPlace?.Location is not { } start || _endPlace?.Location is not { } end)
        {
            _scene.SetStatus("Choose a start and a destination place first; each result choice marks the next free endpoint.");
            return;
        }
        var profile = (MapTravelProfile)Math.Clamp(_scene.ProfileSelect.SelectedIndex, 0, 2);
        try
        {
            _scene.SetStatus($"Finding the {profile.ToString().ToLowerInvariant()} route…");
            var route = await _maps.GetRouteAsync(start, end, profile, _lifetime.Token);
            if (route is null)
            {
                _scene.SetStatus("No route was returned. The routing service may be unavailable for this profile.");
                _scene.ClearRoute();
                return;
            }
            _scene.ShowRoute(route.Points);
            _scene.SetStatus($"Route ({route.Profile}): {route.SummaryText}. Routing by OSRM · data © OpenStreetMap contributors.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException or InvalidOperationException or TimeoutException)
        {
            _scene.SetStatus($"Routing failed: {failure.Message}");
        }
    }

    private Task SaveSelectedPlaceAsync()
    {
        if (_disposed) return Task.CompletedTask;
        var place = _selectedPlace;
        if (place is null)
        {
            _scene.SetStatus("Choose a place first; saving applies to the most recently chosen place.");
            return Task.CompletedTask;
        }
        OpenSaveForm(place);
        return Task.CompletedTask;
    }

    private void OpenSaveForm(MapPlace place)
    {
        CloseSaveForm();
        var document = _structuredFormTemplate.Create(_mapSessionId, "maps", BuildSavePlaceFormInputs(place));
        var surface = new HavenGenUiSceneSurface(_genUiRouter, _genUiInstances);
        surface.ActionCompleted += OnSavePlaceFormActionCompleted;
        _saveFormSurface = surface;
        _placeBeingSaved = place;
        surface.Present(document);
        _scene.ShowSaveForm(surface.Root);
        _scene.SetStatus($"Edit the saved name and note for {place.DisplayName}.");
    }

    private static IReadOnlyDictionary<string, JsonElement> BuildSavePlaceFormInputs(MapPlace place) =>
        new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["title"] = JsonSerializer.SerializeToElement("Save place details"),
            ["schema"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { id = "displayName", label = "Saved place name", type = "text", placeholder = "Name this place" },
                new { id = "note", label = "Note", type = "text", placeholder = "Optional note" }
            }),
            ["initialValues"] = JsonSerializer.SerializeToElement(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["displayName"] = place.DisplayName,
                ["note"] = place.DetailLine ?? string.Empty
            })
        };

    private void OnSavePlaceFormActionCompleted(object? sender, GenUiActionResult result)
    {
        if (result.ActionId == "structured-form.submit" && result.Status == GenUiActionStatus.Completed)
            _ = PersistSavePlaceFormAsync(result.StructuredResult);
    }

    private void OnSaveFormCancelled(object? sender, EventArgs e) => CloseSaveForm();
    private void OnOpenDataRequested(object? sender, EventArgs e) => DataWorkbookRequested?.Invoke(_savedPlaces.DataWorkbookId);

    private async Task PersistSavePlaceFormAsync(JsonElement values)
    {
        if (_disposed || Interlocked.Exchange(ref _savingPlace, 1) != 0) return;
        var place = _placeBeingSaved;
        if (place is null)
        {
            _scene.SetStatus("Choose a place first; saving applies to the most recently chosen place.");
            Interlocked.Exchange(ref _savingPlace, 0);
            return;
        }
        if (!TryCreateSavedPlaceFromForm(place, values, DateTimeOffset.UtcNow, out var saved, out var error))
        {
            _scene.SetStatus(error ?? "The saved place name is required.");
            Interlocked.Exchange(ref _savingPlace, 0);
            return;
        }
        try
        {
            await _savedPlaces.SaveAsync(saved!, _lifetime.Token);
            _saved.Insert(0, saved!);
            while (_saved.Count > MapsStoreLogic.MaxSavedPlaces) _saved.RemoveAt(_saved.Count - 1);
            RefreshSavedPlacesList();
            _scene.SetStatus($"Saved “{saved!.DisplayName}” to this device.");
            CloseSaveForm();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _scene.SetStatus($"The place could not be saved: {failure.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _savingPlace, 0);
        }
    }

    internal static bool TryCreateSavedPlaceFromForm(
        MapPlace place,
        JsonElement values,
        DateTimeOffset savedAt,
        out SavedMapPlace? saved,
        out string? validationError)
    {
        ArgumentNullException.ThrowIfNull(place);
        saved = null;
        validationError = null;
        if (values.ValueKind != JsonValueKind.Object
            || !values.TryGetProperty("structured-form.input.displayName", out var nameValue)
            || nameValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameValue.GetString()))
        {
            validationError = "The saved place name is required.";
            return false;
        }
        var displayName = nameValue.GetString()!.Trim();
        if (displayName.Length > 160) displayName = displayName[..160];
        string? note = null;
        if (values.TryGetProperty("structured-form.input.note", out var noteValue) && noteValue.ValueKind == JsonValueKind.String)
        {
            var noteText = noteValue.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(noteText)) note = noteText.Length > 4000 ? noteText[..4000] : noteText;
        }
        saved = new SavedMapPlace(Guid.NewGuid().ToString("N"), displayName, note, place.Location, savedAt);
        return true;
    }

    private void CloseSaveForm()
    {
        _scene.HideSaveForm();
        var surface = _saveFormSurface;
        _saveFormSurface = null;
        _placeBeingSaved = null;
        if (surface is null) return;
        surface.ActionCompleted -= OnSavePlaceFormActionCompleted;
        if (surface.Document is { } document) _genUiInstances.Remove(document.Origin.InstanceId);
        surface.Dispose();
    }

    private async Task CopySelectedCoordinatesAsync()
    {
        if (_disposed) return;
        var location = _selectedPlace?.Location ?? _endPlace?.Location ?? _startPlace?.Location;
        if (location is null)
        {
            _scene.SetStatus("Choose a place first; copying applies to the most recently chosen place.");
            return;
        }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            _scene.SetStatus("The platform clipboard is unavailable.");
            return;
        }
        try
        {
            var text = $"{location.Latitude.ToString("0.######", CultureInfo.InvariantCulture)}, " +
                       $"{location.Longitude.ToString("0.######", CultureInfo.InvariantCulture)}";
            await clipboard.SetTextAsync(text);
            _scene.SetStatus($"Copied coordinates: {text}.");
        }
        catch (Exception failure) when (failure is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _scene.SetStatus($"Coordinates could not be copied: {failure.Message}");
        }
    }

    private void RefreshSavedPlacesList()
    {
        _scene.SetSavedPlaces([.. _saved.Select(place => place.DisplayName)]);
    }

    private void RefreshRecentSearchesList()
    {
        _scene.SetRecentSearches([.. _recentSearches]);
    }

    private static string Describe(MapPlace place)
    {
        var detail = string.IsNullOrWhiteSpace(place.DetailLine) ? string.Empty : $" — {place.DetailLine}";
        var category = string.IsNullOrWhiteSpace(place.Category) ? string.Empty : $" ({place.Category})";
        return $"{place.DisplayName}{category}{detail}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SizeChanged -= OnSizeChanged;
        Interlocked.Exchange(ref _tileLoading, null)?.Cancel();
        _lifetime.Cancel();
        CloseSaveForm();
        _scene.SaveFormCancelled -= OnSaveFormCancelled;
        _scene.DataWorkbookRequested -= OnOpenDataRequested;
        DataWorkbookRequested = null;
        _lifetime.Dispose();
        Scene.Root = null;
        _scene.Dispose();
    }
}
