using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application.Call;
using Haven.Desktop.Views.Shell.VoiceVision;

namespace Haven.Desktop.Tests.VoiceVision;

/// <summary>
/// Focused presentation-layer checks for the Voice &amp; Vision CUI:
/// both authored documents parse without CUI errors, and the binding context projects
/// truthful state, gates action availability by real capability, and routes known
/// commands to the host. These tests read the authored .cui sources from the repository
/// so they validate the exact shipped markup without relying on EmbeddedResource wiring.
/// </summary>
public sealed class VoiceVisionCuiPresentationTests
{
    [Fact]
    public void Pill_and_session_documents_parse_without_CUI_errors()
    {
        AssertValid(PillPath, "VoiceVisionPill.cui");
        AssertValid(SessionPath, "VoiceVisionSession.cui");
    }

    [Fact]
    public void Bindings_project_truthful_capability_gated_state()
    {
        var state = ActiveState();
        var bindings = CreateBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

        Assert.True(bindings.TryGetValue("pill.statusText", out var status));
        Assert.Equal("Listening", status);
        Assert.True(bindings.TryGetValue("pill.canStart", out var canStart));
        Assert.False((bool)canStart!);
        Assert.True(bindings.TryGetValue("pill.canEnd", out var canEnd));
        Assert.True((bool)canEnd!);
        Assert.True(bindings.TryGetValue("pill.muteOpacity", out var opacity));
        Assert.Equal(0.35, opacity);

        Assert.True(bindings.TryGetValue("session.visualSources", out var sources));
        var list = Assert.IsAssignableFrom<IReadOnlyList<VisualSourceDescriptor>>(sources);
        Assert.Single(list);
        Assert.True(bindings.TryGetItemValue(list[0], "typeLabel", out var type));
        Assert.Equal("Camera", type);
    }

    [Fact]
    public void Bindings_gate_availability_by_runtime_capability()
    {
        var idle = VoiceVisionRuntimeState.Empty;
        var bindings = CreateBindings(() => idle, (_, _, _) => ValueTask.CompletedTask);

        // Empty projection is truthful: nothing can be ended/muted/shared without a live session.
        Assert.False(bindings.IsActionAvailable("voice.start"));
        Assert.False(bindings.IsActionAvailable("voice.end"));
        Assert.False(bindings.IsActionAvailable("voice.mute"));
        Assert.False(bindings.IsActionAvailable("voice.screen-share"));
        Assert.True(bindings.IsActionAvailable("voice.captions")); // captions can always be toggled
        Assert.True(bindings.IsActionAvailable("voice.open-full"));
        Assert.Null(bindings.IsActionAvailable("unknown.command"));

        // A ready projection with start capability and an active session admit the right actions.
        var canStart = idle with { CanStart = true };
        var startBindings = CreateBindings(() => canStart, (_, _, _) => ValueTask.CompletedTask);
        Assert.True(startBindings.IsActionAvailable("voice.start"));
        Assert.False(startBindings.IsActionAvailable("voice.end"));
    }

    [Fact]
    public async Task Bindings_dispatch_known_commands_to_the_host()
    {
        var state = ActiveState();
        var dispatched = new List<(string Command, object? Parameter)>();
        var bindings = CreateBindings(() => state, (command, parameter, _) =>
        {
            dispatched.Add((command, parameter));
            return ValueTask.CompletedTask;
        });

        await bindings.DispatchAsync("voice.end", null, CancellationToken.None);
        await bindings.DispatchAsync("voice.pause-resume", null, CancellationToken.None);

        Assert.Equal(2, dispatched.Count);
        Assert.Equal("voice.end", dispatched[0].Command);
        Assert.Equal("voice.pause-resume", dispatched[1].Command);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => bindings.DispatchAsync("voice.not-ours", null, CancellationToken.None).AsTask());
    }

    private static VoiceVisionCuiBindings CreateBindings(
        Func<VoiceVisionRuntimeState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch)
        => new(state, dispatch);

    private static VoiceVisionRuntimeState ActiveState() => new(
        SessionId: Guid.NewGuid(),
        ConversationId: Guid.NewGuid(),
        SpaceId: null,
        Mode: VisionVoiceMode.Conversational,
        State: MultimodalSessionState.Listening,
        Presentation: VoiceVisionPresentationKind.Pill,
        IsActive: true,
        IsMuted: true,
        IsPaused: false,
        IsListening: true,
        IsSpeaking: false,
        IsProcessing: false,
        IsReconnecting: false,
        IsFailed: false,
        AudioLevel: 0.5,
        StatusText: "Listening",
        PermissionNotice: string.Empty,
        IsLocalOnly: true,
        EffectiveModelPolicy: "local-test",
        VoiceModelId: "model.voice",
        VoiceId: "v1",
        SelectedVoiceName: "Neutral",
        ModeLabel: "Conversational",
        VoiceNames: ["Neutral"],
        SelectedVoiceIndex: 0,
        ModeNames: ["Conversational"],
        SelectedModeIndex: 0,
        CanStart: false,
        CanEnd: true,
        CanShareScreen: false,
        CanUseCamera: false,
        CanEnableCaptions: true,
        IsScreenSharing: false,
        HasCameraSource: true,
        AreCaptionsEnabled: true,
        IsTranscriptVisible: false,
        VisualSources: [new(Guid.NewGuid(), VisualSourceType.Camera, "camera", "Camera", VisualSourceState.Active, true, true, DateTimeOffset.UtcNow)]);

    private static void AssertValid(string path, string sourceName)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Authored Voice &amp; Vision CUI source not found at {path}.");
        var parser = new CuiRichParser();
        var document = parser.Parse(File.ReadAllText(path), sourceName);
        Assert.NotNull(document);
        Assert.DoesNotContain(parser.Diagnostics.Diagnostics, d => d.Severity == CuiDiagnosticSeverity.Error);
    }

    private static string PillPath => Path.Combine(RepoRoot, "9to1 Workspace/shared/src/Haven.Desktop/Interface/Shell/Pop-Ups/VoiceVision/VoiceVisionPill.cui");
    private static string SessionPath => Path.Combine(RepoRoot, "9to1 Workspace/shared/src/Haven.Desktop/Interface/Shell/Pop-Ups/VoiceVision/VoiceVisionSession.cui");

    private static string RepoRoot =>
        LocateRepoRoot();

    private static string LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "9to1 Workspace")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The repository root could not be located from the test base directory.");
    }
}
