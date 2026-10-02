using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Haven.Application.Go;

/// <summary>Query-time discovery of owner references. Provider failures and latency never hold back another provider.</summary>
public sealed class GoService(IEnumerable<IGoProvider> providers, TimeSpan? providerDeadline = null)
{
    private readonly IGoProvider[] _providers = providers.ToArray();
    private readonly TimeSpan _deadline = providerDeadline ?? TimeSpan.FromSeconds(10);
    public IAsyncEnumerable<GoUpdate> QueryAsync(GoQuery query, CancellationToken ct = default)
        => QueryCoreAsync(query, null, ct);

    public IAsyncEnumerable<GoUpdate> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor expectedActor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return QueryCoreAsync(query, expectedActor, ct);
    }

    private async IAsyncEnumerable<GoUpdate> QueryCoreAsync(GoQuery query, AuthenticatedResourceActor? expectedActor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (query.Text is null || query.Text.Length > 4096 || query.Limit is < 1 or > 1000 || query.Category?.Length > 128 ||
            _providers.Any(p => string.IsNullOrWhiteSpace(p.ProviderId)) || _providers.Select(p => p.ProviderId).Distinct(StringComparer.Ordinal).Count() != _providers.Length)
            throw new ArgumentException("A bounded query and uniquely registered canonical providers are required.");
        if (_deadline <= TimeSpan.Zero || _deadline > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(providerDeadline));
        var scope = Snapshot(query.Scope);
        query = query with { Scope = scope };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var channel = Channel.CreateBounded<GoUpdate>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        // Omitted providers are never called: narrowing prevents even their discovery metadata from being fetched.
        var runs = _providers.Where(provider => Includes(scope?.ProviderIds, provider.ProviderId))
            .Select(provider => RunAsync(provider, query, expectedActor, channel.Writer, lifetime.Token)).ToArray();
        var completion = CompleteAsync(runs, channel.Writer);
        try { await foreach (var update in channel.Reader.ReadAllAsync(ct)) yield return update; await completion; }
        finally { lifetime.Cancel(); await completion; }
    }
    private async Task RunAsync(IGoProvider provider, GoQuery query, AuthenticatedResourceActor? expectedActor, ChannelWriter<GoUpdate> writer, CancellationToken ct)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); request.CancelAfter(_deadline);
        var pump = Task.Run(async () =>
        {
            var count = 0; var seen = new HashSet<GoCanonicalReference>();
            var results = expectedActor is null ? provider.QueryAsync(query, request.Token)
                : provider is IGoOriginalActorQuery originalOwner
                    ? originalOwner.QueryForActorAsync(query, expectedActor, request.Token)
                    : throw new UnauthorizedAccessException("The owner cannot admit original-session discovery.");
            await foreach (var returnedResult in results.WithCancellation(request.Token))
            {
                request.Token.ThrowIfCancellationRequested();
                var result = ValidateResult(provider, returnedResult);
                if (!Includes(query.Scope?.Owners, result.Reference.Owner) || !Includes(query.Scope?.Kinds, result.Reference.Kind) ||
                    query.Category is { } category && result.Category != category) continue;
                // Only admitted results enter the deduplication set: excluded owner metadata cannot grow it.
                // Each new member is counted toward the bounded query limit below.
                if (!seen.Add(result.Reference)) continue;
                var scopedResult = result with { Actions = result.Actions.Where(a => Includes(query.Scope?.ActionIds, a.Id)).ToArray() };
                await writer.WriteAsync(new(provider.ProviderId, scopedResult, false, null), request.Token);
                if (++count == query.Limit) break;
            }
        }, ct);
        try
        {
            // A provider that ignores cancellation still cannot keep the Go query open indefinitely.
            await pump.WaitAsync(request.Token);
            await writer.WriteAsync(new(provider.ProviderId, null, true, null), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (OperationCanceledException) { await writer.WriteAsync(new(provider.ProviderId, null, true, "Provider timed out"), ct); }
        catch (Exception)
        { await writer.WriteAsync(new(provider.ProviderId, null, true, "Provider unavailable; its existing data was preserved"), ct); }
        finally { request.Cancel(); _ = pump.ContinueWith(task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default); }
    }
    private static async Task CompleteAsync(Task[] runs, ChannelWriter<GoUpdate> writer)
    { try { await Task.WhenAll(runs); writer.TryComplete(); } catch (Exception ex) { writer.TryComplete(ex); } }
    public Task InvokeAsync(GoResult result, string actionId, CancellationToken ct = default)
        => InvokeAsync(result, actionId, null, ct);
    public Task InvokeAsync(GoResult result, string actionId, GoScope? scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var owner = _providers.SingleOrDefault(p => p.ProviderId == result.ProviderId) ?? throw new InvalidOperationException("The canonical provider is unavailable.");
        result = ValidateResult(owner, result);
        scope = Snapshot(scope);
        if (!Includes(scope?.ProviderIds, result.ProviderId) || !Includes(scope?.Owners, result.Reference.Owner) ||
            !Includes(scope?.Kinds, result.Reference.Kind) || !Includes(scope?.ActionIds, actionId) || !result.Actions.Any(a => a.Id == actionId))
            throw new UnauthorizedAccessException("The result or owner action is outside this Go scope.");
        // Scope/result fields are not grants. The owner must re-resolve identity/revision and authenticate/authorize now.
        return owner.InvokeAsync(result.Reference, actionId, ct);
    }
    /// <summary>Original-session invocation. Actor metadata is not a grant; the owning provider must recheck it at final admission.</summary>
    public Task InvokeForActorAsync(GoResult result, string actionId, AuthenticatedResourceActor expectedActor,
        GoScope? scope = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        ArgumentNullException.ThrowIfNull(result);
        var provider = _providers.SingleOrDefault(p => p.ProviderId == result.ProviderId)
            ?? throw new InvalidOperationException("The canonical provider is unavailable.");
        result = ValidateResult(provider, result);
        scope = Snapshot(scope);
        if (!Includes(scope?.ProviderIds, result.ProviderId) || !Includes(scope?.Owners, result.Reference.Owner) ||
            !Includes(scope?.Kinds, result.Reference.Kind) || !Includes(scope?.ActionIds, actionId) || !result.Actions.Any(a => a.Id == actionId))
            throw new UnauthorizedAccessException("The result or owner action is outside this Go scope.");
        if (provider is not IGoOriginalActorInvocation owner)
            throw new UnauthorizedAccessException("The owner cannot admit an original-session invocation.");
        return owner.InvokeForActorAsync(result.Reference, actionId, expectedActor, ct);
    }
    public Task<GoResult?> ResolveAsync(string providerId, GoCanonicalLocator locator, GoScope? scope = null, CancellationToken ct = default)
        => ResolveCoreAsync(providerId, locator, null, scope, ct);

    public Task<GoResult?> ResolveForActorAsync(string providerId, GoCanonicalLocator locator,
        AuthenticatedResourceActor expectedActor, GoScope? scope = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return ResolveCoreAsync(providerId, locator, expectedActor, scope, ct);
    }

    private async Task<GoResult?> ResolveCoreAsync(string providerId, GoCanonicalLocator locator,
        AuthenticatedResourceActor? expectedActor, GoScope? scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(locator);
        scope = Snapshot(scope);
        if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 4096 ||
            string.IsNullOrWhiteSpace(locator.Owner) || locator.Owner.Length > 4096 ||
            string.IsNullOrWhiteSpace(locator.Kind) || locator.Kind.Length > 256 ||
            string.IsNullOrWhiteSpace(locator.Id) || locator.Id.Length > 4096)
            throw new ArgumentException("A bounded canonical owner locator is required.");
        if (!Includes(scope?.ProviderIds, providerId) || !Includes(scope?.Owners, locator.Owner) || !Includes(scope?.Kinds, locator.Kind))
            throw new UnauthorizedAccessException("This retained identity is outside the current Go scope.");
        if (_providers.Any(p => string.IsNullOrWhiteSpace(p.ProviderId)) || _providers.Select(p => p.ProviderId).Distinct(StringComparer.Ordinal).Count() != _providers.Length)
            throw new InvalidOperationException("Canonical providers must be uniquely registered.");
        var owner = _providers.SingleOrDefault(p => p.ProviderId == providerId);
        if (expectedActor is null && owner is not IGoCanonicalResolver) return null;
        if (expectedActor is not null && owner is not IGoOriginalActorCanonicalResolver)
            throw new UnauthorizedAccessException("The owner cannot admit original-session resolution.");
        if (_deadline <= TimeSpan.Zero || _deadline > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(providerDeadline));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); request.CancelAfter(_deadline);
        var pending = Task.Run(() => expectedActor is null
            ? ((IGoCanonicalResolver)owner!).ResolveAsync(locator, request.Token)
            : ((IGoOriginalActorCanonicalResolver)owner!).ResolveForActorAsync(locator, expectedActor, request.Token), request.Token);
        try
        {
            var result = await pending.WaitAsync(request.Token);
            if (result is null) return null;
            result = ValidateResult(owner!, result);
            if (result.Reference.Owner != locator.Owner || result.Reference.Kind != locator.Kind || result.Reference.Id != locator.Id)
                throw new InvalidDataException("The owner returned a different canonical identity.");
            return result with { Actions = result.Actions.Where(action => Includes(scope?.ActionIds, action.Id)).ToArray() };
        }
        finally
        {
            request.Cancel();
            _ = pending.ContinueWith(task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
    private static GoResult ValidateResult(IGoProvider provider, GoResult result)
    {
        if (result is null || result.ProviderId != provider.ProviderId || result.Reference is null ||
            string.IsNullOrWhiteSpace(result.Reference.Owner) || string.IsNullOrWhiteSpace(result.Reference.Kind) || string.IsNullOrWhiteSpace(result.Reference.Id) ||
            string.IsNullOrWhiteSpace(result.Reference.Revision) || result.Reference.Owner.Length > 4096 || result.Reference.Kind.Length > 256 || result.Reference.Id.Length > 4096 || result.Reference.Revision.Length > 4096 ||
            string.IsNullOrWhiteSpace(result.Label) || result.Label.Length > 4096 || string.IsNullOrWhiteSpace(result.Category) || result.Category.Length > 128 || result.Actions is null)
            throw new InvalidDataException("Provider returned an invalid canonical result.");
        var actions = new List<GoAction>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in result.Actions)
        {
            if (actions.Count == 64 || action is null || string.IsNullOrWhiteSpace(action.Id) || action.Id.Length > 256 ||
                string.IsNullOrWhiteSpace(action.Label) || action.Label.Length > 256 || !ids.Add(action.Id))
                throw new InvalidDataException("Provider returned an invalid canonical result.");
            actions.Add(action);
        }
        return result with { Actions = actions.ToArray() };
    }
    private static bool Includes(IReadOnlySet<string>? allowed, string value) => allowed is null || allowed.Contains(value);
    private static GoScope? Snapshot(GoScope? scope)
    {
        if (scope is null) return null;
        static IReadOnlySet<string>? Copy(IReadOnlySet<string>? values)
        {
            if (values is null) return null;
            var detached = new List<string>();
            foreach (var value in values)
            {
                if (detached.Count == 256 || string.IsNullOrWhiteSpace(value) || value.Length > 4096)
                    throw new ArgumentException("Go scope identifiers must be bounded and explicit.");
                detached.Add(value);
            }
            return detached.ToFrozenSet(StringComparer.Ordinal);
        }
        return new(Copy(scope.ProviderIds), Copy(scope.Owners), Copy(scope.Kinds), Copy(scope.ActionIds));
    }
}
