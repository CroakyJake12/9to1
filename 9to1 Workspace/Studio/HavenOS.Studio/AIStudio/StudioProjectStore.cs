using System.Text.Json;

namespace HavenOS.AIStudio;

public interface IStudioProjectStore
{
    Task<IReadOnlyList<StudioProject>> ListAsync(bool includeDeleted, CancellationToken cancellationToken);
    Task<StudioProject?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task WriteAsync(StudioProject project, CancellationToken cancellationToken);
}

/// <summary>Atomic, schema-versioned local persistence for AI Studio project-backed artifacts.</summary>
public sealed class JsonStudioProjectStore(string rootPath) : IStudioProjectStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = StudioJson.Options;
    private readonly string _path = Path.Combine(Path.GetFullPath(rootPath), "projects.v1.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<StudioProject>> ListAsync(bool includeDeleted, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = (await ReadDocumentAsync(cancellationToken).ConfigureAwait(false)).Projects;
            return items.Where(item => includeDeleted || !item.IsDeleted)
                .OrderByDescending(item => item.ModifiedAt).ThenBy(item => item.ProjectId).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<StudioProject?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadDocumentAsync(cancellationToken).ConfigureAwait(false)).Projects.FirstOrDefault(item => item.ProjectId == id); }
        finally { _gate.Release(); }
    }

    public async Task WriteAsync(StudioProject project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
            var items = document.Projects.ToList();
            var existing = items.FindIndex(item => item.ProjectId == project.ProjectId);
            if (existing < 0) items.Add(project);
            else items[existing] = project;
            await WriteDocumentAsync(new StudioStoreDocument(CurrentSchemaVersion, items), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<StudioStoreDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new StudioStoreDocument(CurrentSchemaVersion, []);
        await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        StudioStoreDocument? document;
        try { document = await JsonSerializer.DeserializeAsync<StudioStoreDocument>(input, Json, cancellationToken).ConfigureAwait(false); }
        catch (JsonException ex) { throw new InvalidDataException("AI Studio project store is invalid; source data has been preserved.", ex); }
        if (document is null) throw new InvalidDataException("AI Studio project store is empty or invalid; source data has been preserved.");
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"AI Studio project store schema {document.SchemaVersion} is unsupported; expected {CurrentSchemaVersion}. No data was changed.");
        return document;
    }

    private async Task WriteDocumentAsync(StudioStoreDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, document, Json, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
