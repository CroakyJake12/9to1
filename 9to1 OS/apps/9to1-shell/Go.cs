using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Haven.Application;

namespace NineToOne.Os.Shell;

public sealed record GoQuery(string Text, string? Category = null, int Limit = 100);
public sealed record GoCanonicalReference(string Owner, string Kind, string Id, string Revision);
public sealed record GoAction(string Id, string Label);
public sealed record GoResult(string ProviderId, GoCanonicalReference Reference, string Label, string Category, IReadOnlyList<GoAction> Actions);
public sealed record GoUpdate(string ProviderId, GoResult? Result, bool Complete, string? Failure);
public interface IGoProvider
{
    string ProviderId { get; }
    IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, CancellationToken cancellationToken);
    Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken cancellationToken);
}

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
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var channel = Channel.CreateBounded<GoUpdate>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        var runs = _providers.Select(provider => RunAsync(provider, query, channel.Writer, lifetime.Token)).ToArray();
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
                await writer.WriteAsync(new(provider.ProviderId, result, false, null), request.Token);
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
    {
        if (!result.Actions.Any(a => a.Id == actionId)) throw new UnauthorizedAccessException("The result does not declare this owner action.");
        var owner = _providers.SingleOrDefault(p => p.ProviderId == result.ProviderId) ?? throw new InvalidOperationException("The canonical provider is unavailable.");
        return owner.InvokeAsync(result.Reference, actionId, ct);
    }
}

public sealed class InstalledApplicationsGoProvider(IInstalledApplicationRegistry registry, LinuxApplicationLauncher launcher) : IGoProvider
{
    public string ProviderId => "os.installed-applications";
    public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        if (query.Category is not (null or "Apps")) yield break;
        foreach (var app in (await registry.RefreshAsync(ct)).OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.ApplicationId))
        {
            ct.ThrowIfCancellationRequested();
            if (!app.Enabled || !app.ProfileAccessible || app.ProviderId != "linux.xdg-desktop" || !app.Label.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
            yield return new(ProviderId, new("Home", "os.installed-application", app.ApplicationId.ToString("D"), app.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), app.Label, "Apps", [new("Open", "Open")]);
        }
    }
    public Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct)
    {
        if (reference.Owner != "Home" || reference.Kind != "os.installed-application" || actionId != "Open" || !Guid.TryParse(reference.Id, out var id) || !long.TryParse(reference.Revision, out var revision))
            throw new UnauthorizedAccessException("Unknown canonical application action.");
        return launcher.LaunchAsync(id, revision, ct);
    }
}
