using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Destination adapter; capability adapters must call Haven's normal capability/permission runtime.</summary>
public interface IGenUiEventHandler
{
    GenUiRouteKind RouteKind { get; }
    bool CanHandle(string targetKey);
    Task<GenUiActionResult> HandleAsync(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        CancellationToken cancellationToken);
}

public interface IGenUiEventAuditSink
{
    ValueTask RecordAsync(GenUiEvent semanticEvent, GenUiActionResult result, CancellationToken cancellationToken);
}

/// <summary>
/// Routes meaningful UI interaction to the cheapest authoritative destination.
/// It never treats a click as permission and never converts a structured event
/// into a synthetic natural-language user message.
/// </summary>
public sealed class GenerativeUiEventRouter(
    IEnumerable<IGenUiEventHandler> handlers,
    IGenUiEventAuditSink audit,
    GenUiInstanceStore instances,
    IPermissionDecisionEngine? permissions = null)
{
    private readonly IReadOnlyList<IGenUiEventHandler> _handlers = handlers.ToArray();

    public async Task<GenUiActionResult> RouteAsync(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(semanticEvent);
        ArgumentNullException.ThrowIfNull(binding);
        var errors = GenerativeUiContractValidator.Validate(semanticEvent);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        errors = GenerativeUiContractValidator.Validate(binding);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        if (!binding.ActionId.Equals(semanticEvent.ActionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Event action ID does not match its registered binding.");

        var document = instances.TryGet(semanticEvent.Origin.InstanceId);
        var component = document is null || document.Origin != semanticEvent.Origin
            ? null
            : FindComponent(document.Root, semanticEvent.ComponentId);
        var registeredBinding = component is null
            ? null
            : component.Actions.FirstOrDefault(action => action.ActionId.Equals(binding.ActionId, StringComparison.Ordinal));
        if (registeredBinding != binding)
        {
            var rejected = Result(semanticEvent, GenUiActionStatus.Denied,
                "The action is not declared by the current generated UI instance.");
            await audit.RecordAsync(semanticEvent, rejected, cancellationToken).ConfigureAwait(false);
            return rejected;
        }

        // App, capability, and external actions always go through the shared
        // permission decision engine, even when their trusted descriptor does
        // not require an interactive prompt for this risk class.
        var requiresBroker = binding.RequiresPermission
            || binding.RiskClass >= CapabilityRiskClass.Consequential
            || binding.Route is GenUiRouteKind.App or GenUiRouteKind.Capability or GenUiRouteKind.External;
        if (requiresBroker && permissions is null)
        {
            var unavailable = Result(
                semanticEvent,
                GenUiActionStatus.Unavailable,
                "The permission service is unavailable; the generated action was not executed.");
            await audit.RecordAsync(semanticEvent, unavailable, cancellationToken).ConfigureAwait(false);
            return unavailable;
        }

        var decision = permissions?.Evaluate(
            binding.TargetKey,
            binding.RiskClass,
            binding.RequiresPermission,
            $"Generated UI requested action '{binding.ActionId}'.");
        if (decision?.Kind == PermissionDecisionKind.Ask)
        {
            var pending = Result(
                semanticEvent,
                GenUiActionStatus.PermissionRequired,
                decision.Reason,
                System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    scope = decision.Scope,
                    action = binding.ActionId,
                    reason = decision.Reason,
                    allow = true,
                    deny = true,
                    alwaysAllow = true
                }));
            await audit.RecordAsync(semanticEvent, pending, cancellationToken).ConfigureAwait(false);
            return pending;
        }
        if (decision?.Kind == PermissionDecisionKind.Denied
            || decision is not null && !Enum.IsDefined(decision.Kind))
        {
            var denied = Result(
                semanticEvent,
                GenUiActionStatus.Denied,
                decision?.Reason is { Length: > 0 } reason
                    ? reason
                    : "The generated action was denied by the permission service.");
            await audit.RecordAsync(semanticEvent, denied, cancellationToken).ConfigureAwait(false);
            return denied;
        }

        var handler = _handlers.FirstOrDefault(candidate =>
            candidate.RouteKind == binding.Route && candidate.CanHandle(binding.TargetKey));
        var result = handler is null
            ? Result(semanticEvent, GenUiActionStatus.Unavailable,
                $"No {binding.Route} handler is registered for '{binding.TargetKey}'.")
            : await handler.HandleAsync(semanticEvent, binding, cancellationToken).ConfigureAwait(false);

        if (result.EventId != semanticEvent.EventId || result.Origin != semanticEvent.Origin)
            throw new InvalidOperationException("Action result lost the originating event or instance identity.");
        if (!result.ComponentId.Equals(semanticEvent.ComponentId, StringComparison.Ordinal)
            || !result.ActionId.Equals(semanticEvent.ActionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Action result changed the originating component or action identity.");
        if (!Enum.IsDefined(result.Status) || result.Patches is null || result.Summary is null)
            throw new InvalidOperationException("Action result status, patches, and summary must be valid.");
        if (result.Status != GenUiActionStatus.Completed && result.Patches.Count > 0)
            throw new InvalidOperationException("A non-completed action cannot mutate generated UI state.");
        if (result.Patches.Count > 100)
            throw new InvalidOperationException("An action result exceeds the patch batch limit.");
        if (result.Patches.Any(patch => patch.InstanceId != semanticEvent.Origin.InstanceId || patch.PatchId == Guid.Empty))
            throw new InvalidOperationException("Action result patches must target the originating instance and have stable IDs.");
        if (result.StructuredResult.ValueKind == JsonValueKind.Undefined
            || JsonSerializer.SerializeToUtf8Bytes(result.StructuredResult).Length > GenerativeUiContractValidator.MaximumJsonBytes)
            throw new InvalidOperationException("Action result payload is missing or exceeds the generated UI contract limit.");

        await instances.ApplyResultAsync(result, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(semanticEvent, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static GenUiActionResult Result(
        GenUiEvent semanticEvent,
        GenUiActionStatus status,
        string summary,
        JsonElement? structuredResult = null,
        IReadOnlyList<GenUiStatePatch>? patches = null) => new(
        Guid.NewGuid(),
        semanticEvent.EventId,
        semanticEvent.Origin,
        semanticEvent.ComponentId,
        semanticEvent.ActionId,
        status,
        summary,
        structuredResult ?? JsonSerializer.SerializeToElement(new { }),
        patches ?? [],
        DateTimeOffset.UtcNow);

    private static GenUiComponent? FindComponent(GenUiComponent root, string componentId)
    {
        if (root.ComponentId.Equals(componentId, StringComparison.Ordinal)) return root;
        foreach (var child in root.Children)
        {
            var match = FindComponent(child, componentId);
            if (match is not null) return match;
        }
        return null;
    }
}

/// <summary>Registers deterministic handlers without involving a model.</summary>
public sealed class GenUiLocalActionRegistry : IGenUiEventHandler
{
    private readonly ConcurrentDictionary<string, Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public GenUiRouteKind RouteKind => GenUiRouteKind.Local;

    public bool CanHandle(string targetKey) => _handlers.ContainsKey(targetKey);

    public void Register(
        string targetKey,
        Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(targetKey, handler))
            throw new InvalidOperationException($"Local GenUI handler '{targetKey}' is already registered.");
    }

    public void RegisterOrReplace(
        string targetKey,
        Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[targetKey] = handler;
    }

    public Task<GenUiActionResult> HandleAsync(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        CancellationToken cancellationToken) =>
        _handlers.TryGetValue(binding.TargetKey, out var handler)
            ? handler(semanticEvent, cancellationToken)
            : Task.FromResult(GenerativeUiEventRouter.Result(
                semanticEvent, GenUiActionStatus.Unavailable, $"Local handler '{binding.TargetKey}' is unavailable."));
}

/// <summary>Bounded semantic audit; it deliberately stores no raw conversation transcript.</summary>
public sealed class BoundedGenUiEventAuditSink : IGenUiEventAuditSink
{
    private const int MaximumEntries = 500;
    private readonly object _gate = new();
    private readonly Queue<GenUiAuditEntry> _entries = new();

    public IReadOnlyList<GenUiAuditEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    public ValueTask RecordAsync(GenUiEvent semanticEvent, GenUiActionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = semanticEvent.InteractionContext.Length <= 256
            ? semanticEvent.InteractionContext
            : semanticEvent.InteractionContext[..256];
        var summary = result.Summary.Length <= 512 ? result.Summary : result.Summary[..512];
        lock (_gate)
        {
            _entries.Enqueue(new GenUiAuditEntry(
                semanticEvent.EventId,
                semanticEvent.EventType,
                semanticEvent.Origin,
                semanticEvent.ComponentId,
                semanticEvent.ActionId,
                semanticEvent.Source,
                context,
                result.Status,
                summary,
                result.Timestamp));
            while (_entries.Count > MaximumEntries) _entries.Dequeue();
        }
        return ValueTask.CompletedTask;
    }
}

public sealed record GenUiAuditEntry(
    Guid EventId,
    GenUiEventType EventType,
    GenUiOrigin Origin,
    string ComponentId,
    string ActionId,
    GenUiEventSource Source,
    string InteractionContext,
    GenUiActionStatus Status,
    string Summary,
    DateTimeOffset Timestamp);
