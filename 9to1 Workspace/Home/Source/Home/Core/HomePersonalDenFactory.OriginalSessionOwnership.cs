using System.Runtime.CompilerServices;
using Haven.Application;
using NineToOne.Dulche.Den;

namespace HavenOS.Home.Core;

public sealed partial class HomePersonalDenFactory
{
    private sealed record OriginalDenSessionOwnership(OriginalDenInvocation Invocation,
        AuthenticatedResourceActor Actor, string DenId, DulcheDen Den,
        IDenAccessPolicy Policy, VerifiedResourceStoreOwnership Binding);
    private sealed record OriginalDenSessionTask(Task<HomePersonalDenSession> Raw);
    private readonly ConditionalWeakTable<HomePersonalDenSession, OriginalDenSessionOwnership> _originalDenSessions = new();
    private readonly ConditionalWeakTable<OriginalDenInvocation, OriginalDenSessionTask> _originalDenSessionTasks = new();
    private void RetainOriginalDenSessionTask(OriginalDenInvocation invocation, Task<HomePersonalDenSession> raw) =>
        _originalDenSessionTasks.Add(invocation, new(raw));
    private HomePersonalDenSession CaptureOriginalDenSession(OriginalDenInvocation invocation,
        AuthenticatedResourceActor actor, string denId, VerifiedResourceStoreOwnership binding, DulcheDen den)
    {
        var session = new HomePersonalDenSession(actor, denId, den);
        _originalDenSessions.Add(session, new(invocation, actor, denId, den, den.AccessPolicy, binding));
        return session;
    }
    /// <summary>Pure historical projection of the SAME receipt verified by this factory's
    /// healthy original Open. This performs no IO and issues no current grant. A consumer
    /// must separately revalidate this exact receipt in actual held Home state before effect.
    /// Public session copies, borrowed policy objects and other factory instances refuse.</summary>
    public bool TryObserveOriginalDenOwnership(HomePersonalDenSession sameSession,
        out VerifiedResourceStoreOwnership? sameOriginalReceipt)
    {
        sameOriginalReceipt = null;
        if (sameSession is null || !_originalDenSessions.TryGetValue(sameSession, out var observation) ||
            !_originalDenSessionTasks.TryGetValue(observation.Invocation, out var task) ||
            !task.Raw.IsCompletedSuccessfully || !ReferenceEquals(task.Raw.GetAwaiter().GetResult(), sameSession) ||
            !_originalDenInvocations.TryGetValue(task.Raw, out var sameInvocation) ||
            !ReferenceEquals(sameInvocation, observation.Invocation) || sameSession.Actor != observation.Actor ||
            sameSession.DenId != observation.DenId || !ReferenceEquals(sameSession.Den, observation.Den) ||
            !ReferenceEquals(observation.Den.Store, provider.Store) ||
            !ReferenceEquals(observation.Den.AccessPolicy, observation.Policy) ||
            observation.Den.PrincipalId != observation.Actor.ActorId ||
            observation.Binding.Receipt is null || observation.Binding.ResourceKind != "den" ||
            observation.Binding.StoreId != observation.DenId || observation.Binding.ProfileId != observation.Actor.ProfileId)
            return false;
        sameOriginalReceipt = observation.Binding; return true;
    }
}
