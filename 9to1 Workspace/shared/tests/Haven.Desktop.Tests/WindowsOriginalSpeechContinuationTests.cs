#if HAVEN_WINDOWS_DESKTOP
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

// Requires genuine Windows voice bank/media runtime and audible default output. No fallback, mock or skip.
// This platform owning probe is inactive until a dedicated Windows gate supplies the same production symbol/SDK.
public sealed class WindowsOriginalSpeechContinuationTests
{
    [Fact]
    public async Task Actual_hybrid_preserves_selected_Windows_route_and_disposal_retires_paused_original_without_replay()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        await using var windows = new WindowsNaturalSpeechOutputService();
        Assert.True(windows.IsAvailable, windows.UnavailableReason);
        var voices = windows.Voices; Assert.NotEmpty(voices); var voiceId = voices[0].Id;
        await using var hybrid = new HybridNaturalSpeechOutputService(windows);
        Assert.True(hybrid.CanContinueVoice(voiceId)); Assert.False(hybrid.CanContinueVoice("kokoro:af_heart"));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => hybrid.StartContinuableAsync(
            "Unsupported selected voice must not retarget to Windows.", "kokoro:af_heart", "default", ct));
        var original = await hybrid.StartContinuableAsync(string.Join(" ", Enumerable.Repeat(
            "Original hybrid routed narration retains its selected voice and media.", 100)), voiceId, "default", ct);
        Task? queuedStart = null; Task? queuedSpeak = null;
        try
        {
            var playing = await original.ResumeAsync(ct); Assert.Equal(voiceId, playing.VoiceId);
            var paused = await original.PauseAsync(ct); Assert.True(paused.IsPaused);
            Assert.Equal(original.PlaybackId, paused.PlaybackId); Assert.False(original.Completion.IsCompleted);
            Assert.True(hybrid.IsOriginalPlayback(original)); Assert.True(hybrid.WasIssuedPlayback(original));
            // A second real wrapper sharing the actual Windows provider has its own pending
            // start, but cannot authenticate the first wrapper's already-issued playback.
            await using (var foreign = new HybridNaturalSpeechOutputService(windows))
            using (var foreignLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var pendingForeign = foreign.StartContinuableAsync("Foreign queued narration must not adopt original media.", voiceId, "default", foreignLifetime.Token);
                try
                {
                    Assert.False(pendingForeign.IsCompleted);
                    Assert.False(foreign.IsOriginalPlayback(original)); Assert.False(foreign.WasIssuedPlayback(original));
                    foreignLifetime.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingForeign);
                    Assert.True(hybrid.IsOriginalPlayback(original)); Assert.True(hybrid.WasIssuedPlayback(original));
                }
                finally
                {
                    foreignLifetime.Cancel();
                    try { await pendingForeign; } catch (OperationCanceledException) { }
                }
            }

            queuedStart = hybrid.StartContinuableAsync("Queued continuation must not synthesize after disposal.", voiceId, "default", ct);
            queuedSpeak = hybrid.SpeakAsync("Queued ordinary speech must not start after disposal.", voiceId, "default", ct);
            Assert.False(queuedStart.IsCompleted); Assert.False(queuedSpeak.IsCompleted);
            await hybrid.DisposeAsync().AsTask().WaitAsync(ct);
            Assert.False(hybrid.IsOriginalPlayback(original)); Assert.True(hybrid.WasIssuedPlayback(original));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queuedStart);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queuedSpeak);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original.Completion);
            await Assert.ThrowsAsync<InvalidOperationException>(() => original.ResumeAsync(ct));
        }
        finally
        {
            deadline.Cancel(); await windows.StopAsync(CancellationToken.None);
            if (queuedStart is not null) { try { await queuedStart; } catch { } }
            if (queuedSpeak is not null) { try { await queuedSpeak; } catch { } }
        }
    }

    [Fact]
    public async Task Actual_native_disposal_drains_queued_original_requests_without_replacement_playback()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        await using var output = new WindowsNaturalSpeechOutputService();
        Assert.True(output.IsAvailable, output.UnavailableReason);
        var voices = output.Voices; Assert.NotEmpty(voices); var voiceId = voices[0].Id;
        var original = await output.StartContinuableAsync(string.Join(" ", Enumerable.Repeat(
            "Original native narration retains its media while queued callers await admission.", 100)), voiceId, "default", ct);
        Task? queuedStart = null; Task? queuedSpeak = null; Exception? observationFailure = null;
        try
        {
            await original.ResumeAsync(ct); var paused = await original.PauseAsync(ct);
            Assert.True(paused.IsPaused); Assert.False(original.Completion.IsCompleted);
            queuedStart = output.StartContinuableAsync("Queued continuation must remain unavailable after disposal.", voiceId, "default", ct);
            queuedSpeak = output.SpeakAsync("Queued speech must not replace disposed original media.", voiceId, "default", ct);
            Assert.False(queuedStart.IsCompleted); Assert.False(queuedSpeak.IsCompleted);
            await output.DisposeAsync().AsTask().WaitAsync(ct);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original.Completion);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queuedStart);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queuedSpeak);
        }
        catch (Exception error) { observationFailure = error; throw; }
        finally
        {
            deadline.Cancel();
            try { await output.StopAsync(CancellationToken.None); }
            catch (ObjectDisposedException) { } // The owning positive path intentionally disposed this service.
            catch (Exception) when (observationFailure is not null) { } // Preserve the original observation failure.
            finally
            {
                if (queuedStart is not null) { try { await queuedStart; } catch { } }
                if (queuedSpeak is not null) { try { await queuedSpeak; } catch { } }
            }
        }
    }

    [Fact]
    public async Task Actual_original_player_pauses_at_confirmed_position_resumes_without_replacement_and_retires_on_stop()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        await using var output = new WindowsNaturalSpeechOutputService();
        Assert.True(output.IsAvailable, output.UnavailableReason);
        var originalVoices = output.Voices; Assert.NotEmpty(originalVoices); var originalVoice = originalVoices[0].Id;
        var original = await output.StartContinuableAsync(string.Join(" ", Enumerable.Repeat(
            "Original sustained narration continues from its retained playback position.", 100)), originalVoice, "default", ct);
        try
        {
            var playing = await original.ResumeAsync(ct); // Actual native Playing acknowledgement, not a timer estimate.
            Assert.Equal(originalVoice, original.VoiceId); Assert.Equal(originalVoice, playing.VoiceId);
            Assert.True(output.IsOriginalPlayback(original)); Assert.True(output.WasIssuedPlayback(original));
            await using var foreignIssuer = new WindowsNaturalSpeechOutputService();
            Assert.False(foreignIssuer.IsOriginalPlayback(original)); Assert.False(foreignIssuer.WasIssuedPlayback(original));
            Assert.Equal(original.PlaybackId, playing.PlaybackId); Assert.False(playing.IsPaused);
            var paused = await original.PauseAsync(ct);
            Assert.True(paused.IsPaused); Assert.Equal(original.PlaybackId, paused.PlaybackId);
            Assert.True(paused.Position >= TimeSpan.Zero);
            var samePaused = await original.PauseAsync(ct);
            Assert.Equal(paused.Position, samePaused.Position); Assert.False(original.Completion.IsCompleted);
            var resumed = await original.ResumeAsync(ct);
            Assert.Equal(original.PlaybackId, resumed.PlaybackId); Assert.False(resumed.IsPaused);
            Assert.True(resumed.Position >= paused.Position);
            await output.StopAsync(ct);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original.Completion);
            Assert.False(output.IsOriginalPlayback(original)); Assert.True(output.WasIssuedPlayback(original));
            await Assert.ThrowsAsync<InvalidOperationException>(() => original.PauseAsync(ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => original.ResumeAsync(ct));
        }
        finally { await output.StopAsync(CancellationToken.None); }
    }
    [Fact]
    public async Task Actual_original_handle_stop_cannot_cancel_a_later_native_replacement_utterance()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45)); var ct = deadline.Token;
        await using var output = new WindowsNaturalSpeechOutputService();
        Assert.True(output.IsAvailable, output.UnavailableReason);
        var voices = output.Voices; Assert.NotEmpty(voices); var voiceId = voices[0].Id;
        Haven.Application.IOriginalSpeechPlaybackStop? initial = null, replacement = null;
        Exception? primary = null;
        try
        {
            var first = await output.StartContinuableAsync(string.Join(" ", Enumerable.Repeat(
                "The first original native utterance can be stopped only by its own handle.", 100)), voiceId, "default", ct);
            var firstStop = Assert.IsAssignableFrom<Haven.Application.IOriginalSpeechPlaybackStop>(first); initial = firstStop;
            var paused = await first.PauseAsync(ct); Assert.True(paused.IsPaused);
            Assert.True(await firstStop.StopOriginalAsync(ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Completion.WaitAsync(ct));
            Assert.False(output.IsOriginalPlayback(first)); Assert.True(output.WasIssuedPlayback(first));
            replacement = Assert.IsAssignableFrom<Haven.Application.IOriginalSpeechPlaybackStop>(
                await output.StartContinuableAsync(string.Join(" ", Enumerable.Repeat(
                    "The independently issued replacement must survive retained old cleanup.", 100)), voiceId, "default", ct));
            var replacementId = replacement.PlaybackId;
            Assert.True((await replacement.PauseAsync(ct)).IsPaused);
            Assert.False(await firstStop.StopOriginalAsync(ct));
            Assert.False(replacement.Completion.IsCompleted); Assert.True(output.IsOriginalPlayback(replacement));
            var playing = await replacement.ResumeAsync(ct);
            Assert.False(playing.IsPaused); Assert.Equal(replacementId, playing.PlaybackId);
            Assert.False(await firstStop.StopOriginalAsync(ct));
            Assert.True(output.IsOriginalPlayback(replacement)); Assert.False(replacement.Completion.IsCompleted);
            Assert.True(await replacement.StopOriginalAsync(ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacement.Completion.WaitAsync(ct));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Exception? cleanupFailure = null;
            foreach (var issued in new[] { initial, replacement })
            {
                if (issued is null) continue;
                try { await issued.StopOriginalAsync(CancellationToken.None); }
                catch (Exception error) { cleanupFailure ??= error; }
                finally
                {
                    try { await issued.Completion; } catch (OperationCanceledException) { }
                    catch (Exception error) { cleanupFailure ??= error; }
                }
            }
            if (primary is null && cleanupFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

}
#endif
