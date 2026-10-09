using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

// Maintained controlled synchronous leaf factories, with exact finite caller phase and raw
// capture. These do not stand in for the genuine native Llama/HostLocal source implementation.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    private static Task<T> OriginalFixtureFactory<T>(Func<Task<T>> factory, Action<Action> scope, Action<Task> retain)
    {
        Task<T>? raw = null; var errors = new List<Exception>(); var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
        void Add(Exception error) { lock (errors) if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error); }
        try
        {
            scope(() =>
            {
                if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                { var refusal = new InvalidOperationException("The actual controlled source callback retired or was reused."); Add(refusal); throw refusal; }
                try { raw = factory(); retain(raw); } catch (Exception error) { Add(error); throw; }
            });
            if (Volatile.Read(ref used) == 0) Add(new InvalidOperationException("The actual controlled source callback was not invoked."));
        }
        catch (Exception error) { Add(error); }
        finally { Interlocked.Exchange(ref active, 0); }
        Exception[] actualErrors; lock (errors) actualErrors = errors.ToArray();
        if (actualErrors.Length != 0) throw new AggregateException("Actual controlled source/caller failure.", actualErrors);
        return raw ?? throw new InvalidOperationException("No actual controlled source Task returned.");
    }
    private sealed partial class Actors : ITaskRunOriginalTaskActorObservationSource
    {
        public Task<AuthenticatedResourceActor?> GetOriginalCurrentWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            OriginalFixtureFactory(() => GetCurrentAsync(token).AsTask(), scope, retain);
    }
    private sealed partial class Provider : ITaskRunOriginalProviderCatalogueSource
    {
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalTaskSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            OriginalFixtureFactory(() => GetModelsAsync(token), scope, retain);
    }
    private sealed partial class PolicyProvider : ITaskRunOriginalProviderCatalogueSource
    {
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalTaskSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            OriginalFixtureFactory(() => GetModelsAsync(token), scope, retain);
    }
    private sealed partial class InferenceLocalMetadataProvider : ITaskRunOriginalProviderCatalogueSource
    {
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalTaskSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            OriginalFixtureFactory(() => GetModelsAsync(token), scope, retain);
    }
}
