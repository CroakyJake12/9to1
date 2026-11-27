using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperSavedProjectsPage
{
    private BuildProjectStartPreferences? _startPreferences;
    private BuildProjectStartPreferences.Snapshot? _startPreferencesSnapshot;
    private string _projectView = "recent";
    private Guid? _editingSetupDraftId;
    private bool _editingSetupDraftWasSaved, _setupPreferencesBusy, _startPreferencesAvailable;
    private string _setupDraftName = "";
    private string _setupStatus = "Setup draft storage is unavailable in this host. Choices stay while this page is open.";
    private long _setupInputRevision, _setupNavigationRevision;

    internal void ConfigureStartPreferences(BuildProjectStartPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (_gate)
        {
            if (_active || _work.IsRetiring || _startPreferences is not null)
                throw new InvalidOperationException("Bind the original profile's Build preferences once, before activation.");
            _startPreferences = preferences;
            _setupStatus = "Reading saved setup drafts and project pins…";
        }
    }

    private Task<BuildProjectStartPreferences.Snapshot?> ReadStartPreferencesAsync(
        DesktopOriginalWorkLifetime.Original original, CancellationToken token) => _startPreferences is null
        ? Task.FromResult<BuildProjectStartPreferences.Snapshot?>(null)
        : ReadBoundAsync(original, token);

    private async Task<BuildProjectStartPreferences.Snapshot?> ReadBoundAsync(
        DesktopOriginalWorkLifetime.Original original, CancellationToken token) =>
        await original.AwaitAsync(InvokeOriginalSource(() => _startPreferences!.ReadAsync(token))).ConfigureAwait(false);

    private void PublishStartPreferencesLocked(BuildProjectStartPreferences.Snapshot? observed)
    {
        if (observed is null) return;
        if (_startPreferencesSnapshot is null || observed.List.Revision >= _startPreferencesSnapshot.List.Revision)
            _startPreferencesSnapshot = observed;
        _startPreferencesAvailable = true;
        _setupStatus = _startPreferences!.CanEdit
            ? "Save setup choices without creating source, folders or projects. Creation still requires the existing review and approvals."
            : "Saved setup choices are read-only because guarded preference updates are unavailable.";
    }

    private bool TryGetSetupValue(string path, out object? value)
    {
        value = path switch
        {
            "SetupDraftName" => _setupDraftName,
            "SetupStatus" => _setupStatus,
            "SetupFormVisible" => _startSurface is "new" or "extension",
            "SetupSaveLabel" => _editingSetupDraftWasSaved ? "Save draft changes" : "Save setup draft",
            "CanSaveSetupDraft" => IsActionAvailable("dev.setup.save"),
            "CanStartAnotherDraft" => IsActionAvailable("dev.setup.new"),
            "ProjectViewRecent" => _projectView == "recent",
            "ProjectViewPinned" => _projectView == "pinned",
            "ProjectViewDrafts" => _projectView == "drafts",
            "ProjectListVisible" => _projectView != "drafts",
            "SetupDraftListVisible" => _projectView is "drafts" or "pinned",
            "SetupDrafts" => FilteredSetupDrafts(),
            "SetupEmptyDrafts" => _projectView == "drafts" && FilteredSetupDrafts().Length == 0,
            "SetupEmptyMessage" => !_startPreferencesAvailable ? _setupStatus :
                _projectQuery.Trim().Length > 0 ? "No setup drafts match this search." : "No setup drafts saved yet. Start a new project and save its setup choices.",
            "ProjectViewStatus" => ProjectViewStatus(),
            _ => null
        };
        return value is not null;
    }

    private bool TrySetSetupValue(string path, object? value)
    {
        if (path != "SetupDraftName" || value is not string text) return false;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                _setupDraftName = text;
                _setupInputRevision = checked(_setupInputRevision + 1);
            }
            NotifyOriginal(original);
        });
        return true;
    }

    private bool TryGetSetupItemValue(object item, string path, out object? value)
    {
        lock (_gate)
        {
            if (item is BuildSetupDraft draft)
            {
                value = path switch
                {
                    "SelectionKey" => draft.Id.ToString("D"),
                    "Title" => draft.Name,
                    "Destination" => draft.Destination.Length == 0 ? "No destination chosen" : draft.Destination,
                    "TemplateSummary" => DraftTemplateSummary(draft),
                    "SavedLabel" => "Saved " + draft.UpdatedAt.ToUniversalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) + " UTC",
                    "PinLabel" => draft.Pinned ? "Unpin" : "Pin",
                    "PinAccessibleName" => (draft.Pinned ? "Unpin " : "Pin ") + draft.Name,
                    "CanPin" => IsActionAvailable("dev.setup.pin-draft"),
                    "CanResume" => IsActionAvailable("dev.setup.resume"),
                    _ => null
                };
                return value is not null;
            }
            if (ProjectIdentity(item) is { } identity)
            {
                var pinned = _startPreferencesSnapshot?.List.Projects.Any(project => project.Identity == identity && project.Pinned) == true;
                value = path switch
                {
                    "PinLabel" => pinned ? "Unpin" : "Pin",
                    "CanPin" => IsActionAvailable("dev.setup.pin-project"),
                    _ => null
                };
                return value is not null;
            }
        }
        value = null;
        return false;
    }

    private BuildSetupDraft[] FilteredSetupDrafts()
    {
        if (!_startPreferencesAvailable || _startPreferencesSnapshot is null || _projectView == "recent") return [];
        var query = _projectQuery.Trim();
        return _startPreferencesSnapshot.List.Drafts.Where(draft => _projectView != "pinned" || draft.Pinned)
            .Where(draft => query.Length == 0 || (draft.Name + "\n" + draft.Destination + "\n" + draft.ProviderId + "\n" + draft.TemplateId)
                .Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(draft => draft.UpdatedAt).ThenBy(draft => draft.Id).ToArray();
    }

    private string DraftTemplateSummary(BuildSetupDraft draft)
    {
        if (draft.Flow == "extension") return "Extension setup · target SDK has not been selected";
        if (draft.ProviderId is null) return "Project setup · no template selected";
        var option = _templateOptions.SingleOrDefault(item => item.Template.ProviderId == draft.ProviderId && item.Template.TemplateId == draft.TemplateId);
        return option is null ? "Saved template unavailable: " + draft.ProviderId + " / " + draft.TemplateId
            : option.Template.DisplayName + (option.CanCreate ? " · review required" : " · required toolchain unavailable");
    }

    private string ProjectViewStatus()
    {
        if (_projectView == "drafts") return "Setup drafts contain saved choices only. Resume does not create a project or start tools.";
        if (_projectView == "recent") return "Recently opened projects appear first. Other current saved projects remain available below.";
        if (!_startPreferencesAvailable || _startPreferencesSnapshot is null) return _setupStatus;
        if (!_metadataAvailable) return _status;
        var current = UnfilteredProjectItems().Select(item => ProjectIdentity(item.Value)).Where(identity => identity is not null).ToHashSet();
        var missing = _startPreferencesSnapshot.List.Projects.Count(project => project.Pinned && !current.Contains(project.Identity));
        return missing == 0 ? "Pinned projects still use their current canonical source. Pinned setup drafts remain uncreated."
            : $"{missing} pinned project reference(s) are unavailable in the current source. Refresh to recheck; no replacement project was selected.";
    }

    private static BuildProjectStartIdentity? ProjectIdentity(object item) => item switch
    {
        ManualProjectItem manual => BuildProjectStartIdentity.ForManual(manual.Reference),
        ProjectItem task => BuildProjectStartIdentity.ForTask(task.Original.SpaceId, task.Original.ContextReferenceId, task.Original.Link.Project),
        _ => null
    };

    private bool IsPinnedProject(object item) => ProjectIdentity(item) is { } identity &&
        _startPreferencesSnapshot?.List.Projects.Any(project => project.Identity == identity && project.Pinned) == true;
    private DateTimeOffset? ProjectLastOpened(object item) => ProjectIdentity(item) is { } identity
        ? _startPreferencesSnapshot?.List.Projects.SingleOrDefault(project => project.Identity == identity)?.LastOpenedAt : null;

    private static bool IsSetupCommand(string command) => command is "dev.setup.save" or "dev.setup.resume" or
        "dev.setup.pin-draft" or "dev.setup.pin-project" or "dev.setup.new" or
        "dev.setup.recent" or "dev.setup.pinned" or "dev.setup.drafts";

    private bool IsSetupActionAvailable(string command)
    {
        if (_manualBusy || _templateBusy || _startFolderPicking) return false;
        if (command is "dev.setup.recent" or "dev.setup.pinned" or "dev.setup.drafts") return true;
        if (command == "dev.setup.new") return !_setupPreferencesBusy;
        if (_setupPreferencesBusy || !_startPreferencesAvailable || _startPreferencesSnapshot is null) return false;
        if (command == "dev.setup.resume") return FilteredSetupDrafts().Length > 0;
        if (_startPreferences?.CanEdit != true) return false;
        return command switch
        {
            "dev.setup.save" => (_startSurface is "new" or "extension") && BuildProjectStartState.ValidText(_setupDraftName.Trim(), 120) &&
                (!_editingSetupDraftWasSaved || _startPreferencesSnapshot.List.Drafts.Any(draft => draft.Id == _editingSetupDraftId)),
            "dev.setup.pin-draft" => FilteredSetupDrafts().Length > 0,
            "dev.setup.pin-project" => _metadataAvailable && MergeProjectItems().Length > 0,
            _ => false
        };
    }

    private ValueTask DispatchSetupActionAsync(string command, object? parameter, CancellationToken caller)
    {
        if (command is "dev.setup.recent" or "dev.setup.pinned" or "dev.setup.drafts" or "dev.setup.new")
            return ChangeSetupView(command, caller);

        long generation, setupRevision, templateRevision, navigationRevision;
        string surface;
        BuildProjectStartPreferences.Snapshot expected;
        BuildSetupDraft? draft = null;
        BuildProjectStartIdentity? project = null;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("This setup metadata action is unavailable.");
            generation = _generation; setupRevision = _setupInputRevision; templateRevision = _templateInputRevision; surface = _startSurface;
            navigationRevision = _setupNavigationRevision;
            expected = _startPreferencesSnapshot!;
            if (command == "dev.setup.save")
            {
                var id = _editingSetupDraftId ??= Guid.NewGuid(); // One ID survives retry or newer unsaved inputs.
                var prior = expected.List.Drafts.SingleOrDefault(item => item.Id == id);
                var now = DateTimeOffset.UtcNow;
                if (prior?.UpdatedAt is { } later && later > now) now = later;
                draft = new(id, _setupDraftName.Trim(), surface, _manualFolderPath,
                    surface == "new" && _selectedTemplateProviderId.Length > 0 ? _selectedTemplateProviderId : null,
                    surface == "new" && _selectedTemplateId.Length > 0 ? _selectedTemplateId : null,
                    prior?.CreatedAt ?? now, now, prior?.Pinned == true);
            }
            else if (command is "dev.setup.resume" or "dev.setup.pin-draft")
                draft = FilteredSetupDrafts().SingleOrDefault(item => item.Id.ToString("D") == parameter as string)
                    ?? throw new InvalidOperationException("Choose the same saved setup draft from the current filtered list.");
            else if (command == "dev.setup.pin-project")
                project = ProjectIdentity(MergeProjectItems().SingleOrDefault(item => SelectionKey(item) == parameter as string)
                    ?? throw new InvalidOperationException("Choose the same project from the current filtered list."));
            _setupPreferencesBusy = true;
        }
        try { return new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, caller, _connectionLifetime, _windowLifetime);
            try
            {
                original.DemandPublication();
                await PublishAsync(original, () => _setupStatus = command == "dev.setup.resume"
                    ? "Opening saved setup choices…" : "Saving setup preferences…").ConfigureAwait(false);
                var home = await ObserveOriginalHomeAsync(original, linked.Token).ConfigureAwait(false);
                if (home.State != CuiSceneAvailabilityState.Ready)
                { await PublishAsync(original, () => _setupStatus = home.Message).ConfigureAwait(false); return; }
                original.DemandPublication();
                if (command == "dev.setup.resume")
                {
                    var current = await original.AwaitAsync(InvokeOriginalSource(() => _startPreferences!.ReadAsync(linked.Token))).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        PublishStartPreferencesLocked(current);
                        if (_startPreferencesSnapshot!.List.Drafts.SingleOrDefault(item => item.Id == draft!.Id) != draft)
                        { _setupStatus = "This saved draft changed or was removed. Refresh and choose its current entry; existing setup choices were retained."; return; }
                        if (_setupInputRevision != setupRevision || _templateInputRevision != templateRevision ||
                            _setupNavigationRevision != navigationRevision || _startSurface != surface)
                        { _setupStatus = "Setup choices changed while the draft was opening. Your current choices were retained."; return; }
                        ResumeSetupDraftLocked(draft!);
                    }).ConfigureAwait(false);
                    return;
                }
                var candidate = expected.List;
                if (command == "dev.setup.save")
                    candidate = candidate with { Drafts = candidate.Drafts.Where(item => item.Id != draft!.Id).Append(draft!).ToArray() };
                else if (command == "dev.setup.pin-draft")
                    candidate = candidate with { Drafts = candidate.Drafts.Select(item => item.Id == draft!.Id ? item with { Pinned = !item.Pinned } : item).ToArray() };
                else
                {
                    var prior = candidate.Projects.SingleOrDefault(item => item.Identity == project);
                    candidate = candidate with { Projects = candidate.Projects.Where(item => item.Identity != project)
                        .Append(new(project!, prior?.Pinned != true, prior?.LastOpenedAt)).ToArray() };
                }
                var saved = await original.AwaitAsync(InvokeOriginalSource(() => _startPreferences!.SaveAsync(expected, candidate, linked.Token))).ConfigureAwait(false);
                if (IsCurrent(generation))
                    await PublishAsync(original, () =>
                    {
                        PublishStartPreferencesLocked(saved.Current);
                        if (!saved.Saved)
                        { _setupStatus = "Build preferences changed elsewhere. The current saved list was refreshed; your setup choices were retained. Review and save again."; return; }
                        if (command == "dev.setup.save")
                        {
                            if (_editingSetupDraftId == draft!.Id) _editingSetupDraftWasSaved = true;
                            var sameInputs = _setupInputRevision == setupRevision && _templateInputRevision == templateRevision && _startSurface == surface;
                            _setupStatus = "Saved setup draft ‘" + draft!.Name + "’. " + (sameInputs
                                ? "No folder, source or project was created." : "Newer setup choices remain unsaved; no folder, source or project was created.");
                        }
                        else _setupStatus = "Pin preference saved. Project and draft identities are unchanged.";
                    }).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                original.Retain(error);
                if (IsCurrent(generation))
                {
                    Task? status = null;
                    try
                    {
                        status = PublishAsync(original, () => _setupStatus = "Setup metadata could not be changed. Your setup choices were retained. " + error.Message);
                        await original.AwaitAsync(status).ConfigureAwait(false);
                    }
                    catch (Exception publicationError) { original.Capture(status, publicationError); }
                }
                original.ThrowRetained();
            }
            finally
            {
                lock (_gate) _setupPreferencesBusy = false;
                if (IsCurrent(generation)) await PublishAsync(original, () => { }).ConfigureAwait(false);
            }
        })); }
        catch { lock (_gate) _setupPreferencesBusy = false; throw; }
    }

    private ValueTask ChangeSetupView(string command, CancellationToken caller)
    {
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication(); caller.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (IsActionAvailable(command) != true) throw new InvalidOperationException("This setup view is unavailable.");
                _setupNavigationRevision = checked(_setupNavigationRevision + 1);
                if (command == "dev.setup.new")
                {
                    _editingSetupDraftId = null; _editingSetupDraftWasSaved = false; _setupDraftName = "";
                    _setupInputRevision = checked(_setupInputRevision + 1);
                    _setupStatus = "Name this new setup draft. Existing setup choices are retained until you change them.";
                }
                else { _projectView = command["dev.setup.".Length..]; _projectPage = 0; }
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }

    private void ResumeSetupDraftLocked(BuildSetupDraft draft)
    {
        _editingSetupDraftId = draft.Id; _editingSetupDraftWasSaved = true; _setupDraftName = draft.Name;
        _startSurface = draft.Flow; _manualFolderPath = draft.Destination;
        _templateProjectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(draft.Destination));
        _selectedTemplateProviderId = draft.ProviderId ?? ""; _selectedTemplateId = draft.TemplateId ?? "";
        _templateQuery = "";
        _templateProviderIndex = _templateProjectTypeIndex = _templateLanguageIndex = _templateFrameworkIndex =
            _templateRuntimeIndex = _templatePlatformIndex = _templateSurfaceIndex = _templateOrganisationIndex = _templateReadinessIndex = 0;
        _templateReview = null;
        _templateInputRevision = checked(_templateInputRevision + 1); _setupInputRevision = checked(_setupInputRevision + 1);
        _templateReviewStatus = "Saved setup choices restored. Review the current template and exact files again before creation.";
        _setupStatus = "Resumed ‘" + draft.Name + "’. " + DraftTemplateSummary(draft) + ". No project was opened or created.";
    }
}
