using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

/// <summary>Controlled producer faults over the actual production transaction and diagnostics contract.</summary>
public sealed class NotesTransactionExceptionCustodyTests : IDisposable
{
    private readonly Paths paths = new();

    [Fact]
    public async Task Original_transaction_body_retains_every_member_of_a_multiply_faulted_task()
    {
        var first = new IOException("First original body failure");
        var second = new ApplicationException("Second original body failure");
        var original = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        original.SetException(new Exception[] { first, second });
        var repository = Repository(new CompoundDiagnostics(first, second));

        var observed = await Assert.ThrowsAsync<AggregateException>(() =>
            repository.WithTransactionAsync(_ => original.Task, CancellationToken.None));

        RequireOriginalMembers(observed, first, second);
        ProbeReleasedLease();
    }

    [Fact]
    public async Task Diagnostics_wrapped_task_retains_all_original_faults_after_the_physical_lease_releases()
    {
        var first = new IOException("First original diagnostic failure");
        var second = new ApplicationException("Second original diagnostic failure");
        var calls = 0;
        NotesRepository? repository = null;
        var diagnostics = new CompoundDiagnostics(first, second, () =>
        {
            calls++;
            ProbeReleasedLease();
            // The exact raw owner can re-enter because both owning locks are released.
            Assert.Null(repository!.LoadAsync(Guid.NewGuid(), CancellationToken.None).GetAwaiter().GetResult());
        });
        repository = Repository(diagnostics);

        var observed = await Assert.ThrowsAsync<AggregateException>(() => repository.WithTransactionAsync(async transaction =>
        {
            await transaction.WriteDiagnosticAsync(diagnostics, ReliabilitySeverity.Warning, "notes", "compound", "Controlled compound fault");
            return 0;
        }, CancellationToken.None));

        Assert.Equal(1, calls);
        RequireOriginalMembers(observed, first, second);
        ProbeReleasedLease();
    }

    [Fact]
    public async Task Equal_by_value_exception_objects_are_retained_as_distinct_original_causes()
    {
        var first = new EqualByValueFailure("First actual object");
        var second = new EqualByValueFailure("Second actual object");
        Assert.True(first.Equals(second));
        Assert.False(ReferenceEquals(first, second));
        var original = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        original.SetException(new Exception[] { first, second });

        var observed = await Assert.ThrowsAsync<AggregateException>(() =>
            Repository(new CompoundDiagnostics(first, second)).WithTransactionAsync(_ => original.Task, CancellationToken.None));

        RequireOriginalMembers(observed, first, second);
        Assert.Equal(2, observed.Flatten().InnerExceptions.Count);
        ProbeReleasedLease();
    }

    [Fact]
    public async Task Committed_document_keeps_its_actual_receipt_and_reports_every_original_diagnostic_failure()
    {
        var first = new IOException("First actual saved diagnostic failure");
        var second = new ApplicationException("Second actual saved diagnostic failure");
        var repository = Repository(new CompoundDiagnostics(first, second));
        var document = NotesDocument.Create("Known committed document");

        var result = await repository.SaveAsync(document, "Controlled compound diagnostic", CancellationToken.None);

        Assert.Equal(1, result.Version);
        Assert.Equal(1, document.Version);
        var warning = Assert.IsType<string>(result.PostCommitWarning);
        Assert.Contains(nameof(IOException), warning);
        Assert.Contains(nameof(ApplicationException), warning);
        var current = Assert.IsType<NotesDocument>(await repository.LoadAsync(document.Id, CancellationToken.None));
        Assert.Equal(document.Id, current.Id);
        Assert.Equal(result.Version, current.Version);
        Assert.Equal(document.Title, current.Title);
        ProbeReleasedLease();
    }

    [Fact]
    public async Task A_faulted_original_cancellation_exception_stays_faulted_with_its_exact_cause()
    {
        var exact = new OperationCanceledException("EXACT_FAULTED_ORIGINAL_OCE");
        var original = Task.FromException<int>(exact);
        var returned = Repository(new CompoundDiagnostics(exact, exact))
            .WithTransactionAsync(_ => original, CancellationToken.None);

        var observed = await Record.ExceptionAsync(() => returned);

        Assert.True(original.IsFaulted);
        Assert.True(returned.IsFaulted);
        Assert.False(returned.IsCanceled);
        Assert.Contains(OriginalCauses(Assert.IsType<AggregateException>(observed)), cause => ReferenceEquals(cause, exact));
        ProbeReleasedLease();
    }

    [Fact]
    public async Task A_synchronous_owner_cancellation_exception_is_retained_as_a_fault()
    {
        var exact = new OperationCanceledException("EXACT_SYNCHRONOUS_ORIGINAL_OCE");
        var returned = Repository(new CompoundDiagnostics(exact, exact))
            .WithTransactionAsync<int>(_ => throw exact, CancellationToken.None);

        var observed = await Record.ExceptionAsync(() => returned);

        Assert.True(returned.IsFaulted);
        Assert.False(returned.IsCanceled);
        Assert.Contains(OriginalCauses(Assert.IsType<AggregateException>(observed)), cause => ReferenceEquals(cause, exact));
        ProbeReleasedLease();
    }

