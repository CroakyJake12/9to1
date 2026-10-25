namespace HavenOS.Apps.Dev;

/// <summary>Reciprocal exclusion within the SAME canonical store owner, never source or process
/// authority. These cheap private pins acquire no IO/Home/Files lease or callback. The genuine
/// native metadata/root pin remains mandatory at Start, including external-change detection.</summary>
public sealed partial class FileDeveloperWorkspaceStore
{
    private readonly object _runMetadataGate = new();
    private readonly HashSet<RunMetadataReadPin> _runMetadataReaders = [];
    private readonly Dictionary<Guid, int> _runMetadataWriters = [];
    private sealed class RunMetadataReadPin(FileDeveloperWorkspaceStore owner, Guid workspace) : IDisposable
    {
        internal FileDeveloperWorkspaceStore Owner => owner;
        internal Guid Workspace => workspace;
        public void Dispose() { lock (owner._runMetadataGate) owner._runMetadataReaders.Remove(this); }
    }
    private sealed class RunMetadataWriteAdmission(FileDeveloperWorkspaceStore owner, Guid workspace) : IDisposable
    {
        private bool _closed;
        public void Dispose()
        {
            lock (owner._runMetadataGate)
            {
                if (_closed) return; _closed = true;
                if (owner._runMetadataWriters[workspace] == 1) owner._runMetadataWriters.Remove(workspace);
                else owner._runMetadataWriters[workspace]--;
            }
        }
    }
    public IDisposable AcquireOriginalRunMetadataReadPin(Guid workspace)
    {
        if (workspace == Guid.Empty) throw new ArgumentException("The actual saved workspace identity is required.", nameof(workspace));
        lock (_runMetadataGate)
        {
            if (_runMetadataWriters.ContainsKey(workspace))
                throw new InvalidOperationException("The canonical workspace is admitting a metadata write; no manual native Start pin was issued.");
            var pin = new RunMetadataReadPin(this, workspace); _runMetadataReaders.Add(pin); return pin;
        }
    }
    public void DemandOriginalRunMetadataReadPin(Guid workspace, IDisposable pin)
    {
        lock (_runMetadataGate)
            if (pin is not RunMetadataReadPin original || !ReferenceEquals(original.Owner, this) || original.Workspace != workspace ||
                !_runMetadataReaders.Contains(original) || _runMetadataWriters.ContainsKey(workspace))
                throw new UnauthorizedAccessException("The SAME live canonical metadata read exclusion is required alongside the native Start pin.");
    }
    private IDisposable? AcquireOriginalMetadataWriteAdmission(Guid workspace)
    {
        lock (_runMetadataGate)
        {
            if (_runMetadataReaders.Any(pin => pin.Workspace == workspace)) return null;
            _runMetadataWriters.TryGetValue(workspace, out var active); _runMetadataWriters[workspace] = checked(active + 1);
            return new RunMetadataWriteAdmission(this, workspace);
        }
    }
}
