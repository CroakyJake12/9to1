namespace Haven.Desktop.Services;

public static partial class OriginalLocalTaskConsole
{
    private sealed partial class Host
    {
        private sealed class OriginalConsoleSources(DesktopOriginalWorkLifetime.Original original)
        {
            private readonly object _gate = new();
            private readonly HashSet<Task> _retained = new(ReferenceEqualityComparer.Instance);
            private readonly HashSet<Task> _joined = new(ReferenceEqualityComparer.Instance);
            internal void Retain(Task actual)
            { ArgumentNullException.ThrowIfNull(actual); lock (_gate) _retained.Add(actual); }
            internal async Task AwaitAsync(Task actual)
            {
                Retain(actual); lock (_gate) _joined.Add(actual);
                await original.AwaitAsync(actual).ConfigureAwait(false);
            }
            internal async Task<T> AwaitAsync<T>(Task<T> actual)
            {
                Retain(actual); lock (_gate) _joined.Add(actual);
                return await original.AwaitAsync(actual).ConfigureAwait(false);
            }
            internal async Task JoinRemainingAsync(List<Exception> failures)
            {
                while (true)
                {
                    Task[] remaining;
                    lock (_gate) remaining = _retained.Where(actual => _joined.Add(actual)).ToArray();
                    if (remaining.Length == 0) return;
                    foreach (var actual in remaining)
                        try { await original.AwaitAsync(actual).ConfigureAwait(false); }
                        catch (Exception cause) { Capture(failures, actual, cause); }
                }
            }
        }
    }
}
