using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Dev;

/// <summary>
/// Reads the actual existing Tasks/Studio conversation and selected container root. A Dev record,
/// caller-supplied path or CAKE subject cannot substitute another workspace. This observation does
/// not issue permission; the canonical action owner still validates its actual actor and native fence.
/// </summary>
public sealed class DeveloperCanonicalWorkspaceBinding(IConversationRepository conversations, IContainerRepository containers)
{
    [ThreadStatic] private static Dictionary<DeveloperCanonicalWorkspaceBinding, int>? _synchronousSources;
    public void DemandExternalOriginalRetirementJoin()
    { if (_synchronousSources?.ContainsKey(this) == true) throw new InvalidOperationException("A workspace-binding repository callback cannot join its development owner."); }
    public Task<bool> IsCurrentAsync(TaskExecutionSnapshot task, DeveloperResolvedProject project, CancellationToken token) =>
        IsCurrentCoreAsync(task, project, token, null);

    // Only the enclosing admitted Dev original supplies this callback. It enrolls each SAME
    // repository Task before await; this custody notification never grants resource permission.
    internal Task<bool> IsCurrentAsync(TaskExecutionSnapshot task, DeveloperResolvedProject project, CancellationToken token,
        Action<Task> retainOriginal) => IsCurrentCoreAsync(task, project, token, retainOriginal);

    private async Task<bool> IsCurrentCoreAsync(TaskExecutionSnapshot task, DeveloperResolvedProject project,
        CancellationToken token, Action<Task>? retainOriginal)
    {
        var conversation = await ObserveAsync(() => conversations.GetAsync(task.ContextId, token), retainOriginal).ConfigureAwait(false);
        if (conversation is null || conversation.Id != task.ContextId || conversation.IsArchived ||
            conversation.Mode is not (HavenMode.Tasks or HavenMode.Studio) || conversation.ContainerId is not { } containerId)
            return false;
        var selected = (await ObserveAsync(() => containers.GetByModeAsync(conversation.Mode, token), retainOriginal).ConfigureAwait(false))
            .Where(value => value.Id == containerId && !value.IsArchived && value.Mode == conversation.Mode).Take(2).ToArray();
        if (selected.Length != 1 || string.IsNullOrWhiteSpace(selected[0].RootPath) || !Path.IsPathRooted(selected[0].RootPath)) return false;
        // Awaited container reads can race a real workspace-selection change. Re-read the
        // actual conversation after that await; metadata is still a finite observation, not
        // a distributed final-effect lock. The native owner must check its own final fence.
        var current = await ObserveAsync(() => conversations.GetAsync(task.ContextId, token), retainOriginal).ConfigureAwait(false);
        if (current != conversation) return false;
        var currentSelection = (await ObserveAsync(() => containers.GetByModeAsync(current.Mode, token), retainOriginal).ConfigureAwait(false))
            .Where(value => value.Id == containerId && !value.IsArchived && value.Mode == current.Mode).Take(2).ToArray();
        if (currentSelection.Length != 1 || currentSelection[0] != selected[0]) return false;
        token.ThrowIfCancellationRequested();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(selected[0].RootPath).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(project.Root.Location).TrimEnd(Path.DirectorySeparatorChar), comparison);
    }

    private async Task<T> ObserveAsync<T>(Func<Task<T>> source, Action<Task>? retainOriginal)
    {
        Task<T> actual;
        try
        {
            var active = _synchronousSources ??= []; active.TryGetValue(this, out var depth); active[this] = depth + 1;
            try { actual = source(); }
            finally { if (depth == 0) active.Remove(this); else active[this] = depth; }
        }
        catch (OperationCanceledException original) { throw new AggregateException("A synchronous repository source fault is not a canceled I/O Task.", original); }
        retainOriginal?.Invoke(actual);
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
    }
}
