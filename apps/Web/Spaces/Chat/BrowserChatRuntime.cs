using System.Runtime.Versioning;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Spaces.Chat;
using NineToOne.Web.Spaces.Storage;

namespace NineToOne.Web.Spaces.Chat;

/// <summary>Composes the existing Chat pipeline and the exact private storage owner.
/// The hosted transport grants no account, Task, OS tool, Memory or attachment authority.</summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserChatRuntime : IBrowserPrivateContextParticipant
{
    private readonly BrowserPrivateTaskStorage _owner;
    private readonly BrowserDulcheHttpHandler _handler;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private bool _revoked;
    private Task? _close;
    public ChatSessionService Sessions { get; }
    public IChatSpaceBackend Backend { get; }
    public ProviderUsageCaptureBuffer Usage { get; }
    public IPrivacyPreferenceStore Privacy { get; }

    public BrowserChatRuntime(BrowserPrivateTaskStorage owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        var privacy = new BrowserPrivacyPreferenceStore(owner.Settings); Privacy = privacy;
        _handler = new(DemandCurrent, () => !RuntimeSafetyState.IsSafeMode &&
            privacy.TryGetCurrent(out var current) && !current!.LocalOnlyMode);
        _http = new(_handler, disposeHandler: false)
        {
            // This URI is only the .NET request namespace. The handler permits two
            // fixed paths; account-owned JS performs same-origin authenticated I/O.
            BaseAddress = new Uri("https://browser-host.invalid/api/dulche/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        Usage = new();
        var provider = new BrowserHostedOllamaClient(new OllamaClient(_http, Usage), DemandCurrent, privacy);
        var inventory = new ChatModelInventoryCache(provider);
        var policies = new ModelPermissionEvaluator(new VersionedModelPermissionStore(owner.Settings));
        var personalities = new ModelPersonalityService(new VersionedModelPersonalisationStore(owner.Settings));
        Sessions = new(owner.Conversations, provider, new CapabilityPreflightService(), owner.Safety,
            new WorkspaceToolRuntime(new BrowserUnavailableWorkspaceTools()),
            new ComputerToolRuntime(new BrowserUnavailableComputerTools()),
            modelInventory: inventory, personalities: personalities, modelPermissions: policies);
        var models = new ChatSessionSpaceModelCatalog(inventory, _ => Task.FromResult<string?>(null));
        var turns = new ChatSessionSpaceTurnExecutor(Sessions, owner.Conversations, CaptureContextAsync);
        Backend = new HavenChatSpaceBackend(owner.Conversations, owner.Production, owner.Versioning,
            owner.Attachments, models, turns);
    }

    private async Task<ChatSpaceTurnContext> CaptureContextAsync(ChatSpaceTurnRequest request, CancellationToken token)
    {
        DemandCurrent(); token.ThrowIfCancellationRequested();
        // This connection is hosted off-device. A Temporary local Chat may not
        // silently send its contents there, even though the host uses Ollama.
        if (request.Conversation.IsTemporary)
            throw new InvalidOperationException("This hosted model is not available for Temporary local Chat. Select a device-local model or save this Chat first.");
        if (request.AttachmentIds.Count != 0)
            throw new InvalidOperationException("The selected files are saved, but their original input approval is not connected to this hosted model yet. Remove them before sending.");
        var entries = await _owner.Conversations.GetContextEntriesAsync(request.Conversation.Id, token);
        DemandCurrent();
        return new(EffortLevel.Medium, "9to1", string.Empty, PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask)
        {
            ExplicitCapabilities = [ToolCapability.Text],
            RegisteredContext = entries.Count == 0 ? null : string.Join("\n\n", entries.Select(entry => entry.Title + "\n" + entry.Content)),
            OriginalSynchronousScope = _owner.RunOriginalSynchronousScope,
            GenerationOptions = new()
            {
                RequestedRoutingConstraints = new(AllowCloud: true, AllowFallback: false),
                RequestedContextConstraints = new(AllowPersistentMemoryRead: false)
            }
        };
    }
    private void DemandCurrent()
    {
        lock (_gate) if (_revoked) throw new ObjectDisposedException(nameof(BrowserChatRuntime));
        _owner.DemandPrivateContextCurrent();
    }
    public void RevokePrivateContext()
    {
        lock (_gate) _revoked = true; // Fence only; original cleanup runs after every owner is revoked.
        _handler.Revoke();
    }
    public ValueTask DisposeAsync()
    {
        Task actual; TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            _revoked = true; actual = CloseOriginalAsync(start.Task); _close = actual;
        }
        start.SetResult(); return new(actual);
    }
    private async Task CloseOriginalAsync(Task start)
    {
        await start;
        try { await _handler.DisposeAsync(); }
        finally { _http.Dispose(); }
    }
}
