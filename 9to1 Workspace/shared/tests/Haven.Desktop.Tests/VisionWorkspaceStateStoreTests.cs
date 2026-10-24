using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class VisionWorkspaceStateStoreTests
{
    [Fact]
    public async Task RoundTrip_PreservesVisionState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "haven-vision-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "state.json");
            var store = new VisionWorkspaceStateStore(path);
            var expected = new VisionWorkspaceState("image.png", "question", "answer", "vision-model", "ABC");
            await store.SaveAsync(expected, cancellationToken);
            Assert.Equal(expected, await store.LoadAsync(cancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AnalysisKey_ChangesWithPromptOrImage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "haven-vision-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first.bin");
            var second = Path.Combine(root, "second.bin");
            await File.WriteAllBytesAsync(first, [1, 2, 3], cancellationToken);
            await File.WriteAllBytesAsync(second, [1, 2, 4], cancellationToken);
            var a = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(first, "model", "prompt", cancellationToken);
            var same = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(first, "model", "prompt", cancellationToken);
            var promptChanged = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(first, "model", "different", cancellationToken);
            var imageChanged = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(second, "model", "prompt", cancellationToken);
            Assert.Equal(a, same);
            Assert.NotEqual(a, promptChanged);
            Assert.NotEqual(a, imageChanged);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_cleanup_failure_retains_the_original_import_or_cancellation_cause(bool cancelled)
    {
        Exception original = cancelled ? new OperationCanceledException("Import cancelled") : new IOException("Import failed");
        var cleanup = new UnauthorizedAccessException("Temporary cleanup failed");
        var aggregate = Assert.Throws<AggregateException>(() =>
            VisionWorkspaceStateStore.CompleteImportCleanup(() => throw cleanup, original));
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Same(original, aggregate.InnerExceptions[0]);
        Assert.Same(cleanup, aggregate.InnerExceptions[1]);
    }

    [Fact]
    public async Task PersistSource_keeps_concurrent_imports_immutable_and_deduplicates_identical_content()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "haven-vision-sources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var firstBytes = new byte[] { 1, 2, 3, 4 };
            var secondBytes = new byte[] { 9, 8, 7, 6 };
            var firstSource = Path.Combine(root, "first.png");
            var duplicateSource = Path.Combine(root, "duplicate.png");
            var secondSource = Path.Combine(root, "second.png");
            await File.WriteAllBytesAsync(firstSource, firstBytes, cancellationToken);
            await File.WriteAllBytesAsync(duplicateSource, firstBytes, cancellationToken);
            await File.WriteAllBytesAsync(secondSource, secondBytes, cancellationToken);
            var store = new VisionWorkspaceStateStore(Path.Combine(root, "state.json"));

            var persisted = await Task.WhenAll(
                store.PersistSourceAsync(firstSource, cancellationToken),
                store.PersistSourceAsync(duplicateSource, cancellationToken),
                store.PersistSourceAsync(secondSource, cancellationToken));

            Assert.Equal(persisted[0], persisted[1]);
            Assert.NotEqual(persisted[0], persisted[2]);
            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(persisted[0], cancellationToken));
            Assert.Equal(Path.GetFileNameWithoutExtension(persisted[0]).Length, 64);
            Assert.Equal(secondBytes, await File.ReadAllBytesAsync(persisted[2], cancellationToken));
            Assert.Equal(Path.GetFileNameWithoutExtension(persisted[2]).Length, 64);

            var repeated = await store.PersistSourceAsync(firstSource, cancellationToken);
            Assert.Equal(persisted[0], repeated);
            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(persisted[0], cancellationToken));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "Vision"), ".vision-source-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }
}
