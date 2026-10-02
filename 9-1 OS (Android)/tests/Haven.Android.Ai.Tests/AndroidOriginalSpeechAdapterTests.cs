using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Android;

// Tests the exact linked Android fallback. No Android/Windows/native audio or
// neural playback is manufactured; unavailable original Windows media stays denied.
public sealed class AndroidOriginalSpeechAdapterTests
{
    [Fact]
    public async Task UnsupportedOriginalMediaCannotIssueOrAdoptSuppliedPlayback()
    {
        await using var adapter = new WindowsNaturalSpeechOutputService();
        IContinuableSpeechOutputService continuation = adapter;
        IOriginalSpeechPlaybackReceiptIssuer issuer = adapter;
        Assert.False(adapter.IsAvailable);
        Assert.Empty(adapter.Voices);
        Assert.False(continuation.CanContinueVoice("David"));
        Assert.False(continuation.CanContinueVoice("kokoro:af_heart"));
        Assert.False(continuation.CanContinueVoice(""));
        var foreign = new NeverObservedPlayback();
        Assert.False(issuer.WasIssuedPlayback(foreign));
        Assert.False(issuer.IsOriginalPlayback(foreign));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => continuation.StartContinuableAsync(
            "Original narration", "David", "default", CancellationToken.None));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => adapter.SpeakAsync(
            "Original narration", "David", "default", CancellationToken.None));
        await adapter.StopAsync(CancellationToken.None);
        Assert.False(issuer.WasIssuedPlayback(foreign));
    }

    [Fact]
    public async Task CancellationRemainsObservableBeforeUnavailableStartAndOrdinaryControls()
    {
        await using var adapter = new WindowsNaturalSpeechOutputService();
        using var lifetime = new CancellationTokenSource(); lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.StartContinuableAsync(
            "Original narration", "David", "default", lifetime.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.SpeakAsync(
            "Original narration", "David", "default", lifetime.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.StopAsync(lifetime.Token));
        Assert.False(adapter.CanContinueVoice("David"));
    }

    private sealed class NeverObservedPlayback : ISpeechPlaybackContinuation
    {
        public Guid PlaybackId => throw new InvalidOperationException("Foreign metadata is not issuance.");
        public string VoiceId => throw new InvalidOperationException("Foreign metadata is not issuance.");
        public Task Completion => throw new InvalidOperationException("Foreign media is not owned.");
        public Task<SpeechPlaybackCheckpoint> PauseAsync(CancellationToken ct) => throw new InvalidOperationException("Foreign media is not owned.");
        public Task<SpeechPlaybackCheckpoint> ResumeAsync(CancellationToken ct) => throw new InvalidOperationException("Foreign media is not owned.");
    }
}
