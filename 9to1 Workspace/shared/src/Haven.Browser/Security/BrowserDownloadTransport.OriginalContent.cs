using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Application;

namespace Haven.Browser;

public sealed partial class BrowserDownloadTransport
{
    private readonly IBrowserOriginalNativeDownloadPhysicalOwner? _originalPhysicalOwner;
    private readonly ConditionalWeakTable<NativeDownloadPlan, OriginalPlan> _nativeOriginalPlans = new();
    private readonly ConditionalWeakTable<IBrowserOriginalNativeDownloadTransportPlan, OriginalPlan> _transportOriginalPlans = new();
    private readonly object _originalTransportGate = new();
    public IBrowserOriginalNativeDownloadPhysicalOwner? OriginalPhysicalOwner => _originalPhysicalOwner;

    private sealed class OriginalPlan(NativeDownloadPlan native,
        BrowserOriginalNativeDownloadPlanDescription description) : IBrowserOriginalNativeDownloadTransportPlan
    {
        public Guid ActionId => Native.ActionId;
        internal readonly NativeDownloadPlan Native = native;
        internal readonly BrowserOriginalNativeDownloadPlanDescription Description = description;
        internal Task? Preparation;
        internal Task<IBrowserOriginalDownloadContent>? Finalization;
        internal Task<IBrowserOriginalDownloadContent>? PhysicalFinalization;
        internal IBrowserOriginalDownloadContent? Content;
    }

    private async Task CaptureOriginalPreparedPlanAsync(NativeDownloadPlan samePlan, CancellationToken token)
    {
        if (_originalPhysicalOwner is null) return;
        if (!ReferenceEquals(_originalPhysicalOwner.OriginalTransportSource, this))
            throw new UnauthorizedAccessException("The actual original physical owner belongs to another Browser transport.");
        var original = new OriginalPlan(samePlan, new(samePlan.ActionId, _downloadDirectory,
            samePlan.PartialPath, samePlan.FinalPath, samePlan.FileName, samePlan.RecordAddress, samePlan.PreparedAt));
        lock (_originalTransportGate)
        {
            _nativeOriginalPlans.Add(samePlan, original); _transportOriginalPlans.Add(original, original);
        }
        original.Preparation = _originalPhysicalOwner.PrepareOriginalTransportPlanWithinSourceAsync(
            original, static body => body(), static _ => { }, token);
        await original.Preparation.ConfigureAwait(false);
    }

    public bool IsIssuedOriginalTransportPlan(IBrowserOriginalNativeDownloadTransportPlan originalPlan) =>
        _transportOriginalPlans.TryGetValue(originalPlan, out var actual) && ReferenceEquals(originalPlan, actual);

    public BrowserOriginalNativeDownloadPlanDescription GetOriginalTransportPlanDescription(
        IBrowserOriginalNativeDownloadTransportPlan originalPlan) =>
        _transportOriginalPlans.TryGetValue(originalPlan, out var actual) && ReferenceEquals(originalPlan, actual)
            ? actual.Description : throw new UnauthorizedAccessException("Use the SAME actual configured Browser transport plan.");

    public IBrowserOriginalNativeDownloadTransportPlan GetOriginalTransportPlan(NativeDownloadPlan samePlan)
    {
        if (_originalPhysicalOwner is null || !_nativeOriginalPlans.TryGetValue(samePlan, out var actual) ||
            !ReferenceEquals(actual.Native, samePlan) || actual.Preparation?.IsCompletedSuccessfully != true ||
            !ReferenceEquals(_originalPhysicalOwner.OriginalTransportSource, this))
            throw new InvalidOperationException("Original pinned download transport is unavailable. A copied or legacy path plan is not original custody.");
        return actual;
    }

    public Task<IBrowserOriginalDownloadContent> FinalizeOriginalNativeDownloadWithinSourceAsync(
        NativeDownloadPlan samePlan, IBrowserOriginalNativeDownloadCompletionSource sameCompletionSource,
        IBrowserOriginalNativeDownloadCompletion originalCompletion, string? contentType,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var actual = (OriginalPlan)GetOriginalTransportPlan(samePlan);
        lock (_originalTransportGate)
        {
            if (actual.Finalization is not null) return actual.Finalization;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.Finalization = Run(start.Task); start.SetResult(); return actual.Finalization;
        }
        async Task<IBrowserOriginalDownloadContent> Run(Task begin)
        {
            await begin.ConfigureAwait(false);
            var failures = new List<Exception>(); IBrowserOriginalDownloadContent? result = null;
            var active = 1; var entered = 0; var thread = Environment.CurrentManagedThreadId;
            void Callback()
            {
                try
                {
                    if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId ||
                        Interlocked.Exchange(ref entered, 1) != 0)
                        throw new InvalidOperationException("The original native finalization callback is inactive, repeated or foreign-thread.");
                    if (!ReferenceEquals(originalCompletion.OriginalPlan, actual))
                        throw new UnauthorizedAccessException("Use the completion of this SAME privately issued native plan.");
                    actual.PhysicalFinalization = _originalPhysicalOwner!.FinalizeOriginalDownloadWithinSourceAsync(
                        actual, sameCompletionSource, originalCompletion, contentType, scope, retain, cancellationToken)
                        ?? throw new InvalidOperationException("The actual physical finalization returned no Task.");
                    // Capture precedes the foreign parent-retainer/post-scope callback.
                    retain(actual.PhysicalFinalization);
                }
                catch (Exception cause) { Remember(cause); throw; }
            }
            try
            {
                scope(Callback);
                if (entered != 1) Remember(new InvalidOperationException("The actual original finalization callback was not invoked."));
            }
            catch (Exception cause) { Remember(cause); }
            finally { Volatile.Write(ref active, 0); }
            if (actual.PhysicalFinalization is { } raw)
            {
                try { result = await raw.ConfigureAwait(false); actual.Content = result; }
                catch (Exception observed)
                {
                    if (raw.Exception is { } clrContainer)
                        foreach (var cause in clrContainer.InnerExceptions) Remember(cause);
                    else Remember(observed);
                }
            }
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Original native finalization callback/raw source failed.", failures);
            return result ?? throw new InvalidOperationException("No actual original pinned download content was acquired.");
            void Remember(Exception cause)
            {
                if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
            }
        }
    }
}
