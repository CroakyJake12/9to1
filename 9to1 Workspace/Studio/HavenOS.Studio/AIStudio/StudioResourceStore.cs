using System.Text.Json;

namespace HavenOS.AIStudio;

public interface IStudioResourceStore
{
    Task<IReadOnlyList<StudioResource>> ListAsync(StudioResourceKind? kind, CancellationToken cancellationToken);
    Task<StudioResource?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<EvaluationRun>> ListEvaluationRunsAsync(Guid suiteId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TestRun>> ListTestRunsAsync(Guid suiteId, CancellationToken cancellationToken);
    Task WriteResourceAsync(StudioResource resource, CancellationToken cancellationToken);
    Task WriteEvaluationRunAsync(EvaluationRun run, CancellationToken cancellationToken);
    Task WriteTestRunAsync(TestRun run, CancellationToken cancellationToken);
}

public sealed record StudioResourceStoreDocument(int SchemaVersion, IReadOnlyList<StudioResource> Resources,
    IReadOnlyList<EvaluationRun> EvaluationRuns, IReadOnlyList<TestRun> TestRuns);

/// <summary>Durable storage for AI Studio suites, reusable resources and reproducible run records.</summary>
public sealed class JsonStudioResourceStore(string rootPath) : IStudioResourceStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _path = Path.Combine(Path.GetFullPath(rootPath), "resources.v1.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<StudioResource>> ListAsync(StudioResourceKind? kind, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Resources
            .Where(item => item.DeletedAt is null && (kind is null || item.Kind == kind))
            .OrderByDescending(item => item.ModifiedAt).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<StudioResource?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Resources.FirstOrDefault(item => item.ResourceId == id); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<EvaluationRun>> ListEvaluationRunsAsync(Guid suiteId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadAsync(cancellationToken).ConfigureAwait(false)).EvaluationRuns.Where(item => item.EvalSuiteId == suiteId).OrderByDescending(item => item.StartedAt).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<TestRun>> ListTestRunsAsync(Guid suiteId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadAsync(cancellationToken).ConfigureAwait(false)).TestRuns.Where(item => item.TestSuiteId == suiteId).OrderByDescending(item => item.StartedAt).ToArray(); }
        finally { _gate.Release(); }
    }

    public Task WriteResourceAsync(StudioResource resource, CancellationToken cancellationToken) => MutateAsync(document =>
    {
        var resources = document.Resources.ToList();
        var index = resources.FindIndex(item => item.ResourceId == resource.ResourceId);
        if (index < 0) resources.Add(resource); else resources[index] = resource;
        return document with { Resources = resources };
    }, cancellationToken);

    public Task WriteEvaluationRunAsync(EvaluationRun run, CancellationToken cancellationToken) => MutateAsync(document =>
        document with { EvaluationRuns = document.EvaluationRuns.Append(run).ToArray() }, cancellationToken);

    public Task WriteTestRunAsync(TestRun run, CancellationToken cancellationToken) => MutateAsync(document =>
        document with { TestRuns = document.TestRuns.Append(run).ToArray() }, cancellationToken);

    private async Task MutateAsync(Func<StudioResourceStoreDocument, StudioResourceStoreDocument> mutation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await WriteAsync(mutation(await ReadAsync(cancellationToken).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<StudioResourceStoreDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new StudioResourceStoreDocument(CurrentSchemaVersion, [], [], []);
        await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        StudioResourceStoreDocument? document;
        try { document = await JsonSerializer.DeserializeAsync<StudioResourceStoreDocument>(input, StudioJson.Options, cancellationToken).ConfigureAwait(false); }
        catch (JsonException ex) { throw new InvalidDataException("AI Studio resource store is invalid; source data was preserved.", ex); }
        if (document is null) throw new InvalidDataException("AI Studio resource store is empty or invalid; source data was preserved.");
        if (document.SchemaVersion != CurrentSchemaVersion) throw new InvalidDataException($"AI Studio resource store schema {document.SchemaVersion} is unsupported; expected {CurrentSchemaVersion}. No data was changed.");
        return document;
    }

    private async Task WriteAsync(StudioResourceStoreDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, document, StudioJson.Options, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
