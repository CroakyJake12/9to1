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

        Assert.Equal(JsonSerializer.Serialize(updated), JsonSerializer.Serialize(await store.GetAsync(initial.SessionId, CancellationToken.None)));
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

    [Fact]
    public async Task Independent_surfaces_cannot_both_commit_the_same_revision()
    {
        var settings = new MemorySettingsStore();
        var first = new MultimodalSessionStore(settings);
        var second = new MultimodalSessionStore(settings);
        var initial = CreateSession();
        await first.CreateAsync(initial, CancellationToken.None);
        settings.SynchronizeTwoReads = true;
        async Task<bool> Attempt(MultimodalSessionStore store, MultimodalSessionState state)
        {
            try { await store.UpdateAsync(initial.SessionId, 1, initial with { Revision = 2, State = state }, CancellationToken.None); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(Attempt(first, MultimodalSessionState.Paused), Attempt(second, MultimodalSessionState.Ended));
        Assert.Single(results, succeeded => succeeded);
        Assert.Equal(2, (await first.GetAsync(initial.SessionId, CancellationToken.None))!.Revision);
    }

    [Fact]
    public async Task Awaited_create_and_update_capture_nested_languages_sources_and_resume_outline()
    {
        var settings = new MemorySettingsStore { PauseNextRead = true };
        var store = new MultimodalSessionStore(settings);
        string[] locales = ["en-GB", "fr-FR"];
        LiveTranslateOutputPreference[] outputs = [new("fr-FR", true, false)];
        string[] glossaries = ["original-glossary"];
        VisualSourceDescriptor[] sources = [new(Guid.NewGuid(), VisualSourceType.UploadedImage,
            "explicit-upload", "original", VisualSourceState.Active, false, true, DateTimeOffset.UtcNow)];
        var initial = CreateSession() with { VoiceMode = VisionVoiceMode.LiveTranslate,
            VisualSources = sources, LiveTranslateLanguages = new("en-GB", locales,
                LiveTranslateDirection.OneWay, outputs, glossaries) };
        var create = store.CreateAsync(initial, TestContext.Current.CancellationToken);
        await settings.ReadPaused.Task.WaitAsync(TestContext.Current.CancellationToken);
        locales[1] = "de-DE"; outputs[0] = new("de-DE", false, true); glossaries[0] = "injected";
        sources[0] = sources[0] with { DisplayName = "injected" };
        settings.ResumeRead.TrySetResult();
        var created = await create;
        var reopened = (await store.GetAsync(initial.SessionId, TestContext.Current.CancellationToken))!;
        Assert.Equal("fr-FR", reopened.LiveTranslateLanguages!.Locales[1]);
        Assert.Equal("fr-FR", reopened.LiveTranslateLanguages.Outputs[0].Locale);
        Assert.Equal("original-glossary", reopened.LiveTranslateLanguages.GlossaryIds[0]);
        Assert.Equal("original", reopened.VisualSources[0].DisplayName);
        Assert.Equal("fr-FR", created.LiveTranslateLanguages!.Locales[1]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)created.LiveTranslateLanguages.Locales)[1] = "changed");

        string[] sections = ["original-plan"]; string[] refs = ["source-entity"];
        var updated = reopened with { Revision = 2, VoiceMode = VisionVoiceMode.Monologue,
            LiveTranslateLanguages = null, Monologue = new(Guid.NewGuid(), "Brief", null, sections, 0, TimeSpan.Zero, true, refs) };
        settings.ReadPaused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.ResumeRead = new(TaskCreationOptions.RunContinuationsAsynchronously); settings.PauseNextRead = true;
        var update = store.UpdateAsync(initial.SessionId, 1, updated, TestContext.Current.CancellationToken);
        await settings.ReadPaused.Task.WaitAsync(TestContext.Current.CancellationToken);
        sections[0] = "injected-plan"; refs[0] = "injected-ref";
        settings.ResumeRead.TrySetResult();
        await update;
        reopened = (await store.GetAsync(initial.SessionId, TestContext.Current.CancellationToken))!;
        Assert.Equal("original-plan", reopened.Monologue!.Sections[0]);
        Assert.Equal("source-entity", reopened.Monologue.SourceRefs[0]);
    }

    [Fact]
    public async Task Unsupported_capture_state_and_missing_translation_language_set_never_replace_metadata()
    {
        var settings = new MemorySettingsStore(); var store = new MultimodalSessionStore(settings);
        var original = CreateSession(); await store.CreateAsync(original, TestContext.Current.CancellationToken);
        foreach (var invalid in new[]
        {
            original with { Revision = 2, VoiceMode = (VisionVoiceMode)99 },
            original with { Revision = 2, State = (MultimodalSessionState)99 },
            original with { Revision = 2, MicrophoneState = (MicrophoneCaptureState)99 },
            original with { Revision = 2, SpaceId = Guid.Empty },
            original with { Revision = 2, SpaceId = Guid.NewGuid() },
            original with { Revision = 2, VoiceMode = VisionVoiceMode.LiveTranslate, LiveTranslateLanguages = null },
            original with { Revision = 2, VisualSources = [null!] }
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateAsync(original.SessionId, 1, invalid, TestContext.Current.CancellationToken));
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(await store.GetAsync(original.SessionId, TestContext.Current.CancellationToken)));
        }
        var key = "visionvoice.multimodal-session.v1." + original.SessionId.ToString("N");
        var unknownState = new MultimodalSessionStore.MultimodalSessionEnvelope(1, original with { State = (MultimodalSessionState)99 });
        await settings.SetAsync(key, unknownState, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(original.SessionId, TestContext.Current.CancellationToken));
        Assert.Same(unknownState, await settings.GetAsync<MultimodalSessionStore.MultimodalSessionEnvelope>(key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Mode_change_captures_selected_languages_before_awaited_canonical_session_read()
    {
        var settings = new MemorySettingsStore(); var store = new MultimodalSessionStore(settings);
        var initial = CreateSession(); await store.CreateAsync(initial, TestContext.Current.CancellationToken);
        string[] locales = ["en-GB", "fr-FR"];
        LiveTranslateOutputPreference[] outputs = [new("fr-FR", true, false)];
        string[] glossaries = ["selected-glossary"];
        settings.PauseNextGet = true;
        var change = new VisionVoiceSessionService(store).SetModeAsync(initial.SessionId, 1, VisionVoiceMode.LiveTranslate,
            new("en-GB", locales, LiveTranslateDirection.OneWay, outputs, glossaries), TestContext.Current.CancellationToken);
        await settings.ReadPaused.Task.WaitAsync(TestContext.Current.CancellationToken);
        locales[1] = "de-DE"; outputs[0] = new("de-DE", false, true); glossaries[0] = "injected-glossary";
        settings.ResumeRead.TrySetResult();
        var result = await change;
        Assert.True(result.IsSuccess);
        Assert.Equal(initial.ConversationId, result.Value!.ConversationId);
        Assert.Equal(initial.SpaceId, result.Value.SpaceId);
        Assert.Equal("fr-FR", result.Value.LiveTranslateLanguages!.Locales[1]);
        Assert.Equal("fr-FR", result.Value.LiveTranslateLanguages.Outputs[0].Locale);
        Assert.Equal("selected-glossary", result.Value.LiveTranslateLanguages.GlossaryIds[0]);
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

    [Theory]
    [InlineData(MultimodalSessionState.Starting)]
    [InlineData(MultimodalSessionState.Listening)]
    [InlineData(MultimodalSessionState.UserSpeaking)]
    [InlineData(MultimodalSessionState.Processing)]
    [InlineData(MultimodalSessionState.AssistantSpeaking)]
    [InlineData(MultimodalSessionState.Paused)]
    [InlineData(MultimodalSessionState.Reconnecting)]
    [InlineData(MultimodalSessionState.Stopped)]
    [InlineData(MultimodalSessionState.Ended)]
    [InlineData(MultimodalSessionState.Failed)]
    public async Task Repeated_state_commits_a_revision_without_claiming_new_capture_or_changing_session_metadata(MultimodalSessionState state)
    {
        var settings = new MemorySettingsStore(); var store = new MultimodalSessionStore(settings);
        var initial = CreateSession() with { State = state, MicrophoneState = MicrophoneCaptureState.Off };
        await store.CreateAsync(initial, CancellationToken.None);
        var service = new VisionVoiceSessionService(store);
        var result = await service.TransitionAsync(initial.SessionId, 1, state, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Revision);
        Assert.Equal(JsonSerializer.Serialize(initial with { Revision = 2 }), JsonSerializer.Serialize(result.Value));
        Assert.Equal(JsonSerializer.Serialize(result.Value), JsonSerializer.Serialize(await store.GetAsync(initial.SessionId, CancellationToken.None)));
        var stale = await service.TransitionAsync(initial.SessionId, 1, state, CancellationToken.None);
        Assert.Equal(VisionVoiceErrorCode.Conflict, stale.Error!.Code);
        Assert.Equal(2, (await store.GetAsync(initial.SessionId, CancellationToken.None))!.Revision);
    }

    [Fact]
    public async Task Same_state_command_cannot_overwrite_a_competing_surface_after_its_original_read()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var ct = timeout.Token;
        var settings = new MemorySettingsStore(); var store = new MultimodalSessionStore(settings);
        var initial = CreateSession(); await store.CreateAsync(initial, ct);
        settings.PauseNextGet = true;
        var command = new VisionVoiceSessionService(store).TransitionAsync(initial.SessionId, 1, initial.State, ct);
        await settings.ReadPaused.Task.WaitAsync(ct);
        var competing = initial with { Revision = 2, State = MultimodalSessionState.Paused };
        try { await new MultimodalSessionStore(settings).UpdateAsync(initial.SessionId, 1, competing, ct); }
        finally { settings.ResumeRead.TrySetResult(); }
        var result = await command;
        Assert.Equal(VisionVoiceErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(JsonSerializer.Serialize(competing), JsonSerializer.Serialize(await store.GetAsync(initial.SessionId, ct)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Exhausted_persisted_session_revision_returns_conflict_and_preserves_exact_metadata(int action)
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new MemorySettingsStore(); var store = new MultimodalSessionStore(settings);
        var session = CreateSession() with { Revision = long.MaxValue };
        var key = "visionvoice.multimodal-session.v1." + session.SessionId.ToString("N");
        var envelope = new MultimodalSessionStore.MultimodalSessionEnvelope(1, session);
        await settings.SetAsync(key, envelope, ct); // Actual persisted boundary metadata, not a substituted service.
        var before = JsonSerializer.Serialize(envelope);
        var service = new VisionVoiceSessionService(store);
        var result = action switch
        {
            0 => await service.TransitionAsync(session.SessionId, long.MaxValue, session.State, ct),
            1 => await service.SetModeAsync(session.SessionId, long.MaxValue, session.VoiceMode, cancellationToken: ct),
            _ => await service.SetRetentionAsync(session.SessionId, long.MaxValue, true, true, true, ct)
        };
        Assert.False(result.IsSuccess);
        Assert.Equal(VisionVoiceErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(before, JsonSerializer.Serialize(await settings.GetAsync<MultimodalSessionStore.MultimodalSessionEnvelope>(key, ct)));
        Assert.Equal(JsonSerializer.Serialize(session), JsonSerializer.Serialize(await store.GetAsync(session.SessionId, ct)));
        Assert.False(MultimodalSessionLifecycle.TryTransition(session, session.State, out var same, out _));
        Assert.Same(session, same);
        Assert.False(MultimodalSessionLifecycle.TrySetMode(session, session.VoiceMode, out var mode, out _));
        Assert.Same(session, mode);
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
        public bool SynchronizeTwoReads { get; set; }
        public bool PauseNextRead; public bool PauseNextGet;
        public TaskCompletionSource ReadPaused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_values)
            {
                var current = _values.TryGetValue(key, out var value) ? JsonSerializer.Serialize(value) : null;
                if (current != expectedJson) return Task.FromResult(new SettingsCompareExchangeResult(false, current, 1));
                if (replacementJson is null) _values.Remove(key);
                else _values[key] = JsonSerializer.Deserialize<MultimodalSessionStore.MultimodalSessionEnvelope>(replacementJson)!;
                return Task.FromResult(new SettingsCompareExchangeResult(true, replacementJson, 1));
            }
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            var captured = _values.TryGetValue(key, out var value) ? value as T : null;
            if (PauseNextGet)
            {
                PauseNextGet = false; ReadPaused.TrySetResult();
                await ResumeRead.Task.WaitAsync(cancellationToken);
            }
            return captured;
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

        public async Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SettingsExportManifest snapshot;
            lock (_values) snapshot = new SettingsExportManifest
            {
                Settings = _values.ToDictionary(pair => pair.Key, pair => JsonSerializer.Serialize(pair.Value), StringComparer.OrdinalIgnoreCase)
            };
            if (PauseNextRead)
            {
                PauseNextRead = false; ReadPaused.TrySetResult();
                await ResumeRead.Task.WaitAsync(cancellationToken);
            }
            if (SynchronizeTwoReads)
            {
                if (Interlocked.Increment(ref _readCount) == 2) { SynchronizeTwoReads = false; _bothRead.TrySetResult(); }
                await _bothRead.Task.WaitAsync(cancellationToken);
            }
            return snapshot;
        }

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingsImportResult(true, manifest.Settings, "Imported"));
        }
    }
}
