using Haven.Core;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Haven.Application;

/// <summary>Discovers one coherent capability catalogue for the current platform, including transient connection-backed capabilities.</summary>
public sealed class CapabilityRegistryService(ICapabilityRepository repository, IEnumerable<IDynamicCapabilityProvider>? dynamicProviders = null)
{
    private readonly object _originalIssuer = new();
    private readonly ConditionalWeakTable<CapabilityOriginalCatalogueObservation, CapabilityOriginalCatalogueObservation> _originalCatalogues = new();
    private readonly object _originalDynamicGate = new();
    private IReadOnlyList<ICapabilityOriginalDynamicReadSource>? _originalDynamicSources;
    private bool _originalDiscoveryStarted;
    public IReadOnlyList<ICapabilityOriginalDynamicReadSource> OriginalDynamicReadSources
    { get { lock (_originalDynamicGate) return _originalDynamicSources ?? Array.AsReadOnly(Array.Empty<ICapabilityOriginalDynamicReadSource>()); } }

    /// <summary>One-time actual composition only. Sources observe metadata for the
    /// SAME configured maintained providers; this grants no store or runtime authority.</summary>
    public void BindOriginalDynamicReadSources(IReadOnlyList<ICapabilityOriginalDynamicReadSource> actualSources)
    {
        ArgumentNullException.ThrowIfNull(actualSources);
        if (actualSources.Count > 64 || actualSources.Any(value => value is null) ||
            actualSources.Distinct(ReferenceEqualityComparer.Instance).Count() != actualSources.Count)
            throw new ArgumentException("Use bounded distinct actual protected dynamic sources.", nameof(actualSources));
        var captured = Array.AsReadOnly(actualSources.ToArray());
        lock (_originalDynamicGate)
        {
            if (_originalDiscoveryStarted || _originalDynamicSources is not null)
                throw new InvalidOperationException("Original dynamic composition is immutable after binding or discovery.");
            _originalDynamicSources = captured;
        }
    }
    public bool HasOriginalRepository(ICapabilityRepository sameActual) => ReferenceEquals(repository, sameActual);
    public bool IsIssuedOriginalCatalogue(CapabilityOriginalCatalogueObservation sameActual) => sameActual is not null &&
        ReferenceEquals(sameActual.Issuer, _originalIssuer) && _originalCatalogues.TryGetValue(sameActual, out var issued) &&
        ReferenceEquals(sameActual, issued);

