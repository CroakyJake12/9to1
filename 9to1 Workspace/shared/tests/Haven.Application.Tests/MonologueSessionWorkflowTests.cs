using System.Text.Json;
using Haven.Application;
using Haven.Application.Call;

namespace Haven.Application.Tests;

// Actual canonical file-backed metadata/CAS. No speech, provider, source-reading or device authority is simulated.
public sealed class MonologueSessionWorkflowTests
{
    [Fact]
    public async Task Plan_progress_pause_and_resume_reopen_the_same_canonical_run_and_conversation()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var session = (await f.Service.StartAsync(Guid.NewGuid(), Guid.NewGuid(), VisionVoiceMode.Monologue, "local-only", true, cancellationToken: ct)).Value!;
        string[] sections = ["Introduction", "Findings"]; string[] sources = ["artifact-original"];
        using var heldRead = new FileStream(f.StatePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var pendingPlan = f.Service.PlanMonologueAsync(session.SessionId, 1, "Project briefing", TimeSpan.FromMinutes(10), sections, sources, ct);
        try
        {
            Assert.False(pendingPlan.IsCompleted); // Actual canonical settings read is waiting for its real physical lease.
            sections[0] = "Caller replacement"; sources[0] = "Caller replacement";
        }
        catch
        {
            heldRead.Dispose();
            try { await pendingPlan; } catch { } // Observe completion before fixture cleanup; preserve the original assertion.
            throw;
        }
        finally { heldRead.Dispose(); }
        var planned = await pendingPlan;
        Assert.True(planned.IsSuccess); var run = planned.Value!.Monologue!;
        var speaking = await f.Service.RecordMonologueProgressAsync(session.SessionId, 2, run.RunId, 0, TimeSpan.FromSeconds(12), false, ct);
        Assert.True(speaking.IsSuccess);
        var paused = await f.Service.RecordMonologueProgressAsync(session.SessionId, 3, run.RunId, 0, TimeSpan.FromSeconds(12), true, ct);
        Assert.True(paused.IsSuccess);
        var reopened = new VisionVoiceSessionService(new MultimodalSessionStore(new VersionedAtomicSettingsStore(f.Paths)));
        var read = await reopened.GetSessionAsync(session.SessionId, ct);
        Assert.True(read.IsSuccess); Assert.True(read.Value!.Monologue!.IsPaused);
        Assert.Equal(run.RunId, read.Value.Monologue.RunId); Assert.Equal(session.ConversationId, read.Value.ConversationId);
        Assert.Equal(session.SpaceId, read.Value.SpaceId); Assert.Equal("Introduction", read.Value.Monologue.Sections[0]);
        Assert.Equal("artifact-original", read.Value.Monologue.SourceRefs[0]);
        var resumed = await reopened.RecordMonologueProgressAsync(session.SessionId, 4, run.RunId, 0, TimeSpan.FromSeconds(12), false, ct);
        Assert.True(resumed.IsSuccess); Assert.False(resumed.Value!.Monologue!.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(12), resumed.Value.Monologue.Position);
        Assert.Equal(session.State, resumed.Value.State); Assert.Equal(session.MicrophoneState, resumed.Value.MicrophoneState);
        var next = await reopened.RecordMonologueProgressAsync(session.SessionId, 5, run.RunId, 1, TimeSpan.Zero, false, ct);
        Assert.True(next.IsSuccess); Assert.Equal(1, next.Value!.Monologue!.CurrentSection);
        Assert.Equal(run.RunId, next.Value.Monologue.RunId); Assert.Equal(run.Objective, next.Value.Monologue.Objective);
    }

