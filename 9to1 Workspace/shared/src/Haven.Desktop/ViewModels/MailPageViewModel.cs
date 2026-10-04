using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public enum MailUiState
{
    Loading,
    Ready,
    Empty,
    ConnectionRequired,
    PermissionRequired,
    Offline,
    Error
}

public sealed partial class MailPageViewModel : ObservableObject, IDisposable
{
    private object _selectedMessageLifetime = new();
    private object? _updatingSelectedMessageFlagsLifetime;
    private bool _disposed;
    internal event Action? SelectedMessageFlagsUpdating;
    internal event Action<bool>? SelectedMessageFlagsUpdated;
    internal Task Initialization { get; }
    internal Task SelectedMessageLoad { get; private set; } = Task.CompletedTask;
    private readonly IMailService _mail;
    private readonly IProviderModelClient _models;
    private CancellationTokenSource? _loadCancellation;
    private MailAccount? _selectedAccount;
    private MailFolder? _selectedFolder;
    private MailMessageSummary? _selectedSummary;
    private MailMessage? _selectedMessage;
    private MailUiState _state = MailUiState.Loading;
    private string _status = "Loading Mail…";
    private string _searchText = string.Empty;
    private bool _unreadOnly;
    private bool _flaggedOnly;
    private bool _isBusy;
    private DateTimeOffset? _lastLoadedAt;
    private bool _isStale;
    private bool _isComposeOpen;
    private bool _isSendConfirmationOpen;
    private string _composeTo = string.Empty;
    private string _composeCc = string.Empty;
    private string _composeBcc = string.Empty;
    private string _composeSubject = string.Empty;
    private string _composeBody = string.Empty;
    private string _composeStatus = string.Empty;
    private string? _composeDraftId;
    private Guid? _composeLocalDraftId;
    private MailResponseKind _composeResponseKind = MailResponseKind.New;
    private string? _composeSourceMessageId;
    private string? _composeThreadId;
    private bool _isAiPanelVisible;
    private string _aiTitle = string.Empty;
    private string _aiText = string.Empty;

    public MailPageViewModel(IMailService mail, IProviderModelClient models)
    {
        _mail = mail;
        _models = models;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SearchCommand = new AsyncRelayCommand(LoadMessagesAsync);
        ComposeCommand = new RelayCommand(OpenNewCompose);
        CloseComposeCommand = new RelayCommand(CloseCompose);
        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync);
        RequestSendCommand = new RelayCommand(RequestSend);
        ConfirmSendCommand = new AsyncRelayCommand(ConfirmSendAsync);
        CancelSendCommand = new RelayCommand(() => IsSendConfirmationOpen = false);
        ReplyCommand = new RelayCommand(() => OpenResponse(MailResponseKind.Reply));
        ReplyAllCommand = new RelayCommand(() => OpenResponse(MailResponseKind.ReplyAll));
        ForwardCommand = new RelayCommand(() => OpenResponse(MailResponseKind.Forward));
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        ToggleReadCommand = new AsyncRelayCommand(ToggleReadAsync);
        ToggleFlagCommand = new AsyncRelayCommand(ToggleFlagAsync);
        SummarizeCommand = new AsyncRelayCommand(SummarizeAsync);
        DraftWithAiCommand = new AsyncRelayCommand(DraftWithAiAsync);
        CloseAiCommand = new RelayCommand(() => IsAiPanelVisible = false);
        RemoveComposeAttachmentCommand = new RelayCommand<MailComposeAttachmentItem>(RemoveComposeAttachment);

