#if !ANDROID
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Haven.Desktop.Views.Pages.Development;

namespace Haven.Desktop.Services;

/// <summary>Actual transferred Assistant Dev source and current native Dev host. This
/// observes genuine Home compatibility/currentness and issues no rendered-frame or effect grant.</summary>
internal sealed class AssistantNativeTaskSceneReadiness : ICuiSceneReadiness,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
{
    private DeveloperProjectWorkbenchPage? _page;
    private CuiSceneHost? _host;
    private readonly DenAssistantOriginalDevelopmentOwner _owner;
    private readonly AssistantDevelopmentBinding _binding;
    private readonly IAssistantOriginalDevelopmentCustody _custody;
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly HostLocalTaskActorSource _actors;
    private readonly CancellationToken _appLifetime, _windowLifetime;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    [ThreadStatic] private static List<AssistantNativeTaskSceneReadiness>? _physical;
    private Task? _custodyClose;
    private AuthenticatedResourceActor? _originalActor;
    private AuthenticatedResourceActor? _originalHomeActor;

    private AssistantNativeTaskSceneReadiness(DenAssistantOriginalDevelopmentOwner owner, AssistantDevelopmentBinding binding,
        IAssistantOriginalDevelopmentCustody custody, IServiceProvider provider,
        HomeNativeWindowsComposition home, HostLocalTaskActorSource actors,
        CancellationToken appLifetime, CancellationToken windowLifetime)
    {
        _owner = owner; _binding = binding;
        _custody = custody; _provider = provider; _home = home; _actors = actors;
        _appLifetime = appLifetime; _windowLifetime = windowLifetime;
        _work = new(StopOriginalAsync, static () => Task.CompletedTask);
        // No callbacks, compatibility checks or native publication during construction.
    }

    internal static AssistantNativeTaskSceneReadiness CaptureOriginalSameProcess(
        DenAssistantOriginalDevelopmentOwner sameOwner,
        AssistantDevelopmentBinding sameBinding, IAssistantOriginalDevelopmentCustody sameCustody,
        IServiceProvider sameProvider, HomeNativeWindowsComposition sameHome,
        HostLocalTaskActorSource sameActors, CancellationToken actualAppLifetime,
        CancellationToken actualWindowLifetime, Action<AssistantNativeTaskSceneReadiness> retainOriginalOwner)
    {
        ArgumentNullException.ThrowIfNull(sameOwner); ArgumentNullException.ThrowIfNull(retainOriginalOwner);
        ArgumentNullException.ThrowIfNull(sameBinding); ArgumentNullException.ThrowIfNull(sameCustody);
        var actual = new AssistantNativeTaskSceneReadiness(sameOwner, sameBinding, sameCustody,
            sameProvider, sameHome, sameActors, actualAppLifetime, actualWindowLifetime);
        actual.InvokeCapturedSource(() => retainOriginalOwner(actual));
        actual._work.DemandAdmission();
        // Trusted root composition supplies the actual owner; interface presence/IDs do not bind it.
        actual.InvokeCapturedSource(() =>
        {
            if (!ReferenceEquals(sameProvider.GetService<IAssistantOriginalDevelopmentOwner>(), sameOwner) ||
                !ReferenceEquals(sameProvider.GetRequiredService<HostLocalTaskActorSource>(), sameActors) ||
                !ReferenceEquals(sameOwner.OriginalTaskActorOwner, sameActors) ||
                !ReferenceEquals(sameOwner.OriginalProjectReadOwner, sameProvider.GetRequiredService<HomeColdProjectReadReconciliation>()))
                throw new UnauthorizedAccessException("Retain the actual configured Assistant development and Task actor sources.");
            WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(sameProvider, sameHome);
            if (!actualAppLifetime.CanBeCanceled || !actualWindowLifetime.CanBeCanceled)
                throw new ArgumentException("Retain the actual App and native owning window lifetimes.");
            actualAppLifetime.ThrowIfCancellationRequested(); actualWindowLifetime.ThrowIfCancellationRequested();
        });
        actual._work.DemandAdmission();
        return actual;
    }

