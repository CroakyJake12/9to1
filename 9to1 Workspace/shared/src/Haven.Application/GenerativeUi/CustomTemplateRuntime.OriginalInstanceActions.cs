using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class CustomTemplateRuntime
{
    private readonly ConditionalWeakTable<GenUiDocument, OriginalInstanceActions> _originalInstanceActions = new();
    private readonly Dictionary<Guid, OriginalInstanceActions> _constructingInstanceActions = [];
    private readonly object _originalInstanceGate = new();

    private static string OriginalActionTarget(Guid instanceId, string actionId) =>
        $"custom:{instanceId:N}:{actionId}";

    private static GenUiComponent BindOriginalInstanceTargets(Guid instanceId, GenUiComponent component) => component with
    {
        Actions = component.Actions.Select(action => action with
            { TargetKey = OriginalActionTarget(instanceId, action.ActionId) }).ToArray(),
        Children = component.Children.Select(child => BindOriginalInstanceTargets(instanceId, child)).ToArray()
    };

    private void RegisterOriginalInstanceHandler(GenUiOrigin origin, string actionId,
        Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>> body)
    {
        OriginalInstanceActions issued;
        lock (_originalInstanceGate)
        {
            if (!_constructingInstanceActions.TryGetValue(origin.InstanceId, out issued!))
                _constructingInstanceActions.Add(origin.InstanceId, issued = new(origin));
        }
        var target = OriginalActionTarget(origin.InstanceId, actionId);
        Task<GenUiActionResult> Handle(GenUiEvent actual, CancellationToken token)
        {
            lock (issued.Gate)
            {
                if (issued.Retired || actual.Origin != issued.Origin || actual.ActionId != actionId)
                    return Task.FromResult(GenerativeUiEventRouter.Result(actual, GenUiActionStatus.Denied,
                        "The custom action belongs to a different or retired generated surface."));
            }
            // The maintained router independently checks the current document,
            // component and SAME registered binding before applying these patches.
            return body(actual, token);
        }
        Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>> handler = Handle;
        lock (issued.Gate) issued.Handlers.Add((target, handler)); // Before actual registry publication.
        _localActions.Register(target, handler);
    }

    private GenUiDocument RememberOriginalInstanceActions(GenUiDocument sameDocument)
    {
        lock (_originalInstanceGate)
        {
            if (!_constructingInstanceActions.Remove(sameDocument.Origin.InstanceId, out var issued))
                issued = new(sameDocument.Origin); // A valid static document is also an original issued instance.
            _originalInstanceActions.Add(sameDocument, issued);
        }
        return sameDocument;
    }

    /// <summary>Release only this runtime's SAME issued document after its actual event owner has joined.</summary>
    public bool ReleaseOriginalInstanceActions(GenUiDocument sameDocument)
    {
        ArgumentNullException.ThrowIfNull(sameDocument);
        if (!_originalInstanceActions.TryGetValue(sameDocument, out var issued)) return false;
        lock (issued.Gate)
        {
            if (issued.Released) return true;
            if (issued.ReleaseFailure is { } originalFailure) throw originalFailure;
            issued.Retired = true;
            foreach (var (target, handler) in issued.Handlers)
                if (!_localActions.RemoveOriginalHandler(target, handler))
                    throw issued.ReleaseFailure = new InvalidOperationException("The original custom action registration was replaced or removed before its owner joined.");
            issued.Released = true;
        }
        return true;
    }

    private sealed class OriginalInstanceActions(GenUiOrigin origin)
    {
        internal readonly object Gate = new();
        internal readonly GenUiOrigin Origin = origin;
        internal readonly List<(string Target, Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>> Handler)> Handlers = [];
        internal bool Retired;
        internal bool Released;
        internal Exception? ReleaseFailure;
    }
}
