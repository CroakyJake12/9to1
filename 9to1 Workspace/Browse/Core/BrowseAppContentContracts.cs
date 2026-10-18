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

/// <summary>
/// Composition-issued proof that the trusted native app-content registry holds no
/// binding for the exact request this chrome minted for one admission. The registry
/// creates it at its binding lookup, strictly before any session, owner, view or
/// subscription exists, so the chrome can treat that one admission as having nothing
/// to retire: the action stays failed and inspectable and only the retirement
/// obligation is withdrawn.
/// The constructor is internal to the composition boundary, so an ordinary factory
/// cannot claim this proof after acquiring effects, and a refusal naming a different
/// request never matches. Every other fault, including a factory that throws after
/// taking an effect, a wrapper that reuses a genuine refusal, an unwitnessed refusal
/// and any mixed fault graph, stays a conservative retirement failure.
/// </summary>
public abstract class BrowseNativeAdmissionRefusal : NotSupportedException
{
    internal BrowseNativeAdmissionRefusal(BrowseAppContentRequest request, IBrowseAppContentFactory issuingFactory)
        : base("This app does not have a registered native surface in the current host.")
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        IssuingFactory = issuingFactory ?? throw new ArgumentNullException(nameof(issuingFactory));
    }

    /// <summary>The exact chrome-minted request this refusal answers. Provenance is
    /// per admission, so a captured refusal cannot bless a later or unrelated action.</summary>
    public BrowseAppContentRequest Request { get; }

    /// <summary>The factory instance that issued the proof.</summary>
    public IBrowseAppContentFactory IssuingFactory { get; }
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

/// <summary>Optional reversible preparation. Capability is not authorization. Legacy
/// bool participants remain supported without claiming a frozen edit boundary.</summary>
public interface IBrowseAppContentClosePreparationParticipant
{
    bool SupportsReversibleClosePreparation { get; }
    /// <summary>The actual originating command and loader actions must settle before
    /// this task yields a preparation. Null is an ordinary veto. The external caller
    /// owns abort on every path that does not accept irreversible retirement.</summary>
    Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default);
}

public interface IBrowseAppContentClosePreparation
{
    IBrowseAppContentSession Session { get; }
    bool IsCurrent { get; }
    /// <summary>Idempotent retained restoration task; never reverses an accepted close.</summary>
    Task AbortAsync();
    /// <summary>Synchronously validates the private preparation and seals mutation
    /// admission before returning the SAME Session.OriginalClose task. Stale/foreign/
    /// aborted preparations refuse before caching any irreversible close.</summary>
    Task BeginClose();
}
