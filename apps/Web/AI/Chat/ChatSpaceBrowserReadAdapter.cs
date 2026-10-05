using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Spaces.Chat;

namespace NineToOne.Web.AI.Chat;

/// <summary>
/// Unregistered read bridge to the actual owning Chat controller and embedded CUI.
/// A host must supply its real backend. This bridge supplies no persistence, actor,
/// model/executor, mutation catalog, control factory, route or transfer authority.
/// </summary>
public sealed class ChatSpaceBrowserReadAdapter : ICuiBindingContext, ICuiActionAvailability, IDisposable
{
    private readonly ChatSpaceController _owner;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _reads = new(1, 1);
    private volatile bool _disposed;
    private volatile bool _readVisible;

    public ChatSpaceBrowserReadAdapter(IChatSpaceBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _owner = new ChatSpaceController(backend);
        Document = ChatSpaceCuiDocument.Load();
    }

    public CuiDocument Document { get; }
    public ChatSpaceViewState OwnerState
    {
        get
        {
            if (_disposed || !_readVisible) return ChatSpaceViewState.Empty;
            var current = _owner.State;
            // The owner retains prior state on read failure. Preserve its actual diagnostic,
            // but do not re-expose previously loaded private values after a denied refresh.
            return current.Status.Tone == ChatSpaceStatusTone.Error
                ? ChatSpaceViewState.Empty with { Status = current.Status, Revision = current.Revision }
                : current;
        }
    }

    /// <summary>Names only. Registered factories still require real behavioral acceptance.</summary>
    public IReadOnlyList<string> MissingElementTypes(CuiControlRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return ChatSpaceCuiDocument.DescendantsAndSelf(Document.Components)
            .Select(component => component.Type).Distinct(StringComparer.Ordinal)
            .Where(type => !registry.TryResolveElement(type, out _))
            .Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Reads only through the owning controller/backend. Owner errors remain in its
    /// existing status; cancellation is preserved. A fresh adapter is required for
    /// a different actor/store context. No detached read confers access permission.
    /// </summary>
    public async Task LoadAsync(Guid? conversationId = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (conversationId == Guid.Empty) throw new ArgumentException("A nonempty canonical conversation ID is required.", nameof(conversationId));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (!await _reads.WaitAsync(0, request.Token).ConfigureAwait(false))
            throw new InvalidOperationException("An owning Chat read is already in progress.");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _readVisible = false;
            await _owner.InitializeAsync(conversationId, request.Token).ConfigureAwait(false);
            request.Token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _readVisible = true;
        }
        finally { _reads.Release(); }
    }

    // No markup action is enabled by a source read. In particular, composer.canSend
    // cannot authorize an executor merely because a model name exists in inventory.
    public bool? IsActionAvailable(string command) => false;

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        if (_disposed) return false;
        var state = OwnerState;
        switch (path)
        {
            case "recentQuery": value = string.Empty; break;
            case "recentChats": value = state.RecentChats; break;
            case "models": value = state.Models; break;
            case "conversation.title": value = state.Conversation?.Title ?? "New chat"; break;
            case "conversation.branches": value = state.Conversation?.Branches ?? []; break;
            case "conversation.currentBranchId": value = state.Conversation?.CurrentBranchId; break;
            case "conversation.messages": value = state.Conversation?.Messages ?? []; break;
            case "composer.selectedModelName": value = state.Composer.SelectedModelName; break;
            case "composer.attachments": value = state.Composer.Attachments; break;
            case "composer.draft": value = state.Composer.Draft; break;
            case "composer.canSend": value = false; break;
            case "status.message": value = state.Status.Message; break;
            default: return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        // In-flight owning reads retain their linked token until settlement; no
        // semaphore/source disposal may race their finally or Register calls.
    }
}
