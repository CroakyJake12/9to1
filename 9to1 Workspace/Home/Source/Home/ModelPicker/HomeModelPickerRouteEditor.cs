using System.Text.Json;
using HavenOS.Home.Core;

namespace HavenOS.Home;

public sealed record HomeModelPickerEditorState(
    bool IsBusy,
    string StatusCode,
    string StatusMessage,
    HomeModelPickerSnapshot? Snapshot,
    string? SelectedRouteId,
    HomeModelRouteContract? DraftRoute,
    bool HasUnsavedChanges,
    HomeModelRoutePreview? Preview)
{
    public static HomeModelPickerEditorState Initial { get; } = new(
        false, "NotLoaded", "Model routes have not been loaded.", null, null, null, false, null);

    public IReadOnlyList<HomeModelRouteCandidate> Candidates =>
        DraftRoute?.Candidates ?? Array.Empty<HomeModelRouteCandidate>();
}

/// <summary>
/// Home's route editor projects and dispatches edits through the shared feature provider.
/// It does not perform model eligibility or fallback decisions locally.
/// </summary>
public sealed class HomeModelPickerRouteEditor(IHomeModelPickerFeatureProvider provider)
{
    private readonly IHomeModelPickerFeatureProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private HomeModelPickerEditorState _state = HomeModelPickerEditorState.Initial;

    public HomeModelPickerEditorState Current => Volatile.Read(ref _state);

    public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> RefreshAsync(
        string scope,
        string category,
        bool discardUnsavedChanges = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(category))
            return Fail<HomeModelPickerSnapshot>("InvalidRouteScope", "Choose a route scope and capability category.");
        if (Current.HasUnsavedChanges && !discardUnsavedChanges)
            return Fail<HomeModelPickerSnapshot>("UnsavedChanges", "Save or discard route edits before refreshing.");

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Publish(Current with { IsBusy = true, StatusCode = "Loading", StatusMessage = "Loading model routes.", Preview = null });
            var result = await _provider.GetSnapshotAsync(scope.Trim(), category.Trim(), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || result.Value is null)
            {
                Publish(Current with { IsBusy = false, StatusCode = EmptyCode(result.Code), StatusMessage = result.Message });
                return result.Succeeded
                    ? Fail<HomeModelPickerSnapshot>("InvalidProviderResult", "The model service returned no route snapshot.")
                    : result;
            }

