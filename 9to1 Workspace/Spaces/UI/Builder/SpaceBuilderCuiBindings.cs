using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace HavenOS.Apps.Spaces.Builder;

/// <summary>Host supplies synchronous draft edits and owns/awaits asynchronous commands.</summary>
public sealed class SpaceBuilderCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly Func<SpaceBuilderState> _state;
    private readonly Func<IReadOnlyList<Haven.Core.ExtensionSkillCatalogEntry>> _installedSkills;
    private readonly Func<IReadOnlyList<Haven.Core.InstalledExtensionPackage>> _installedPlugins;
    private readonly Action<SpaceBuilderField, string> _edit;
    private readonly Action<SpaceBuilderCategory, SpaceCapabilityCategoryState> _setCategory;
    private readonly Action<SpaceCapabilityKind, string> _detachCapability;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Func<bool> _isCurrent;
    private readonly Func<bool> _canGoBack;
    private readonly Func<IReadOnlyList<SpaceBuilderBoardsSourceOption>> _sourceOptions;
    private readonly Func<bool> _canBrowseSources;
    private readonly Action<SpaceBuilderEditorView>? _selectView;
    private readonly Action<SpaceBuilderExpertSection>? _selectExpertSection;
    private readonly Func<SpaceBuilderPreview?>? _previewDraft;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>>? _validateDraft;
    private static readonly string[] CategoryOptions = ["Inherited", "Enabled", "Disabled"];
    private static readonly string[] WebAccessOptions = ["Allowed", "Ask", "Disabled"];

    public SpaceBuilderCuiBindings(Func<SpaceBuilderState> state, Action<SpaceBuilderField, string> edit,
        Action<SpaceBuilderCategory, SpaceCapabilityCategoryState> setCategory,
        Action<SpaceCapabilityKind, string> detachCapability,
        Func<IReadOnlyList<Haven.Core.ExtensionSkillCatalogEntry>> installedSkills,
        Func<IReadOnlyList<Haven.Core.InstalledExtensionPackage>> installedPlugins,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Func<bool>? isCurrent = null)
        : this(state, edit, setCategory, detachCapability, installedSkills, installedPlugins, dispatch, isCurrent, null) { }

    public SpaceBuilderCuiBindings(Func<SpaceBuilderState> state, Action<SpaceBuilderField, string> edit,
        Action<SpaceBuilderCategory, SpaceCapabilityCategoryState> setCategory,
        Action<SpaceCapabilityKind, string> detachCapability,
        Func<IReadOnlyList<Haven.Core.ExtensionSkillCatalogEntry>> installedSkills,
        Func<IReadOnlyList<Haven.Core.InstalledExtensionPackage>> installedPlugins,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Func<bool>? isCurrent = null, Func<bool>? canGoBack = null,
        Func<IReadOnlyList<SpaceBuilderBoardsSourceOption>>? sourceOptions = null, Func<bool>? canBrowseSources = null,
        Action<SpaceBuilderEditorView>? selectView = null, Action<SpaceBuilderExpertSection>? selectExpertSection = null,
        Func<SpaceBuilderPreview?>? previewDraft = null,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? validateDraft = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _edit = edit ?? throw new ArgumentNullException(nameof(edit));
        _setCategory = setCategory ?? throw new ArgumentNullException(nameof(setCategory));
        _detachCapability = detachCapability ?? throw new ArgumentNullException(nameof(detachCapability));
        _installedSkills = installedSkills ?? (() => []);
        _installedPlugins = installedPlugins ?? (() => []);
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isCurrent = isCurrent ?? (() => true);
        _canGoBack = canGoBack ?? (() => false);
        _sourceOptions = sourceOptions ?? (() => []);
        _canBrowseSources = canBrowseSources ?? (() => false);
        _selectView = selectView;
        _selectExpertSection = selectExpertSection;
        _previewDraft = previewDraft;
        _validateDraft = validateDraft;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private int CategoryIndex(SpaceBuilderCategory category) => category switch
    {
        SpaceBuilderCategory.Skills => (int)_state().Draft.CategoryPolicy.Skills,
        SpaceBuilderCategory.Plugins => (int)_state().Draft.CategoryPolicy.Plugins,
        SpaceBuilderCategory.Apps => (int)_state().Draft.CategoryPolicy.Apps,
        SpaceBuilderCategory.Models => (int)_state().Draft.CategoryPolicy.Models,
        SpaceBuilderCategory.Memory => (int)_state().Draft.CategoryPolicy.Memory,
        _ => 0
    };

    public bool TryGetValue(string path, out object? value)
    {
        var state = _state();
        value = path switch
        {
            "builder.name" => state.Draft.Name,
            "builder.description" => state.Draft.Description,
            "builder.icon" => state.Draft.IconKey,
            "builder.instructions" => state.Draft.Instructions,
            "builder.context.sourceOnly" => state.Draft.ContextPolicy.SourceOnly,
            "builder.context.includeSelection" => state.Draft.ContextPolicy.IncludeCurrentSelection,
            "builder.context.webAccess" => (int)state.Draft.ContextPolicy.WebAccess,
            "builder.context.webAccessOptions" => WebAccessOptions,
            "builder.context.maximumRetrievedItems" => state.Draft.ContextPolicy.MaximumRetrievedItems?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            "builder.boardOptions" => _sourceOptions(),
            "builder.canBrowseSources" => _isCurrent() && state.CanEdit && !state.IsBusy && _canBrowseSources(),
            "builder.contextReferences" => state.Draft.ContextReferences.Select(reference => new
            {
                id = reference.ContextId.ToString("D"),
                label = _sourceOptions().FirstOrDefault(option => option.Id == reference.CanonicalEntityId)?.Label ??
                    (reference.OwnerAppId + " / " + reference.CanonicalEntityId),
                revision = reference.RevisionToken ?? "Current version at read",
                policy = reference.Permission + (reference.IncludedAutomatically ? "; included automatically" : "; not included automatically")
            }).ToArray(),
            "builder.hasContextReferences" => state.Draft.ContextReferences.Count != 0,
            "builder.legacyFiles" => state.Draft.LegacyFiles.Select(reference => new { path = reference.Path, label = reference.DisplayName }).ToArray(),
            "builder.hasLegacyFiles" => state.Draft.LegacyFiles.Count != 0,
            "builder.inherit.sources" => state.Draft.Inheritance.Sources,
            "builder.inherit.apps" => state.Draft.Inheritance.ConnectedApps,
            "builder.inherit.models" => state.Draft.Inheritance.ModelPolicy,
            "builder.inherit.memory" => state.Draft.Inheritance.MemoryPolicy,
            "builder.inherit.plugins" => state.Draft.Inheritance.Plugins,
            "builder.inherit.skills" => state.Draft.Inheritance.Skills,
            "builder.inherit.agents" => state.Draft.Inheritance.Agents,
            "builder.capabilities" => state.Draft.CapabilityBindings.Select(b => new
            {
                id = b.CapabilityId,
                kind = b.Kind.ToString(),
                key = b.Kind.ToString() + ":" + b.CapabilityId,
                version = b.VersionPin ?? "(unpinned)"
            }).ToArray(),
            "builder.hasNoCapabilities" => state.Draft.CapabilityBindings.Count == 0,
            "builder.installedSkills" => _installedSkills().Select(s => new
            {
                id = s.SkillId,
                name = s.Name,
                version = s.PackageVersion,
                status = s.IsAvailable ? "Available" : "Unavailable",
                attached = state.Draft.CapabilityBindings.Any(b => b.Kind == SpaceCapabilityKind.Skill && b.CapabilityId == s.SkillId)
            }).ToArray(),
            "builder.installedPlugins" => _installedPlugins().Select(p => new
            {
                id = p.Manifest.PackageId,
                name = p.Manifest.DisplayName,
                version = p.Manifest.Version,
                state = p.State.ToString(),
                attached = state.Draft.CapabilityBindings.Any(b => b.Kind == SpaceCapabilityKind.Plugin && b.CapabilityId == p.Manifest.PackageId)
            }).ToArray(),
            "builder.category.options" => CategoryOptions,
            "builder.category.skills" => CategoryIndex(SpaceBuilderCategory.Skills),
            "builder.category.plugins" => CategoryIndex(SpaceBuilderCategory.Plugins),
            "builder.category.apps" => CategoryIndex(SpaceBuilderCategory.Apps),
            "builder.category.models" => CategoryIndex(SpaceBuilderCategory.Models),
            "builder.category.memory" => CategoryIndex(SpaceBuilderCategory.Memory),
            "builder.canEdit" => _isCurrent() && state.CanEdit,
            "builder.canSave" => _isCurrent() && state.CanSave,
            "builder.canDiscard" => _isCurrent() && state.CanDiscard,
            "builder.canReload" => _isCurrent() && state.CanReload,
            "builder.canGoBack" => _isCurrent() && _canGoBack(),
            "builder.status" => state.StatusMessage,
            "builder.view.isBasic" => state.EditorView == SpaceBuilderEditorView.Basic,
            "builder.view.isAdvanced" => state.EditorView == SpaceBuilderEditorView.Advanced,
            "builder.view.isExpert" => state.EditorView == SpaceBuilderEditorView.Expert,
            "builder.expert.isDetails" => state.ExpertSection == SpaceBuilderExpertSection.Details,
            "builder.expert.isHarness" => state.ExpertSection == SpaceBuilderExpertSection.HarnessLogic,
            "builder.expert.isModel" => state.ExpertSection == SpaceBuilderExpertSection.ModelLogic,
            "builder.expert.isUserMessage" => state.ExpertSection == SpaceBuilderExpertSection.UserMessageLogic,
            "builder.expert.isFiles" => state.ExpertSection == SpaceBuilderExpertSection.Files,
            "builder.hasExpertConfiguration" => state.HasExpertConfiguration,
            "builder.graph.nodeCount" => state.Draft.LayoutDocument?.Nodes.Count ?? 0,
            "builder.graph.edgeCount" => state.Draft.LayoutDocument?.Edges.Count ?? 0,
            "builder.preview.exists" => state.Preview is not null,
            "builder.preview.name" => state.Preview?.Name ?? string.Empty,
            "builder.preview.description" => state.Preview?.Description ?? string.Empty,
            "builder.preview.instructions" => state.Preview?.Instructions ?? string.Empty,
            "builder.preview.notice" => state.Preview?.Notice ?? string.Empty,
            "builder.validation.summary" => state.ValidationIssues.Count == 0 ? string.Empty : string.Join(" ", state.ValidationIssues),
            _ => null
        };
        return value is not null;
    }

    public bool TrySetValue(string path, object? value)
    {
        if (!_isCurrent() || !_state().CanEdit) return false;
        SpaceBuilderField? field = path switch
        {
            "builder.name" => SpaceBuilderField.Name,
            "builder.description" => SpaceBuilderField.Description,
            "builder.icon" => SpaceBuilderField.IconKey,
            "builder.instructions" => SpaceBuilderField.Instructions,
            "builder.context.maximumRetrievedItems" => SpaceBuilderField.MaximumRetrievedItems,
            _ => null
        };
        if (field is not null && value is string text)
        {
            _edit(field.Value, text);
            return true;
        }
        SpaceBuilderField? policySwitch = path switch
        {
            "builder.context.sourceOnly" => SpaceBuilderField.SourceOnly,
            "builder.context.includeSelection" => SpaceBuilderField.IncludeCurrentSelection,
            "builder.inherit.sources" => SpaceBuilderField.InheritSources,
            "builder.inherit.apps" => SpaceBuilderField.InheritConnectedApps,
            "builder.inherit.models" => SpaceBuilderField.InheritModelPolicy,
            "builder.inherit.memory" => SpaceBuilderField.InheritMemoryPolicy,
            "builder.inherit.plugins" => SpaceBuilderField.InheritPlugins,
            "builder.inherit.skills" => SpaceBuilderField.InheritSkills,
            "builder.inherit.agents" => SpaceBuilderField.InheritAgents,
            _ => null
        };
        if (policySwitch is not null && value is bool enabled)
        {
            _edit(policySwitch.Value, enabled ? "true" : "false");
            return true;
        }
        if (path == "builder.context.webAccess" && value is int web && web >= 0 && web < WebAccessOptions.Length)
        {
            _edit(SpaceBuilderField.WebAccess, ((SpaceWebAccessPolicy)web).ToString());
            return true;
        }
        SpaceBuilderCategory? cat = path switch
        {
            "builder.category.skills" => SpaceBuilderCategory.Skills,
            "builder.category.plugins" => SpaceBuilderCategory.Plugins,
            "builder.category.apps" => SpaceBuilderCategory.Apps,
            "builder.category.models" => SpaceBuilderCategory.Models,
            "builder.category.memory" => SpaceBuilderCategory.Memory,
            _ => null
        };
        if (cat is not null && value is int idx && idx >= 0 && idx <= 2)
        {
            _setCategory(cat.Value, (SpaceCapabilityCategoryState)idx);
            return true;
        }
        return false;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is null) return false;
        var type = item.GetType();
        var prop = type.GetProperty(path, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (prop is not null)
        {
            value = prop.GetValue(item);
            return true;
        }
        return false;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    public bool? IsActionAvailable(string command) => command switch
    {
        "space-builder.save" => _isCurrent() && _state().CanSave,
        "space-builder.view.basic" or "space-builder.view.advanced" or "space-builder.view.expert" => _isCurrent(),
        "space-builder.preview" or "space-builder.validate" => _isCurrent() && _state().IsLoaded && !_state().IsBusy,
        "space-builder.expert.details" or "space-builder.expert.harness" or "space-builder.expert.model" or
            "space-builder.expert.user-message" or "space-builder.expert.files" => _isCurrent(),
        "space-builder.discard" => _isCurrent() && _state().CanDiscard,
        "space-builder.reload" => _isCurrent() && _state().CanReload,
        "space-builder.back" => _isCurrent() && _canGoBack(),
        "space-builder.source.browse" or "space-builder.source.attach" => _isCurrent() && _state().CanEdit && !_state().IsBusy && _canBrowseSources(),
        "space-builder.source.remove" or "space-builder.legacy-file.remove" => _isCurrent() && _state().CanEdit,
        "space-builder.capability.remove" => _isCurrent() && _state().CanEdit,
        "space-builder.skill.attach" or "space-builder.plugin.attach" => _isCurrent() && _state().CanEdit,
        _ => null
    };

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true)
            throw new InvalidOperationException("This Space Builder command is unavailable.");
        if (command.StartsWith("space-builder.view.", StringComparison.Ordinal))
        {
            _selectView?.Invoke(command switch
            {
                "space-builder.view.basic" => SpaceBuilderEditorView.Basic,
                "space-builder.view.advanced" => SpaceBuilderEditorView.Advanced,
                "space-builder.view.expert" => SpaceBuilderEditorView.Expert,
                _ => throw new InvalidOperationException("Unknown Builder view.")
            });
            Refresh();
            return;
        }
        if (command == "space-builder.preview")
        {
            _previewDraft?.Invoke();
            Refresh();
            return;
        }
        if (command == "space-builder.validate")
        {
            if (_validateDraft is not null) await _validateDraft(cancellationToken);
            Refresh();
            return;
        }
        if (command.StartsWith("space-builder.expert.", StringComparison.Ordinal))
        {
            _selectExpertSection?.Invoke(command switch
            {
                "space-builder.expert.details" => SpaceBuilderExpertSection.Details,
                "space-builder.expert.harness" => SpaceBuilderExpertSection.HarnessLogic,
                "space-builder.expert.model" => SpaceBuilderExpertSection.ModelLogic,
                "space-builder.expert.user-message" => SpaceBuilderExpertSection.UserMessageLogic,
                "space-builder.expert.files" => SpaceBuilderExpertSection.Files,
                _ => throw new InvalidOperationException("Unknown Expert section.")
            });
            Refresh();
            return;
        }
        if (command == "space-builder.capability.remove" && parameter is string compositeKey)
        {
            var parts = compositeKey.Split(':', 2);
            if (parts.Length == 2 && Enum.TryParse<SpaceCapabilityKind>(parts[0], out var kind))
            {
                _detachCapability(kind, parts[1]);
                return;
            }
        }
        await _dispatch(command, parameter, cancellationToken);
    }
}
