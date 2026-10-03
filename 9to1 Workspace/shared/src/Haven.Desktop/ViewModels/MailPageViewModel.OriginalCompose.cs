using Haven.Application;

namespace Haven.Desktop.ViewModels;

public sealed partial class MailPageViewModel
{
    private object _originalComposeGeneration = new();

    internal event Action? OriginalComposeRetired;

    private void RetireOriginalCompose()
    {
        Interlocked.Exchange(ref _originalComposeGeneration, new object());
        OriginalComposeRetired?.Invoke();
    }

    private void SetOriginalComposeDraftId(Guid? value)
    {
        if (_composeLocalDraftId == value) return;
        RetireOriginalCompose();
        _composeLocalDraftId = value;
    }

    // This token retains UI destination identity only. It grants no Files read or Mail send authority.
    internal sealed class OriginalComposeSelection
    {
        private readonly MailPageViewModel _owner;
        private readonly object _generation;
        private readonly MailAccount _account;
        internal Guid AccountId => _account.AccountId;
        internal Guid DraftId { get; }

        internal OriginalComposeSelection(MailPageViewModel owner, object generation, MailAccount account, Guid draftId)
        { _owner = owner; _generation = generation; _account = account; DraftId = draftId; }

        internal bool IsFor(MailPageViewModel owner) => ReferenceEquals(_owner, owner);

        internal bool IsCurrent => !Volatile.Read(ref _owner._disposed) && Volatile.Read(ref _owner._isComposeOpen) &&
            ReferenceEquals(Volatile.Read(ref _owner._originalComposeGeneration), _generation) &&
            ReferenceEquals(Volatile.Read(ref _owner._selectedAccount), _account);
    }

    internal OriginalComposeSelection? CaptureOriginalComposeSelection()
    {
        if (_disposed || !_isComposeOpen || _selectedAccount is not { } account) return null;
        var draftId = _composeLocalDraftId ?? Guid.NewGuid();
        SetOriginalComposeDraftId(draftId);
        return new(this, Volatile.Read(ref _originalComposeGeneration), account, draftId);
    }

    internal void SetOriginalComposeAttachmentStatus(OriginalComposeSelection original, string message)
    { if (original.IsFor(this) && original.IsCurrent) ComposeStatus = message; }

    internal bool TryAddOriginalComposeAttachment(OriginalComposeSelection original, string fileName,
        string contentType, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(content);
        if (!original.IsCurrent || !original.IsFor(this)) return false;
        if (content.Length is 0 or > 32 * 1024 * 1024) throw new ArgumentException("Choose a nonempty attachment no larger than 32 MiB.");
        AddComposeAttachment(fileName, contentType, content.ToArray());
        return true;
    }
}
