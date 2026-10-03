using System.Runtime.ExceptionServices;

namespace HavenOS.Home.Core;

/// <summary>Retains original callback Tasks through coalesced cancel/drain. This component
/// issues no Agent, Den, tool, actor or permission authority.</summary>
public sealed class HomeAgentOriginalWork : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private static void Add(List<Exception> errors, Exception error)
        { if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error); }
        private static void ThrowFailures(IReadOnlyList<Exception> errors)
        {
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count > 1) throw new AggregateException("Original Agent work and cleanup failed.", errors);
        }
        private sealed class Operation { internal Task Task = Task.CompletedTask; internal CancellationToken Token; internal CancellationToken Caller { get; init; } }
        private readonly List<Operation> _originals = [];
        private Task? _close;
        private HomeNativeCoreApiSessions.AgentConnection? _connection;
        internal HomeAgentExecutionAdmissions? Issuer;
        public CancellationToken Lifetime => _lifetime.Token;
        internal HomeNativeCoreApiSessions.AgentConnection Connection => _connection ?? throw new UnauthorizedAccessException("The original accepted Agent connection is unavailable.");
        internal void BindConnection(HomeNativeCoreApiSessions.AgentConnection original) => _connection = original;
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller)
        {
            lock (_gate)
            {
                if (_close is not null) throw new ObjectDisposedException(nameof(HomeAgentOriginalWork));
                _originals.RemoveAll(operation => operation.Task.IsCompleted);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var operation = new Operation { Caller = caller };
                var sameTask = ExecuteAsync(start.Task, operation, action, caller);
                operation.Task = sameTask; _originals.Add(operation);
                start.SetResult();
                return sameTask;
            }
        }
        private async Task<T> ExecuteAsync<T>(Task start, Operation operation, Func<CancellationToken, Task<T>> action, CancellationToken caller)
        {
            await start.ConfigureAwait(false);
            List<Exception> errors = [];
            CancellationTokenSource? linked = null;
            T? result = default;
            try
            {
                linked = _connection is null ? CancellationTokenSource.CreateLinkedTokenSource(caller, Lifetime) :
                    CancellationTokenSource.CreateLinkedTokenSource(caller, Lifetime, _connection.Lifetime);
                operation.Token = linked.Token;
                result = await action(linked.Token).ConfigureAwait(false);
            }
            catch (Exception error) { Add(errors, error); }
            finally
            { if (linked is not null) try { linked.Dispose(); } catch (Exception error) { Add(errors, error); } }
            ThrowFailures(errors);
            return result!;
        }
        public Task CloseAsync()
        {
            lock (_gate)
            {
                if (_close is not null) return _close;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseOriginalAsync(start.Task, _originals.Where(operation => !operation.Task.IsCompleted).ToArray());
                start.SetResult();
                return _close;
            }
        }
        public ValueTask DisposeAsync() => new(CloseAsync());
        private async Task CloseOriginalAsync(Task start, Operation[] originalTasks)
        {
            await start.ConfigureAwait(false);
            List<Exception> errors = [];
            try { _lifetime.Cancel(); } catch (Exception error) { Add(errors, error); }
            foreach (var original in originalTasks)
                try { await original.Task.ConfigureAwait(false); }
                catch (OperationCanceledException error) when (_lifetime.IsCancellationRequested &&
                    !original.Caller.IsCancellationRequested && original.Token.IsCancellationRequested && error.CancellationToken == original.Token) { }
                catch (Exception error) { Add(errors, error); }
            if (Issuer is not null)
                try { await Issuer.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
            try { _lifetime.Dispose(); } catch (Exception error) { Add(errors, error); }
            ThrowFailures(errors);
        }
    }
