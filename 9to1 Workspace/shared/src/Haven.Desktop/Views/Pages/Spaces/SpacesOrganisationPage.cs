using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Spaces;

internal enum SpacesOrganisationKind { Groups, Specialists }
internal sealed record SpacesGroupTarget(Guid Id, string Version);
internal sealed record SpacesGroupRow(ContainerDefinition Definition, SpacesGroupTarget Target, int Row, int Column);
internal sealed record SpacesGroupMember(Conversation Conversation, SpacesGroupTarget Group);
internal sealed record SpacesOrganisationReadAuthority(HomeNativeWindowsComposition Composition,
    AuthenticatedResourceActor Actor, long ServicesRevision);

/// <summary>Read/inspect destinations for existing Chat containers and Specialist definitions.
/// The existing Chat owns attachment; the host owns navigation. This page cannot run a model,
/// mutate a group, write a definition, grant access, or open an arbitrary file path.</summary>
internal sealed class SpacesOrganisationPage : UserControl, ICuiWritableBindingContext,
    ICuiRepeatItemBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly SpacesOrganisationKind _kind;
    private readonly Guid _spaceId;
    private readonly IContainerRepository _containers;
    private readonly IConversationRepository _conversations;
    private readonly IContainerResourceRepository _resources;
    private readonly ICatalogRepository _catalogue;
    private readonly Func<bool> _ownerCurrent;
    private readonly Func<CancellationToken, Task<SpacesOrganisationReadAuthority?>> _admit;
    private readonly Func<SpacesGroupMember, Task, CancellationToken, Task<bool>>? _openMember;
    private readonly Func<HomeModelPickerSpecialistSelection?, CancellationToken, Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>>>? _conversation;
    private readonly Func<bool> _return;
    private readonly CuiThemePreferenceContext? _theme;
    private readonly CuiControlLoader _loader = new(HomeAppsCuiDocument.CreateControlRegistry());
    private readonly CuiViewModel _values = new();
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly Control _surface;
    private ContainerDefinition[] _groups = [];
    private AgentDefinition[] _definitions = [];
    private SpacesGroupRow[] _groupRows = [];
    private HomeSpecialistRow[] _specialistRows = [];
    private SpacesGroupMember[] _members = [];
    private ContainerResource[] _references = [];
    private ContainerDefinition? _selectedGroup;
    private HomeSpecialistRow? _selectedSpecialist;
    private HomeModelPickerConversationSnapshot? _conversationSnapshot;
    private string _query = string.Empty, _status = "Loading…";
    private long _generation, _selectionRevision, _bindingRevision;
    private int _columns = 1;
    private bool _active, _admitted, _loading, _attaching, _stopped;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private event PropertyChangedEventHandler? _changed;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    { add => _changed += value; remove => _changed -= value; }

    internal SpacesOrganisationPage(SpacesOrganisationKind kind, Guid spaceId,
        IContainerRepository containers, IConversationRepository conversations, IContainerResourceRepository resources,
        ICatalogRepository catalogue, Func<bool> ownerCurrent, Func<CancellationToken, Task<SpacesOrganisationReadAuthority?>> admit,
        Func<bool> returnToConversation,
        Func<SpacesGroupMember, Task, CancellationToken, Task<bool>>? openMember = null,
        Func<HomeModelPickerSpecialistSelection?, CancellationToken, Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>>>? conversation = null,
        CuiThemePreferenceContext? theme = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _kind = kind; _spaceId = spaceId; _containers = containers; _conversations = conversations;
        _resources = resources; _catalogue = catalogue; _ownerCurrent = ownerCurrent; _admit = admit;
        _return = returnToConversation; _openMember = openMember; _conversation = conversation; _theme = theme;
        _work = new(StopAsync, CleanupAsync);
        _values.PropertyChanged += OnValuesChanged;
        if (theme is not null) _loader.SetThemePreferenceContext(theme);
        _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
        RefreshBindings();
        using var stream = typeof(SpacesOrganisationPage).Assembly.GetManifestResourceStream(
            "Haven.Desktop.Views.Pages.Spaces.SpacesOrganisation.cui")
            ?? throw new InvalidDataException("The authored Spaces organisation document is missing.");
        using var reader = new StreamReader(stream);
        var loaded = _loader.LoadMarkup(reader.ReadToEnd(), "SpacesOrganisation.cui");
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
        {
            _loader.Dispose();
            throw new InvalidDataException("The Spaces organisation document could not render: " +
                string.Join("; ", loaded.Diagnostics.Select(item => item.Message)));
        }
        _surface = loaded.Root; _loader.WireBindings(_surface); Content = _surface;
        SizeChanged += OnSizeChanged;
    }

    internal SpacesOrganisationKind Kind => _kind;
    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal bool IsAlive => !_work.IsRetiring;
    internal IReadOnlyList<SpacesGroupRow> Groups => _groupRows;
    internal IReadOnlyList<HomeSpecialistRow> Specialists => _specialistRows;
    internal IReadOnlyList<SpacesGroupMember> Members => _members;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    private bool Current => IsAlive && _active && _ownerCurrent();

    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _active = true;
        return _work.RunAsync(original => RefreshAsync(original, token), actual => _initialization ??= actual);
    }
    internal void Deactivate()
    { Dispatcher.UIThread.VerifyAccess(); _active = false; _admitted = false; ++_generation; RefreshBindings(); }
    public bool TryGetValue(string path, out object? value) => _values.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value)
    {
        if (path != "organisation.query" || !Current || !_admitted) return false;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => Current && _admitted);
            original.RunAcceptedPublicationCallback(() => { _query = value?.ToString() ?? string.Empty; RefreshBindings(); });
        });
        return true;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = item switch
        {
            SpacesGroupRow row => path.ToLowerInvariant() switch
            {
                "id" => row.Definition.Id, "target" => row.Target, "name" => row.Definition.Name,
                "description" => string.IsNullOrWhiteSpace(row.Definition.Context) ? "No shared context configured" : row.Definition.Context,
                "updated" => row.Definition.UpdatedAt.ToLocalTime().ToString("g"),
                "row" => row.Row, "column" => row.Column, "selected" => _selectedGroup?.Id == row.Definition.Id, _ => null
            },
            HomeSpecialistRow row => path.ToLowerInvariant() switch
            {
                "id" => row.Definition.Id, "target" => row.Target, "name" => row.Definition.Name,
                "description" => row.Definition.Description, "icon" => row.IconSource, "status" => row.Status,
                "scope" => row.Scope.Summary, "row" => row.Row, "column" => row.Column,
                "selected" => _selectedSpecialist?.Definition.Id == row.Definition.Id, _ => null
            },
            SpacesGroupMember row => path.ToLowerInvariant() switch
            {
                "id" => row.Conversation.Id, "target" => row, "title" => row.Conversation.Title,
                "updated" => row.Conversation.UpdatedAt.ToLocalTime().ToString("g"), _ => null
            },
            ContainerResource row => path.ToLowerInvariant() switch
            { "id" => row.Id, "name" => row.Name, "type" => row.MediaType, _ => null },
            _ => null
        };
        return value is not null;
    }
    public bool? IsActionAvailable(string command) => command switch
    {
        "organisation.refresh" or "organisation.return" => Current && !_attaching,
        "organisation.inspect-group" or "organisation.inspect-specialist" => Current && _admitted && !_loading && !_attaching,
        "organisation.open-member" => Current && _admitted && !_loading && !_attaching && _openMember is not null,
        "organisation.attach" => Current && _admitted && !_loading && !_attaching && CanAttach,
        _ => null
    };
    private bool CanAttach => _selectedSpecialist is { Definition.IsEnabled: true, Scope.IsValid: true } selected &&
        _conversationSnapshot is { IsSafetyLocked: false } observed && _conversation is not null &&
        observed.Specialists.Any(item => item.Id == selected.Definition.Id &&
            string.Equals(item.Version, selected.Target.Snapshot, StringComparison.OrdinalIgnoreCase));

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
        var generation = _generation;
        var selects = command is "organisation.inspect-group" or "organisation.inspect-specialist";
        var revision = selects ? ++_selectionRevision : _selectionRevision;
        var group = _selectedGroup;
        var specialist = _selectedSpecialist;
        var conversation = _conversationSnapshot;
        var issuedGroup = parameter is SpacesGroupTarget groupTarget && _groupRows.Any(row => ReferenceEquals(row.Target, groupTarget));
        var issuedSpecialist = parameter is HomeSpecialistTarget specialistTarget && _specialistRows.Any(row => ReferenceEquals(row.Target, specialistTarget));
        var issuedMember = parameter is SpacesGroupMember groupMember && _members.Any(row => ReferenceEquals(row, groupMember));
        if (command == "organisation.attach") { _attaching = true; RefreshBindings(); }
        return new(_work.RunAsync(async original =>
        {
            if (command == "organisation.refresh") { await RefreshAsync(original, cancellationToken); return; }
            original.BindPublicationGuard(() => Current && generation == _generation && revision == _selectionRevision);
            try
            {
                if (!await AdmitAsync(original, cancellationToken)) return;
                switch (command)
                {
                    case "organisation.return": _return(); return;
                    case "organisation.inspect-group":
                        if (issuedGroup && parameter is SpacesGroupTarget target)
                            await InspectGroupAsync(original, target, cancellationToken);
                        return;
                    case "organisation.inspect-specialist":
                        if (issuedSpecialist && parameter is HomeSpecialistTarget targetAgent)
                            await InspectSpecialistAsync(original, targetAgent, cancellationToken);
                        return;
                    case "organisation.open-member":
                        if (!issuedMember || parameter is not SpacesGroupMember member ||
                            group is null || member.Group != GroupTarget(group)) return;
                        var currentGroup = (await ReadGroupsAsync(original, cancellationToken)).SingleOrDefault(row => row.Id == group.Id);
                        var memberRead = await ReadSourceAsync(original, token => _conversations.GetAsync(member.Conversation.Id, token), cancellationToken);
                        if (!memberRead.Accepted) return;
                        var currentMember = memberRead.Value;
                        if (!await AdmitAsync(original, cancellationToken)) return;
                        if (currentGroup is null || GroupTarget(currentGroup) != member.Group || currentMember != member.Conversation ||
                            currentMember is null || !IsEligibleMember(currentMember, currentGroup.Id))
                        { PublishStatus(original, "This group or conversation changed. Inspect the group again before opening it."); return; }
                        var settlement = Task.WhenAll(original.Task, _loader.WhenActionsIdleAsync());
                        var queued = await original.AwaitAsync(_openMember!(member, settlement, original.Token));
                        PublishStatus(original, queued ? "Opening the saved conversation in a new tab." : "The host could not open this conversation. The original chat is unchanged.");
                        return;
                    case "organisation.attach":
                        if (specialist is null || conversation is null || _conversation is null) return;
                        var definitions = await ReadSpecialistsAsync(original, cancellationToken);
                        var definition = definitions.SingleOrDefault(row => row.Id == specialist.Definition.Id);
                        if (!original.IsAcceptedPublicationCurrent) return;
                        if (definition is null || !definition.IsEnabled || HomeSpecialistCatalogueProjection.Fingerprint(definition) != specialist.Target.Snapshot)
                        { PublishStatus(original, "This Specialist changed or was disabled. Inspect it again before attaching."); return; }
                        var version = conversation.Specialists.Single(item => item.Id == definition.Id).Version;
                        var request = new HomeModelPickerSpecialistSelection(conversation.ConversationId, conversation.ModelSelectionRevision,
                            conversation.SelectedSpecialistId, conversation.SelectedSpecialistVersion, definition.Id, version);
                        var result = await original.AwaitAsync(_conversation(request, original.Token));
                        if (!original.IsAcceptedPublicationCurrent) return;
                        original.RunAcceptedPublicationCallback(() =>
                        { _conversationSnapshot = result.Succeeded ? result.Value : null; _status = result.Message; RefreshBindings(); });
                        return;
                }
            }
            catch
            {
                original.RunAcceptedPublicationCallback(() =>
                { _status = "This source or action failed. Its original failure is retained; the conversation is unchanged."; RefreshBindings(); });
                throw;
            }
            finally
            {
                if (command == "organisation.attach")
                { _attaching = false; if (IsAlive) RefreshBindings(); }
            }
        }));
    }

    private async Task<bool> AdmitAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
        => await ObserveAdmissionAsync(original, caller) is not null;

    private async Task<SpacesOrganisationReadAuthority?> ObserveAdmissionAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        original.Token.ThrowIfCancellationRequested(); caller.ThrowIfCancellationRequested();
        if (!original.IsAcceptedPublicationCurrent) return null;
        var admitted = await original.AwaitAsync(_admit(original.Token));
        caller.ThrowIfCancellationRequested();
        if (!original.IsAcceptedPublicationCurrent) return null;
        if (admitted is null) RefuseCurrentContext(original);
        return admitted;
    }
    private async Task<(bool Accepted, T Value)> ReadSourceAsync<T>(DesktopOriginalWorkLifetime.Original original,
        Func<CancellationToken, Task<T>> read, CancellationToken caller)
    {
        var before = await ObserveAdmissionAsync(original, caller);
        if (before is null) return (false, default!);
        var result = await original.AwaitAsync(read(original.Token));
        var after = await ObserveAdmissionAsync(original, caller);
        if (after is null || before != after)
        {
            RefuseCurrentContext(original);
            return (false, default!);
        }
        return (true, result);
    }
    private void RefuseCurrentContext(DesktopOriginalWorkLifetime.Original original) => original.RunAcceptedPublicationCallback(() =>
    {
        _loading = false; _admitted = false; _groups = []; _definitions = []; _members = []; _references = [];
        _selectedGroup = null; _selectedSpecialist = null; _conversationSnapshot = null;
        _status = "The Space, profile or service context changed. Reopen this destination before using its items.";
        ++_generation; ++_selectionRevision; RefreshBindings();
    });

    private async Task<ContainerDefinition[]> ReadGroupsAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        var read = await ReadSourceAsync(original, token => _containers.GetByModeAsync(HavenMode.Chat, token), caller);
        if (!read.Accepted) return [];
        var rows = read.Value;
        if (rows.Any(row => row.Id == Guid.Empty) || rows.GroupBy(row => row.Id).Any(group => group.Count() != 1))
            throw new InvalidDataException("The canonical Chat container catalogue contains an invalid or duplicate identity.");
        return rows.Where(row => row.Mode == HavenMode.Chat && !row.IsArchived).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Id).ToArray();
    }
    private async Task<AgentDefinition[]> ReadSpecialistsAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        var read = await ReadSourceAsync(original, token => _catalogue.GetAllAgentsAsync(token), caller);
        if (!read.Accepted) return [];
        return HomeSpecialistCatalogueProjection.Validate(read.Value);
    }
    private async Task RefreshAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        var generation = ++_generation; ++_selectionRevision;
        _loading = true; _admitted = false; _selectedGroup = null; _selectedSpecialist = null;
        _members = []; _references = []; _conversationSnapshot = null; _status = "Loading…"; RefreshBindings();
        original.BindPublicationGuard(() => Current && generation == _generation);
        try
        {
            if (!await AdmitAsync(original, caller)) return;
            var groups = _kind == SpacesOrganisationKind.Groups ? await ReadGroupsAsync(original, caller) : [];
            var specialists = _kind == SpacesOrganisationKind.Specialists ? await ReadSpecialistsAsync(original, caller) : [];
            if (!original.IsAcceptedPublicationCurrent) return;
            original.RunAcceptedPublicationCallback(() =>
            {
                _groups = groups; _definitions = specialists; _admitted = true; _loading = false;
                _status = _kind == SpacesOrganisationKind.Groups ? "Inspect a saved group and open its existing conversations." : "Inspect intended use, instructions and permission policy before attaching. No work starts here.";
                RefreshBindings();
            });
        }
        catch
        {
            original.RunAcceptedPublicationCallback(() =>
            { _loading = false; _admitted = false; _groups = []; _definitions = []; _status = "The catalogue is unavailable. Refresh after the source issue is resolved."; RefreshBindings(); });
            throw;
        }
    }
    private async Task InspectGroupAsync(DesktopOriginalWorkLifetime.Original original, SpacesGroupTarget target, CancellationToken caller)
    {
        var groups = await ReadGroupsAsync(original, caller);
        if (!original.IsAcceptedPublicationCurrent) return;
        var group = groups.SingleOrDefault(row => row.Id == target.Id);
        if (group is null) { PublishStatus(original, "This group was removed or archived. Refresh the list."); return; }
        var conversationRead = await ReadSourceAsync(original, token => _conversations.GetRecentInScopeAsync(ConversationScope.ForChatGroup(group.Id), 500, token), caller);
        if (!conversationRead.Accepted) return;
        var resourceRead = await ReadSourceAsync(original, token => _resources.GetByContainerAsync(group.Id, token), caller);
        if (!resourceRead.Accepted) return;
        var conversations = conversationRead.Value;
        var resources = resourceRead.Value;
        var finalGroups = await ReadGroupsAsync(original, caller);
        if (!original.IsAcceptedPublicationCurrent) return;
        if (finalGroups.SingleOrDefault(row => row.Id == group.Id) != group)
        { PublishStatus(original, "This group changed while reading it. Inspect it again."); return; }
        var members = conversations.Where(row => IsEligibleMember(row, group.Id)).ToArray();
        if (members.Select(row => row.Id).Distinct().Count() != members.Length ||
            resources.Any(row => row.ContainerId != group.Id || row.Id == Guid.Empty) || resources.Select(row => row.Id).Distinct().Count() != resources.Count)
            throw new InvalidDataException("The canonical group read contains ambiguous member or source identities.");
        original.RunAcceptedPublicationCallback(() =>
        {
            _groups = finalGroups; _selectedGroup = group;
            _members = members.Select(row => new SpacesGroupMember(row, GroupTarget(group))).ToArray(); _references = resources.ToArray();
            _status = GroupTarget(group) == target ? "Group details loaded. Sources are references; no file was opened." : "The group changed. Its current details are shown.";
            RefreshBindings();
        });
        await RevealDetailsAsync(original);
    }
    private bool IsEligibleMember(Conversation row, Guid groupId) => row.Id != Guid.Empty && row.Mode == HavenMode.Chat &&
        row.Kind == ConversationKind.Chat && row.ContainerId == groupId && !row.IsArchived && !row.IsTemporary &&
        (row.SpaceId == _spaceId || row.SpaceId is null && _spaceId == SpaceRegistry.ChatSpaceId);
    private async Task InspectSpecialistAsync(DesktopOriginalWorkLifetime.Original original, HomeSpecialistTarget target, CancellationToken caller)
    {
        var definitions = await ReadSpecialistsAsync(original, caller);
        if (!original.IsAcceptedPublicationCurrent) return;
        var definition = definitions.SingleOrDefault(row => row.Id == target.AgentId);
        HomeCoreOperationResult<HomeModelPickerConversationSnapshot>? context = null;
        if (definition is not null && _conversation is not null)
        {
            var read = await ReadSourceAsync(original, token => _conversation(null, token), caller);
            if (!read.Accepted) return;
            context = read.Value;
        }
        if (!await AdmitAsync(original, caller)) return;
        original.RunAcceptedPublicationCallback(() =>
        {
            _definitions = definitions; _selectedSpecialist = definition is null ? null : HomeSpecialistCatalogueProjection.Inspect(definition);
            _conversationSnapshot = context?.Succeeded == true ? context.Value : null;
            _status = definition is null ? "This Specialist was removed. Refresh the list." :
                _selectedSpecialist!.Target == target ? "Definition inspected. Attaching changes future messages only; it starts no work." : "The definition changed. Review its current instructions and scope.";
            RefreshBindings();
        });
        await RevealDetailsAsync(original);
    }
    internal static SpacesGroupTarget GroupTarget(ContainerDefinition definition) => new(definition.Id,
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition))));
    private void PublishStatus(DesktopOriginalWorkLifetime.Original original, string status) =>
        original.RunAcceptedPublicationCallback(() => { _status = status; RefreshBindings(); });
    private async Task RevealDetailsAsync(DesktopOriginalWorkLifetime.Original original)
    {
        if (!original.IsAcceptedPublicationCurrent) return;
        // The same admitted selection owns the queued layout and reveal. Retirement or a
        // newer selection withdraws publication, while close still joins this exact task.
        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
        {
            _surface.UpdateLayout();
            if (!original.IsAcceptedPublicationCurrent) return;
            var inspector = _surface.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control => control.Name == "organisation-inspector");
            if (inspector?.IsEffectivelyVisible != true) return;
            // A one-pixel target only exposed the inspector's top edge at the bottom of
            // the viewport. Its full measured bounds reveal the actual inspection area.
            inspector.BringIntoView();
            if (original.IsAcceptedPublicationCurrent) _surface.UpdateLayout();
        }), DispatcherPriority.Loaded).GetTask());
    }
    private void RefreshBindings()
    {
        var publication = ++_bindingRevision;
        void Set(string key, object? value) { if (publication == _bindingRevision) _values.Set(key, value); }
        var term = _query.Trim();
        _groupRows = _admitted ? _groups.Where(row => term.Length == 0 || row.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || row.Context.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select((row, index) => new SpacesGroupRow(row, GroupTarget(row), index / _columns, index % _columns)).ToArray() : [];
        _specialistRows = _admitted ? HomeSpecialistCatalogueProjection.Project(_definitions, _query, HomeSpecialistFilter.All, _columns) : [];
        var selected = _selectedSpecialist;
        Set("organisation.groups", _groupRows); Set("organisation.specialists", _specialistRows);
        Set("organisation.members", _members); Set("organisation.references", _references);
        Set("organisation.isGroups", _kind == SpacesOrganisationKind.Groups); Set("organisation.isSpecialists", _kind == SpacesOrganisationKind.Specialists);
        Set("organisation.title", _kind == SpacesOrganisationKind.Groups ? "Chat Groups" : "Specialists");
        Set("organisation.subtitle", _kind == SpacesOrganisationKind.Groups ? "Your existing conversations and shared context, organised together." : "The right help for the task. Inspect its scope before attaching.");
        Set("organisation.returnLabel", _conversation is null ? "Back to Space" : "Return to conversation");
        Set("organisation.query", _query); Set("organisation.canBrowse", Current && _admitted && !_loading && !_attaching);
        Set("organisation.status", _status); Set("organisation.loading", _loading);
        Set("organisation.columns", string.Join(' ', Enumerable.Repeat("1fr", _columns)));
        Set("organisation.rows", HomeAppHubCatalogue.AutoRows(_kind == SpacesOrganisationKind.Groups ? _groupRows.Length : _specialistRows.Length, _columns));
        Set("organisation.empty", _admitted && (_kind == SpacesOrganisationKind.Groups ? _groupRows.Length == 0 : _specialistRows.Length == 0));
        Set("organisation.emptyText", _query.Length != 0 ? "No matches. Try another search." : _kind == SpacesOrganisationKind.Groups ? "No saved Chat groups are available." : "No Specialists are configured.");
        Set("organisation.hasGroup", _selectedGroup is not null); Set("organisation.hasSpecialist", selected is not null);
        Set("organisation.hasSelection", _selectedGroup is not null || selected is not null);
        Set("organisation.groupName", _selectedGroup?.Name ?? string.Empty);
        Set("organisation.groupContext", TextOr(_selectedGroup?.Context, "No shared context configured."));
        Set("organisation.groupInstructions", TextOr(_selectedGroup?.Instructions, "No group instructions configured."));
        Set("organisation.hasMembers", _members.Length > 0); Set("organisation.hasReferences", _references.Length > 0);
        Set("organisation.noMembers", _members.Length == 0); Set("organisation.noReferences", _references.Length == 0);
        Set("organisation.canOpenMember", _openMember is not null && Current);
        Set("organisation.specialistName", selected?.Definition.Name ?? string.Empty); Set("organisation.description", selected?.Definition.Description ?? string.Empty);
        Set("organisation.instructions", TextOr(selected?.Definition.Instructions, "No additional instructions configured."));
        Set("organisation.model", selected?.ModelPolicy ?? string.Empty); Set("organisation.fallback", selected?.FallbackPolicy ?? string.Empty);
        Set("organisation.updated", selected?.Updated ?? string.Empty);
        Set("organisation.tools", selected is null ? string.Empty : selected.Scope.Capabilities.Count != 0 ? string.Join(" · ", selected.Scope.Capabilities) : selected.Scope.IsValid && !selected.Scope.HasUninspectedFields ? "No tools configured" : "Tool scope could not be confirmed");
        Set("organisation.sources", selected is null ? string.Empty : selected.Scope.Sources.Count != 0 ? string.Join(Environment.NewLine, selected.Scope.Sources) : selected.Scope.IsValid && !selected.Scope.HasUninspectedFields ? "No sources configured" : "Source scope could not be confirmed");
        Set("organisation.policy", selected?.Scope.Note ?? string.Empty);
        Set("organisation.canAttach", IsActionAvailable("organisation.attach") == true);
        Set("organisation.attachReason", selected is { Definition.IsEnabled: false } ? "This definition is disabled." :
            selected is { Scope.IsValid: false } ? "Review the malformed permission policy in its configuration owner before attaching." :
            _conversation is null ? "Open a conversation in this Space to attach a Specialist." :
            _conversationSnapshot?.IsSafetyLocked == true ? "The originating conversation is safety locked." :
            !CanAttach ? "Inspect a current definition and conversation before attaching." :
            "Attach to the retained conversation for future messages. Existing grants still apply.");
        if (publication == _bindingRevision) _loader.RefreshBindings();
    }
    private static string TextOr(string? text, string empty) => string.IsNullOrWhiteSpace(text) ? empty : text;
    private void OnValuesChanged(object? sender, PropertyChangedEventArgs args) => _changed?.Invoke(this, args);
    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (!args.WidthChanged || !IsAlive) return;
        var width = Bounds.Width / (_theme?.Current.Accessibility.DisplayScale ?? 1);
        var columns = width < 620 ? 1 : width < 970 ? 2 : 3;
        if (_columns == columns) return;
        _columns = columns; RefreshBindings();
    }
    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); token.ThrowIfCancellationRequested();
        if (_preparation is null || _preparation.IsCompletedSuccessfully) _preparation = PrepareAsync();
        return _preparation.WaitAsync(token);
    }
    private async Task<bool> PrepareAsync()
    { await _loader.WhenActionsIdleAsync(); await _work.JoinAdmittedOriginalsAsync(); return true; }
    internal Task CloseAndDrainAsync()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); return _work.CloseAndDrainAsync(); }
    internal void RequestRetirement() => _work.RequestRetirement();
    private async Task StopAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); _active = false; _admitted = false; ++_generation;
        SizeChanged -= OnSizeChanged;
        _values.PropertyChanged -= OnValuesChanged;
        _loader.Dispose(); await _loader.WhenActionsIdleAsync();
        if (_preparation is { } preparation) await preparation;
        _stopped = true;
    }
    private Task CleanupAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_stopped) throw new InvalidOperationException("Join the original organisation loader before detaching it.");
        Content = null; _groups = []; _definitions = []; _members = []; _references = []; return Task.CompletedTask;
    }
}
