using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Wave;

/// <summary>The canonical Wave project and existing shared mutation history.
/// The actual host supplies its revision-aware publication operation; this
/// session does not manufacture a Files permission or treat a path as a grant.</summary>
public sealed class WaveEditSession
{
    private sealed class State(WaveProject project) { public WaveProject Project { get; set; } = project; }
    private readonly DocumentMutationHistory<State> _history;
    private readonly Func<WaveProject, long, CancellationToken, Task> _publish;
    private long _revisionCounter, _savedRevision;
    private bool _publishing, _editing;
    private DateTimeOffset _modifiedAt;
    private readonly List<Task> _originalSaves = [], _originalPublications = [];
    private static readonly AsyncLocal<WaveEditSession?> LogicalSave = new();
    public bool IsPublishing => _publishing || _editing;
    internal bool IsCurrentSaveOwner => ReferenceEquals(LogicalSave.Value, this) && _originalSaves.Any(task => !task.IsCompleted);
    public IReadOnlyList<Task> OriginalSaves => _originalSaves.AsReadOnly();
    public IReadOnlyList<Task> OriginalPublications => _originalPublications.AsReadOnly();
    public WaveProject Project => Clone(_history.Current.Project) with { Revision = _revisionCounter, ModifiedAt = _modifiedAt };
    public bool CanUndo => !_publishing && _history.CanUndo;
    public bool CanRedo => !_publishing && _history.CanRedo;
    public bool IsDirty => _revisionCounter != _savedRevision;
    public string? LastAction => _history.LastOperation?.Name;

    public WaveEditSession(WaveProject original, Func<WaveProject, long, CancellationToken, Task> originalPublisher)
    {
        ArgumentNullException.ThrowIfNull(originalPublisher); WaveProjectStore.Validate(original);
        _history = new(new State(Clone(original)), state => new State(Clone(state.Project)), 128); _publish = originalPublisher;
        _revisionCounter = _savedRevision = original.Revision; _modifiedAt = original.ModifiedAt;
    }
    public void Apply(string name, Func<WaveProject, WaveProject> edit)
    {
        if (_publishing || _editing) throw new InvalidOperationException("Project publication or mutation is already in progress.");
        ArgumentNullException.ThrowIfNull(edit); _editing = true;
        try
        {
            var before = Project; var candidate = edit(Clone(before));
            if (candidate.ProjectId != before.ProjectId || candidate.SampleRate != before.SampleRate || candidate.Channels != before.Channels)
                throw new InvalidOperationException("An edit cannot replace the current Wave project identity or audio configuration.");
            candidate = candidate with { Revision = checked(_revisionCounter + 1), ModifiedAt = DateTimeOffset.UtcNow };
            WaveProjectStore.Validate(candidate); _history.Apply(name, DocumentOperationOrigin.User, state => state.Project = candidate);
            _revisionCounter = candidate.Revision; _modifiedAt = candidate.ModifiedAt;
        }
        finally { _editing = false; }
    }
    public void Undo() => Navigate(false);
    public void Redo() => Navigate(true);
    private void Navigate(bool forward)
    {
        if (_publishing || _editing) throw new InvalidOperationException("Project publication or mutation is already in progress.");
        if (forward ? !CanRedo : !CanUndo) return;
        var revision = checked(_revisionCounter + 1);
        if (forward) _history.Redo(); else _history.Undo();
        // The SAME shared engine has restored the structured state. The owner
        // revision is monotonic, preventing old selection/save receipts re-use.
        _revisionCounter = revision; _modifiedAt = DateTimeOffset.UtcNow;
    }
    public Task SaveAsync(CancellationToken token)
    {
        if (_publishing || _editing) throw new InvalidOperationException("Project publication or mutation is already in progress.");
        token.ThrowIfCancellationRequested(); var current = Project; var expected = _savedRevision;
        _originalSaves.RemoveAll(task => task.IsCompletedSuccessfully); _originalPublications.RemoveAll(task => task.IsCompletedSuccessfully);
        if (_originalSaves.Count >= 128) throw new InvalidOperationException("The original failed saves remain held; recover this project before more publication.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _publishing = true;
        var driver = SaveOriginalAsync(start.Task, current, expected, token); _originalSaves.Add(driver); start.SetResult(); return driver;
    }
    private async Task SaveOriginalAsync(Task start, WaveProject current, long expected, CancellationToken token)
    {
        var previous = LogicalSave.Value; LogicalSave.Value = this;
        try
        {
            await start; token.ThrowIfCancellationRequested();
            var actualPublication = _publish(current, expected, token); _originalPublications.Add(actualPublication);
            await actualPublication; _savedRevision = current.Revision;
        }
        finally { _publishing = false; LogicalSave.Value = previous; }
    }
    private static WaveProject Clone(WaveProject project) => JsonSerializer.Deserialize<WaveProject>(JsonSerializer.SerializeToUtf8Bytes(project))
        ?? throw new InvalidDataException("Wave snapshot is missing.");
}
