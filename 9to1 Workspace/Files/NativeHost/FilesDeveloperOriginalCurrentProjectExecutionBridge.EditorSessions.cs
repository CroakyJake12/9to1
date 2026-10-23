using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalCurrentProjectExecutionBridge
{
    // A presentation update does not grant access or weaken the native document/inode proof.
    // It excludes admission only for this exact workspace while the canonical metadata changes.
    private readonly Dictionary<Guid, EditorSessionUpdate> _editorSessionUpdates = [];
    private readonly List<EditorSessionCommand> _editorSessionCommands = [];
    private sealed class EditorSessionCommand(Guid workspaceId, AuthenticatedResourceActor actor)
    {
        internal Guid WorkspaceId { get; } = workspaceId;
        internal AuthenticatedResourceActor Actor { get; } = actor;
        internal Task? Actual;
    }
    private sealed class EditorSessionUpdate(FilesDeveloperOriginalCurrentProjectExecutionBridge owner,
        Guid workspaceId, AuthenticatedResourceActor actor) : IDisposable
    {
        internal AuthenticatedResourceActor Actor { get; } = actor;
        private bool _released;
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_released) return;
                if (!owner._editorSessionUpdates.TryGetValue(workspaceId, out var same) || !ReferenceEquals(same, this))
                    throw new InvalidOperationException("The actual workspace editor-session reservation was replaced.");
                owner._editorSessionUpdates.Remove(workspaceId); _released = true;
            }
        }
    }

    /// <summary>Trusted host exclusion only, not an authority or review token. The caller must
    /// hold a genuine source-issued registered profile/store observation and retain this SAME
    /// reservation through its actual canonical metadata write. Refuses live commands and pins;
    /// ordinary native JSON/hash/inode checks remain unchanged. No unrelated workspace is held.</summary>
    public IDisposable AcquireOriginalEditorSessionUpdate(Guid workspaceId, AuthenticatedResourceActor actualActor)
    {
        ArgumentNullException.ThrowIfNull(actualActor);
        if (workspaceId == Guid.Empty || actualActor.AccountId is not null || actualActor.OrganisationId is not null ||
            string.IsNullOrWhiteSpace(actualActor.ActorId) || !Guid.TryParse(actualActor.ProfileId, out var profile) || profile == Guid.Empty)
            throw new UnauthorizedAccessException("Use the actual personal Home source observation for this workspace.");
        EditorSessionUpdate reservation; Binding[] reviewed;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(FilesDeveloperOriginalCurrentProjectExecutionBridge));
            _editorSessionCommands.RemoveAll(value => value.Actual is { IsCompletedSuccessfully: true });
            if (_editorSessionUpdates.ContainsKey(workspaceId) ||
                _editorSessionCommands.Any(value => value.WorkspaceId == workspaceId) ||
                _pins.Any(value => value.Binding.WorkspaceId == workspaceId &&
                    (!value.Sealed || value.Close is not { IsCompletedSuccessfully: true })))
                throw new InvalidOperationException("Clean tab state cannot change while this workspace has an admitted command, native pin or unresolved execution cleanup. Its previous session was preserved.");
            reservation = new EditorSessionUpdate(this, workspaceId, actualActor);
            _editorSessionUpdates.Add(workspaceId, reservation);
            reviewed = _bindings.Where(value => value.WorkspaceId == workspaceId).ToArray();
        }
        try
        {
            // Query the SAME configured READ issuer outside the bridge gate. Keep the workspace
            // admission fence held throughout, so no new command or pin can race this probe.
            if (reviewed.Any(value => _reads.IsIssuedOriginalCommandRead(value.Frame.Read)))
                throw new InvalidOperationException("A reviewed execution binding still holds this workspace's current READ. The previous editor session was preserved.");
            return reservation;
        }
        catch { reservation.Dispose(); throw; }
    }

    private EditorSessionCommand AdmitEditorSessionCommand(Guid workspaceId, AuthenticatedResourceActor actor)
    {
        lock (_gate)
        {
            if (_retiring || _editorSessionUpdates.ContainsKey(workspaceId))
                throw new InvalidOperationException("This workspace is finishing a clean editor-session update. Retry the command after it settles.");
            _editorSessionCommands.RemoveAll(value => value.Actual is { IsCompletedSuccessfully: true });
            if (_editorSessionCommands.Count >= 128)
                throw new InvalidOperationException("Unresolved original command custody must settle before another command can be admitted.");
            var command = new EditorSessionCommand(workspaceId, actor);
            _editorSessionCommands.Add(command); return command;
        }
    }
    private void CompleteEditorSessionCommandAcquisition(EditorSessionCommand command, Task? actual)
    {
        lock (_gate)
        {
            if (actual is null) _editorSessionCommands.Remove(command);
            else command.Actual = actual;
        }
    }
}
