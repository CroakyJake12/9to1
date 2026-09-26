using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;

namespace HavenOS.Home;

public enum HomeModelPickerAction
{
    Refresh,
    SelectCategory,
    SelectRoute,
    SetCandidateEnabled,
    MoveCandidateUp,
    MoveCandidateDown,
    SaveRoute,
    PreviewResolution,
}

public sealed record HomeModelPickerActionRequest(
    HomeModelPickerAction Action,
    string? Scope = null,
    string? Category = null,
    string? RouteId = null,
    string? ProviderId = null,
    string? ModelId = null,
    string? ArtifactRevision = null,
    bool? Enabled = null,
    string? Capability = null,
    string? AppId = null,
    string? AgentId = null,
    JsonElement? Context = null);

/// <summary>Owns the authored Home model-picker CUI document and the typed action queue for its host.</summary>
public sealed class HomeModelPickerCuiSurface
{
    private readonly Queue<HomeModelPickerActionRequest> _actions = new();

    public HomeModelPickerCuiSurface(CuiDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public CuiDocument Document { get; }
    public HomeModelPickerEditorState State { get; private set; } = HomeModelPickerEditorState.Initial;
    public string Scope { get; private set; } = "global";
    public string Category { get; private set; } = "chat";

    public static HomeModelPickerCuiSurface LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UI", "ModelPicker.cui");
        return new HomeModelPickerCuiSurface(new CuiRichParser().ParseFile(path));
    }

    public void ApplyState(HomeModelPickerEditorState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        if (state.Snapshot is { } snapshot)
        {
            Scope = snapshot.Scope;
            Category = snapshot.Category;
        }
    }

    public bool Request(HomeModelPickerActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action == HomeModelPickerAction.SelectCategory && string.IsNullOrWhiteSpace(request.Category))
            return false;
        if (request.Action == HomeModelPickerAction.SelectRoute && string.IsNullOrWhiteSpace(request.RouteId))
            return false;
        if ((request.Action is HomeModelPickerAction.SetCandidateEnabled or HomeModelPickerAction.MoveCandidateUp or HomeModelPickerAction.MoveCandidateDown)
            && (string.IsNullOrWhiteSpace(request.ProviderId) || string.IsNullOrWhiteSpace(request.ModelId) || string.IsNullOrWhiteSpace(request.ArtifactRevision)))
            return false;
        if (request.Action == HomeModelPickerAction.SetCandidateEnabled && request.Enabled is null)
            return false;

        _actions.Enqueue(request);
        return true;
    }

    public bool TryDequeueAction(out HomeModelPickerActionRequest request)
    {
        if (_actions.TryDequeue(out var action))
        {
            request = action;
            return true;
        }
        request = null!;
        return false;
    }
}

/// <summary>Routes CUI picker actions through Home's shared model-provider contract.</summary>
public sealed class HomeModelPickerCuiController(HomeModelPickerRouteEditor editor, HomeModelPickerCuiSurface? surface = null)
{
    private readonly HomeModelPickerRouteEditor _editor = editor ?? throw new ArgumentNullException(nameof(editor));

    public HomeModelPickerCuiSurface Surface { get; } = surface ?? HomeModelPickerCuiSurface.LoadDefault();

    public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> OpenAsync(
        string scope = "global",
        string category = "chat",
        CancellationToken cancellationToken = default)
    {
        var result = await _editor.RefreshAsync(scope, category, cancellationToken: cancellationToken).ConfigureAwait(false);
        Surface.ApplyState(_editor.Current);
        return result;
    }

    public async Task<HomeCoreOperationResult<object>> ExecuteAsync(
        HomeModelPickerActionRequest action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        HomeCoreOperationResult<object> result;
        try
        {
            result = action.Action switch
            {
                HomeModelPickerAction.Refresh => Box(await _editor.RefreshAsync(
                    action.Scope ?? Surface.Scope,
                    action.Category ?? Surface.Category,
                    cancellationToken: cancellationToken).ConfigureAwait(false)),
                HomeModelPickerAction.SelectCategory => Box(await _editor.RefreshAsync(
                    action.Scope ?? Surface.Scope,
                    Required(action.Category, "capability category"),
                    cancellationToken: cancellationToken).ConfigureAwait(false)),
                HomeModelPickerAction.SelectRoute => Box(_editor.SelectRoute(Required(action.RouteId, "route ID"))),
                HomeModelPickerAction.SetCandidateEnabled => Box(_editor.SetCandidateEnabled(
                    Required(action.ProviderId, "provider ID"), Required(action.ModelId, "model ID"),
                    Required(action.ArtifactRevision, "artifact revision"), action.Enabled
                    ?? throw new ArgumentException("Candidate enabled state is required.", nameof(action)))),
                HomeModelPickerAction.MoveCandidateUp => Box(Move(action, -1)),
                HomeModelPickerAction.MoveCandidateDown => Box(Move(action, 1)),
                HomeModelPickerAction.SaveRoute => Box(await _editor.SaveAsync(cancellationToken).ConfigureAwait(false)),
                HomeModelPickerAction.PreviewResolution => Box(await _editor.PreviewAsync(
                    Required(action.Capability, "required capability"), action.AppId, action.AgentId, action.Context,
                    cancellationToken).ConfigureAwait(false)),
                _ => Failure<object>("UnsupportedAction", "This model-picker action is not supported."),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (ArgumentException exception)
        {
            result = Failure<object>("InvalidArgument", exception.Message);
        }
        Surface.ApplyState(_editor.Current);
        return result;
    }

    private HomeCoreOperationResult<HomeModelRouteContract> Move(HomeModelPickerActionRequest action, int direction)
    {
        var candidate = _editor.Current.Candidates.FirstOrDefault(item =>
            item.ProviderId == action.ProviderId && item.ModelId == action.ModelId && item.ArtifactRevision == action.ArtifactRevision);
        if (candidate is null) return Failure<HomeModelRouteContract>("CandidateNotFound", "That model is not in the selected route.");
        return _editor.MoveCandidate(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision, candidate.Order + direction);
    }

    private static string Required(string? value, string label) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"A {label} is required.")
        : value;

    private static HomeCoreOperationResult<object> Box<T>(HomeCoreOperationResult<T> result) =>
        new(result.Succeeded, result.Code, result.Message, result.Value, result.Recoverable, result.Revision);

    private static HomeCoreOperationResult<T> Failure<T>(string code, string message) => new(false, code, message);
}
