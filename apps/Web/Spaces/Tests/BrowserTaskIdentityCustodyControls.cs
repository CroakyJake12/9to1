using NineToOne.Web.Services;

// Original-operation custody controls only. These do not issue authentication or app permissions.
internal static class Program
{
    public static async Task Main()
    {
        await CancellationCallbackFaultRetainsTheSamePendingRead();
        await RawFaultedCancellationKeepsEveryOriginalFaultAndReleaseCause();
        await GenuineCancellationDrainsBeforeReceiptRelease();
        await SynchronousReadCancellationIsAFaultAndStillReleases();
        await SuccessfulReadConsumesOnceAndReleasesTheSameReceipt();
        await PreCancelledReadStartsNoModuleWork();
        Console.WriteLine("6 original-operation custody controls passed.");
    }

    private static async Task CancellationCallbackFaultRetainsTheSamePendingRead()
    {
        using var stop = new CancellationTokenSource();
        var raw = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationFailure = new IOException("exact cancellation callback failure");
        var released = 0;
        var consumed = 0;
        var driver = BrowserTaskIdentityReadOperation.RunAsync(() => raw.Task,
            () => throw cancellationFailure, json => { consumed++; return json; }, () => released++, stop.Token);
        Exception? controlFailure = null;
        try
        {
            stop.Cancel(); // Callback failure belongs to the SAME operation, never an unobserved caller.
            Require(!driver.IsCompleted && released == 0, "Cancellation fault discarded the actual pending read.");
        }
        catch (Exception error) { controlFailure = error; }
        finally { raw.TrySetResult("same raw response"); }
        var failure = await Failure(driver);
        Require(ReferenceEquals(failure, cancellationFailure), "Original cancellation callback cause was replaced.");
        Require(driver.IsFaulted && !driver.IsCanceled && consumed == 0 && released == 1,
            "Faulted cancellation must drain, refuse publication and release once.");
        if (controlFailure is not null) throw controlFailure;
    }

    private static async Task RawFaultedCancellationKeepsEveryOriginalFaultAndReleaseCause()
    {
        var raw = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oce = new OperationCanceledException("faulted original OCE");
        var sibling = new IOException("original fault sibling");
        var release = new IOException("original release failure");
        var consumed = 0;
        var driver = BrowserTaskIdentityReadOperation.RunAsync(() => raw.Task, () => { },
            json => { consumed++; return json; }, () => throw release, CancellationToken.None);
        raw.SetException([oce, sibling]);
        var failure = await Failure(driver);
        Require(failure is AggregateException aggregate && aggregate.InnerExceptions.Count == 2 &&
            aggregate.InnerExceptions[0] is AggregateException original && original.InnerExceptions.Count == 2 &&
            ReferenceEquals(original.InnerExceptions[0], oce) && ReferenceEquals(original.InnerExceptions[1], sibling) &&
            ReferenceEquals(aggregate.InnerExceptions[1], release), "Actual raw fault siblings or release cause were lost.");
        Require(raw.Task.IsFaulted && driver.IsFaulted && !driver.IsCanceled && consumed == 0,
            "Faulted OCE cannot be converted into cancellation or identity publication.");
    }

    private static async Task GenuineCancellationDrainsBeforeReceiptRelease()
    {
        using var stop = new CancellationTokenSource();
        var raw = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = 0;
        var consumed = 0;
        var cancelled = 0;
        var driver = BrowserTaskIdentityReadOperation.RunAsync(() => raw.Task, () => cancelled++,
            json => { consumed++; return json; }, () => released++, stop.Token);
        Exception? controlFailure = null;
        try
        {
            stop.Cancel();
            Require(cancelled == 1 && !driver.IsCompleted && released == 0, "Cancellation settled before the actual read.");
        }
        catch (Exception error) { controlFailure = error; }
        finally { raw.TrySetResult("response after cancellation"); }
        Require(await Failure(driver) is OperationCanceledException && driver.IsCanceled && consumed == 0 && released == 1,
            "Actual cancellation must refuse consumption and preserve complete receipt drain.");
        if (controlFailure is not null) throw controlFailure;
    }

    private static async Task SynchronousReadCancellationIsAFaultAndStillReleases()
    {
        var original = new OperationCanceledException("synchronous module invocation fault");
        var released = 0;
        var driver = BrowserTaskIdentityReadOperation.RunAsync(() => throw original, () => { },
            json => json, () => released++, CancellationToken.None);
        var failure = await Failure(driver);
        Require(failure is AggregateException group && group.InnerExceptions.Count == 1 &&
            ReferenceEquals(group.InnerExceptions[0], original) && driver.IsFaulted && !driver.IsCanceled && released == 1,
            "A synchronous module OCE fault or receipt release was lost.");
    }

    private static async Task SuccessfulReadConsumesOnceAndReleasesTheSameReceipt()
    {
        var consumed = 0;
        var released = 0;
        const string sameJson = "same original receipt";
        var actual = Task.FromResult(sameJson);
        var driver = BrowserTaskIdentityReadOperation.RunAsync(() => actual, () => throw new Exception("unexpected cancel"),
            json => { Require(ReferenceEquals(json, sameJson), "The raw response was replaced."); consumed++; return json; },
            () => released++, CancellationToken.None);
        Require(await driver == sameJson && driver.IsCompletedSuccessfully && consumed == 1 && released == 1,
            "Successful receipt consumption/release was duplicated or replaced.");
    }

    private static async Task PreCancelledReadStartsNoModuleWork()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var calls = 0;
        var driver = BrowserTaskIdentityReadOperation.RunAsync(() => { calls++; return Task.FromResult("unissued"); },
            () => calls++, json => { calls++; return json; }, () => calls++, stop.Token);
        Require(await Failure(driver) is OperationCanceledException && driver.IsCanceled && calls == 0,
            "A pre-cancelled caller must not create or release an unissued receipt.");
    }

    private static async Task<Exception> Failure(Task actual)
    {
        try { await actual; }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("The original operation unexpectedly succeeded.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
