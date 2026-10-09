using Xunit;

namespace HavenOS.Images.Tests;

// Diagnostic waits bound observation only. A timeout fails the fixture and
// retains the SAME actual task/owner; it never cancels, disposes, substitutes,
// acknowledges or claims successful retirement of the original source.
internal static class PictureOriginalSourceFixture
{
    private static readonly object Gate = new();
    private static readonly List<(object Owner, Task Source, string Stage, TimeoutException Failure)> Pending = [];
    private static readonly List<(object Owner, Task Source, string Stage)> Originals = [];

    internal static void RetainOriginal(object owner, Task original, string stage)
    {
        lock (Gate)
            if (!Originals.Any(entry => ReferenceEquals(entry.Owner, owner) && ReferenceEquals(entry.Source, original)))
                Originals.Add((owner, original, stage));
    }

    private static readonly TimeSpan ObservationLimit = TimeSpan.FromSeconds(30);

    internal static bool HasRetainedPendingSource(object owner)
    {
        lock (Gate) return Pending.Any(entry => ReferenceEquals(entry.Owner, owner));
    }

    internal static async Task ObserveOriginalAsync(this Task original, object owner, string stage)
    {
        RetainOriginal(owner, original, stage);
        try { await original.WaitAsync(ObservationLimit, TestContext.Current.CancellationToken); }
        catch (TimeoutException error) when (!original.IsCompleted)
        {
            var failure = new TimeoutException(stage + " remained pending after 30 seconds; original status: " + original.Status + ". The original source and owner are retained.", error);
            lock (Gate) Pending.Add((owner, original, stage, failure));
            throw failure;
        }
    }

    internal static async Task<T> ObserveOriginalAsync<T>(this Task<T> original, object owner, string stage)
    {
        await ((Task)original).ObserveOriginalAsync(owner, stage);
        return await original;
    }
}