            ValidateSnapshot(result.Value, scope.Trim(), category.Trim());
            var selectedRouteId = Current.SelectedRouteId is { } prior
                && result.Value.Routes.Any(route => route.RouteId == prior)
                    ? prior
                    : result.Value.Routes.FirstOrDefault()?.RouteId;
            var selected = result.Value.Routes.FirstOrDefault(route => route.RouteId == selectedRouteId);
            Publish(new HomeModelPickerEditorState(
                false,
                result.Value.Routes.Count == 0 ? "NoRoutes" : "Ready",
                result.Value.Routes.Count == 0 ? "No routes are configured for this scope and category." : "Model routes are ready.",
                result.Value,
                selectedRouteId,
                selected,
                false,
                null));
            return result;
        }
        catch (OperationCanceledException)
        {
            Publish(Current with { IsBusy = false, StatusCode = "Cancelled", StatusMessage = "Loading model routes was cancelled." });
            throw;
        }
        catch (InvalidDataException exception)
        {
            var failed = Fail<HomeModelPickerSnapshot>("InvalidProviderResult", exception.Message);
            Publish(Current with { IsBusy = false, StatusCode = failed.Code, StatusMessage = failed.Message });
            return failed;
        }
        catch (Exception exception)
        {
            var failed = Fail<HomeModelPickerSnapshot>("HomeServiceUnavailable",
                $"Could not load model routes ({exception.GetType().Name}).");
            Publish(Current with { IsBusy = false, StatusCode = failed.Code, StatusMessage = failed.Message });
            return failed;
        }
        finally { _operationGate.Release(); }
    }

    public HomeCoreOperationResult<HomeModelRouteContract> SelectRoute(string routeId)
    {
        var state = Current;
        if (state.IsBusy) return Fail<HomeModelRouteContract>("OperationInProgress", "Wait for the current model operation to finish.");
        if (state.HasUnsavedChanges) return Fail<HomeModelRouteContract>("UnsavedChanges", "Save or discard route edits before changing routes.");
        var route = state.Snapshot?.Routes.FirstOrDefault(item => item.RouteId == routeId);
        if (route is null) return Fail<HomeModelRouteContract>("RouteNotFound", "That model route is no longer available.");
        Publish(state with { SelectedRouteId = route.RouteId, DraftRoute = route, Preview = null, StatusCode = "Ready", StatusMessage = $"Editing {route.Category} route." });
        return Success(route, state.Snapshot!.Revision);
    }

    public HomeCoreOperationResult<HomeModelRouteContract> SetCandidateEnabled(
        string providerId, string modelId, string artifactRevision, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(artifactRevision))
            return Fail<HomeModelRouteContract>("InvalidModelIdentity", "A provider, model and artifact revision are required.");
        var state = Current;
        if (state.IsBusy) return Fail<HomeModelRouteContract>("OperationInProgress", "Wait for the current model operation to finish.");
        if (state.DraftRoute is not { } route) return Fail<HomeModelRouteContract>("RouteNotSelected", "Select a model route first.");
        var index = FindCandidate(route, providerId, modelId, artifactRevision);
        if (index < 0) return Fail<HomeModelRouteContract>("CandidateNotFound", "That model is not in the selected route.");
        if (route.Candidates[index].Enabled == enabled) return Success(route, state.Snapshot?.Revision ?? 0);
        var candidates = Ordered(route.Candidates).ToArray();
        var currentIndex = Array.FindIndex(candidates, candidate => SameIdentity(candidate, providerId, modelId, artifactRevision));
        candidates[currentIndex] = candidates[currentIndex] with { Enabled = enabled, Order = currentIndex };
        var draft = route with { Candidates = Array.AsReadOnly(candidates) };
        Publish(state with { DraftRoute = draft, HasUnsavedChanges = true, Preview = null, StatusCode = "UnsavedChanges", StatusMessage = "Route candidate changes are not saved." });
        return Success(draft, state.Snapshot?.Revision ?? 0);
    }

    public HomeCoreOperationResult<HomeModelRouteContract> MoveCandidate(
        string providerId, string modelId, string artifactRevision, int destinationIndex)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(artifactRevision))
            return Fail<HomeModelRouteContract>("InvalidModelIdentity", "A provider, model and artifact revision are required.");
        var state = Current;
        if (state.IsBusy) return Fail<HomeModelRouteContract>("OperationInProgress", "Wait for the current model operation to finish.");
        if (state.DraftRoute is not { } route) return Fail<HomeModelRouteContract>("RouteNotSelected", "Select a model route first.");
        var candidates = Ordered(route.Candidates).ToArray();
        var sourceIndex = Array.FindIndex(candidates, candidate => SameIdentity(candidate, providerId, modelId, artifactRevision));
        if (sourceIndex < 0) return Fail<HomeModelRouteContract>("CandidateNotFound", "That model is not in the selected route.");
        if (destinationIndex < 0 || destinationIndex >= candidates.Length)
            return Fail<HomeModelRouteContract>("InvalidOrder", "Choose a position within the route.");
        if (sourceIndex == destinationIndex) return Success(route, state.Snapshot?.Revision ?? 0);

        var moved = candidates[sourceIndex];
        var reordered = candidates.ToList();
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(destinationIndex, moved);
        var normalized = reordered.Select((candidate, index) => candidate with { Order = index }).ToArray();
        var draft = route with { Candidates = Array.AsReadOnly(normalized) };
        Publish(state with { DraftRoute = draft, HasUnsavedChanges = true, Preview = null, StatusCode = "UnsavedChanges", StatusMessage = "Route order is not saved." });
        return Success(draft, state.Snapshot?.Revision ?? 0);
    }

    public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> SaveAsync(CancellationToken cancellationToken = default)
    {
        var initial = Current;
        if (!initial.HasUnsavedChanges || initial.DraftRoute is not { } route || initial.Snapshot is not { } snapshot)
            return Fail<HomeModelPickerSnapshot>("NoChanges", "There are no route edits to save.");
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            initial = Current;
            if (!initial.HasUnsavedChanges || initial.DraftRoute is not { } currentRoute || initial.Snapshot is not { } currentSnapshot)
                return Fail<HomeModelPickerSnapshot>("NoChanges", "There are no route edits to save.");
            var stateBeforeSave = initial;
            Publish(initial with { IsBusy = true, StatusCode = "Saving", StatusMessage = "Saving model route." });
            var updatedRoute = currentRoute with
            {
                Version = checked(currentRoute.Version + 1),
                Candidates = Array.AsReadOnly(Ordered(currentRoute.Candidates)
                    .Select((candidate, index) => candidate with { Order = index }).ToArray()),
            };
            var result = await _provider.UpdateRouteAsync(
                new HomeModelRouteEdit(updatedRoute, currentSnapshot.Revision), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || result.Value is null)
            {
                Publish(stateBeforeSave with { IsBusy = false, StatusCode = EmptyCode(result.Code), StatusMessage = result.Message });
                return result.Succeeded
                    ? Fail<HomeModelPickerSnapshot>("InvalidProviderResult", "The model service returned no updated route snapshot.")
                    : result;
            }

            ValidateSnapshot(result.Value, currentSnapshot.Scope, currentSnapshot.Category);
            var selected = result.Value.Routes.FirstOrDefault(item => item.RouteId == updatedRoute.RouteId);
            Publish(new HomeModelPickerEditorState(
                false,
                selected is null ? "RouteRemoved" : "Saved",
                selected is null ? "The saved route is no longer available." : "Model route saved.",
                result.Value,
                selected?.RouteId,
                selected,
                false,
                null));
            return result;
        }
        catch (OperationCanceledException)
        {
            Publish(Current with { IsBusy = false, StatusCode = "Cancelled", StatusMessage = "Saving the model route was cancelled." });
            throw;
        }
        catch (InvalidDataException exception)
        {
            var failed = Fail<HomeModelPickerSnapshot>("InvalidProviderResult", exception.Message);
            Publish(Current with { IsBusy = false, StatusCode = failed.Code, StatusMessage = failed.Message });
            return failed;
        }
        catch (Exception exception)
        {
            var failed = Fail<HomeModelPickerSnapshot>("HomeServiceUnavailable", $"Could not save the model route ({exception.GetType().Name}).");
            var current = Current;
            Publish(current with { IsBusy = false, StatusCode = failed.Code, StatusMessage = failed.Message });
            return failed;
        }
        finally { _operationGate.Release(); }
    }

    public async Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewAsync(
        string capability,
        string? appId = null,
        string? agentId = null,
        JsonElement? context = null,
        CancellationToken cancellationToken = default)
    {
        var initial = Current;
        if (initial.IsBusy) return Fail<HomeModelRoutePreview>("OperationInProgress", "Wait for the current model operation to finish.");
        if (initial.HasUnsavedChanges) return Fail<HomeModelRoutePreview>("UnsavedChanges", "Save route edits before previewing resolution.");
        if (initial.SelectedRouteId is not { } routeId) return Fail<HomeModelRoutePreview>("RouteNotSelected", "Select a model route first.");
        if (string.IsNullOrWhiteSpace(capability)) return Fail<HomeModelRoutePreview>("InvalidCapability", "Choose a required model capability.");

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Publish(Current with { IsBusy = true, StatusCode = "Resolving", StatusMessage = "Checking route eligibility.", Preview = null });
            var payload = context ?? EmptyObject();
            var result = await _provider.PreviewResolutionAsync(
                new HomeModelRoutePreviewRequest(routeId, capability.Trim(), appId, agentId, payload), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || result.Value is null)
            {
                Publish(Current with { IsBusy = false, StatusCode = EmptyCode(result.Code), StatusMessage = result.Message });
                return result.Succeeded
                    ? Fail<HomeModelRoutePreview>("InvalidProviderResult", "The model service returned no route preview.")
                    : result;
            }

            Publish(Current with { IsBusy = false, StatusCode = result.Value.FailureCode ?? "PreviewReady", StatusMessage = result.Value.ResolutionState, Preview = result.Value });
            return result;
        }
        catch (OperationCanceledException)
        {
            Publish(Current with { IsBusy = false, StatusCode = "Cancelled", StatusMessage = "Model route preview was cancelled." });
            throw;
        }
        catch (Exception exception)
        {
            var failed = Fail<HomeModelRoutePreview>("HomeServiceUnavailable", $"Could not preview model routing ({exception.GetType().Name}).");
            Publish(Current with { IsBusy = false, StatusCode = failed.Code, StatusMessage = failed.Message });
            return failed;
        }
        finally { _operationGate.Release(); }
    }

    private void Publish(HomeModelPickerEditorState state) => Volatile.Write(ref _state, state);

    private static IReadOnlyList<HomeModelRouteCandidate> Ordered(IReadOnlyList<HomeModelRouteCandidate> candidates) =>
        candidates.OrderBy(candidate => candidate.Order).ToArray();

    private static int FindCandidate(HomeModelRouteContract route, string providerId, string modelId, string revision) =>
        Array.FindIndex(route.Candidates.ToArray(), candidate => SameIdentity(candidate, providerId, modelId, revision));

    private static bool SameIdentity(HomeModelRouteCandidate candidate, string providerId, string modelId, string revision) =>
        StringComparer.Ordinal.Equals(candidate.ProviderId, providerId)
        && StringComparer.Ordinal.Equals(candidate.ModelId, modelId)
        && StringComparer.Ordinal.Equals(candidate.ArtifactRevision, revision);

    private static void ValidateSnapshot(HomeModelPickerSnapshot snapshot, string? expectedScope = null,
        string? expectedCategory = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Routes);
        if (snapshot.Revision < 0) throw new InvalidDataException("Model route snapshot has a negative revision.");
        if (string.IsNullOrWhiteSpace(snapshot.Scope) || string.IsNullOrWhiteSpace(snapshot.Category)
            || (expectedScope is not null && !StringComparer.Ordinal.Equals(snapshot.Scope, expectedScope))
            || (expectedCategory is not null && !StringComparer.Ordinal.Equals(snapshot.Category, expectedCategory)))
            throw new InvalidDataException("Model route snapshot does not match the requested scope and category.");
        var routeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in snapshot.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.RouteId) || !routeIds.Add(route.RouteId))
                throw new InvalidDataException("Model route snapshot contains an empty or duplicate route ID.");
            if (route.Version < 0 || string.IsNullOrWhiteSpace(route.Scope) || string.IsNullOrWhiteSpace(route.Category))
                throw new InvalidDataException($"Model route '{route.RouteId}' has invalid version or scope metadata.");
            ArgumentNullException.ThrowIfNull(route.Candidates);
            var identityKeys = new HashSet<string>(StringComparer.Ordinal);
            var orders = new HashSet<int>();
            foreach (var candidate in route.Candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.ProviderId) || string.IsNullOrWhiteSpace(candidate.ModelId)
                    || string.IsNullOrWhiteSpace(candidate.ArtifactRevision) || candidate.Order < 0)
                    throw new InvalidDataException($"Model route '{route.RouteId}' contains an invalid candidate.");
                var identity = $"{candidate.ProviderId}\0{candidate.ModelId}\0{candidate.ArtifactRevision}";
                if (!identityKeys.Add(identity) || !orders.Add(candidate.Order))
                    throw new InvalidDataException($"Model route '{route.RouteId}' contains duplicate candidates or order values.");
            }
        }
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static string EmptyCode(string? code) => string.IsNullOrWhiteSpace(code) ? "HomeOperationFailed" : code;

    private static HomeCoreOperationResult<T> Success<T>(T value, long revision) =>
        new(true, "Succeeded", "Operation succeeded.", value, true, revision);

    private static HomeCoreOperationResult<T> Fail<T>(string code, string message) =>
        new(false, code, message, default, true);
}