    [Fact]
    public async Task An_actually_canceled_original_stays_canceled_after_its_owning_lease_drain()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var original = Task.FromCanceled<int>(source.Token);
        var returned = Repository(new CompoundDiagnostics(new IOException(), new ApplicationException()))
            .WithTransactionAsync(_ => original, CancellationToken.None);

        var observed = await Record.ExceptionAsync(() => returned);

        Assert.True(original.IsCanceled);
        Assert.True(returned.IsCanceled);
        Assert.False(returned.IsFaulted);
        Assert.Equal(source.Token, Assert.IsAssignableFrom<OperationCanceledException>(observed).CancellationToken);
        ProbeReleasedLease();
    }

    [Fact]
    public async Task An_opaque_empty_original_aggregate_and_held_finally_cause_both_survive_the_actual_drain()
    {
        var opaque = new AggregateException("EXACT_OPAQUE_EMPTY_ORIGINAL");
        Assert.Empty(opaque.InnerExceptions);
        var originalBody = Task.FromException<int>(opaque);
        var cleanup = new IOException("EXACT_HELD_DIAGNOSTIC_FINALLY_CAUSE");
        var diagnostics = new HeldFinallyDiagnostics(cleanup);
        var repository = Repository(diagnostics);
        var returned = repository.WithTransactionAsync(transaction =>
        {
            transaction.WriteDiagnosticAsync(diagnostics, ReliabilitySeverity.Warning, "notes", "held-finally", "Controlled original finally")
                .GetAwaiter().GetResult();
            return originalBody;
        }, CancellationToken.None);
        Exception? observed = null;
        Exception? diagnosticFailure = null;
        try
        {
            await diagnostics.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(returned.IsCompleted);
            var actualDiagnostic = Assert.IsAssignableFrom<Task>(diagnostics.Original);
            Assert.False(actualDiagnostic.IsCompleted);
            ProbeReleasedLease();
        }
        finally
        {
            diagnostics.Release.TrySetResult();
            observed = await Record.ExceptionAsync(() => returned);
            if (diagnostics.Original is { } actualDiagnostic)
                diagnosticFailure = await Record.ExceptionAsync(() => actualDiagnostic);
        }
        Assert.True(returned.IsFaulted);
        Assert.Same(cleanup, diagnosticFailure);
        var causes = OriginalCauses(Assert.IsType<AggregateException>(observed)).ToArray();
        Assert.Contains(causes, cause => ReferenceEquals(cause, opaque));
        Assert.Contains(causes, cause => ReferenceEquals(cause, cleanup));
        Assert.True(diagnostics.Original!.IsCompleted);
        ProbeReleasedLease();
    }

    private static IEnumerable<Exception> OriginalCauses(Exception error)
    {
        yield return error; // An empty aggregate is itself an opaque original cause.
        if (error is AggregateException aggregate)
            foreach (var child in aggregate.InnerExceptions)
                foreach (var original in OriginalCauses(child)) yield return original;
    }

    private NotesRepository Repository(IProductionDiagnostics diagnostics) =>
        new(paths, new NotesDocumentValidator(), diagnostics);

    private static void RequireOriginalMembers(AggregateException observed, Exception first, Exception second)
    {
        var members = observed.Flatten().InnerExceptions;
        Assert.Contains(members, member => ReferenceEquals(member, first));
        Assert.Contains(members, member => ReferenceEquals(member, second));
    }

    private void ProbeReleasedLease()
    {
        using var lease = new FileStream(Path.Combine(paths.DataDirectory, "Notes", ".repository.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(lease.CanRead);
    }

    public void Dispose() => paths.Dispose();

    private sealed class EqualByValueFailure(string message) : Exception(message)
    {
        public override bool Equals(object? other) => other is EqualByValueFailure;
        public override int GetHashCode() => 1;
    }

    private sealed class CompoundDiagnostics(Exception first, Exception second, Action? observe = null) : IProductionDiagnostics
    {
        public ValueTask WriteAsync(ReliabilitySeverity severity, string component, string eventName, string message,
            IReadOnlyDictionary<string, string>? data = null, string? correlationId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observe?.Invoke();
            // This is the actual allowed ValueTask(Task) contract. There is no async wrapper
            // that loses its two original fault members before the repository receives it.
            return new ValueTask(Task.WhenAll(Task.FromException(first), Task.FromException(second)));
        }
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HeldFinallyDiagnostics(Exception exactFinallyCause) : IProductionDiagnostics
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? Original { get; private set; }
        public ValueTask WriteAsync(ReliabilitySeverity severity, string component, string eventName, string message,
            IReadOnlyDictionary<string, string>? data = null, string? correlationId = null, CancellationToken cancellationToken = default)
        {
            Original = RunOriginalAsync();
            return new ValueTask(Original);
        }
        private async Task RunOriginalAsync()
        {
            try { await Task.Yield(); }
            finally
            {
                Entered.TrySetResult();
                await Release.Task;
                throw exactFinallyCause;
            }
        }
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-notes-original-faults-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
