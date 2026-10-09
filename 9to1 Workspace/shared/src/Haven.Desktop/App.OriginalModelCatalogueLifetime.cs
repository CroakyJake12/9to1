#if !ANDROID
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private readonly object _actualModelCatalogueGate = new();
    private readonly object _actualModelCatalogueSourceOwner = new();
    private ModelProviderRegistry? _actualModelCatalogueRegistry;
    private CloudflareOriginalTaskLedger? _actualModelCatalogueCloseSources;
    private Task? _actualModelCatalogueAndDenClose;
    private Task? _actualModelCatalogueClose, _actualModelCatalogueDenClose;

    private ModelProviderRegistry RetainOriginalModelCatalogueRegistry(IServiceProvider provider)
    {
        ModelProviderRegistry? captured = null;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            var configured = _services ?? throw new InvalidOperationException("The actual configured App provider is unavailable.");
            // Microsoft DI passes its SAME canonical root scope into singleton
            // factories; the App retains the outer ServiceProvider object.
            if (!ReferenceEquals(provider, configured.GetRequiredService<IServiceProvider>()))
                throw new UnauthorizedAccessException("Use the SAME actual configured App model registry.");
            var actual = provider.GetRequiredService<IModelProviderRegistry>() as ModelProviderRegistry
                ?? throw new InvalidOperationException("The maintained original model catalogue registry is unavailable.");
            lock (_actualModelCatalogueGate)
            {
                if (_actualModelCatalogueAndDenClose is not null ||
                    _actualModelCatalogueRegistry is { } previous && !ReferenceEquals(previous, actual))
                    throw new UnauthorizedAccessException("The actual model registry cannot be replaced or acquired during retirement.");
                _actualModelCatalogueRegistry = captured = actual;
            }
            original.DemandPublication();
            return true;
        }));
        return captured ?? throw new InvalidOperationException("No actual configured model registry was retained.");
    }

    private void DemandOriginalModelCatalogueRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(_actualModelCatalogueSourceOwner);
        _actualModelCatalogueRegistry?.ThrowIfOriginalCatalogueJoinWouldCycle();
    }

    // The existing owning App calls this phase only after its actual business,
    // presentation, native window and developer borrowers have independently joined.
    // Per-view bridges never retire the shared provider registry.
    private Task JoinOriginalModelCataloguesThenDenAsync()
    {
        DemandOriginalModelCatalogueRetirementJoin();
        var registry = _actualModelCatalogueRegistry;
        if (registry is null) return _actualAssistantPersonalDen?.CloseAndDrainAsync() ?? Task.CompletedTask;
        TaskCompletionSource? begin = null; Task actual;
        lock (_actualModelCatalogueGate)
        {
            if (_actualModelCatalogueAndDenClose is null)
            {
                _actualModelCatalogueCloseSources = new();
                _actualModelCatalogueCloseSources.BindOriginalOwner(_actualModelCatalogueSourceOwner);
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _actualModelCatalogueAndDenClose = Close(begin.Task, _actualModelCatalogueCloseSources);
            }
            actual = _actualModelCatalogueAndDenClose;
        }
        begin?.SetResult(); return actual;

        async Task Close(Task start, CloudflareOriginalTaskLedger sources)
        {
            await start.ConfigureAwait(false);
            using var own = CloudflareOriginalExecutionGuard.EnterOriginal(_actualModelCatalogueSourceOwner);
            var healthy = await Join(registry.CloseOriginalCataloguesAndDrainAsync,
                raw => _actualModelCatalogueClose = raw).ConfigureAwait(false);
            // Unknown catalogue originals retain their complete dependency chain.
            // A terminal Task alone is never a successful independent join.
            if (healthy)
                await Join(() => _actualAssistantPersonalDen?.CloseAndDrainAsync() ?? Task.CompletedTask,
                    raw => _actualModelCatalogueDenClose = raw).ConfigureAwait(false);
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (sources.OriginalErrors.Count != 0)
                throw new AggregateException("Actual configured model catalogue then Den retirement failed and remains retained.", sources.OriginalErrors);

            async Task<bool> Join(Func<Task> acquire, Action<Task> capture)
            {
                Task? raw = null; var joined = true;
                try
                {
                    _originalAppWork.RunCloseCallback(() => sources.Invoke(() =>
                    { raw = acquire(); _ = sources.Track(raw); capture(raw); return true; }));
                }
                catch (Exception cause) { joined = false; sources.Retain(cause); }
                if (raw is not null)
                    try { await sources.AwaitAsync(raw).ConfigureAwait(false); }
                    catch (Exception cause) { joined = false; sources.Capture(raw, cause); }
                return raw is not null && joined;
            }
        }
    }
}
#endif
