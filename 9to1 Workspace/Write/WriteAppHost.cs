using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Write;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using HavenOS.Home.Core;
using HavenOS.Apps.Canvas;

namespace HavenOS.Apps.Write;

public enum WriteDocumentEngineLifetimeOwner
{
    None,
    Caller,
    HostServiceProvider
}

public enum WriteDocumentEngineNegotiation
{
    Unavailable,
    RejectedCapabilities,
    CapabilityReadyButSurfaceUnwired
}

/// <summary>Bootstrap-only engine negotiation. Capability readiness is not proof that the
/// current WritePage renders, edits, or saves through this engine.</summary>
public sealed record WriteDocumentEngineBootstrapStatus(
    string OwnerId,
    WriteDocumentEngineLifetimeOwner LifetimeOwner,
    WriteDocumentEngineNegotiation Negotiation,
    WriteEngineCapabilities? Capabilities,
    IReadOnlyList<string> MissingCapabilities,
    string Message)
{
    public bool IsConnectedToEditor => false;
}

/// <summary>Owns the existing Write page and the services acquired by this standalone app.</summary>
public sealed class WriteAppHost : IAsyncDisposable
{
    private readonly HavenEventBus _eventBus;
    private readonly ServiceProvider? _ownedServices;
    private readonly INotesImportExportService _formats;
    private bool _disposed;

    private WriteAppHost(WritePage page, HavenEventBus eventBus, INotesImportExportService formats,
        ServiceProvider? ownedServices, IWriteDocumentEngine? documentEngine,
        WriteDocumentEngineBootstrapStatus documentEngineBootstrap)
    {
        Page = page;
        _eventBus = eventBus;
        _formats = formats;
        _ownedServices = ownedServices;
        DocumentEngine = documentEngine;
        DocumentEngineBootstrap = documentEngineBootstrap;
    }

    public WritePage Page { get; }
    public NotesDocument? Document => Page.Document;
    public bool IsDisposed => _disposed;
    /// <summary>The negotiated engine candidate, if one was supplied. The current page does not
    /// call it; ownership follows <see cref="DocumentEngineBootstrap"/>.</summary>
    public IWriteDocumentEngine? DocumentEngine { get; }
    public WriteDocumentEngineBootstrapStatus DocumentEngineBootstrap { get; }

    public static Task<WriteAppHost> CreateDefaultAsync(Guid? initialDocumentId = null) =>
        CreateDefaultCoreAsync(initialDocumentId, null, null, null);

    public static Task<WriteAppHost> CreateDefaultAsync(Guid? initialDocumentId, HomeProductivityEngine sharedInkEngine)
    {
        ArgumentNullException.ThrowIfNull(sharedInkEngine);
        return CreateDefaultCoreAsync(initialDocumentId, sharedInkEngine, null, null);
    }

    /// <summary>Uses an externally supplied engine only for bootstrap negotiation. The named
    /// caller retains its lifetime; this does not connect the current editor surface to it.</summary>
    public static Task<WriteAppHost> CreateDefaultAsync(Guid? initialDocumentId,
        IWriteDocumentEngine documentEngine, string documentEngineOwnerId)
    {
        ArgumentNullException.ThrowIfNull(documentEngine);
        DemandOwnerId(documentEngineOwnerId);
        return CreateDefaultCoreAsync(initialDocumentId, null, documentEngine, documentEngineOwnerId);
    }

