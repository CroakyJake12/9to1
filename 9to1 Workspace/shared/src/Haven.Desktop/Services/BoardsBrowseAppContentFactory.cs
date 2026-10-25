using CakeOS.Cui.Themes;
using CakeOS.Apps.Boards.App;
using Haven.Browser;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Borrow the actual Home/Notes owners. Allocation creates no ownership, setup,
/// import, permission, model or Ready observation. Embedded and standalone entry points use
/// this same factory and shared Boards core/CUI; global services outlive each tab.</summary>
internal sealed class BoardsBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CancellationToken _appLifetime, _hostLifetime;
    private readonly Func<bool> _reduceMotion;
    private readonly CuiThemePreferenceContext? _theme;
    private readonly Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardReadLease>? _takePreparedBoardRead;
    private readonly Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardWriteLease>? _takePreparedBoardWrite;
    private BoardsBrowseAppContentFactory(IServiceProvider provider, HomeNativeWindowsComposition home,
        CancellationToken appLifetime, CancellationToken hostLifetime, Func<bool> reduceMotion,
        CuiThemePreferenceContext? theme,
        Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardReadLease>? takePreparedBoardRead,
        Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardWriteLease>? takePreparedBoardWrite)
    { _provider = provider; _home = home; _appLifetime = appLifetime; _hostLifetime = hostLifetime;
        _reduceMotion = reduceMotion; _theme = theme; _takePreparedBoardRead = takePreparedBoardRead; _takePreparedBoardWrite = takePreparedBoardWrite; }

    internal static BoardsBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider originalProvider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime,
        CancellationToken originalHostLifetime, Func<bool> actualReduceMotion,
        CuiThemePreferenceContext? actualThemePreference = null,
        Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardReadLease>? takePreparedBoardRead = null,
        Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardWriteLease>? takePreparedBoardWrite = null)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalProvider, sameHome);
        ArgumentNullException.ThrowIfNull(actualReduceMotion);
        if (!originalAppLifetime.CanBeCanceled || !originalHostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual application and host lifetimes.");
        originalAppLifetime.ThrowIfCancellationRequested(); originalHostLifetime.ThrowIfCancellationRequested();
        return new(originalProvider, sameHome, originalAppLifetime, originalHostLifetime,
            actualReduceMotion, actualThemePreference, takePreparedBoardRead, takePreparedBoardWrite);
    }

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _appLifetime.ThrowIfCancellationRequested(); _hostLifetime.ThrowIfCancellationRequested();
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original tab and actual Home profile.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("An owned device Boards library is not a private browser session.");
        var address = BoardsBrowseAppContentAddress.Parse(request.Address);
        if (address.IsFilesDocument)
        {
            if (address.Writable)
            {
                if (_takePreparedBoardWrite is null || address.StoreId is not { } writeStore ||
                    address.ParentFolderId is not { } writeParent || address.FileId is not { } writeFile ||
                    address.RevisionId is not { } writeRevision || address.ReadToken is not { } writeToken)
                    throw new UnauthorizedAccessException("A writable rich Files board requires its exact Home-prepared write lease.");
                var writeLease = _takePreparedBoardWrite(writeToken, writeStore, writeParent, writeFile, writeRevision);
                if (!writeLease.IsOpen || !StringComparer.Ordinal.Equals(writeLease.ProfileId, request.ProfileId))
                {
                    writeLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    throw new UnauthorizedAccessException("The selected writable board belongs to another original Home profile.");
                }
                var writeLifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
                BoardEmbeddedWritePageOwner? embeddedOwner = null;
                try
                {
                    embeddedOwner = new BoardEmbeddedWritePageOwner(_provider, _home, request.ProfileId,
                        request.TabId, _appLifetime, writeLifetime.Token,
                        () => !writeLifetime.IsCancellationRequested, _theme);
                    return new BoardsWritableRichDocumentBrowseAppContentSession(
                        request, address, writeLease, writeLifetime, embeddedOwner);
                }
                catch
                {
                    if (embeddedOwner is not null)
                        embeddedOwner.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    writeLifetime.Dispose();
                    writeLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    throw;
                }
            }
            if (_takePreparedBoardRead is null || address.StoreId is not { } store ||
                address.ParentFolderId is not { } parent || address.FileId is not { } file ||
                address.RevisionId is not { } revision || address.ReadToken is not { } readToken)
                throw new UnauthorizedAccessException("A rich Files board requires its exact Home-prepared read lease.");
            var lease = _takePreparedBoardRead(readToken, store, parent, file, revision);
            if (!lease.IsOpen || !StringComparer.Ordinal.Equals(lease.ProfileId, request.ProfileId))
            {
                lease.DisposeAsync().GetAwaiter().GetResult();
                throw new UnauthorizedAccessException("The selected rich board belongs to another original Home profile.");
            }
            var readLifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
            BoardEmbeddedWritePageOwner? readEmbeddedOwner = null;
            try
            {
                readEmbeddedOwner = new BoardEmbeddedWritePageOwner(_provider, _home, request.ProfileId,
                    request.TabId, _appLifetime, readLifetime.Token,
                    () => !readLifetime.IsCancellationRequested, _theme);
                return new BoardsRichDocumentBrowseAppContentSession(
                    request, address, lease, readLifetime, readEmbeddedOwner);
            }
            catch
            {
                if (readEmbeddedOwner is not null)
                    readEmbeddedOwner.DisposeAsync().AsTask().GetAwaiter().GetResult();
                readLifetime.Dispose();
                lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }
        }
        // All borrowed resolution and identity checks precede the per-tab lifetime acquisition.
        var notes = _provider.GetRequiredService<NotesRepository>();
        var verified = _provider.GetRequiredService<VerifiedNotesRepository>();
        var admission = _provider.GetRequiredService<HomeNotesResourceAdmissionSource>();
        var approvals = _provider.GetRequiredService<HomeNativeApprovalWindowOwner>();
        var evidence = _provider.GetRequiredService<NotesLocalStoreEvidenceProvider>();
        var setup = _provider.GetRequiredService<BoardsOriginalStoreSetupBackend>();
        if (!admission.IsBoundToOriginalNotes(notes) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NotesRepository)), notes) ||
            !ReferenceEquals(_home.Services.GetService(typeof(HomeNotesResourceAdmissionSource)), admission) ||
            !approvals.IsBoundToOriginalComposition(_home) || !setup.HasOriginalComposition(notes, evidence, _home, approvals))
            throw new UnauthorizedAccessException("Boards must borrow the SAME original Notes, verified store and Home admission/review owners.");
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
        try
        {
            var route = new NativeBoardsDesktopRoute(_provider, _home, notes, verified, admission,
                approvals, setup, request.ProfileId, "boards-tab:" + request.TabId.ToString("D"),
                _appLifetime, lifetime.Token);
            return new BoardsBrowseAppContentSession(request, address, route, lifetime, _reduceMotion, _theme);
        }
        catch { lifetime.Dispose(); throw; }
    }
}