    [Fact]
    public async Task Stale_foreign_rewind_invalid_and_terminal_commands_preserve_actual_persisted_plan()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var session = (await f.Service.StartAsync(Guid.NewGuid(), null, VisionVoiceMode.Monologue, "local-only", true, cancellationToken: ct)).Value!;
        Assert.False((await f.Service.PlanMonologueAsync(session.SessionId, 1, "", null, ["One"], [], ct)).IsSuccess);
        Assert.False((await f.Service.PlanMonologueAsync(session.SessionId, 1, "Brief", TimeSpan.FromSeconds(-1), ["One"], [], ct)).IsSuccess);
        var planned = (await f.Service.PlanMonologueAsync(session.SessionId, 1, "Brief", null, ["One", "Two"], [], ct)).Value!;
        var run = planned.Monologue!;
        var progressed = await f.Service.RecordMonologueProgressAsync(session.SessionId, 2, run.RunId, 0, TimeSpan.FromSeconds(10), true, ct);
        Assert.True(progressed.IsSuccess); var bytes = await File.ReadAllBytesAsync(f.StatePath, ct);
        foreach (var command in new[]
        {
            f.Service.RecordMonologueProgressAsync(session.SessionId, 2, run.RunId, 0, TimeSpan.FromSeconds(11), false, ct),
            f.Service.RecordMonologueProgressAsync(session.SessionId, 3, Guid.NewGuid(), 0, TimeSpan.FromSeconds(11), false, ct),
            f.Service.RecordMonologueProgressAsync(session.SessionId, 3, run.RunId, 0, TimeSpan.FromSeconds(9), false, ct),
            f.Service.RecordMonologueProgressAsync(session.SessionId, 3, run.RunId, 2, TimeSpan.Zero, false, ct),
            f.Service.RecordMonologueProgressAsync(session.SessionId, 3, run.RunId, 1, TimeSpan.FromTicks(-1), false, ct)
        }) Assert.False((await command).IsSuccess);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath, ct));
        Assert.False((await f.Service.PlanMonologueAsync(session.SessionId, 3, "Replacement", null, ["Replacement"], [], ct)).IsSuccess);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath, ct));
        var ended = await f.Service.TransitionAsync(session.SessionId, 3, MultimodalSessionState.Ended, ct);
        Assert.True(ended.IsSuccess); bytes = await File.ReadAllBytesAsync(f.StatePath, ct);
        Assert.False((await f.Service.RecordMonologueProgressAsync(session.SessionId, 4, run.RunId, 1, TimeSpan.Zero, false, ct)).IsSuccess);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath, ct));
        var actual = await f.Service.GetSessionAsync(session.SessionId, ct);
        Assert.Equal(JsonSerializer.Serialize(ended.Value), JsonSerializer.Serialize(actual.Value));
    }

    [Fact]
    public async Task Original_continuation_reads_the_exact_reopened_checkpoint_without_replaying_or_writing()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var session = (await f.Service.StartAsync(Guid.NewGuid(), Guid.NewGuid(), VisionVoiceMode.Monologue,
            "local-only", true, cancellationToken: ct)).Value!;
        var planned = (await f.Service.PlanMonologueAsync(session.SessionId, 1, "Brief", null,
            ["Completed chapter", "Current chapter", "Next chapter"], ["original-source"], ct)).Value!;
        var run = planned.Monologue!;
        var confirmed = (await f.Service.RecordMonologueProgressAsync(session.SessionId, 2, run.RunId,
            1, TimeSpan.FromSeconds(17), true, ct)).Value!;
        var before = await File.ReadAllBytesAsync(f.StatePath, ct);
        var reopened = new VisionVoiceSessionService(new MultimodalSessionStore(new VersionedAtomicSettingsStore(f.Paths)));
        var result = await reopened.GetMonologueContinuationAsync(session.SessionId, confirmed.Revision, run.RunId, ct);
        Assert.True(result.IsSuccess); var checkpoint = result.Value!;
        Assert.Equal(session.SessionId, checkpoint.SessionId); Assert.Equal(session.ConversationId, checkpoint.ConversationId);
        Assert.Equal(session.SpaceId, checkpoint.SpaceId); Assert.Equal(run.RunId, checkpoint.RunId);
        Assert.Equal(run.Sections, checkpoint.Sections); Assert.Equal(run.SourceRefs, checkpoint.SourceRefs);
        Assert.Equal(1, checkpoint.CurrentSection); Assert.Equal(TimeSpan.FromSeconds(17), checkpoint.Position);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)checkpoint.Sections)[0] = "Replacement");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)checkpoint.SourceRefs)[0] = "Replacement");
        Assert.False((await reopened.GetMonologueContinuationAsync(session.SessionId, 2, run.RunId, ct)).IsSuccess);
        Assert.False((await reopened.GetMonologueContinuationAsync(session.SessionId, confirmed.Revision, Guid.NewGuid(), ct)).IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath, ct));
        Assert.True((await reopened.GetSessionAsync(session.SessionId, ct)).Value!.Monologue!.IsPaused);
    }

    [Fact]
    public async Task Independently_opened_services_cannot_overwrite_confirmed_playback_progress()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var session = (await f.Service.StartAsync(Guid.NewGuid(), null, VisionVoiceMode.Monologue, "local-only", true, cancellationToken: ct)).Value!;
        var plan = (await f.Service.PlanMonologueAsync(session.SessionId, 1, "Brief", null, ["One"], [], ct)).Value!;
        var second = new VisionVoiceSessionService(new MultimodalSessionStore(new VersionedAtomicSettingsStore(f.Paths)));
        var results = await Task.WhenAll(
            f.Service.RecordMonologueProgressAsync(session.SessionId, 2, plan.Monologue!.RunId, 0, TimeSpan.FromSeconds(1), true, ct),
            second.RecordMonologueProgressAsync(session.SessionId, 2, plan.Monologue.RunId, 0, TimeSpan.FromSeconds(2), false, ct));
        var committed = Assert.Single(results, result => result.IsSuccess);
        var rejected = Assert.Single(results, result => !result.IsSuccess);
        Assert.Equal(VisionVoiceErrorCode.Conflict, rejected.Error!.Code);
        var reopened = new VisionVoiceSessionService(new MultimodalSessionStore(new VersionedAtomicSettingsStore(f.Paths)));
        var actual = await reopened.GetSessionAsync(session.SessionId, ct);
        Assert.Equal(JsonSerializer.Serialize(committed.Value), JsonSerializer.Serialize(actual.Value));
        var actualRun = await reopened.GetMonologueRunAsync(session.SessionId, ct);
        Assert.True(actualRun.IsSuccess); Assert.Equal(JsonSerializer.Serialize(committed.Value!.Monologue), JsonSerializer.Serialize(actualRun.Value));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-monologue-" + Guid.NewGuid().ToString("N"));
        public IAppPaths Paths { get; }
        public VisionVoiceSessionService Service { get; }
        public string StatePath => Path.Combine(root, "settings.json");
        public Fixture() { Directory.CreateDirectory(root); Paths = new PathsAt(root); Service = new(new MultimodalSessionStore(new VersionedAtomicSettingsStore(Paths))); }
        public void Dispose() { Directory.Delete(root, true); }
    }
    private sealed record PathsAt(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
