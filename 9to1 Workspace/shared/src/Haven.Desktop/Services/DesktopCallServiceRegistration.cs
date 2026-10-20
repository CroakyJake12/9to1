/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/DesktopCallServiceRegistration.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns DesktopCallServiceRegistration. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Application.Call;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>
/// Represents desktop call service registration and keeps its related state and behavior together.
/// </summary>
public static class DesktopCallServiceRegistration
{
    /// <summary>
    /// Replaces infrastructure fallbacks with Windows desktop implementations while
    /// preserving a single process-wide instance for Call, preview, Notes dictation,
    /// Notes read aloud and interruption.
    /// </summary>
    public static IServiceCollection AddHavenDesktopCallServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IScreenShareService, WindowsGraphicsCaptureService>();
        services.AddSingleton<WindowsNaturalSpeechOutputService>();
        services.AddSingleton<HybridNaturalSpeechOutputService>();
        services.AddSingleton<ISpeechOutputService>(provider =>
            provider.GetRequiredService<HybridNaturalSpeechOutputService>());
        services.AddSingleton<ISpeechOutputWarmup>(provider =>
            provider.GetRequiredService<HybridNaturalSpeechOutputService>());

        // Call gets a deliberately smaller context and narrow instant social replies.
        // Re-registering these after Infrastructure keeps ordinary Chat/Studio on the
        // full IOllamaClient while the live voice surface uses the optimized wrapper.
        services.AddSingleton<CallOptimizedOllamaClient>();
        services.AddSingleton<IVoiceReactionActionRouter, SurfaceVoiceReactionActionRouter>();
        services.AddSingleton<CallCoordinator>(provider => new CallCoordinator(
            provider.GetRequiredService<ICallRepository>(),
            provider.GetRequiredService<IConversationRepository>(),
            provider.GetRequiredService<CallOptimizedOllamaClient>(),
            provider.GetRequiredService<ISpeechInputService>(),
            provider.GetRequiredService<ISpeechOutputService>(),
            provider.GetRequiredService<IScreenShareService>(),
            voiceProfiles: provider.GetRequiredService<VoiceProfileCatalog>(),
            voiceReactionRouter: provider.GetRequiredService<IVoiceReactionActionRouter>()));
        services.AddSingleton<ResponsiveCallCoordinator>();
        services.AddSingleton<ICallCoordinator>(provider =>
        {
            var models = provider.GetRequiredService<CallOptimizedOllamaClient>();
            _ = models.WarmDefaultSafelyAsync();
            return provider.GetRequiredService<ResponsiveCallCoordinator>();
        });

        // Shared Voice &amp; Vision session store + service: the canonical MultimodalSession
        // metadata graph, revisioned through the same versioned settings store used across the
        // desktop. This is the one process-wide session service every surface (pill/full/composer)
        // reads and writes; there is no second store or parallel session graph.
        services.AddSingleton<MultimodalSessionStore>();
        services.AddSingleton<VisionVoiceSessionService>();

        // The single shared Voice &amp; Vision runtime. One singleton owns the ONE live
        // ICallCoordinator: starting a session for a conversation attaches to the same canonical
        // SessionID, presenting a second surface reuses the same runtime, and closing a
        // presentation is never End (End is only the explicit runtime stop). The adapter
        // (VoiceVisionHostAdapter) is intentionally NOT a DI singleton: its conversation/space/
        // start-option providers are contextual per hosting surface, so each surface constructs it
        // with its own truthful providers rather than fabricating a default model or a permissive
        // local-only claim at composition time.
        services.AddSingleton<IVoiceVisionRuntime>(provider => new VoiceVisionLiveRuntime(
            provider.GetRequiredService<ICallCoordinator>(),
            provider.GetRequiredService<VisionVoiceSessionService>(),
            provider.GetRequiredService<IVersionedSettingsStore>()));

        services.AddSingleton<CallVoicePreviewController>(provider => new CallVoicePreviewController(
            provider.GetRequiredService<ISpeechOutputService>(),
            provider.GetRequiredService<IProductionDiagnostics>()));
        services.AddSingleton<CallCompletionController>();
        services.AddSingleton<NotesDictationController>();
        services.AddSingleton<NotesReadAloudController>();
        return services;
    }
}
