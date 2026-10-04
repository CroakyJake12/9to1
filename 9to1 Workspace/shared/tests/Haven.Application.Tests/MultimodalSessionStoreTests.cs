using System.Text.Json;
using Haven.Application;
using Haven.Application.Call;
using Xunit;

namespace Haven.Application.Tests;

public sealed class MultimodalSessionStoreTests
{
    [Fact]
    public void Live_listener_defaults_to_ephemeral_audio_and_transcript_retention()
    {
        var retention = VisionVoiceRetention.Ephemeral;

        Assert.False(retention.RetainAudio);
        Assert.False(retention.RetainTranscript);
        Assert.Null(retention.ExplicitlyEnabledAt);
    }

    [Fact]
    public void Multilingual_session_accepts_more_than_two_locales_and_requires_three()
    {
        var valid = new LiveTranslateLanguageSet(
            "en-GB",
            ["en-GB", "fr-FR", "de-DE"],
            LiveTranslateDirection.Multilingual,
            [new("fr-FR", true, false), new("de-DE", true, true)],
            []);
        var invalid = valid with { Locales = ["en-GB", "fr-FR"] };

        Assert.Null(valid.Validate());
        Assert.Contains("at least three", invalid.Validate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Continuous_visual_frames_cannot_be_marked_durable()
    {
        var session = CreateSession() with
        {
            VisualSources = [new(
                Guid.NewGuid(),
                VisualSourceType.DisplayShare,
                "User-selected display",
                "Display 1",
                VisualSourceState.Active,
                IsContinuous: true,
                IsEphemeral: false,
                DateTimeOffset.UtcNow)]
        };

        Assert.Contains("must remain ephemeral", session.Validate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Session_requires_opt_in_timestamp_for_ambient_retention()
    {
        var session = CreateSession() with
        {
            Retention = new VisionVoiceRetention(RetainAudio: false, RetainTranscript: true, ExplicitlyEnabledAt: null)
        };

        Assert.Contains("explicit opt-in", session.Validate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reconnecting_session_does_not_claim_the_microphone_is_listening()
    {
        var session = CreateSession() with { State = MultimodalSessionState.AssistantSpeaking };

        Assert.True(MultimodalSessionLifecycle.TryTransition(
            session,
            MultimodalSessionState.Reconnecting,
            out var reconnecting,
            out var error));

        Assert.Null(error);
        Assert.Equal(session.SessionId, reconnecting.SessionId);
        Assert.Equal(session.ConversationId, reconnecting.ConversationId);
        Assert.Equal(session.Revision + 1, reconnecting.Revision);
        Assert.Equal(MicrophoneCaptureState.Disconnected, reconnecting.MicrophoneState);
    }

    [Fact]
    public void Live_mode_does_not_inherit_conversational_transcript_retention()
    {
        var session = CreateSession() with
        {
            VoiceMode = VisionVoiceMode.Conversational,
            Retention = new VisionVoiceRetention(true, true, DateTimeOffset.UtcNow),
            IsTranscriptVisible = true
        };

        Assert.True(MultimodalSessionLifecycle.TrySetMode(
            session,
            VisionVoiceMode.LiveListener,
            out var listener,
            out var error));

        Assert.Null(error);
        Assert.Equal(session.SessionId, listener.SessionId);
        Assert.Equal(session.ConversationId, listener.ConversationId);
        Assert.Equal(VisionVoiceRetention.Ephemeral, listener.Retention);
        Assert.False(listener.IsTranscriptVisible);
    }

    [Fact]
    public void Public_action_catalog_covers_required_api_names_with_security_metadata()
    {
        var expected = new[]
        {
            "9to1.VisionVoice.Start", "9to1.VisionVoice.GetSession", "9to1.VisionVoice.End",
            "9to1.VisionVoice.SetMode", "9to1.VisionVoice.SendText", "9to1.VisionVoice.SetMicrophone",
            "9to1.VisionVoice.SetCamera", "9to1.VisionVoice.SetScreenShare", "9to1.VisionVoice.CaptureScreenshot",
            "9to1.VisionVoice.AddVisualSource", "9to1.VisionVoice.RemoveVisualSource",
            "9to1.VisionVoice.SetTranscriptVisible", "9to1.VisionVoice.SetCaptions",
            "9to1.VisionVoice.SetLiveTranslateLanguages", "9to1.VisionVoice.SetLiveTranslateDirection",
            "9to1.VisionVoice.SetLiveTranslateOutput", "9to1.VisionVoice.SetLiveTranslateGlossaries",
            "9to1.VisionVoice.Overlay.Open", "9to1.VisionVoice.Overlay.Close",
            "9to1.VisionVoice.Overlay.CaptureRegion", "9to1.VisionVoice.Overlay.RunAutomation",
            "9to1.VisionVoice.ContinueInSpaces", "9to1.Dictation.Start", "9to1.Dictation.Stop",
            "9to1.Dictation.GetState", "9to1.Dictation.SetMode"
        };
        var actions = VisionVoiceActionCatalog.All;

        Assert.All(expected, name => Assert.Contains(actions, action => action.Name == name));
        Assert.Equal(actions.Count, actions.Select(action => action.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(actions, action =>
        {
            Assert.NotEmpty(action.Arguments);
            Assert.False(string.IsNullOrWhiteSpace(action.ResultSchema));
            Assert.NotEmpty(action.PermissionScopes);
            Assert.False(string.IsNullOrWhiteSpace(action.AffectedObjects));
        });
        Assert.Equal(VisionVoiceRiskLevel.TargetDefined,
            actions.Single(action => action.Name == "9to1.VisionVoice.Overlay.RunAutomation").Risk);
    }

    [Fact]
    public async Task Create_and_update_preserve_session_identity_and_enforce_revision()
    {
        var settings = new MemorySettingsStore();
        var store = new MultimodalSessionStore(settings);
        var initial = CreateSession();

        await store.CreateAsync(initial, CancellationToken.None);
        var updated = initial with { State = MultimodalSessionState.Paused, Revision = 2 };
        await store.UpdateAsync(initial.SessionId, 1, updated, CancellationToken.None);

        Assert.Equal(updated, await store.GetAsync(initial.SessionId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.UpdateAsync(initial.SessionId, 1, updated with { Revision = 2 }, CancellationToken.None));
    }

    [Fact]
    public async Task Unsupported_schema_is_rejected_without_overwriting_saved_metadata()
    {
        var settings = new MemorySettingsStore();
        var session = CreateSession();
        var key = "visionvoice.multimodal-session.v1." + session.SessionId.ToString("N");
        var unsupported = new MultimodalSessionStore.MultimodalSessionEnvelope(2, session);
        await settings.SetAsync(key, unsupported, CancellationToken.None);
        var store = new MultimodalSessionStore(settings);

        await Assert.ThrowsAsync<NotSupportedException>(() => store.GetAsync(session.SessionId, CancellationToken.None));

        Assert.Same(unsupported, await settings.GetAsync<MultimodalSessionStore.MultimodalSessionEnvelope>(key, CancellationToken.None));
    }

    [Fact]
    public async Task Session_service_starts_on_the_canonical_conversation_with_ephemeral_defaults()
    {
        var settings = new MemorySettingsStore();
        var service = new VisionVoiceSessionService(new MultimodalSessionStore(settings));
        var conversationId = Guid.NewGuid();

        var result = await service.StartAsync(conversationId, Guid.NewGuid(), VisionVoiceMode.LiveListener,
            "local-only", isLocalOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(conversationId, result.Value!.ConversationId);
        Assert.Equal(1, result.Value.Revision);
        Assert.Equal(VisionVoiceRetention.Ephemeral, result.Value.Retention);
        Assert.Null(result.Value.RuntimeSessionReference);
    }

    [Fact]
    public async Task Session_service_requires_explicit_opt_in_before_retaining_ambient_transcript()
    {
        var store = new MultimodalSessionStore(new MemorySettingsStore());
        var service = new VisionVoiceSessionService(store);
        var initial = CreateSession();
        await store.CreateAsync(initial, CancellationToken.None);

        var denied = await service.SetRetentionAsync(initial.SessionId, 1, false, true, userExplicitlyOptedIn: false,
            cancellationToken: TestContext.Current.CancellationToken);
        var allowed = await service.SetRetentionAsync(initial.SessionId, 1, false, true, userExplicitlyOptedIn: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(VisionVoiceErrorCode.PermissionRequired, denied.Error!.Code);
        Assert.True(allowed.IsSuccess);
        Assert.NotNull(allowed.Value!.Retention.ExplicitlyEnabledAt);
        Assert.Equal(2, allowed.Value.Revision);
    }

    [Fact]
    public async Task Session_service_maps_stale_revision_to_structured_conflict()
    {
        var store = new MultimodalSessionStore(new MemorySettingsStore());
        var service = new VisionVoiceSessionService(store);
        var initial = CreateSession();
        await store.CreateAsync(initial, CancellationToken.None);

        var result = await service.TransitionAsync(initial.SessionId, 7, MultimodalSessionState.Paused,
            TestContext.Current.CancellationToken);

        Assert.Equal(VisionVoiceErrorCode.Conflict, result.Error!.Code);
        Assert.Equal("Revision", result.Error.Target);
    }

    private static MultimodalSession CreateSession() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        VisionVoiceMode.LiveListener,
        "runtime-session-1",
        "local-only",
        "model.voice.realtime",
        "voice.neutral",
        MultimodalSessionState.Listening,
        MicrophoneCaptureState.Listening,
        [],
        "microphone-default",
        "speaker-default",
        DateTimeOffset.UtcNow,
        1,
        VisionVoiceRetention.Ephemeral);

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingsExportManifest
            {
                Settings = _values.ToDictionary(pair => pair.Key, pair => JsonSerializer.Serialize(pair.Value), StringComparer.OrdinalIgnoreCase)
            });
        }

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingsImportResult(true, manifest.Settings, "Imported"));
        }
    }
}
