using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Write;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Apps.Write;

/// <summary>Owns the existing Write page and the services acquired by this standalone app.</summary>
public sealed class WriteAppHost : IAsyncDisposable
{
    private readonly HavenEventBus _eventBus;
    private readonly ServiceProvider? _ownedServices;
    private bool _disposed;

    private WriteAppHost(WritePage page, HavenEventBus eventBus, ServiceProvider? ownedServices)
    {
        Page = page;
        _eventBus = eventBus;
        _ownedServices = ownedServices;
    }

    public WritePage Page { get; }
    public NotesDocument? Document => Page.Document;
    public bool IsDisposed => _disposed;

    public static async Task<WriteAppHost> CreateDefaultAsync(Guid? initialDocumentId = null)
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
            return CreateCore(
                provider.GetRequiredService<INotesRepository>(),
                provider.GetRequiredService<INotesImportExportService>(),
                provider.GetRequiredService<INotesAttachmentStore>(),
                initialDocumentId,
                provider.GetRequiredService<INotesAiService>(),
                provider.GetRequiredService<IProviderModelClient>(),
                provider.GetRequiredService<NotesReadAloudController>(),
                provider.GetRequiredService<IWriteNativeDocumentPackageStore>(),
                provider);
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
        IWriteNativeDocumentPackageStore? nativePackageStore = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(formats);
        return CreateCore(repository, formats, attachments, initialDocumentId, ai, aiModels,
            readAloud, nativePackageStore, ownedServices: null);
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!await TrySaveBeforeCloseAsync())
            throw new InvalidOperationException("Write has pending work or an unsaved document. Finish it before closing.");

        _disposed = true;
        var failures = new List<Exception>();
        try { Page.Dispose(); }
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
        IWriteNativeDocumentPackageStore? nativePackageStore, ServiceProvider? ownedServices)
    {
        var eventBus = new HavenEventBus();
        try
        {
            var page = new WritePage(eventBus, repository, formats, attachments, initialDocumentId,
                ai, aiModels, readAloud, nativePackageStore);
            return new WriteAppHost(page, eventBus, ownedServices);
        }
        catch
        {
            eventBus.Dispose();
            throw;
        }
    }
}
