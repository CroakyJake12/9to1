using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalCurrentProjectExecutionBridge
{
    public sealed class OriginalSourceInvocation<T>
    {
        private readonly Exception[] _publication;
        private readonly Action _demandJoin;
        private readonly object _gate = new();
        private Task<T>? _join;
        internal OriginalSourceInvocation(Task<T> actual, Exception[] publication, Action demandJoin)
            => (OriginalSourceTask, _publication, _demandJoin) = (actual, publication, demandJoin);
        public Task<T> OriginalSourceTask { get; }
        public Task<T> JoinOriginalAsync()
        {
            _demandJoin();
            lock (_gate) return _join ??= JoinAsync();
        }
        private async Task<T> JoinAsync()
        {
            var errors = new List<Exception>(_publication); T value = default!;
            try { value = await OriginalSourceTask.ConfigureAwait(false); }
            catch (Exception cause) { AddTask(errors, OriginalSourceTask, cause); }
            Throw(errors); return value;
        }
    }

    /// <summary>Finite attachment for an existing source/editor owner. This issues no
    /// Task, READ, execution permission or result; the actual factory keeps its own lifetime.
    /// The same workspace command fence precedes acquisition and records the exact raw Task.</summary>
    public OriginalSourceInvocation<T> CaptureOriginalSourceWithinSource<T>(
        IDeveloperOriginalProjectCommandRead sameRead, DeveloperProjectReference sameProject,
        DeveloperCanonicalActionContext sameAction, Func<Task<T>> originalSource,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSource);
        Task<T>? actual = null; EditorSessionCommand? command = null; var errors = new List<Exception>();
        try
        {
            Scope(() => FilesOriginalSourceCallbackScope.Invoke(() =>
            {
                token.ThrowIfCancellationRequested();
                lock (_gate) if (_retiring) throw new ObjectDisposedException(nameof(FilesDeveloperOriginalCurrentProjectExecutionBridge));
                if (!_reads.IsIssuedOriginalCommandRead(sameRead))
                    throw new UnauthorizedAccessException("The SAME genuinely issued current command READ is required.");
                DemandDescriptor(sameRead.OriginalDescriptor, sameProject, sameAction);
                command = AdmitEditorSessionCommand(sameProject.WorkspaceId, sameRead.OriginalDescriptor.OriginalActor);
                var previous = _command.Value; _command.Value = new(sameRead, sameProject, sameAction);
                try
                {
                    actual = originalSource() ?? throw new InvalidOperationException("The original source returned no Task.");
                    retainOriginalTask(actual);
                }
                finally { _command.Value = previous; CompleteEditorSessionCommandAcquisition(command, actual); }
            }, originalSynchronousScope));
        }
        catch (Exception cause) { Add(errors, cause); }
        if (actual is null) { Throw(errors); throw new InvalidOperationException("No actual source operation was acquired."); }
        return new(actual, errors.ToArray(), () =>
        { DemandExternalOriginalExecutionTrustJoin(); sameRead.DemandExternalOriginalJoin(); });
    }
}
