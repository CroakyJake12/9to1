using Dulche.Runtime;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Application-only callback ports adapt to the SAME actual scoped provider driver.
/// They carry custody, never catalogue eligibility, model use or route authority.</summary>
public sealed partial class LlamaCppModelProvider : ITaskRunOriginalProviderCatalogueSource
{
    public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalTaskSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        // Return the exact old scoped whole, not an async proxy or second reader.
        return GetModelsWithinOriginalSourceAsync(
            new ApplicationCatalogueOriginalScope(originalSynchronousScope, retainOriginalTask), cancellationToken);
    }

    private sealed class ApplicationCatalogueOriginalScope(Action<Action> caller, Action<Task> retainer)
        : IInferenceEngineOriginalSourceScope
    {
        private readonly object _gate = new();
        private readonly HashSet<Task> _originals = new(ReferenceEqualityComparer.Instance);
        private bool _failed;
        public T InvokeOriginalFactory<T>(Func<T> factory) => Invoke(factory, cleanup: false);
        public T InvokeOriginalCleanup<T>(Func<T> factory) => Invoke(factory, cleanup: true);
        private T Invoke<T>(Func<T> factory, bool cleanup)
        {
            ArgumentNullException.ThrowIfNull(factory);
            var active = 1; var once = 0; var thread = Environment.CurrentManagedThreadId;
            var causes = new List<Exception>(); var causeGate = new object(); T actual = default!;
            void Keep(Exception cause)
            {
                lock (causeGate) if (!causes.Any(known => ReferenceEquals(known, cause))) causes.Add(cause);
                lock (_gate) _failed = true;
            }
            void Run()
            {
                try
                {
                    if (Volatile.Read(ref active) == 0 || Environment.CurrentManagedThreadId != thread
                        || Interlocked.CompareExchange(ref once, 1, 0) != 0)
                        throw new InvalidOperationException("The actual catalogue callback requires its issuing thread, live synchronous phase and one invocation.");
                    lock (_gate) if (_failed && !cleanup)
                        throw new InvalidOperationException("The actual catalogue source already failed productive admission.");
                    actual = factory();
                    if (actual is Task task) PublishActualTask(task);
                }
                catch (Exception cause) { Keep(cause); throw; }
            }
            // The provider's operative InvokePhysical wraps this ENTIRE caller boundary.
            // Capture actual products privately; an Action or swallowed exception grants nothing.
            try { caller(Run); }
            catch (Exception cause) { Keep(cause); }
            finally { Volatile.Write(ref active, 0); }
            if (Volatile.Read(ref once) == 0)
                Keep(new InvalidOperationException("The actual catalogue caller omitted its finite callback."));
            Exception[] errors; lock (causeGate) errors = causes.ToArray();
            if (errors.Length != 0)
                throw new AggregateException("All actual catalogue factory and caller causes are retained.", errors);
            return actual;
        }
        private void PublishActualTask(Task sameTask)
        {
            // Keep the exact original before an external retainer can fail or reenter.
            lock (_gate) if (!_originals.Add(sameTask)) return;
            retainer(sameTask);
        }
        public void RetainOriginalTask(Task sameActualTask)
        {
            ArgumentNullException.ThrowIfNull(sameActualTask);
            lock (_gate) if (_originals.Contains(sameActualTask)) return;
            // Publication also has the actual finite caller phase. Explicit cleanup admission
            // preserves already-owned originals after a source failure; it starts no new work.
            _ = Invoke(() => { PublishActualTask(sameActualTask); return true; }, cleanup: true);
        }
    }
}