    /// <summary>Actual source-scoped READ discovery. This path never calls public
    /// repository GetCapabilities/Seed and never initializes an absent database/table.</summary>
    public async Task<CapabilityOriginalCatalogueObservation> DiscoverWithinOriginalSourceAsync(
        ICapabilityOriginalRepositoryReadSource actualSource, AuthenticatedResourceActor actualActor,
        CapabilityPlatform platform, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (platform is not (CapabilityPlatform.Windows or CapabilityPlatform.Android or CapabilityPlatform.Linux))
            throw new ArgumentOutOfRangeException(nameof(platform));
        OriginalInvoke(scope, () =>
        {
            if (!actualSource.HasOriginalCapabilityRepository(repository))
                throw new UnauthorizedAccessException("The SAME configured protected capability repository source is required.");
            return true;
        });
        var observed = await OriginalRead(() => actualSource.ReadOriginalCapabilitiesWithinSourceAsync(
            actualActor, scope, retain, token), scope, retain).ConfigureAwait(false);
        var items = OriginalInvoke(scope, () =>
        {
            if (!actualSource.IsIssuedOriginalRepositoryObservation(observed) || observed.Actor != actualActor)
                throw new UnauthorizedAccessException("The actual capability READ source returned a foreign actor observation.");
            if (observed.State == CapabilityOriginalCatalogueState.SetupRequired)
            {
                if (observed.Definitions.Count != 0) throw new InvalidDataException("An unavailable source cannot disclose capability rows.");
                return new List<CapabilityDefinition>();
            }
            if (observed.State != CapabilityOriginalCatalogueState.Available)
                throw new InvalidDataException("The actual source returned an unknown capability state.");
            return new List<CapabilityDefinition>(observed.Definitions);
        });
        var configuredSources = OriginalInvoke(scope, () =>
        {
            lock (_originalDynamicGate)
            {
                _originalDiscoveryStarted = true;
                return _originalDynamicSources ?? Array.AsReadOnly(Array.Empty<ICapabilityOriginalDynamicReadSource>());
            }
        });
        var providers = OriginalInvoke(scope, () =>
        {
            var values = (dynamicProviders ?? []).Take(65).ToArray();
            if (values.Length > 64 || values.Any(value => value is null) ||
                values.Distinct(ReferenceEqualityComparer.Instance).Count() != values.Length)
                throw new InvalidDataException("The actual maintained dynamic provider set is invalid or exceeds its bound.");
            return values;
        });
        var dynamicOriginals = new List<(ICapabilityOriginalDynamicReadSource Source, ICapabilityOriginalRepositoryObservation Observation)>();
        var unavailableDynamic = false;
        foreach (var provider in providers)
        {
            var matches = OriginalInvoke(scope, () => configuredSources.Where(source => source.HasOriginalDynamicProvider(provider)).ToArray());
            if (matches.Length > 1) throw new InvalidOperationException("The SAME maintained provider has multiple protected READ producers.");
            if (matches.Length == 0 || observed.State != CapabilityOriginalCatalogueState.Available)
            { unavailableDynamic = true; continue; }
            var dynamicSource = matches[0];
            var metadata = await OriginalRead(() => dynamicSource.ReadOriginalDynamicCapabilitiesWithinSourceAsync(
                actualActor, platform, scope, retain, token), scope, retain).ConfigureAwait(false);
            OriginalInvoke(scope, () =>
            {
                if (!dynamicSource.IsIssuedOriginalDynamicObservation(metadata) || metadata.Actor != actualActor)
                    throw new UnauthorizedAccessException("The actual configured dynamic READ producer returned a foreign observation.");
                if (metadata.Definitions.Count > 1024) throw new InvalidDataException("The actual dynamic capability metadata exceeds its bound.");
                if (metadata.State == CapabilityOriginalCatalogueState.SetupRequired)
                {
                    if (metadata.Definitions.Count != 0) throw new InvalidDataException("An unavailable dynamic source cannot disclose rows.");
                    unavailableDynamic = true;
                }
                else if (metadata.State == CapabilityOriginalCatalogueState.Available) items.AddRange(metadata.Definitions);
                else throw new InvalidDataException("The actual dynamic source returned an unknown state.");
                dynamicOriginals.Add((dynamicSource, metadata)); return true;
            });
        }
        // No ordinary provider GetCapabilities/ReadWriteCreate call occurs here.
        // Revalidate ALL actual source windows before publishing joined metadata.
        foreach (var original in dynamicOriginals)
            await OriginalRead(() => original.Source.RevalidateOriginalDynamicObservationWithinSourceAsync(
                original.Observation, actualActor, scope, retain, token), scope, retain).ConfigureAwait(false);
        await OriginalRead(() => actualSource.RevalidateOriginalRepositoryObservationWithinSourceAsync(
            observed, actualActor, scope, retain, token), scope, retain).ConfigureAwait(false);
        var result = OriginalInvoke(scope, () => new CapabilityOriginalCatalogueObservation(_originalIssuer, actualSource,
            observed, platform, Array.AsReadOnly(items.Where(item => item.IsEnabled && item.Platforms.HasFlag(platform))
                .GroupBy(item => item.Id).Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
                .OrderBy(item => item.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => item.OwnerAppKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()), unavailableDynamic));
        OriginalInvoke(scope, () => { _originalCatalogues.Add(result, result); return true; }); return result;
    }
    private static T OriginalInvoke<T>(Action<Action> scope, Func<T> body)
    {
        var phase = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
        var errors = new List<Exception>(); Exception? protocol = null; T result = default!;
        try
        {
            scope(() =>
            {
                if (Volatile.Read(ref phase) == 0 || thread != Environment.CurrentManagedThreadId ||
                    Interlocked.CompareExchange(ref used, 1, 0) != 0)
                {
                    Interlocked.CompareExchange(ref protocol, new InvalidOperationException("The original catalogue callback expired, repeated or changed thread."), null);
                    throw protocol!;
                }
                try { result = body(); } catch (Exception cause) { AddOriginal(errors, cause); throw; }
            });
            if (used != 1) AddOriginal(errors, new InvalidOperationException("The original catalogue callback did not run."));
        }
        catch (Exception cause) { AddOriginal(errors, cause); }
        finally { Volatile.Write(ref phase, 0); }
        if (protocol is not null) AddOriginal(errors, protocol);
        ThrowOriginal(errors); return result;
    }
    private static async Task<T> OriginalRead<T>(Func<Task<T>> factory, Action<Action> scope, Action<Task> retain)
    {
        Task<T>? actual = null; var errors = new List<Exception>(); T value = default!;
        try { OriginalInvoke(scope, () => { actual = factory() ?? throw new InvalidOperationException("No actual catalogue source Task returned."); retain(actual); return true; }); }
        catch (Exception cause) { AddOriginal(errors, cause); }
        if (actual is not null)
            try { value = await actual.ConfigureAwait(false); }
            catch (Exception cause) { foreach (var direct in actual.Exception?.InnerExceptions.ToArray() ?? [cause]) AddOriginal(errors, direct); }
        else if (errors.Count == 0) AddOriginal(errors, new InvalidOperationException("No original catalogue source was acquired."));
        ThrowOriginal(errors); return value;
    }
    private static async Task OriginalRead(Func<Task> factory, Action<Action> scope, Action<Task> retain)
    {
        Task? actual = null; var errors = new List<Exception>();
        try { OriginalInvoke(scope, () => { actual = factory() ?? throw new InvalidOperationException("No actual catalogue source Task returned."); retain(actual); return true; }); }
        catch (Exception cause) { AddOriginal(errors, cause); }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { foreach (var direct in actual.Exception?.InnerExceptions.ToArray() ?? [cause]) AddOriginal(errors, direct); }
        else if (errors.Count == 0) AddOriginal(errors, new InvalidOperationException("No original catalogue source was acquired."));
        ThrowOriginal(errors);
    }
    private static void AddOriginal(List<Exception> errors, Exception cause)
    { if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
    private static void ThrowOriginal(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original catalogue source/callback failed.", errors);
    }
    public async Task<IReadOnlyList<CapabilityDefinition>> DiscoverAsync(
        CapabilityPlatform platform,
        CancellationToken cancellationToken)
    {
        if (platform is not (CapabilityPlatform.Windows or CapabilityPlatform.Android or CapabilityPlatform.Linux))
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Select one current host platform.");

        var items = new List<CapabilityDefinition>(await repository.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false));
        if (dynamicProviders is not null)
            foreach (var provider in dynamicProviders)
                items.AddRange(await provider.GetCapabilitiesAsync(platform, cancellationToken).ConfigureAwait(false));

        return items
            .Where(item => item.IsEnabled && item.Platforms.HasFlag(platform))
            .GroupBy(item => item.Id)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderBy(item => item.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.OwnerAppKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
