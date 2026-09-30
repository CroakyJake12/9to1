using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Haven.Application.Go;

/// <summary>Query-time discovery of owner references. Provider failures and latency never hold back another provider.</summary>
public sealed class GoService(IEnumerable<IGoProvider> providers, TimeSpan? providerDeadline = null)
{
    private readonly IGoProvider[] _providers = providers.ToArray();
    private readonly TimeSpan _deadline = providerDeadline ?? TimeSpan.FromSeconds(10);
    public async IAsyncEnumerable<GoUpdate> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct = default)
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
            .Select(provider => RunAsync(provider, query, channel.Writer, lifetime.Token)).ToArray();
        var completion = CompleteAsync(runs, channel.Writer);
        try { await foreach (var update in channel.Reader.ReadAllAsync(ct)) yield return update; await completion; }
        finally { lifetime.Cancel(); await completion; }
    }
    private async Task RunAsync(IGoProvider provider, GoQuery query, ChannelWriter<GoUpdate> writer, CancellationToken ct)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); request.CancelAfter(_deadline);
        var pump = Task.Run(async () =>
        {
            var count = 0; var seen = new HashSet<GoCanonicalReference>();
            await foreach (var result in provider.QueryAsync(query, request.Token).WithCancellation(request.Token))
            {
                request.Token.ThrowIfCancellationRequested();
                if (result is null || result.ProviderId != provider.ProviderId || result.Reference is null ||
                    string.IsNullOrWhiteSpace(result.Reference.Owner) || string.IsNullOrWhiteSpace(result.Reference.Kind) || string.IsNullOrWhiteSpace(result.Reference.Id) ||
                    string.IsNullOrWhiteSpace(result.Reference.Revision) || result.Reference.Owner.Length > 4096 || result.Reference.Kind.Length > 256 || result.Reference.Id.Length > 4096 || result.Reference.Revision.Length > 4096 ||
                    string.IsNullOrWhiteSpace(result.Label) || result.Label.Length > 4096 || string.IsNullOrWhiteSpace(result.Category) || result.Category.Length > 128 || result.Actions is null || result.Actions.Count > 64 ||
                    result.Actions.Any(a => a is null || string.IsNullOrWhiteSpace(a.Id) || a.Id.Length > 256 || string.IsNullOrWhiteSpace(a.Label) || a.Label.Length > 256) || result.Actions.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != result.Actions.Count)
                    throw new InvalidDataException("Provider returned an invalid canonical result.");
                if (!seen.Add(result.Reference)) continue;
                if (!Includes(query.Scope?.Owners, result.Reference.Owner) || !Includes(query.Scope?.Kinds, result.Reference.Kind) ||
                    query.Category is { } category && result.Category != category) continue;
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
        scope = Snapshot(scope);
        if (!Includes(scope?.ProviderIds, result.ProviderId) || !Includes(scope?.Owners, result.Reference.Owner) ||
            !Includes(scope?.Kinds, result.Reference.Kind) || !Includes(scope?.ActionIds, actionId) || !result.Actions.Any(a => a.Id == actionId))
            throw new UnauthorizedAccessException("The result or owner action is outside this Go scope.");
        var owner = _providers.SingleOrDefault(p => p.ProviderId == result.ProviderId) ?? throw new InvalidOperationException("The canonical provider is unavailable.");
        // Scope/result fields are not grants. The owner must re-resolve identity/revision and authenticate/authorize now.
        return owner.InvokeAsync(result.Reference, actionId, ct);
    }
    private static bool Includes(IReadOnlySet<string>? allowed, string value) => allowed is null || allowed.Contains(value);
    private static GoScope? Snapshot(GoScope? scope)
    {
        if (scope is null) return null;
        static IReadOnlySet<string>? Copy(IReadOnlySet<string>? values)
        {
            if (values is null) return null;
            if (values.Count > 256 || values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 4096))
                throw new ArgumentException("Go scope identifiers must be bounded and explicit.");
            return values.ToFrozenSet(StringComparer.Ordinal);
        }
        return new(Copy(scope.ProviderIds), Copy(scope.Owners), Copy(scope.Kinds), Copy(scope.ActionIds));
    }
}