    internal ICuiSceneReadiness BindOriginalDevelopment(DeveloperProjectWorkbenchPage samePage)
    {
        ArgumentNullException.ThrowIfNull(samePage);
        InvokeCapturedSource(() => _work.RunSynchronous(original =>
        {
            original.DemandPublication();
            var context = samePage.OriginalReadinessContext;
            if (context.TaskId != _binding.CanonicalTask.TaskId || context.ExecutionId != _binding.CanonicalTask.ExecutionId ||
                context.ContextId != _binding.Conversation.Conversation.Id || context.ContextId != _binding.CanonicalTask.ContextId)
                throw new UnauthorizedAccessException("The actual acquired Dev host differs from the bound original Task/Run/context.");
            lock (_gate)
            {
                if (_page is not null) throw new InvalidOperationException("The actual Dev host has already been bound.");
                _page = samePage; _host = samePage.OriginalHost;
            }
            original.DemandPublication();
        }));
        return this;
    }

    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
        new(_work.RunAsync(original => CheckOriginalAsync(original, cancellationToken)));

    private async Task<CuiSceneAvailability> CheckOriginalAsync(DesktopOriginalWorkLifetime.Original original,
        CancellationToken caller)
    {
        var page = _page ?? throw new InvalidOperationException("The actual Dev host is not bound.");
        var host = _host ?? throw new InvalidOperationException("The actual Dev native host is not bound.");
        var generation = page.OriginalReadinessGeneration;
        original.BindPublicationGuard(() => page.IsOriginalReadinessPresentationCurrent(host, generation));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(original.Token, caller,
            _appLifetime, _windowLifetime);
        var token = lifetime.Token;
        Demand(original, token);
        var actor = await ReadActualActorAsync(original, token).ConfigureAwait(false);
        lock (_gate)
        {
            _originalActor ??= actor;
            if (_originalActor != actor) throw new UnauthorizedAccessException("The original current Task actor changed.");
        }
        var before = await ReadActualBindingAsync(original, token).ConfigureAwait(false);
        DemandObservedIdentity(before, actor);
        void OwnSource(Action callback) => InvokeSource(original, () => { Demand(original, token); callback(); return true; });
        var home = await original.AwaitAsync(InvokeSource(original, () =>
            WindowsHomeSameProcessRuntimeObservation.CheckAsync(_provider, _home, _appLifetime,
                _windowLifetime, OwnSource, () => Demand(original, token), token))).ConfigureAwait(false);
        if (await ReadActualActorAsync(original, token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The actual Task actor changed during Home compatibility observation.");
        var after = await ReadActualBindingAsync(original, token).ConfigureAwait(false);
        DemandObservedIdentity(after, actor);
        if (before.HomeActor != after.HomeActor ||
            before.CanonicalTask.PersistenceRevision != after.CanonicalTask.PersistenceRevision ||
            before.CanonicalTask.OwnerBinding != after.CanonicalTask.OwnerBinding ||
            before.Project.Reference != after.Project.Reference || before.Project.Root != after.Project.Root ||
            before.Project.Repository != after.Project.Repository)
            throw new InvalidOperationException("The original Task/project changed during compatibility observation; refresh it.");
        if (await ReadActualActorAsync(original, token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The actual Task actor changed before native publication.");
        Demand(original, token);
        var current = InvokeSource(original, () =>
            WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, home));
        Demand(original, token);
        return current;
    }

    private async Task<AuthenticatedResourceActor> ReadActualActorAsync(DesktopOriginalWorkLifetime.Original original,
        CancellationToken token)
    {
        Demand(original, token);
        var actual = InvokeSource(original, () => _actors.GetCurrentAsync(token).AsTask());
        var actor = await original.AwaitAsync(actual).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original actual Task profile is unavailable.");
        Demand(original, token);
        if (string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
            throw new InvalidDataException("The actual current Task actor has no authenticated profile binding.");
        return actor;
    }

    private async Task<AssistantDevelopmentCurrentObservation> ReadActualBindingAsync(
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        Demand(original, token);
        void Scope(Action callback) => InvokeSource(original, () => { Demand(original, token); callback(); return true; });
        void Retain(Task actual) { _ = original.AwaitAsync(actual); }
        var actual = InvokeSource(original, () => _owner.ValidateOriginalBindingWithinSourceAsync(
            _binding, _custody, Scope, Retain, token));
        var observed = await original.AwaitAsync(actual).ConfigureAwait(false);
        Demand(original, token);
        return observed;
    }

    private void DemandObservedIdentity(AssistantDevelopmentCurrentObservation observed, AuthenticatedResourceActor actor)
    {
        var homeActor = observed.HomeActor ??
            throw new UnauthorizedAccessException("The actual transferred source did not retain its Home owner observation.");
        if (string.IsNullOrWhiteSpace(homeActor.ActorId) || string.IsNullOrWhiteSpace(homeActor.ProfileId) ||
            string.IsNullOrWhiteSpace(homeActor.AuthenticationRevision))
            throw new UnauthorizedAccessException("The actual transferred source has no current Home owner observation.");
        lock (_gate)
        {
            _originalHomeActor ??= homeActor;
            if (_originalHomeActor != homeActor)
                throw new UnauthorizedAccessException("The original transferred Home owner changed.");
        }
        var task = observed.CanonicalTask;
        if (observed.TaskActor != actor || observed.Project.Reference != _binding.Project.Reference ||
            task.TaskId != _binding.CanonicalTask.TaskId || task.ExecutionId != _binding.CanonicalTask.ExecutionId ||
            task.ContextId != _binding.CanonicalTask.ContextId || task.ContextId != _binding.Conversation.Conversation.Id ||
            task.OwnerBinding is not { } owner || owner.ActorId != actor.ActorId || owner.ProfileId != actor.ProfileId ||
            owner.AccountId != actor.AccountId || owner.OrganisationId != actor.OrganisationId ||
            owner.AuthenticationRevision != actor.AuthenticationRevision)
            throw new UnauthorizedAccessException("The transferred actual Assistant project/Task/actor binding changed.");
    }

    private void Demand(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); original.DemandPublication();
        InvokeSource(original, () =>
        {
            if (_page is null || !ReferenceEquals(_host, _page.OriginalHost) ||
                !ReferenceEquals(_provider.GetService<IAssistantOriginalDevelopmentOwner>(), _owner) ||
                !ReferenceEquals(_provider.GetRequiredService<HostLocalTaskActorSource>(), _actors))
                throw new UnauthorizedAccessException("The actual native Assistant development composition changed.");
            return true;
        });
        token.ThrowIfCancellationRequested(); original.DemandPublication();
    }

    private T InvokeSource<T>(DesktopOriginalWorkLifetime.Original original, Func<T> source)
    {
        var physical = _physical ??= []; physical.Add(this);
        try { return source(); }
        catch (Exception failure)
        {
            original.Retain(failure);
            if (failure is OperationCanceledException) throw new AggregateException("Actual native readiness source faulted synchronously.", failure);
            throw;
        }
        finally { physical.RemoveAt(physical.Count - 1); }
    }

    private void InvokeCapturedSource(Action source)
    {
        var physical = _physical ??= []; physical.Add(this);
        try { source(); }
        finally { physical.RemoveAt(physical.Count - 1); }
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physical?.Any(actual => ReferenceEquals(actual, this)) == true)
            throw new InvalidOperationException("An actual Assistant readiness source cannot join its own encompassing drain.");
        _work.DemandExternalClose(); _custody.DemandExternalOriginalRetirementJoin();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task StopOriginalAsync()
    {
        List<Exception> failures = [];
        try { _custody.RequestRetirement(); }
        catch (Exception failure) { failures.Add(failure); }
        try { _custodyClose ??= _custody.CloseAndDrainAsync(); }
        catch (Exception failure) { failures.Add(failure); }
        if (_custodyClose is not null)
            try { await _custodyClose.ConfigureAwait(false); }
            catch (Exception failure)
            {
                if (_custodyClose.Exception is { InnerExceptions.Count: > 0 } group) failures.AddRange(group.InnerExceptions);
                else failures.Add(failure);
            }
        var distinct = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (distinct.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(distinct[0]).Throw();
        if (distinct.Length > 1) throw new AggregateException("The actual transferred Assistant Dev custody did not drain cleanly.", distinct);
    }
}

#endif
