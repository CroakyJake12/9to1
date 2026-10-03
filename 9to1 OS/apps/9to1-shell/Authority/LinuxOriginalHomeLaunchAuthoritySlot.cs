using Haven.Application;
using System.Runtime.Versioning;
namespace NineToOne.Os.Shell.Authority;

// Only explicitly supervised Home composition registers this slot. Ordinary Home
// retains UnavailableHomeNativeControlledLaunchAuthority. Binding does not mint a
// client or adopt a lease; it accepts one concrete privately captured Home client.
[SupportedOSPlatform("linux")]
internal sealed class LinuxOriginalHomeLaunchAuthoritySlot : IHomeNativeControlledLaunchAuthority, IAsyncDisposable
{
    private readonly object _gate = new();
    private LinuxAdministratorOriginalHomeLaunchClient? _original;
    private bool _retired;
    private Task? _disposeTask;
    internal bool BindOriginal(LinuxAdministratorOriginalHomeLaunchClient original)
    {
        ArgumentNullException.ThrowIfNull(original);
        lock (_gate)
        {
            if (_retired || _original is not null) return false;
            _original = original; return true;
        }
    }
    public async ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation, CancellationToken ct)
    {
        LinuxAdministratorOriginalHomeLaunchClient? original;
        lock (_gate) { if (_retired) return false; original = _original; }
        if (original is null) { ct.ThrowIfCancellationRequested(); return false; }
        var current = await original.IsCurrentAsync(observation, ct).ConfigureAwait(false);
        lock (_gate) return current && !_retired && ReferenceEquals(_original, original);
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null; LinuxAdministratorOriginalHomeLaunchClient? original = null; Task task;
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                _retired = true; original = _original;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
            }
            task = _disposeTask;
        }
        if (completion is not null) _ = DrainAsync(original, completion);
        return new(task);
    }
    private static async Task DrainAsync(LinuxAdministratorOriginalHomeLaunchClient? original, TaskCompletionSource completion)
    {
        try
        {
            if (original is not null) await original.DisposeAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