        Initialization = InitializeAsync();
    }

    public ObservableCollection<MailAccount> Accounts { get; } = [];
    public ObservableCollection<MailFolder> Folders { get; } = [];
    public ObservableCollection<MailMessageSummary> Messages { get; } = [];
    public ObservableCollection<MailMessage> ThreadMessages { get; } = [];
    public ObservableCollection<MailComposeAttachmentItem> ComposeAttachments { get; } = [];

    public MailAccount? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (!SetProperty(ref _selectedAccount, value)) return;
            RetireOriginalCompose();
            RetireSelectedMessage();
            _loadCancellation?.Cancel();
            // Retire old-account rows synchronously; never invoke folder loading before new access admission.
            SetProperty(ref _selectedFolder, null, nameof(SelectedFolder));
            RaisePropertyChanged(nameof(FolderLabel));
            SelectedSummary = null;
            Messages.Clear(); Folders.Clear(); LastLoadedAt = null; IsStale = false;
            if (value is null)
            { IsBusy = false; State = MailUiState.Empty; Status = "Select a Mail account."; RaiseStateProperties(); }
            RaisePropertyChanged(nameof(AccountLabel));
            RaiseCapabilityProperties();
            MailboxLoad = LoadAccountAsync();
        }
    }

    public MailFolder? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (!SetProperty(ref _selectedFolder, value)) return;
            RaisePropertyChanged(nameof(FolderLabel));
            MailboxLoad = LoadMessagesAsync();
        }
    }

    public MailMessageSummary? SelectedSummary
    {
        get => _selectedSummary;
        set
        {
            // Native selection bindings report transient rows while the acknowledged row is replaced.
            if (ReferenceEquals(_updatingSelectedMessageFlagsLifetime, _selectedMessageLifetime)) return;
            if (!SetProperty(ref _selectedSummary, value)) return;
            RetireSelectedMessage();
            SelectedMessageLoad = LoadSelectedMessageAsync();
        }
    }

    public MailMessage? SelectedMessage
    {
        get => _selectedMessage;
        private set
        {
            if (!SetProperty(ref _selectedMessage, value)) return;
            RaisePropertyChanged(nameof(HasSelectedMessage));
            RaisePropertyChanged(nameof(ReadActionLabel));
            RaisePropertyChanged(nameof(FlagActionLabel));
        }
    }

    public MailUiState State { get => _state; private set { if (SetProperty(ref _state, value)) RaiseStateProperties(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public bool UnreadOnly { get => _unreadOnly; set => SetProperty(ref _unreadOnly, value); }
    public bool FlaggedOnly { get => _flaggedOnly; set => SetProperty(ref _flaggedOnly, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseStateProperties(); } }
    public DateTimeOffset? LastLoadedAt { get => _lastLoadedAt; private set { if (SetProperty(ref _lastLoadedAt, value)) RaisePropertyChanged(nameof(LastUpdatedLabel)); } }
    public bool IsStale { get => _isStale; private set => SetProperty(ref _isStale, value); }
    public bool IsComposeOpen
    {
        get => _isComposeOpen;
        private set
        {
            if (!value && _isComposeOpen) RetireOriginalCompose();
            SetProperty(ref _isComposeOpen, value);
        }
    }
    public bool IsSendConfirmationOpen { get => _isSendConfirmationOpen; private set => SetProperty(ref _isSendConfirmationOpen, value); }
    public string ComposeTo { get => _composeTo; set => SetProperty(ref _composeTo, value); }
    public string ComposeCc { get => _composeCc; set => SetProperty(ref _composeCc, value); }
    public string ComposeBcc { get => _composeBcc; set => SetProperty(ref _composeBcc, value); }
    public string ComposeSubject { get => _composeSubject; set => SetProperty(ref _composeSubject, value); }
    public string ComposeBody { get => _composeBody; set => SetProperty(ref _composeBody, value); }
    public string ComposeStatus { get => _composeStatus; private set => SetProperty(ref _composeStatus, value); }
    public bool IsAiPanelVisible { get => _isAiPanelVisible; private set => SetProperty(ref _isAiPanelVisible, value); }
    public string AiTitle { get => _aiTitle; private set => SetProperty(ref _aiTitle, value); }
    public string AiText { get => _aiText; private set => SetProperty(ref _aiText, value); }

    public bool HasAccounts => Accounts.Count > 0;
    public bool HasSelectedMessage => SelectedMessage is not null;
    public bool ShowConnectionRequired => State == MailUiState.ConnectionRequired;
    public bool ShowPermissionRequired => State == MailUiState.PermissionRequired;
    public bool ShowOffline => State == MailUiState.Offline;
    public bool ShowError => State == MailUiState.Error;
    public bool ShowEmpty => State == MailUiState.Empty;
    public string AccountLabel => SelectedAccount is null ? "No account" : $"{SelectedAccount.DisplayName} · {SelectedAccount.Address}";
    public string FolderLabel => SelectedFolder?.DisplayName ?? "All mail";
    public string LastUpdatedLabel => LastLoadedAt is { } loaded ? $"Updated {loaded.LocalDateTime:t}" : "Not refreshed yet";
    public string ReadActionLabel => SelectedMessage?.IsRead == true ? "Mark unread" : "Mark read";
    public string FlagActionLabel => SelectedMessage?.IsFlagged == true ? "Unflag" : "Flag";
    public bool CanArchive => Supports(MailProviderCapabilities.Archive);
    public bool CanDelete => Supports(MailProviderCapabilities.Delete);
    public bool CanChangeReadState => Supports(MailProviderCapabilities.ReadState);
    public bool CanFlag => Supports(MailProviderCapabilities.Flag);
    public bool CanSaveDraft => Supports(MailProviderCapabilities.Drafts);
    public bool CanSend => Supports(MailProviderCapabilities.Send);
    public bool CanUseAttachments => Supports(MailProviderCapabilities.Attachments);

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand ComposeCommand { get; }
    public RelayCommand CloseComposeCommand { get; }
    public AsyncRelayCommand SaveDraftCommand { get; }
    public RelayCommand RequestSendCommand { get; }
    public AsyncRelayCommand ConfirmSendCommand { get; }
    public RelayCommand CancelSendCommand { get; }
    public RelayCommand ReplyCommand { get; }
    public RelayCommand ReplyAllCommand { get; }
    public RelayCommand ForwardCommand { get; }
    public AsyncRelayCommand ArchiveCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand ToggleReadCommand { get; }
    public AsyncRelayCommand ToggleFlagCommand { get; }
    public AsyncRelayCommand SummarizeCommand { get; }
    public AsyncRelayCommand DraftWithAiCommand { get; }
    public RelayCommand CloseAiCommand { get; }
    public RelayCommand<MailComposeAttachmentItem> RemoveComposeAttachmentCommand { get; }

    public void AddComposeAttachment(string fileName, string contentType, byte[] content)
    {
        if (content.Length == 0) return;
        ComposeAttachments.Add(new MailComposeAttachmentItem(fileName, contentType, content));
        ComposeStatus = $"Attached {fileName}.";
        NotifyComposeChanged();
    }

    public async Task<byte[]> DownloadAttachmentAsync(MailAttachmentDescriptor attachment, CancellationToken cancellationToken)
    {
        if (SelectedAccount is null || SelectedMessage is null)
            throw new InvalidOperationException("Select a message before downloading an attachment.");
        return await _mail.DownloadAttachmentAsync(SelectedAccount.AccountId, SelectedMessage.Id, attachment.Id, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RetireOriginalCompose();
        RetireSelectedMessage();
        CancelDraftAutosave();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        State = MailUiState.Loading;
        Status = "Loading connected mail accounts…";
        try
        {
            var accounts = await _mail.GetAccountsAsync(CancellationToken.None);
            Accounts.Clear();
            foreach (var account in accounts) Accounts.Add(account);
            RaisePropertyChanged(nameof(HasAccounts));
            if (Accounts.Count == 0)
            {
                State = MailUiState.ConnectionRequired;
                Status = "Connect Google or Microsoft in Settings to use Mail.";
                return;
            }
            SelectedAccount = Accounts[0];
            await RestoreLatestDraftAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyFailure(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (SelectedAccount is null)
        {
            await InitializeAsync();
            return;
        }
        await LoadAccountAsync();
    }

    internal Task MailboxLoad { get; private set; } = Task.CompletedTask;

    private bool IsCurrentMailboxLoad(MailAccount account, CancellationToken token) =>
        !_disposed && !token.IsCancellationRequested && ReferenceEquals(SelectedAccount, account);

    private async Task LoadAccountAsync()
    {
        var account = SelectedAccount;
        if (account is null || _disposed) return;
        var cancellationToken = BeginLoad();
        try
        {
            IsBusy = true;
            State = MailUiState.Loading;
            Status = "Checking mailbox access…";
            var access = await _mail.CheckAccessAsync(account.AccountId, cancellationToken);
            if (!IsCurrentMailboxLoad(account, cancellationToken)) return;
            if (!access.Succeeded)
            {
                ApplyFailure(access);
                return;
            }

            var folders = await _mail.GetFoldersAsync(account.AccountId, cancellationToken);
            if (!IsCurrentMailboxLoad(account, cancellationToken)) return;
            Folders.Clear();
            foreach (var folder in folders) Folders.Add(folder);
            RefreshMoveTargets();
            var inbox = Folders.FirstOrDefault(folder => folder.Kind == MailFolderKind.Inbox) ?? Folders.FirstOrDefault();
            if (!Equals(SelectedFolder, inbox)) SelectedFolder = inbox;
            else await LoadMessagesCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentMailboxLoad(account, cancellationToken)) ApplyFailure(ex);
        }
        finally
        {
            if (IsCurrentMailboxLoad(account, cancellationToken)) IsBusy = false;
        }
    }

    private async Task LoadMessagesAsync()
    {
        var account = SelectedAccount;
        if (account is null || _disposed) return;
        var cancellationToken = BeginLoad();
        try
        {
            IsBusy = true;
            await LoadMessagesCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentMailboxLoad(account, cancellationToken)) ApplyFailure(ex);
        }
        finally
        {
            if (IsCurrentMailboxLoad(account, cancellationToken)) IsBusy = false;
        }
    }

    private async Task LoadMessagesCoreAsync(CancellationToken cancellationToken)
    {
        var account = SelectedAccount ?? throw new InvalidOperationException("No Mail account is selected.");
        if (!IsCurrentMailboxLoad(account, cancellationToken)) return;
        Status = "Loading messages…";
        var page = await _mail.GetMessagesAsync(new MailQuery(
            account.AccountId, SelectedFolder?.Id, SearchText, UnreadOnly, FlaggedOnly), cancellationToken);
        if (!IsCurrentMailboxLoad(account, cancellationToken)) return;
        Messages.Clear();
        foreach (var message in page.Messages) Messages.Add(message);
        SelectedSummary = Messages.FirstOrDefault();
        LastLoadedAt = page.FetchedAt;
        IsStale = false;
        State = Messages.Count == 0 ? MailUiState.Empty : MailUiState.Ready;
        Status = Messages.Count == 0 ? "No messages match this view." : $"{Messages.Count} message{(Messages.Count == 1 ? string.Empty : "s")} loaded.";
        RaiseStateProperties();
    }

    private void RetireSelectedMessage()
    {
        _selectedMessageLifetime = new();
        SelectedMessage = null;
        ThreadMessages.Clear();
    }

    private async Task LoadSelectedMessageAsync()
    {
        var account = SelectedAccount;
        var summary = SelectedSummary;
        var originalLifetime = _selectedMessageLifetime;
        bool Current() => !_disposed && ReferenceEquals(originalLifetime, _selectedMessageLifetime) &&
            ReferenceEquals(account, SelectedAccount) && ReferenceEquals(summary, SelectedSummary);
        if (account is null || summary is null || !Current()) return;
        try
        {
            var thread = await _mail.GetThreadAsync(account.AccountId, summary.ThreadId, CancellationToken.None);
            if (!Current()) return;
            var originalThread = thread.OrderBy(message => message.ReceivedAt).ToList();
            var selected = originalThread.FirstOrDefault(message => message.Id == summary.Id);
            if (selected is null)
            {
                selected = await _mail.GetMessageAsync(account.AccountId, summary.Id, CancellationToken.None);
                if (!Current()) return;
                originalThread.Add(selected);
            }
            if (!Current()) return;
            ThreadMessages.Clear();
            foreach (var message in originalThread) ThreadMessages.Add(message);
            SelectedMessage = selected;

            // The selected original message may already have an in-flight remote read acknowledgement.
            // Retirement refuses its presentation; it does not claim to cancel that provider effect.
            if (!selected.IsRead && Current())
            {
                var result = await _mail.SetReadAsync(account.AccountId, summary.Id, true, CancellationToken.None);
                if (!Current()) return;
                if (result.Succeeded)
                {
                    SelectedMessage = selected with { IsRead = true };
                    var index = ThreadMessages.ToList().FindIndex(message => message.Id == summary.Id);
                    if (index >= 0) ThreadMessages[index] = SelectedMessage;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (Current()) ApplyFailure(ex, preserveMessages: true);
        }
    }

    private void OpenNewCompose()
    {
        ResetCompose();
        IsComposeOpen = true;
        ComposeStatus = "New message";
    }

    private void OpenResponse(MailResponseKind kind)
    {
        if (SelectedMessage is null || SelectedAccount is null) return;
        ResetCompose();
        _composeResponseKind = kind;
        _composeSourceMessageId = SelectedMessage.Id;
        _composeThreadId = SelectedMessage.ThreadId;
        if (kind == MailResponseKind.Reply)
        {
            ComposeTo = SelectedMessage.From.Address;
            ComposeSubject = PrefixSubject(SelectedMessage.Subject, "Re:");
        }
        else if (kind == MailResponseKind.ReplyAll)
        {
            var own = SelectedAccount.Address;
            var to = new[] { SelectedMessage.From }.Concat(SelectedMessage.To)
                .Where(address => !address.Address.Equals(own, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(address => address.Address, StringComparer.OrdinalIgnoreCase);
            ComposeTo = string.Join("; ", to.Select(address => address.Address));
            ComposeCc = string.Join("; ", SelectedMessage.Cc.Where(address => !address.Address.Equals(own, StringComparison.OrdinalIgnoreCase)).Select(address => address.Address));
            ComposeSubject = PrefixSubject(SelectedMessage.Subject, "Re:");
        }
        else
        {
            ComposeSubject = PrefixSubject(SelectedMessage.Subject, "Fwd:");
            ComposeBody = $"\n\n-------- Forwarded message --------\nFrom: {SelectedMessage.From.Label}\nDate: {SelectedMessage.ReceivedAt.LocalDateTime:g}\nSubject: {SelectedMessage.Subject}\n\n{SelectedMessage.PlainTextBody}";
        }
        IsComposeOpen = true;
        ComposeStatus = kind switch
        {
            MailResponseKind.Reply => "Reply",
            MailResponseKind.ReplyAll => "Reply all",
            _ => "Forward"
        };
    }

    private void CloseCompose()
    {
        IsSendConfirmationOpen = false;
        IsComposeOpen = false;
    }

    private async Task SaveDraftAsync()
    {
        var draft = BuildDraft();
        if (draft is null) return;
        try
        {
            ComposeStatus = "Saving draft…";
            var result = await _mail.SaveDraftAsync(draft, CancellationToken.None);
            if (result.Succeeded)
            {
                SetOriginalComposeDraftId(result.LocalDraftId ?? _composeLocalDraftId);
                _composeDraftId = result.ProviderId ?? _composeDraftId;
                ComposeStatus = SafeFailureMessage(result);
            }
            else ApplyComposeFailure(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyComposeFailure(ex);
        }
    }

    private void RequestSend()
    {
        if (BuildDraft() is null) return;
        IsSendConfirmationOpen = true;
        ComposeStatus = "Review recipients before sending.";
    }

    private async Task ConfirmSendAsync()
    {
        var draft = BuildDraft();
        if (draft is null) return;
        try
        {
            IsSendConfirmationOpen = false;
            ComposeStatus = "Sending…";
            var result = await _mail.SendAsync(draft, CancellationToken.None);
            if (!result.Succeeded)
            {
                ApplyComposeFailure(result);
                return;
            }
            ComposeStatus = result.Message;
            CancelDraftAutosave();
            IsComposeOpen = false;
            ResetCompose();
            await LoadMessagesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyComposeFailure(ex);
        }
    }

    private async Task ArchiveAsync() => await MutateSelectedAsync((account, id) => _mail.ArchiveAsync(account, id, CancellationToken.None));
    private async Task DeleteAsync() => await MutateSelectedAsync((account, id) => _mail.DeleteAsync(account, id, CancellationToken.None));

    private async Task ToggleReadAsync()
    {
        if (SelectedMessage is null) return;
        var target = !SelectedMessage.IsRead;
        await MutateSelectedAsync((account, id) => _mail.SetReadAsync(account, id, target, CancellationToken.None),
            reload: false, onSuccess: () => UpdateSelectedMessageFlags(isRead: target));
    }

    private async Task ToggleFlagAsync()
    {
        if (SelectedMessage is null) return;
        var target = !SelectedMessage.IsFlagged;
        await MutateSelectedAsync((account, id) => _mail.SetFlaggedAsync(account, id, target, CancellationToken.None),
            reload: false, onSuccess: () => UpdateSelectedMessageFlags(isFlagged: target));
    }

    private void UpdateSelectedMessageFlags(bool? isRead = null, bool? isFlagged = null, bool? isImportant = null)
    {
        if (SelectedMessage is not { } message) return;
        var account = SelectedAccount;
        var originalLifetime = _selectedMessageLifetime;
        bool Current() => !_disposed && ReferenceEquals(originalLifetime, _selectedMessageLifetime) &&
            ReferenceEquals(account, SelectedAccount) && SelectedMessage?.Id == message.Id;
        var updated = message with
        {
            IsRead = isRead ?? message.IsRead,
            IsFlagged = isFlagged ?? message.IsFlagged,
            IsImportant = isImportant ?? message.IsImportant
        };
        _updatingSelectedMessageFlagsLifetime = originalLifetime;
        try
        {
            SelectedMessageFlagsUpdating?.Invoke();
            if (!Current()) return;
            for (var index = 0; index < ThreadMessages.Count; index++)
            {
                if (ThreadMessages[index].Id == message.Id) ThreadMessages[index] = updated;
                if (!Current()) return;
            }
            for (var index = 0; index < Messages.Count; index++)
            {
                var summary = Messages[index];
                if (summary.Id != message.Id) continue;
                Messages[index] = summary with
                {
                    IsRead = isRead ?? summary.IsRead,
                    IsFlagged = isFlagged ?? summary.IsFlagged,
                    IsImportant = isImportant ?? summary.IsImportant
                };
                if (!Current()) return;
            }
            if (SelectedSummary is { } selected && selected.Id == message.Id)
            {
                var summary = Messages.FirstOrDefault(row => row.Id == message.Id) ?? selected with
                {
                    IsRead = isRead ?? selected.IsRead,
                    IsFlagged = isFlagged ?? selected.IsFlagged,
                    IsImportant = isImportant ?? selected.IsImportant
                };
                // A flag acknowledgement updates the same selection; it must not retire or reread its thread.
                SetProperty(ref _selectedSummary, summary, nameof(SelectedSummary));
            }
            if (!Current()) return;
            SelectedMessage = updated;
            if (Current()) NotifyMessageSelectionChanged();
        }
        finally
        {
            try { SelectedMessageFlagsUpdated?.Invoke(Current()); }
            finally { _updatingSelectedMessageFlagsLifetime = null; }
        }
    }

    private async Task MutateSelectedAsync(Func<Guid, string, Task<MailOperationResult>> action, bool reload = true, Action? onSuccess = null)
    {
        var account = SelectedAccount;
        var message = SelectedMessage;
        var originalLifetime = _selectedMessageLifetime;
        bool Current() => !_disposed && ReferenceEquals(originalLifetime, _selectedMessageLifetime) &&
            ReferenceEquals(account, SelectedAccount) && SelectedMessage?.Id == message?.Id;
        if (account is null || message is null || !Current()) return;
        try
        {
            var result = await action(account.AccountId, message.Id);
            if (!Current()) return;
            if (!result.Succeeded)
            {
                ApplyFailure(result, preserveMessages: true);
                return;
            }
            Status = result.Message;
            if (!Current()) return;
            onSuccess?.Invoke();
            if (reload && Current()) await LoadMessagesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (Current()) ApplyFailure(ex, preserveMessages: true);
        }
    }

    private async Task SummarizeAsync()
    {
        if (SelectedMessage is null) return;
        await RunAiAsync("Summary", $"Summarize this email in concise bullets. Include action items and dates only when present.\n\nSubject: {SelectedMessage.Subject}\nFrom: {SelectedMessage.From.Label}\n\n{SelectedMessage.PlainTextBody}", applyToCompose: false);
    }

    private async Task DraftWithAiAsync()
    {
        if (SelectedMessage is null) return;
        await RunAiAsync("Draft reply", $"Draft a concise professional reply to this email. Return only the draft body and do not invent facts.\n\nSubject: {SelectedMessage.Subject}\nFrom: {SelectedMessage.From.Label}\n\n{SelectedMessage.PlainTextBody}", applyToCompose: true);
    }

    private async Task RunAiAsync(string title, string prompt, bool applyToCompose)
    {
        try
        {
            IsAiPanelVisible = true;
            AiTitle = title;
            AiText = "Working…";
            var models = await _models.GetModelsAsync(CancellationToken.None);
            var model = models.FirstOrDefault();
            if (model is null)
            {
                AiText = "No compatible model is available. Configure a model in Settings.";
                return;
            }
            var text = await _models.CompleteAsync(new OllamaChatRequest(
                model.Name, [new OllamaMessage("user", prompt)], EffortLevel.Low,
                "You are Haven Mail's contextual writing assistant. Work only from the supplied email content."), CancellationToken.None);
            AiText = text.Trim();
            if (applyToCompose)
            {
                OpenResponse(MailResponseKind.Reply);
                ComposeBody = AiText;
                ComposeStatus = "AI draft inserted. Review it before sending.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AiText = "AI help is unavailable right now.";
        }
    }

    private MailDraft? BuildDraft()
    {
        if (SelectedAccount is null)
        {
            ComposeStatus = "Connect and select a Mail account first.";
            return null;
        }
        var invalidAddress = FirstInvalidAddress(ComposeTo, ComposeCc, ComposeBcc);
        if (invalidAddress is not null)
        {
            ComposeStatus = $"Check the recipient address: {invalidAddress}";
            return null;
        }

        var to = ParseAddresses(ComposeTo);
        var cc = ParseAddresses(ComposeCc);
        var bcc = ParseAddresses(ComposeBcc);
        if (to.Count == 0 && cc.Count == 0 && bcc.Count == 0)
        {
            ComposeStatus = "Add at least one recipient.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(ComposeSubject))
        {
            ComposeStatus = "Add a subject before saving or sending.";
            return null;
        }
        var hasHtml = !string.IsNullOrWhiteSpace(ComposeHtmlBody);
        var draftId = _composeLocalDraftId ?? Guid.NewGuid();
        SetOriginalComposeDraftId(draftId);
        return new MailDraft(
            SelectedAccount.AccountId, _composeDraftId, _composeResponseKind, _composeSourceMessageId, _composeThreadId,
            to, cc, bcc, ComposeSubject.Trim(), hasHtml ? ComposeHtmlBody : ComposeBody, hasHtml,
            ComposeAttachments.Select(item => new MailDraftAttachment(item.FileName, item.ContentType, item.Content, item.LocalId)).ToArray(),
            LocalId: draftId, Provider: SelectedAccount.Provider);
    }

    private static IReadOnlyList<string> SplitAddresses(string value) => value
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IReadOnlyList<MailAddress> ParseAddresses(string value) =>
        SplitAddresses(value).Select(address => new MailAddress(string.Empty, address)).ToArray();

    private static string? FirstInvalidAddress(params string[] values) => values
        .SelectMany(SplitAddresses)
        .FirstOrDefault(address => !System.Net.Mail.MailAddress.TryCreate(address, out _));

    private void RemoveComposeAttachment(MailComposeAttachmentItem? item)
    {
        if (item is null || !ComposeAttachments.Remove(item)) return;
        NotifyComposeChanged();
    }

    private void ResetCompose()
    {
        RetireOriginalCompose();
        CancelDraftAutosave();
        ComposeTo = ComposeCc = ComposeBcc = ComposeSubject = ComposeBody = string.Empty;
        ResetRichCompose();
        ComposeAttachments.Clear();
        _composeDraftId = null;
        SetOriginalComposeDraftId(null);
        _composeResponseKind = MailResponseKind.New;
        _composeSourceMessageId = null;
        _composeThreadId = null;
        IsSendConfirmationOpen = false;
    }

    private CancellationToken BeginLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        return _loadCancellation.Token;
    }

    private void ApplyFailure(Exception exception, bool preserveMessages = false)
    {
        if (exception is MailProviderException provider)
            ApplyFailure(new MailOperationResult(false, provider.Message, provider.FailureKind), preserveMessages);
        else
            ApplyFailure(new MailOperationResult(false, "The mail provider could not complete this operation.", MailFailureKind.ProviderError), preserveMessages);
    }

    private void ApplyFailure(MailOperationResult result, bool preserveMessages = false)
    {
        State = result.FailureKind switch
        {
            MailFailureKind.NotConnected => MailUiState.ConnectionRequired,
            MailFailureKind.PermissionDenied => MailUiState.PermissionRequired,
            MailFailureKind.Offline => MailUiState.Offline,
            _ => MailUiState.Error
        };
        Status = result.Message;
        IsStale = preserveMessages && Messages.Count > 0;
        if (!preserveMessages)
        {
            Messages.Clear();
            SelectedSummary = null;
        }
        RaiseStateProperties();
    }

    private void ApplyComposeFailure(Exception exception)
    {
        ComposeStatus = exception is MailProviderException provider ? SafeFailureMessage(new MailOperationResult(false, provider.Message, provider.FailureKind)) : "The mail provider could not complete this action.";
    }

    private void ApplyComposeFailure(MailOperationResult result) => ComposeStatus = SafeFailureMessage(result);

    private static string SafeFailureMessage(MailOperationResult result) => result.FailureKind switch
    {
        MailFailureKind.NotConnected => "Connect a Mail account to continue.",
        MailFailureKind.ReconnectRequired => "Your Mail session needs to be reconnected in Settings.",
        MailFailureKind.PermissionDenied => "Haven does not currently have permission for that Mail action.",
        MailFailureKind.Offline => "Haven cannot reach the Mail provider right now.",
        MailFailureKind.InvalidRequest => "The Mail provider could not complete that request.",
        _ => "The Mail provider could not complete this operation."
    };

    private void RaiseStateProperties()
    {
        RaisePropertyChanged(nameof(ShowConnectionRequired));
        RaisePropertyChanged(nameof(ShowPermissionRequired));
        RaisePropertyChanged(nameof(ShowOffline));
        RaisePropertyChanged(nameof(ShowError));
        RaisePropertyChanged(nameof(ShowEmpty));
    }

    private bool Supports(MailProviderCapabilities capability) =>
        SelectedAccount?.Capabilities.HasFlag(capability) == true;

    private void RaiseCapabilityProperties()
    {
        RaisePropertyChanged(nameof(CanArchive));
        RaisePropertyChanged(nameof(CanDelete));
        RaisePropertyChanged(nameof(CanChangeReadState));
        RaisePropertyChanged(nameof(CanFlag));
        RaisePropertyChanged(nameof(CanSaveDraft));
        RaisePropertyChanged(nameof(CanSend));
        RaisePropertyChanged(nameof(CanUseAttachments));
        RaisePropertyChanged(nameof(CanManageFolders));
        RaisePropertyChanged(nameof(CanManageSelectedFolder));
        RaisePropertyChanged(nameof(CanMove));
        RaisePropertyChanged(nameof(CanSpam));
        RaisePropertyChanged(nameof(CanImportant));
        RaisePropertyChanged(nameof(CanBulk));
    }

    private static string PrefixSubject(string subject, string prefix) =>
        subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? subject : $"{prefix} {subject}";
}

public sealed class MailComposeAttachmentItem
{
    public MailComposeAttachmentItem(string fileName, string contentType, byte[] content)
        : this(fileName, contentType, content, Guid.NewGuid()) { }

    public MailComposeAttachmentItem(string fileName, string contentType, byte[] content, Guid localId)
    {
        LocalId = localId == Guid.Empty ? Guid.NewGuid() : localId;
        FileName = fileName;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        Content = content;
    }

    public Guid LocalId { get; }
    public string FileName { get; }
    public string ContentType { get; }
    public byte[] Content { get; }
    public string SizeLabel => Content.Length < 1024 ? $"{Content.Length} B" : Content.Length < 1024 * 1024 ? $"{Content.Length / 1024d:0.#} KB" : $"{Content.Length / 1024d / 1024d:0.#} MB";
}
