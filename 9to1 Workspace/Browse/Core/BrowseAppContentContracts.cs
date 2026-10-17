using Haven.Browser;
using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>Requested presentation identity. These values never authenticate a caller.</summary>
public sealed record BrowseAppContentRequest(Guid TabId, AppContentAddress Address,
    string ProfileId, BrowserTabPrivacy Privacy);

/// <summary>
/// The composition owner closes over the genuine shared services and authenticated
/// actor. It synchronously creates an unstarted per-tab owner so acquired work cannot
/// escape if initialization fails. Generic cui:// content uses a separate sandboxed owner.
/// </summary>
public interface IBrowseAppContentFactory
{
    IBrowseAppContentSession CreateSession(BrowseAppContentRequest request);
}

/// <summary>Explicit composition for Browse-owned Home and New Tab content only.
/// Invocation is thread-neutral; native owners marshal actual UI operations asynchronously.
/// This contract does not admit native-app or generic CUI namespaces.</summary>
public interface IBrowseInternalContentFactory
{
    IBrowseAppContentSession CreateSession(BrowseAppContentRequest request);
}

/// <summary>A navigation request from an existing content owner. The host retains
/// execution separately from the originating CUI action so retirement cannot join itself.</summary>
public sealed record BrowseNavigationIntent(Guid SourceTabId, AppContentAddress Address, bool OpenInNewTab);

/// <summary>
/// A borrowed-service native app presentation, shared by embedded and standalone
/// entry points. Closing a presentation does not dispose Home Core/shared services.
/// </summary>
public interface IBrowseAppContentSession
{
    AppContentAddress Address { get; }
    string Title { get; }
    string IconKey { get; }
    event EventHandler? StateChanged;
    bool IsPublicationCurrent { get; }
    bool CanRetireOriginalView { get; }
    Task? OriginalInitialization { get; }
    Task? OriginalClose { get; }

    /// <summary>Returns the same retained original initialization task on repeated calls.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves unsaved work/task ownership before retirement. False leaves the owner,
    /// commands and view alive. The exact returned task is retained by the tab owner.
    /// </summary>
    Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws admission and drains the actual owner; repeated calls return the same
    /// retained original task. A fault must not detach or replace a still-live view.
    /// </summary>
    Task CloseAndDrainAsync();
}