    private static async Task<WriteAppHost> CreateDefaultCoreAsync(Guid? initialDocumentId,
        HomeProductivityEngine? sharedInkEngine, IWriteDocumentEngine? injectedDocumentEngine,
        string? injectedDocumentEngineOwnerId)
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddHavenDesktopCallServices();
        // Read aloud consumes the original Call owner without acquiring the
        // desktop registration's unrelated, unawaited model warmup at startup.
        services.AddSingleton<ICallCoordinator>(provider =>
            provider.GetRequiredService<ResponsiveCallCoordinator>());
        services.AddSingleton<IWriteNativeDocumentPackageStore, WriteNativeDocumentPackageStore>();
        var provider = services.BuildServiceProvider();
        try
        {
            var documentEngine = injectedDocumentEngine ?? provider.GetService<IWriteDocumentEngine>();
            var documentEngineOwnerId = injectedDocumentEngine is not null
                ? injectedDocumentEngineOwnerId!
                : documentEngine is null ? null : "WriteAppHost.ServiceProvider";
            var lifetimeOwner = injectedDocumentEngine is not null
                ? WriteDocumentEngineLifetimeOwner.Caller
                : documentEngine is null ? WriteDocumentEngineLifetimeOwner.None : WriteDocumentEngineLifetimeOwner.HostServiceProvider;
            return CreateCore(
                provider.GetRequiredService<INotesRepository>(),
                provider.GetRequiredService<INotesImportExportService>(),
                provider.GetRequiredService<INotesAttachmentStore>(),
                initialDocumentId,
                provider.GetRequiredService<INotesAiService>(),
                provider.GetRequiredService<IProviderModelClient>(),
                provider.GetRequiredService<NotesReadAloudController>(),
                provider.GetRequiredService<IWriteNativeDocumentPackageStore>(),
                provider, sharedInkEngine, documentEngine, documentEngineOwnerId, lifetimeOwner);
        }
        catch (Exception acquisitionFailure)
        {
            try { await provider.DisposeAsync(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Write startup and service cleanup failed.",
                    acquisitionFailure, cleanupFailure);
            }
            throw;
        }
    }

    public static WriteAppHost Create(
        INotesRepository repository,
        INotesImportExportService formats,
        Guid? initialDocumentId = null,
        INotesAttachmentStore? attachments = null,
        INotesAiService? ai = null,
        IOllamaClient? aiModels = null,
        NotesReadAloudController? readAloud = null,
        IWriteNativeDocumentPackageStore? nativePackageStore = null,
        IWriteDocumentEngine? documentEngine = null,
        string? documentEngineOwnerId = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(formats);
        ValidateInjectedEngine(documentEngine, documentEngineOwnerId);
        return CreateCore(repository, formats, attachments, initialDocumentId, ai, aiModels,
            readAloud, nativePackageStore, ownedServices: null, sharedInkEngine: null,
            documentEngine: documentEngine, documentEngineOwnerId: documentEngineOwnerId,
            lifetimeOwner: documentEngine is null ? WriteDocumentEngineLifetimeOwner.None : WriteDocumentEngineLifetimeOwner.Caller);
    }

    /// <summary>Borrows the host's actual Home engine without replacing its registry or lifetime.</summary>
    public static WriteAppHost Create(
        INotesRepository repository, INotesImportExportService formats, Guid? initialDocumentId,
        INotesAttachmentStore? attachments, INotesAiService? ai, IOllamaClient? aiModels,
        NotesReadAloudController? readAloud, IWriteNativeDocumentPackageStore? nativePackageStore,
        HomeProductivityEngine sharedInkEngine, IWriteDocumentEngine? documentEngine = null,
        string? documentEngineOwnerId = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(sharedInkEngine);
        ValidateInjectedEngine(documentEngine, documentEngineOwnerId);
        return CreateCore(repository, formats, attachments, initialDocumentId, ai, aiModels,
            readAloud, nativePackageStore, ownedServices: null, sharedInkEngine: sharedInkEngine,
            documentEngine: documentEngine, documentEngineOwnerId: documentEngineOwnerId,
            lifetimeOwner: documentEngine is null ? WriteDocumentEngineLifetimeOwner.None : WriteDocumentEngineLifetimeOwner.Caller);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Page.InitializeAsync(cancellationToken);
    }

    public Task<bool> OpenDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Page.OpenDocumentAsync(documentId, cancellationToken);
    }

    public Task<bool> TrySaveBeforeCloseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Page.PrepareToCloseAsync("Autosave before closing Write", cancellationToken);
    }

    /// <summary>
    /// Hands the open document to the owner's print service. The document must be
    /// open: printing the last closed document would silently print work the
    /// writer can no longer see, so the guard is an explicit failure instead.
    /// </summary>
    public Task PrintAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Document is not { } document) throw new InvalidOperationException("Open a document first.");
        return _formats.PrintAsync(document, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!await TrySaveBeforeCloseAsync())
            throw new InvalidOperationException("Write has pending work or an unsaved document. Finish it before closing.");

        _disposed = true;
        var failures = new List<Exception>();
        try { await Page.CloseAndDrainAsync(); }
        catch (Exception error) { failures.Add(error); }
        try { _eventBus.Dispose(); }
        catch (Exception error) { failures.Add(error); }
        if (_ownedServices is not null)
        {
            try { await _ownedServices.DisposeAsync(); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0)
            throw new AggregateException("Write service cleanup failed after saving the document.", failures);
    }

    private static WriteAppHost CreateCore(
        INotesRepository repository, INotesImportExportService formats,
        INotesAttachmentStore? attachments, Guid? initialDocumentId,
        INotesAiService? ai, IOllamaClient? aiModels, NotesReadAloudController? readAloud,
        IWriteNativeDocumentPackageStore? nativePackageStore, ServiceProvider? ownedServices,
        HomeProductivityEngine? sharedInkEngine, IWriteDocumentEngine? documentEngine,
        string? documentEngineOwnerId, WriteDocumentEngineLifetimeOwner lifetimeOwner)
    {
        var eventBus = new HavenEventBus();
        try
        {
            // Same Home-authoritative implementation/handler as the embedded surface. The
            // donor is only activated by the shared input's real capability probe.
            var engine = sharedInkEngine ?? new HomeProductivityEngine(handlers: [new CanvasInkObjectHandler()]);
            var page = new WritePage(engine, eventBus, repository, formats, attachments, initialDocumentId,
                ai, aiModels, readAloud, nativePackageStore);
            var bootstrap = NegotiateDocumentEngine(documentEngine, documentEngineOwnerId, lifetimeOwner);
            return new WriteAppHost(page, eventBus, formats, ownedServices, documentEngine, bootstrap);
        }
        catch
        {
            eventBus.Dispose();
            throw;
        }
    }

    private static WriteDocumentEngineBootstrapStatus NegotiateDocumentEngine(
        IWriteDocumentEngine? engine, string? ownerId, WriteDocumentEngineLifetimeOwner lifetimeOwner)
    {
        if (engine is null)
            return new("none", WriteDocumentEngineLifetimeOwner.None, WriteDocumentEngineNegotiation.Unavailable,
                null, ["IWriteDocumentEngine is not registered or injected."],
                "The current WritePage remains on NotesRepository storage; no donor document engine is available.");

        var identity = string.IsNullOrWhiteSpace(ownerId) ? "unspecified-owner" : ownerId;
        WriteEngineCapabilities capabilities;
        try
        {
            capabilities = engine.Capabilities;
            if (capabilities is null) throw new InvalidOperationException("The engine returned no capability record.");
        }
        catch (Exception error)
        {
            return new(identity, lifetimeOwner, WriteDocumentEngineNegotiation.RejectedCapabilities, null,
                ["Capabilities query failed"], "The supplied document engine could not report capabilities: " + error.Message);
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(capabilities.EngineId)) missing.Add("engine identity");
        if (string.IsNullOrWhiteSpace(capabilities.EngineVersion)) missing.Add("engine version");
        if (!capabilities.SupportsTiledRendering) missing.Add("tiled rendering");
        if (!capabilities.SupportsTextInput) missing.Add("text input");
        if (!capabilities.SupportsPointerInput) missing.Add("pointer input");
        if (!capabilities.SupportsSemanticCommands) missing.Add("semantic commands");
        if (!capabilities.SupportsAccessibilitySnapshot) missing.Add("accessibility snapshot");
        if (missing.Count != 0)
            return new(identity, lifetimeOwner, WriteDocumentEngineNegotiation.RejectedCapabilities,
                capabilities, missing.AsReadOnly(), "The supplied document engine lacks required capabilities: " + string.Join(", ", missing) + ".");

        return new(identity, lifetimeOwner, WriteDocumentEngineNegotiation.CapabilityReadyButSurfaceUnwired,
            capabilities, [], "Engine capabilities were negotiated, but the current WritePage is not connected to this engine; NotesDocument remains authoritative.");
    }

    private static void ValidateInjectedEngine(IWriteDocumentEngine? engine, string? ownerId)
    {
        if (engine is null && ownerId is not null)
            throw new ArgumentException("An engine owner ID requires an injected document engine.", nameof(ownerId));
        if (engine is not null) DemandOwnerId(ownerId);
    }

    private static void DemandOwnerId(string? ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Injected document engines require an explicit lifetime owner ID.", nameof(ownerId));
    }
}
