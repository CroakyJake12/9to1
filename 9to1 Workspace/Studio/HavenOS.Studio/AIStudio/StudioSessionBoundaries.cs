using System.Runtime.ExceptionServices;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.AIStudio;

public sealed record StudioAgentListObservation(HomePersonalDenSession Session, AgentDefinitionRecord[] Agents);

/// <summary>Reads from the original Home session; a returned list is an observation, never an execution grant.</summary>
public static class StudioDenObservationBoundary
{
    public static bool SameSession(HomePersonalDenSession original, HomePersonalDenSession current) =>
        original.Actor == current.Actor && original.DenId == current.DenId &&
        ReferenceEquals(original.Den.Store, current.Den.Store);

    public static void RequireSameSession(HomePersonalDenSession original, HomePersonalDenSession current)
    {
        if (!SameSession(original, current))
            throw new UnauthorizedAccessException("The Den or Home session no longer matches the retained Agent editor.");
    }

    public static async Task<StudioAgentListObservation> ReadAgentsAsync(
        Func<CancellationToken, Task<HomePersonalDenSession>> openCurrent, string? selectedDenId,
        CancellationToken cancellationToken = default)
    {
        var original = await openCurrent(cancellationToken);
        if (selectedDenId is null || original.DenId != selectedDenId)
            throw new UnauthorizedAccessException("The selected Den changed before listing Agents.");
        var agents = (await original.Den.ListAsync<AgentDefinitionRecord>("personal", cancellationToken))
            .OrderBy(item => item.DisplayName, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var current = await openCurrent(cancellationToken);
        if (!SameSession(original, current))
            throw new UnauthorizedAccessException("The Den or Home session changed while listing Agents.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(original, agents);
    }
}

/// <summary>Attempts each caller-owned retirement action, preserving all distinct failures.</summary>
public static class StudioOwnedCleanup
{
    public static async Task RunAsync(IReadOnlyList<Action> clearActions, Action? disposeOriginalHost,
        Func<ValueTask>? disposeOriginalPreview)
    {
        List<Exception>? failures = null;
        void Retain(Exception failure)
        {
            failures ??= [];
            if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
        }
        foreach (var clear in clearActions)
            try { clear(); } catch (Exception failure) { Retain(failure); }
        try { disposeOriginalHost?.Invoke(); } catch (Exception failure) { Retain(failure); }
        if (disposeOriginalPreview is not null)
            try { await disposeOriginalPreview(); } catch (Exception failure) { Retain(failure); }
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }
}
