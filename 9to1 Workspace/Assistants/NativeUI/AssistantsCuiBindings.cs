using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Portable product projection. The host supplies actual dispatch/publication ownership.</summary>
public sealed partial class AssistantsCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly CuiViewModel _values = new();
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Action<Action> _publish;
    private readonly Func<bool> _isCurrent;
    private AssistantsWorkspaceSnapshot _snapshot = AssistantsWorkspaceSnapshot.Empty;
    private AssistantConfigurationDraft? _draft;
    private AssistantModelChoice? _selectedModel;
    private string _prompt = "";
    private Guid? _promptConversation;
    private string _route = "home";
    private bool _unavailable;
    private bool _streaming;
    private readonly bool _hasDevelopmentRoute;
    private bool _projectChooserVisible;
    private bool _projectBusy;
    private bool _hasPendingProjectSubmission;
    private bool _hasSettledProjectRefusal;
    private bool _canRetryProjectSubmission;
    private string _pendingProjectSubmissionTitle = "";
    private bool _projectOpensDevelopment;
    private string _projectStatus = "";
    private IReadOnlyList<OriginalProjectRow> _projectRows = [];
    private IReadOnlyList<AssistantCompatibleConversationCheckpoint> _compatiblePending = [];
    private IReadOnlyList<CompatiblePendingRow> _compatiblePendingRows = [];
    private IReadOnlyList<ConfiguredProjectPreferenceRow> _configurationProjectRows = [];
    private readonly AssistantConfigurationCapabilityPreferences _configurationCapabilities = new();
    private AssistantConfigurationDraft? _configurationProjectDraft;
    private IReadOnlyList<HavenOS.Apps.Dev.DeveloperProjectReference>? _configurationProjectReferences;
    private bool _hasMoreProjects;
    private string _projectSearch = "";
    private long _projectSearchRevision;
    private long _publishedProjectSearchRevision = -1;
    private volatile bool _revoked;

    public AssistantsCuiBindings(Func<string, object?, CancellationToken, ValueTask> dispatch,
        Action<Action> publish, Func<bool> isCurrent, bool hasOriginalDevelopmentRoute = false)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        _hasDevelopmentRoute = hasOriginalDevelopmentRoute;
        _values.PropertyChanged += (_, args) => { if (Current) PropertyChanged?.Invoke(this, args); };
        Refresh();
    }

    /// <summary>Read-only setup state with no controller, actor, store or executable action.</summary>
    public static AssistantsCuiBindings CreateUnavailable(string reason)
    {
        var bindings = new AssistantsCuiBindings(
            (_, _, _) => ValueTask.FromException(new UnauthorizedAccessException(reason)), action => action(), () => true);
        bindings._unavailable = true;
        bindings.Refresh();
        bindings.Set("Status", reason); bindings.Set("DependencyStatus", reason);
        bindings.Set("HasDependencyStatus", true);
        return bindings;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? PromptChanged;
    public AssistantConfigurationDraft? Draft => Current ? _draft : null;
    public string Prompt => Current ? _prompt : "";
    public AssistantModelChoice? SelectedModel => Current ? _selectedModel : null;
    public AssistantsWorkspaceSnapshot Snapshot => Current ? _snapshot : AssistantsWorkspaceSnapshot.Empty;
    internal AssistantConfigurationDraft? OriginalDraft => _draft;
    internal string OriginalPrompt => _prompt;
    internal bool IsProjectSelectionVisible => Current && _projectChooserVisible;
    internal string OriginalProjectSearch => _projectSearch;
    internal long ProjectSearchRevision => _projectSearchRevision;
    internal bool IsMemoryVisible => Current && _route == "memory";
    internal bool IsLegacyMigrationVisible => Current && _route == "migration";
    private bool Current => !_revoked && _isCurrent();
    public void Revoke() => _revoked = true;

    public void ApplySnapshot(AssistantsWorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Current || snapshot.Revision < _snapshot.Revision) return;
        var previous = _snapshot.SelectedAssistant;
        var selectionChanged = previous?.Identity != snapshot.SelectedAssistant?.Identity ||
            previous?.Configuration.Model != snapshot.SelectedAssistant?.Configuration.Model;
        _snapshot = snapshot;
        if (previous?.Identity != snapshot.SelectedAssistant?.Identity || previous?.Revision != snapshot.SelectedAssistant?.Revision ||
            !ReferenceEquals(_compatiblePending, snapshot.DeclinedPendingConversations))
            SetCompatiblePendingRows(snapshot.DeclinedPendingConversations);
        // Discovery can replace a descriptor's metadata without changing the user's
        // selected model. Rebind to the SAME current owner-returned provider/name.
        var currentChoice = !selectionChanged && _selectedModel is { } selected
            ? snapshot.Models.FirstOrDefault(choice => SameModelIdentity(choice, selected)) : null;
        _selectedModel = currentChoice ?? ChooseConfiguredModel(snapshot);
        if (snapshot.Conversation is { } data && _promptConversation != data.Conversation.Id)
        {
            _promptConversation = data.Conversation.Id;
            _prompt = data.Draft?.Content ?? "";
        }
        Refresh();
    }

    public void ShowHome() { _route = "home"; Refresh(); }
    public void ShowMemory() { _route = "memory"; Refresh(); }
    public void ShowLegacyMigration() { _route = "migration"; Refresh(); }
    public void ShowWork() { _route = "work"; Refresh(); }
    internal void ReturnToConfiguration() { _route = "configuration"; Refresh(); }
    internal void BeginConfigurationCapabilityReview(AssistantConfigurationDraft.Submission submitted)
    { _configurationCapabilities.Begin(submitted); Refresh(); }
    internal bool PublishConfigurationCapabilityCatalogue(AssistantConfigurationDraft.Submission submitted,
        AssistantOriginalConfigurationCapabilityCatalogue catalogue)
    { var accepted = _configurationCapabilities.PublishCatalogue(_draft, submitted, catalogue); Refresh(); return accepted; }
    internal AssistantConfigurationCapabilityPreferences.Row? FindConfigurationCapabilityRow(object? parameter) =>
        _configurationCapabilities.FindCurrentRow(_draft, parameter);
    internal bool ApplyOriginalConfigurationCapabilityChoice(AssistantConfigurationDraft.Submission submitted,
        AssistantOriginalConfigurationCapabilityChoice choice)
    { var accepted = _configurationCapabilities.ApplyChoice(_draft, submitted, choice); Refresh(); return accepted; }
    internal void SetConfigurationCapabilityBusy(bool busy)
    { _configurationCapabilities.Busy = busy; Refresh(); }
    internal void SetConfigurationCapabilityStatus(string detail)
    { _configurationCapabilities.Detail = detail; Refresh(); }

    internal void ApplyOriginalProjectPreference(AssistantConfigurationDraft.Submission submitted,
        AssistantOriginalProjectChoice actualChoice)
    {
        var draft = _draft;
        if (!Current || draft is null || !ReferenceEquals(draft, submitted.Owner) ||
            draft.Identity != submitted.Identity || draft.ExpectedRevision != submitted.ExpectedRevision ||
            draft.OriginalGeneration != submitted.Generation)
            return; // A newer form owns its edits; accepted READ still joins at its actual source.
        var reference = actualChoice.Project.Reference; // SAME producer-issued choice, saved preference only.
        if (!draft.Configuration.ProjectReferences.Contains(reference))
            draft.SetConfiguration(draft.Configuration with
            { ProjectReferences = draft.Configuration.ProjectReferences.Append(reference).ToArray() });
        Refresh();
    }
    internal HavenOS.Apps.Dev.DeveloperProjectReference DemandConfiguredProjectPreference(object? parameter)
    {
        if (parameter is not ConfiguredProjectPreferenceRow row || !_configurationProjectRows.Any(actual => ReferenceEquals(actual, row)))
            throw new InvalidOperationException("Review a development project from this same current configuration draft.");
        return row.Original;
    }
    public void EditConfiguration(AssistantDefinitionSnapshot? definition)
    { _draft = new(definition); _route = "configuration"; Refresh(); }
    public void ConfigurationSaved(AssistantConfigurationDraft.Submission submitted, AssistantDefinitionSnapshot saved)
    { (_draft ?? throw new InvalidOperationException("The form is unavailable.")).Acknowledge(submitted, saved); Refresh(); }
    public void DiscardConfiguration() { _draft?.DiscardChanges(); Refresh(); }
    public void ChooseConfiguredModel(AssistantModelChoice choice)
    {
        if (_draft is null || !_snapshot.Models.Contains(choice))
            throw new InvalidOperationException("Choose a currently available model for this configuration.");
        _draft.SetConfiguration(_draft.Configuration with
        { Model = _draft.Configuration.Model with { ProviderId = choice.ProviderId, ModelId = choice.Model.Name } });
        Refresh();
    }
    public void ChooseConfiguredEffort(EffortLevel effort)
    {
        if (_draft is null || !Enum.IsDefined(effort)) throw new InvalidOperationException("Choose a supported reasoning level.");
        _draft.SetConfiguration(_draft.Configuration with { Model = _draft.Configuration.Model with { Effort = effort } });
        Refresh();
    }
    public void SelectModel(AssistantModelChoice choice)
    {
        if (!_snapshot.Models.Contains(choice)) throw new InvalidOperationException("Choose a currently available model.");
        _selectedModel = choice; Refresh();
    }

    private static bool SameModelIdentity(AssistantModelChoice left, AssistantModelChoice right) =>
        left.ProviderId.Equals(right.ProviderId, StringComparison.OrdinalIgnoreCase) &&
        left.Model.Name.Equals(right.Model.Name, StringComparison.OrdinalIgnoreCase);

    private static AssistantModelChoice? ChooseConfiguredModel(AssistantsWorkspaceSnapshot snapshot)
    {
        var preference = snapshot.SelectedAssistant?.Configuration.Model;
        var provider = preference?.ProviderId;
        var name = preference?.ModelId;
        var candidates = snapshot.Models.Where(choice => string.IsNullOrWhiteSpace(provider) ||
            choice.ProviderId.Equals(provider, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(name)) return candidates.FirstOrDefault();
        // A missing configured primary is visible and requires an explicit choice.
        // UI discovery does not grant fallback routing or replace saved intent.
        return candidates.FirstOrDefault(choice => choice.Model.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            choice.Model.Name.Equals(choice.ProviderId + ":" + name, StringComparison.OrdinalIgnoreCase));
    }
    internal void BeginProjectSelection(bool openDevelopment)
    { _projectChooserVisible = true; _projectOpensDevelopment = openDevelopment; _projectRows = []; _projectStatus = "Reading saved projects from their genuine source…"; _hasMoreProjects = false; Refresh(); }
    internal void SetProjectSubmission(bool pending, bool canRetry, string title, bool settledPreEffectRefusal = false)
    { _hasPendingProjectSubmission = pending; _hasSettledProjectRefusal = settledPreEffectRefusal; _canRetryProjectSubmission = canRetry; _pendingProjectSubmissionTitle = title; Refresh(); }
    internal void SetProjectBusy(bool busy) { _projectBusy = busy; Refresh(); }
    internal void SetProjectStatus(string message) { _projectStatus = message; Refresh(); }
    internal void SetProjectCatalogue(AssistantOriginalProjectCatalogue actual) => SetProjectCatalogue(actual, _projectSearchRevision);
    internal void SetProjectCatalogue(AssistantOriginalProjectCatalogue actual, long actualSearchRevision)
    {
        if (actualSearchRevision != _projectSearchRevision) return;
        _projectRows = actual.Candidates.Select((candidate, index) => new OriginalProjectRow(candidate, index)).ToArray();
        _hasMoreProjects = actual.NextContinuation is not null; _publishedProjectSearchRevision = actualSearchRevision;
        _projectStatus = actual.Message; Refresh();
    }
    internal void CloseProjectSelection() { _projectChooserVisible = false; _projectRows = []; _projectStatus = ""; Refresh(); }
    internal AssistantOriginalProjectCandidate DemandOriginalProjectCandidate(object? parameter)
    { if (!_projectChooserVisible || _publishedProjectSearchRevision != _projectSearchRevision ||
        parameter is not OriginalProjectRow row || !_projectRows.Any(actual => ReferenceEquals(actual, row)))
        throw new InvalidOperationException("Choose a project from this current original source catalogue."); return row.Original; }

    private void SetCompatiblePendingRows(IReadOnlyList<AssistantCompatibleConversationCheckpoint> actual)
    {
        _compatiblePending = actual;
        var selected = _snapshot.SelectedAssistant;
        _compatiblePendingRows = selected is null ? [] : actual.Where(value => value.Identity == selected.Identity &&
            value.DefinitionRevision == selected.Revision)
            .Select((checkpoint, index) => new CompatiblePendingRow(checkpoint, index)).ToArray();
    }
    internal void SetCompatiblePending(IReadOnlyList<AssistantCompatibleConversationCheckpoint> actual)
    { SetCompatiblePendingRows(actual); Refresh(); }
    internal AssistantCompatibleConversationCheckpoint DemandCompatiblePending(object? parameter)
    {
        var selected = _snapshot.SelectedAssistant;
        if (selected is null || parameter is not CompatiblePendingRow row || !_compatiblePendingRows.Any(actual => ReferenceEquals(actual, row)) ||
            row.Original.Identity != selected.Identity || row.Original.DefinitionRevision != selected.Revision)
            throw new InvalidOperationException("Read the current source-issued pending creation and select its actual row.");
        return row.Original;
    }

    public void SetStreaming(bool streaming) { _streaming = streaming; Refresh(); }
    public void SetMessages(IReadOnlyList<AssistantMessagePresentation> messages)
    {
        Set("Messages", messages);
        Set("HasConversationMessages", messages.Count != 0);
        Set("ShowEmptyConversation", messages.Count == 0 && _snapshot.ConversationBinding is not null);
        RefreshConversationSearch();
    }
    public void SetConversationStatus(string status) => Set("ConversationStatus", status);
    public bool ClearSubmittedPrompt(string original)
    { if (_prompt != original) return false; _prompt = ""; Refresh(); return true; }

    public bool TryGetValue(string path, out object? value)
    {
        if (Current) return _values.TryGetValue(path, out value);
        value = path.StartsWith("Can", StringComparison.Ordinal) || path.StartsWith("Has", StringComparison.Ordinal) ||
            path.StartsWith("Show", StringComparison.Ordinal) ? false : null;
        return true;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (!Current || _unavailable || _snapshot.IsRetiring || _branchBusy) return false;
        var accepted = false;
        _publish(() =>
        {
            if (!Current || _branchBusy) return;
            if (path == "ConversationSearchQuery") accepted = SetOriginalConversationSearchQuery(value);
            else if (path == "ProjectSearchText" && value is string search && search.Length <= 256 &&
                !_hasPendingProjectSubmission && _projectChooserVisible && !_projectBusy && !Busy)
            {
                if (_projectSearch != search)
                { _projectSearch = search; _projectSearchRevision = checked(_projectSearchRevision + 1); }
                accepted = true; Refresh();
            }
            else if (path == "Prompt" && value is string prompt)
            { _prompt = prompt; accepted = true; Refresh(); PromptChanged?.Invoke(); }
            else if (_draft is not null && !_snapshot.IsSaving && value is string text)
            { accepted = _draft.TrySetText(path, text); if (accepted) Refresh(); }
            else if (_draft is not null && !_snapshot.IsSaving && value is bool choice)
            { accepted = _draft.TrySetBoolean(path, choice); if (accepted) Refresh(); }
        });
        return accepted;
    }

    private bool CanChooseOriginalConfigurationProject()
    {
        var draft = _draft; var selected = _snapshot.SelectedAssistant;
        return draft?.Identity is not null && selected is not null && selected.Identity == draft.Identity &&
            selected.Revision == draft.ExpectedRevision && !_hasPendingProjectSubmission && !_projectBusy && !Busy;
    }

    public bool? IsActionAvailable(string command) => !_unavailable && Current && !_snapshot.IsRetiring && (command switch
    {
        "assistants.project.newtask" => CanNewConversation && !_projectBusy,
        "assistants.development.open" => _hasDevelopmentRoute && _snapshot.ConversationBinding is not null && _snapshot.Work?.Controls?.CanonicalTaskContext is not null && !Busy && !_projectBusy,
        "assistants.project.choose" => !_hasPendingProjectSubmission && _projectChooserVisible && _projectRows.Count != 0 &&
            _publishedProjectSearchRevision == _projectSearchRevision && !_projectBusy && !Busy,
        "assistants.resources.add" => CanChooseOriginalConfigurationProject(),
        "assistants.configuration.capabilities.read" => CanChooseOriginalConfigurationProject() && !_configurationCapabilities.Busy,
        "assistants.configuration.capabilities.setup.review" => CanReviewOriginalCapabilityInitialization(),
        "assistants.configuration.capabilities.setup.start" => CanStartOriginalCapabilityInitialization(),
        "assistants.configuration.capability.choose" => CanChooseOriginalConfigurationProject() &&
            _configurationCapabilities.CanSelect(_draft),
        "assistants.configuration.project.review" => _configurationProjectRows.Count != 0 &&
            IsActionAvailable("assistants.resources.add") == true,
        "assistants.project.pending.read" => _snapshot.SelectedAssistant is not null && !_hasPendingProjectSubmission && !_projectBusy && !Busy,
        "assistants.project.pending.resume" => _compatiblePendingRows.Count != 0 && !_hasPendingProjectSubmission && !_projectBusy && !Busy,
        "assistants.project.retry" => _projectChooserVisible && (_hasPendingProjectSubmission || _hasSettledProjectRefusal) && _canRetryProjectSubmission && !_projectBusy && !Busy,
        "assistants.project.refresh" or "assistants.project.search" => !_hasPendingProjectSubmission && _projectChooserVisible && !_projectBusy && !Busy,
        "assistants.project.older" => !_hasPendingProjectSubmission && _projectChooserVisible && _hasMoreProjects &&
            _publishedProjectSearchRevision == _projectSearchRevision && !_projectBusy && !Busy,
        "assistants.project.cancel" => _projectChooserVisible && !_projectBusy && !Busy,
        "assistants.create.start" => !Busy,
        "assistants.avatar.choose" => CanChooseAvatar,
        "assistants.avatar.select" => HasCurrentAvatarRows,
        "assistants.avatar.cancel" => _avatarChooserVisible && CanChooseAvatar,
        "assistants.configuration.save" => _draft is not null && !Busy && _draft.Configuration.Name.Trim().Length != 0,
        "assistants.configuration.discard" => _draft?.IsDirty == true && !Busy,
        "assistants.configuration.model.choose" => CanChooseConfigurationModel,
        "assistants.configuration.models.read" => CanReadConfigurationModels,
        "assistants.configuration.effort.choose" => _draft is not null && !Busy,
        "assistants.attachment.pick" => CanChangeAttachments && _hasAttachmentPicker && AssistantDraftAttachmentProjection.TryRead(_snapshot.Conversation, out _),
        "assistants.attachment.detach" => CanChangeAttachments && _attachmentRows.Count != 0,
        "assistants.conversation.find" => CanFindOriginalConversation && _conversationSearchQuery.Trim().Length != 0,
        "assistants.conversation.find.open" or "assistants.conversation.find.clear" or "assistants.conversation.find.show" => CanFindOriginalConversation,
        "assistants.branch.create" or "assistants.branch.switch" => CanChangeConversationBranch,
        "assistants.send" => CanSend,
        "assistants.task.start" => CanStartTask,
        "assistants.task.stop" => _snapshot.Work?.Controls?.CanStop == true && !Busy,
        "assistants.pause" => _snapshot.Work?.Controls?.CanPause == true && !Busy,
        "assistants.resume" => CanResume && !Busy,
        "assistants.task.recovery.review" => CanReviewOriginalColdTask,
        "assistants.task.recovery.resume" => CanRequestOriginalColdTask,
        "assistants.task.steer" or "assistants.task.queue" => CanSteer,
        "assistants.draft.save" => _snapshot.ConversationBinding is not null && !Busy,
        "assistants.configuration.open" => _snapshot.SelectedAssistant is not null && !Busy,
        "assistants.disable" or "assistants.enable" or "assistants.archive" or "assistants.restore" => CanChangeIdentityAvailability(command),
        "assistants.conversation.new" or "assistants.task.new" => CanNewConversation,
        "assistants.open" or "assistants.conversation.open" or "assistants.refresh" or "assistants.work.refresh" => !Busy,
        "assistants.home" or "assistants.legacy.open" => !Busy && !_streaming,
        "assistants.configuration.back" => _draft?.IsDirty != true && !Busy,
        "assistants.model.refresh" => CanRefreshConversationModels,
        "assistants.model.choose" => !_streaming && !Busy && _snapshot.Models.Count != 0,
        "assistants.computer.open" => _snapshot.ConversationBinding is not null && !Busy && !_streaming,
        "assistants.memory.open" => _snapshot.ConversationBinding is not null && !Busy && !_streaming,
        "assistants.resources.open" => _snapshot.SelectedAssistant is not null && !Busy,
        _ => false
    });

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (!IsCurrentOriginalRepeatAction(command, parameter)) return ValueTask.CompletedTask;
        if (IsActionAvailable(command) != true)
            return ValueTask.FromException(new InvalidOperationException("This Assistant action is unavailable."));
        return _dispatch(command, parameter, cancellationToken);
    }

    private bool Busy => _conversationSearchBusy || _branchBusy || _attachmentBusy || _snapshot.IsLoading || _snapshot.IsSaving || _configurationInitializationBusy || _configurationModelsBusy;
    private bool CanNewConversation => _snapshot.SelectedAssistant is { Configuration.Enabled: true, Configuration.Archived: false } && !Busy && !_streaming;
    private bool CanSend => !Busy && !_streaming && _selectedModel is not null && _prompt.Trim().Length != 0 &&
        _snapshot.Conversation?.Conversation.Kind == ConversationKind.Chat && IsIdentityEnabled;
    private bool CanStartTask => IsIdentityEnabled && !Busy && !_streaming && _selectedModel is not null && _prompt.Trim().Length != 0 &&
        _snapshot.Conversation?.Conversation.Kind == ConversationKind.Task && _snapshot.Conversation.CanonicalTask is null &&
        _snapshot.Work?.CanonicalTask is null;
    private bool CanResume => _snapshot.Work?.Controls is { } control &&
        (control.CanResumeUnstartedOriginal || control.CanResumeOriginalToolCheckpoint);
    private bool CanSteer => !_taskFollowUpBusy && !Busy && _prompt.Trim().Length != 0 && _snapshot.Work?.Controls is not null &&
        _snapshot.Work.CanonicalTask?.State is TaskExecutionLifecycle.Running or TaskExecutionLifecycle.WaitingSafeBoundary;

    private void Set(string key, object? value)
    {
        _publish(() => { if (Current) _values.Set(key, value); });
    }

    private void Refresh()
    {
        RefreshTaskProgress();
        RefreshAttachments();
        var selected = _snapshot.SelectedAssistant;
        var configuration = _draft?.Configuration;
        var usable = !_unavailable && Current && !_snapshot.IsRetiring;
        Set("CanReviewLegacy", IsActionAvailable("assistants.legacy.open") == true);
        Set("CanInspectMemory", IsActionAvailable("assistants.memory.open") == true); Set("ShowMemory", _route == "memory"); Set("ShowMiniComputer", _route == "mini-computer");
        Set("ShowLegacyMigration", _route == "migration");
        Set("ShowHome", _route == "home"); Set("ShowConfiguration", _route == "configuration"); Set("ShowWork", _route == "work");
        Set("Assistants", _snapshot.Assistants.Select(definition => new AssistantRow(definition.Identity,
            $"{Uri.EscapeDataString(definition.Identity.DenId)}/{Uri.EscapeDataString(definition.Identity.NamespaceId)}/{Uri.EscapeDataString(definition.Identity.DefinitionId)}", definition.Configuration.Name, definition.Configuration.Description,
            definition.Configuration.Archived ? "Archived" : definition.Configuration.Enabled ? "Enabled" : "Disabled", usable && !Busy, definition.Configuration.IconResourceId)).ToArray());
        Set("HasAssistants", _snapshot.Assistants.Count != 0);
        Set("RecentConversations", _snapshot.Conversations.OrderByDescending(conversation => conversation.UpdatedAt).Take(8).Select(conversation => new ConversationRow(
            conversation.ConversationId, conversation.Title, selected?.Configuration.Name ?? "",
            conversation.UpdatedAt.ToLocalTime().ToString("g"), usable && !Busy)).ToArray());
        Set("HasRecentConversations", _snapshot.Conversations.Count != 0);
        Set("SelectedConversations", _snapshot.Conversations.Select(conversation => new ConversationRow(
            conversation.ConversationId, conversation.Title, selected?.Configuration.Name ?? "",
            conversation.UpdatedAt.ToLocalTime().ToString("g"), usable && !Busy)).ToArray());
        Set("HasSelectedConversations", _snapshot.Conversations.Count != 0);
        var work = _snapshot.Work?.CanonicalTask;
        var active = work?.State is TaskExecutionLifecycle.Running or TaskExecutionLifecycle.WaitingSafeBoundary or TaskExecutionLifecycle.Blocked or TaskExecutionLifecycle.Suspended;
        Set("ActiveWork", active && work is not null ? new[] { new WorkRow(work.TaskId, work.PromptSummary, work.State.ToString(), false) } : Array.Empty<WorkRow>());
        Set("HasActiveWork", active);
        Set("SelectedWork", work is null ? Array.Empty<WorkRow>() : new[] { new WorkRow(work.TaskId, work.PromptSummary, work.State.ToString(), false) });
        Set("HasSelectedWork", work is not null);
        Set("Status", _snapshot.IsSaving ? "Saving…" : _snapshot.IsLoading ? "Loading…" : selected is null ? "Choose an Assistant or create one." : "");
        Set("Error", _snapshot.Error ?? ""); Set("HasError", _snapshot.Error is not null);
        Set("DependencyStatus", string.Join(" · ", _snapshot.HostCapabilities.Where(capability => capability.State != AssistantSupportState.Available).Select(capability => capability.Reason)));
        Set("HasDependencyStatus", _snapshot.HostCapabilities.Any(capability => capability.State != AssistantSupportState.Available));
        RefreshHomeReadiness();
        Set("CanCreate", usable && !Busy); Set("CanRefresh", usable && !Busy);
        Set("SelectedAssistantName", selected?.Configuration.Name ?? "Assistant");
        Set("SelectedAssistantDescription", selected?.Configuration.Description ?? "");
        Set("SelectedAssistantActivity", work?.State.ToString() ?? "No current task selected.");
        Set("CanConfigure", usable && selected is not null && !Busy);
        Set("ShowProjectSelection", _projectChooserVisible); Set("ProjectChoices", _projectChooserVisible && _publishedProjectSearchRevision == _projectSearchRevision ? _projectRows : []); Set("HasProjectChoices", _projectRows.Count != 0);
        Set("ProjectSelectionTitle", _projectOpensDevelopment ? "Open this Task in Dev" : "Start a Task in a saved project");
        Set("ProjectSelectionStatus", _projectStatus); Set("ProjectSelectionMore", _hasMoreProjects ? "Older saved contexts are available." : "");
        Set("CompatiblePendingCreations", _compatiblePendingRows);
        Set("HasCompatiblePendingCreations", _compatiblePendingRows.Count != 0);
        Set("CanReadCompatiblePending", IsActionAvailable("assistants.project.pending.read") == true);
        Set("CanResumeCompatiblePending", IsActionAvailable("assistants.project.pending.resume") == true);
        Set("ProjectSearchText", _projectSearch); Set("CanSearchProjects", IsActionAvailable("assistants.project.search") == true);
        Set("CanReadOlderProjects", IsActionAvailable("assistants.project.older") == true);
        Set("CanNewProjectTask", IsActionAvailable("assistants.project.newtask") == true);
        Set("CanOpenDevelopment", IsActionAvailable("assistants.development.open") == true);
        Set("DevelopmentRouteStatus", _hasDevelopmentRoute ? "Dev opens only after the canonical owner confirms the SAME Task and project." : "The genuine native Dev route is unavailable here.");
        Set("CanChooseProject", IsActionAvailable("assistants.project.choose") == true); Set("CanCloseProjectSelection", IsActionAvailable("assistants.project.cancel") == true);
        Set("CanRefreshProjects", IsActionAvailable("assistants.project.refresh") == true);
        Set("HasPendingProjectCreation", _hasPendingProjectSubmission);
        Set("HasDeclinedProjectCreation", _hasSettledProjectRefusal);
        Set("PendingProjectCreationTitle", _pendingProjectSubmissionTitle);
        Set("CanRetryProjectCreation", IsActionAvailable("assistants.project.retry") == true);
        Set("CanNewConversation", usable && CanNewConversation); Set("CanRefreshWork", usable && _snapshot.ConversationBinding is not null && !Busy);
        Set("CanInspectResources", usable && selected is not null && !Busy);
        Set("CanPause", usable && _snapshot.Work?.Controls?.CanPause == true && !Busy);
        Set("CanResume", usable && CanResume && !Busy); Set("CanStop", usable && _snapshot.Work?.Controls?.CanStop == true && !Busy);
        Set("CanDelete", false);
        Set("ConfigurationTitle", _draft?.Identity is null ? "Create Assistant" : "Configure Assistant");
        Set("ConfigurationIdentityLabel", _draft?.Identity is null ? "Your Assistant keeps its identity across conversations." : $"Saved revision {_draft.ExpectedRevision}");
        Set("DraftName", configuration?.Name ?? ""); Set("DraftDescription", configuration?.Description ?? "");
        Set("DraftPurpose", configuration?.Purpose ?? ""); Set("DraftInstructions", configuration?.Instructions ?? "");
        Set("DraftRole", configuration?.Role ?? "");
        Set("DraftAllowCloud", configuration?.Model.AllowCloud == true);
        Set("DraftAllowFallback", configuration?.Model.AllowFallback == true);
        RefreshMemoryConfiguration(configuration, usable);
        Set("ConfiguredModelLabel", string.IsNullOrWhiteSpace(configuration?.Model.ModelId)
            ? "No preferred model. Choose from the current saved configuration catalogue."
            : $"Preferred model: {configuration.Model.ModelId} · {configuration.Model.ProviderId ?? "current provider"}");
        RefreshConfigurationModels(usable);
        Set("EffortChoices", Enum.GetValues<EffortLevel>().Select(effort => new EffortRow(effort.ToString(), effort,
            configuration?.Model.Effort == effort ? $"{effort} · selected" : effort.ToString(), usable && _draft is not null && !Busy)).ToArray());
        Set("CanEditConfiguration", usable && !Busy && _draft is not null); Set("CanLeaveConfiguration", _draft?.IsDirty != true && !Busy);
        Set("CanSaveConfiguration", usable && _draft is not null && !Busy && configuration?.Name.Trim().Length > 0);
        Set("CanDiscardConfiguration", usable && _draft?.IsDirty == true && !Busy);
        Set("SaveConfigurationLabel", _snapshot.IsSaving ? "Saving…" : _draft?.Identity is null ? "Create Assistant" : "Save changes");
        Set("ConfigurationStatus", _draft?.IsDirty == true ? "Unsaved changes" : "");
        Set("ConfigurationError", _snapshot.Error ?? ""); Set("HasConfigurationError", _snapshot.Error is not null);
        RefreshAvatarConfiguration();
        Set("EffectivePolicyLabel", string.Join(" · ", selected?.Capabilities.Select(capability => $"{capability.Feature}: {capability.State}") ?? []));
        Set("ConfigurationSettings", (selected?.Capabilities ?? []).Where(capability => capability.Feature != "Own Assistant memory").Select(capability => new SettingRow(capability.Feature, capability.Feature,
            $"{capability.Feature}: {capability.State}", capability.Reason, false)).ToArray());
        Set("Resources", (configuration?.KnowledgeResourceIds ?? selected?.Configuration.KnowledgeResourceIds ?? [])
            .Select(resource => new ResourceRow(resource, resource, "Configured reference; effective access is checked by Home.", false)).ToArray());
        if (!ReferenceEquals(_configurationProjectDraft, _draft) ||
            !ReferenceEquals(_configurationProjectReferences, configuration?.ProjectReferences))
        {
            _configurationProjectDraft = _draft; _configurationProjectReferences = configuration?.ProjectReferences;
            _configurationProjectRows = (configuration?.ProjectReferences ?? [])
                .Select((reference, index) => new ConfiguredProjectPreferenceRow(reference, index)).ToArray();
        }
        Set("ConfiguredDevelopmentProjects", _configurationProjectRows);
        Set("ConfigurationCapabilityRows", _configurationCapabilities.HasCurrentRows(_draft) ? _configurationCapabilities.Rows : []);
        Set("CanReadConfigurationCapabilities", IsActionAvailable("assistants.configuration.capabilities.read") == true);
        Set("CanChooseConfigurationCapability", IsActionAvailable("assistants.configuration.capability.choose") == true);
        Set("ConfigurationCapabilityStatus", _configurationCapabilities.Detail);
        RefreshConfigurationInitialization();
        Set("ConfiguredToolPreferences", (configuration?.ToolIds.Count ?? 0) + " saved tool preferences");
        Set("ConfiguredAppPreferences", (configuration?.ConnectedAppIds.Count ?? 0) + " saved connected App preferences");
        Set("CanReviewConfiguredProject", IsActionAvailable("assistants.configuration.project.review") == true);
        Set("ConfigurationProjectStatus", _draft?.Identity is null ? "Save the Assistant first, then choose development projects through their current source."
            : "Saved project references are preferences. Home rechecks current access for every use.");
        Set("CanAddResource", IsActionAvailable("assistants.resources.add") == true); Set("CanConfigureProactivity", false); Set("CanOpenMiniComputer", IsActionAvailable("assistants.computer.open") == true);
        RefreshProactivityConfiguration(configuration, usable);
        Set("ConversationTitle", _snapshot.Conversation?.Conversation.Title ?? "Start a conversation");
        Set("ConversationAvailability", _snapshot.ConversationBinding is null ? "Create or open a conversation for this Assistant." : "");
        Set("Models", _snapshot.Models.Select(model => new ModelRow($"{model.ProviderId}/{model.Model.Name}",
            $"{model.Model.Name} · {model.ProviderId}", model)).ToArray());
        Set("HasModels", _snapshot.Models.Count != 0); Set("CanChooseModel", usable && !Busy && !_streaming);
        Set("SelectedModelLabel", _selectedModel is null
            ? !string.IsNullOrWhiteSpace(selected?.Configuration.Model.ModelId)
                ? $"Configured model unavailable: {selected.Configuration.Model.ModelId}. Choose an available model explicitly."
                : "No authorized model is currently available."
            : $"Model: {_selectedModel.Model.Name} · {_selectedModel.ProviderId}");
        Set("Prompt", _prompt); Set("CanEditPrompt", usable && _snapshot.ConversationBinding is not null && !_branchBusy);
        Set("CanSend", usable && CanSend); Set("CanStartTask", usable && CanStartTask); Set("CanSteer", usable && CanSteer);
        Set("CanSaveDraft", usable && _snapshot.ConversationBinding is not null && !Busy);
        Set("SendLabel", _streaming ? "Responding…" : "Send");
        RefreshConversationAvailability(usable);
        RefreshIdentityAvailability();
        RefreshConversationBranches();
        RefreshConversationSearch();
        Set("AttachmentSummary", string.Join(" · ", _attachmentRows.Select(attachment => attachment.Name)));
    }

    public sealed class ConfiguredProjectPreferenceRow
    {
        internal ConfiguredProjectPreferenceRow(HavenOS.Apps.Dev.DeveloperProjectReference original, int rowId)
        { Original = original; Id = rowId; }
        internal HavenOS.Apps.Dev.DeveloperProjectReference Original { get; }
        public int Id { get; }
        public string Name => "Saved development project " + (Id + 1);
        public string AvailabilityLabel => "Configured preference; review through its canonical project source and fresh Home READ.";
    }

    public sealed class CompatiblePendingRow
    {
        internal CompatiblePendingRow(AssistantCompatibleConversationCheckpoint original, int rowId)
        { Original = original; Id = rowId; }
        internal AssistantCompatibleConversationCheckpoint Original { get; }
        public int Id { get; }
        public string Title => Original.Title;
        public string Reason => Original.Reason;
    }

    public sealed class OriginalProjectRow
    {
        internal OriginalProjectRow(AssistantOriginalProjectCandidate original, int rowId) { Original = original; Id = rowId; }
        internal AssistantOriginalProjectCandidate Original { get; }
        public int Id { get; } // Per-catalogue display key only; candidate reference remains the original command input.
        public string Title => Original.Title;
    }

    public sealed record AssistantRow(AssistantIdentity Identity, string Id, string Name, string Description, string ActivityLabel, bool CanOpen,
        string? IconResourceId = null)
    {
        public string AvatarKey => AssistantAvatarCatalogue.DisplayKey(IconResourceId);
        public string AvatarLabel => "Assistant icon: " + AssistantAvatarCatalogue.Label(IconResourceId);
    }
    public sealed record ConversationRow(Guid Id, string Title, string AssistantName, string UpdatedLabel, bool CanOpen);
    public sealed record WorkRow(Guid Id, string Title, string StatusLabel, bool CanOpen);
    public sealed record SettingRow(string Id, string Category, string ChoiceLabel, string AvailabilityLabel, bool CanChoose);
    public sealed record ResourceRow(string Id, string Name, string AvailabilityLabel, bool CanReview);
    public sealed record ModelRow(string Id, string Label, AssistantModelChoice Original);
    public sealed record EffortRow(string Id, EffortLevel Original, string Label, bool CanChoose);
}
