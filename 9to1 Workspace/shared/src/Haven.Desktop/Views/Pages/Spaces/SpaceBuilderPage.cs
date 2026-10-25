using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces.Builder;

namespace Haven.Desktop.Views.Pages.Spaces;

/// <summary>Retained canonical Space Builder content surface. Borrowed SpaceRegistry;
/// hosts the authored SpaceBuilder.cui through SpaceBuilderController and
/// SpaceBuilderCuiBindings.</summary>
public sealed partial class SpaceBuilderPage : UserControl, IDesktopOriginalRetirementParticipant,
    IDesktopOriginalRetirementJoinGuard, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SpaceBuilderController _controller;
    private readonly SpaceRegistry _originalRegistry;
    private OriginalSpaceRequestPolicySource? _boardsSource;
    private ISpaceDraftActivationValidator? _activationValidator;
    private IReadOnlyList<SpaceBuilderBoardsSourceOption> _boardsChoices = [];
    private long? _boardsChoicesRevision;
    private bool _sourcePickerBusy;
    private readonly ExtensionManager? _extensions;
    private readonly SpaceBuilderCuiBindings _bindings;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiSceneHost _host;
    private readonly Grid _contentGrid;
    private SpaceLayoutEditorPage? _graphEditor;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly Func<Task, bool>? _queueBack;
    private Task? _activation;
    private Task? _hostClose;
    private Task<CuiSceneAvailability>? _show;
    private bool _live = true;
    private bool _activated;
    private long _editEpoch;
    private int _controllerOperations;
    private ClosePreparation? _lease;
    private IReadOnlyList<Haven.Core.ExtensionSkillCatalogEntry> _installedSkills = [];
    private IReadOnlyList<Haven.Core.InstalledExtensionPackage> _installedPlugins = [];
    private Task<ClosePreparation?>? _preparation;
    private Decision? _decision;
    private readonly List<Decision> _decisions = [];

    public SpaceBuilderPage(SpaceRegistry registry, Guid spaceId, ICuiSceneReadiness readiness)
        : this(registry, spaceId, readiness, null, null) { }

    public SpaceBuilderPage(SpaceRegistry registry, Guid spaceId, ICuiSceneReadiness readiness, Func<Task, bool>? queueBack)
        : this(registry, spaceId, readiness, queueBack, null) { }

    public SpaceBuilderPage(SpaceRegistry registry, Guid spaceId, ICuiSceneReadiness readiness, Func<Task, bool>? queueBack, ExtensionManager? extensions)
    {
        Dispatcher.UIThread.VerifyAccess();
        _originalRegistry = registry;
        _controller = new(registry, spaceId);
        _extensions = extensions;
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _queueBack = queueBack;
        var document = SpaceBuilderCuiDocument.Load();
        _host = new CuiSceneHost();
        _work = new(StopAsync, CleanupAsync);
        _bindings = new(() => _controller.State,
            (field, value) => TryEdit(field, value),
            (category, state) => TrySetCategory(category, state),
            (kind, id) => TryDetachCapability(kind, id),
            () => _installedSkills,
            () => _installedPlugins,
            DispatchAsync, CanMutate, () => _queueBack is not null,
            () => _boardsChoices, () => _boardsSource is not null && !_sourcePickerBusy,
            view => { _controller.SelectEditorView(view); }, section => { _controller.SelectExpertSection(section); },
            _controller.PreviewDraft, ValidateDraftWithOwnerAsync,
            (contextId, included) => { _controller.SetContextReferenceIncluded(contextId, included); },
            (capability, required) => { _controller.SetModelCapabilityRequired(capability, required); },
            allowed => { _controller.SetPersonalMemoryReadAllowed(allowed); },
            (kind, id, policy) => { _controller.SetCapabilityOverride(kind, id, policy); }, TryUpdateExamplePair);
        _controller.StateChanged += OnStateChanged;
        Document = document;
        _contentGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        _contentGrid.Children.Add(_host);
        Content = _contentGrid;
    }

    public CuiDocument Document { get; }
    public SpaceBuilderState State => _controller.State;
    public Task? OriginalClose => _work.OriginalClose;
    internal CuiSceneHost Host => _host;
    internal Task<CuiSceneAvailability>? Show => _show;
    /// <summary>The close-preparation decision scene this page actually presented, so a reviewer can
    /// prove the visible three-choice decision rather than trusting a returned boolean.</summary>
    internal CuiSceneHost? DecisionHost { get { lock (_gate) return _decision?.Host; } }

    private bool IsPresentationCurrent() => Volatile.Read(ref _live);
    private bool CanMutate()
    {
        lock (_gate) return _live && _activated && !_work.IsRetiring && _lease is null && !_sourcePickerBusy;
    }

    internal void BindOriginalBoardsContextSource(OriginalSpaceRequestPolicySource source)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_gate)
        {
            if (_activation is not null || _boardsSource is not null || !source.HasOriginalContextRegistry(_originalRegistry))
                throw new InvalidOperationException("Bind the SAME original Space/Notes source once, before page activation.");
            _boardsSource = source;
        }
    }

    internal void BindOriginalActivationValidator(ISpaceDraftActivationValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (_activationValidator is not null && !ReferenceEquals(_activationValidator, validator))
            throw new InvalidOperationException("The original Space activation validator cannot be replaced.");
        _activationValidator = validator;
        _controller.SetActivationValidatorAvailable(true);
    }

    private Task RunSourceChoiceAsync(ISpaceBuilderOriginalBoardsSourceChoice? choice, CancellationToken caller)
    {
        lock (_gate)
        {
            if (!CanMutate() || _controller.State.IsBusy || _boardsSource is not { } source)
                throw new InvalidOperationException("The original canonical source chooser is unavailable.");
            if (choice is not null && !_boardsChoices.Any(option => ReferenceEquals(option.Input, choice)))
                throw new InvalidOperationException("Choose an item from this page's current original source list.");
            var state = _controller.State;
            var revision = state.SpaceRevision ?? throw new InvalidOperationException("Load the actual Space revision first.");
            _controllerOperations++; _sourcePickerBusy = true;
            try { return _work.RunAsync(async original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token);
                try
                {
                    RefreshBindings();
                    void Own(Action body) => Source(() => { body(); return true; });
                    if (choice is null)
                    {
                        var options = await original.AwaitAsync(Source(() => source.GetOriginalBoardsContextChoicesAsync(
                            _originalRegistry, state.SpaceId, revision, Own, linked.Token))).ConfigureAwait(false);
                        original.DemandPublication();
                        if (_controller.State.SpaceRevision != revision)
                            throw new InvalidOperationException("The Space changed while the source chooser was open. Refresh it.");
                        _boardsChoices = options; _boardsChoicesRevision = revision;
                        _controller.ReportSourceStatus(options.Count == 0 ? "No canonical Boards notebooks were found in the reviewed library."
                            : "Choose a reviewed Boards notebook to add its reference. Source bodies still require Home approval when used.");
                    }
                    else
                    {
                        var reference = await original.AwaitAsync(Source(() => source.ResolveOriginalBoardsContextChoiceAsync(
                            choice, _originalRegistry, state.SpaceId, revision, Own, linked.Token))).ConfigureAwait(false);
                        original.DemandPublication();
                        if (!_controller.AttachContextReference(reference, revision))
                            throw new InvalidOperationException("The Space draft changed before the source reference could be added. Refresh it.");
                    }
                }
                catch (Exception error)
                {
                    if (original.IsPublicationCurrent) _controller.ReportSourceStatus("Could not select the original source; the draft is preserved: " + error.Message);
                    throw;
                }
                finally
                {
                    lock (_gate) { _controllerOperations--; _sourcePickerBusy = false; }
                    RefreshBindings();
                }
            }); }
            catch { _controllerOperations--; _sourcePickerBusy = false; throw; }
        }
    }

    public Task ActivateAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            _work.DemandAdmission();
            return _activation ??= _work.RunAsync(async original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, original.Token);
                var loaded = await original.AwaitAsync(Source(() => _controller.LoadAsync(linked.Token))).ConfigureAwait(false);
                var spaceForGraph = await original.AwaitAsync(Source(() => _originalRegistry.GetAsync(_controller.State.SpaceId, linked.Token))).ConfigureAwait(false);
                if (spaceForGraph is null || spaceForGraph.IsArchived)
                    throw new InvalidOperationException("The Space was unavailable while opening its graph editor.");
                if (!loaded) throw new InvalidOperationException("The saved Space draft could not be loaded for its graph editor.");
                spaceForGraph = ProjectCurrentDraft(spaceForGraph, _controller.State);
                if (loaded && _extensions is not null)
                {
                    List<string> catalogFailures = [];
                    try { _installedSkills = await original.AwaitAsync(Source(() => _extensions.ListSkillsAsync(linked.Token))).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                    catch (Exception error) { _installedSkills = []; catalogFailures.Add("Skills: " + error.Message); }
                    try { _installedPlugins = await original.AwaitAsync(Source(() => _extensions.ListPluginsAsync(linked.Token))).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                    catch (Exception error) { _installedPlugins = []; catalogFailures.Add("Plugins: " + error.Message); }
                    if (catalogFailures.Count > 0)
                        _controller.ReportSourceStatus("Installed capability catalog is incomplete; existing Space bindings were preserved. " +
                            $"Loaded {_installedSkills.Count} Skills and {_installedPlugins.Count} Plugins. " + string.Join("; ", catalogFailures));
                    else
                        _controller.ReportSourceStatus($"Loaded {_installedSkills.Count} installed Skills and {_installedPlugins.Count} installed Plugins. " +
                            "Space bindings remain canonical references and do not grant permissions.");
                }
                else if (loaded)
                    _controller.ReportSourceStatus("Installed Skills/Plugins are unavailable in this host; existing Space bindings were preserved.");
                Task<CuiSceneAvailability>? show = null;
                var dispatch = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    original.DemandPublication();
                    lock (_gate) _activated = true;
                    BuildGraphEditor(spaceForGraph);
                    _bindings.Refresh();
                    show = _show = Source(() => _host.ShowAsync(new("spaces.builder", "Space Builder", "Space Builder",
                        Document, _bindings, _bindings, _readiness) { IsPublicationCurrent = IsPresentationCurrent }, linked.Token));
                }).GetTask();
                try { await original.AwaitAsync(dispatch).ConfigureAwait(false); }
                catch (Exception error) { original.Capture(dispatch, error); }
                if (show is not null)
                    try { await original.AwaitAsync(show).ConfigureAwait(false); }
                    catch (Exception error) { original.Capture(show, error); }
                original.ThrowRetained();
            });
        }
    }

    public bool TryEdit(SpaceBuilderField field, string value)
    {
        lock (_gate)
        {
            if (!CanMutate()) return false;
            var accepted = false;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                original.DemandPublication();
                accepted = Source(() => _controller.SetField(field, value));
                if (accepted) _editEpoch++;
            });
            return accepted;
        }
    }

    public bool TryDetachCapability(SpaceCapabilityKind kind, string capabilityId)
    {
        lock (_gate)
        {
            if (!CanMutate()) return false;
            var accepted = false;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                original.DemandPublication();
                accepted = Source(() => _controller.DetachCapability(kind, capabilityId));
                if (accepted) _editEpoch++;
            });
            return accepted;
        }
    }

    public bool TrySetCategory(SpaceBuilderCategory category, SpaceCapabilityCategoryState state)
    {
        lock (_gate)
        {
            if (!CanMutate()) return false;
            var accepted = false;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                original.DemandPublication();
                accepted = Source(() => _controller.SetCategoryState(category, state));
                if (accepted) _editEpoch++;
            });
            return accepted;
        }
    }

    public bool TryAttachCapability(SpaceCapabilityBinding binding)
    {
        lock (_gate)
        {
            if (!CanMutate()) return false;
            var accepted = false;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                original.DemandPublication();
                accepted = Source(() => _controller.AttachCapability(binding));
                if (accepted) _editEpoch++;
            });
            return accepted;
        }
    }

    public Task<bool> SaveAsync(CancellationToken token = default) => RunControllerAsync(_controller.SaveAsync, token);
    public Task<bool> ActivateDraftAsync(CancellationToken token = default) => _activationValidator is { } validator
        ? RunControllerAsync(cancellation => _controller.ActivateAsync(cancellation), token, refreshGraph: true,
            originalOperation: (own, retain, cancellation) => _controller.ActivateAsync(validator, own, retain, cancellation))
        : RunControllerAsync(cancellation => _controller.ActivateAsync(cancellation), token, refreshGraph: true);
    public Task<bool> DiscardChangesAsync(CancellationToken token = default) => RunControllerAsync(_controller.DiscardChangesAsync, token, refreshGraph: true);
    public Task<bool> ReloadAsync(CancellationToken token = default) => RunControllerAsync(_controller.LoadAsync, token, refreshGraph: true);

    private async Task<IReadOnlyList<string>> ValidateDraftWithOwnerAsync(CancellationToken token)
    {
        if (_activationValidator is not { } validator)
            return await _controller.ValidateDraftAsync(token).ConfigureAwait(false);

        await RunControllerAsync(_ => Task.FromResult(true), token, originalOperation: async (own, retain, cancellation) =>
        {
            await _controller.ValidateDraftAsync(validator, own, retain, cancellation).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
        return _controller.State.ValidationIssues;
    }

    private Task<bool> RunControllerAsync(Func<CancellationToken, Task<bool>> operation, CancellationToken caller,
        bool refreshGraph = false,
        Func<Action<Action>, Action<Task>, CancellationToken, Task<bool>>? originalOperation = null)
    {
        lock (_gate)
        {
            if (!CanMutate()) return Task.FromResult(false);
            _controllerOperations++;
            try { return _work.RunAsync(async original =>
            {
                try
                {
                    original.BindPublicationGuard(IsPresentationCurrent);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token);
                    void Own(Action body) => Source(() => { body(); return true; });
                    void Retain(Task actual) => _ = original.AwaitAsync(actual);
                    var actual = Source(() => originalOperation is null
                        ? operation(linked.Token)
                        : originalOperation(Own, Retain, linked.Token));
                    var result = await original.AwaitAsync(actual).ConfigureAwait(false);
                    if (result && refreshGraph)
                    {
                        var savedSpace = await original.AwaitAsync(Source(() => _originalRegistry.GetAsync(_controller.State.SpaceId, linked.Token))).ConfigureAwait(false);
                        var state = _controller.State;
                        if (savedSpace is null)
                            throw new InvalidOperationException("The Space disappeared while refreshing its saved draft.");
                        var draftSpace = ProjectCurrentDraft(savedSpace, state);
                        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            original.DemandPublication();
                            _graphEditor?.ReloadDraft(draftSpace);
                        }).GetTask()).ConfigureAwait(false);
                    }
                    return result;
                }
                finally { lock (_gate) _controllerOperations--; }
            }); }
            catch { _controllerOperations--; throw; }
        }
    }

    private async ValueTask DispatchAsync(string command, object? parameter, CancellationToken token)
    {
        if (command == "space-builder.example.add" || command == "space-builder.example.remove")
        {
            lock (_gate)
            {
                if (!CanMutate()) throw new InvalidOperationException("This Space draft cannot be edited now.");
                _work.RunSynchronous(original =>
                {
                    original.BindPublicationGuard(IsPresentationCurrent);
                    original.DemandPublication();
                    var accepted = Source(() => command == "space-builder.example.add"
                        ? _controller.AddExamplePair()
                        : parameter is string key && int.TryParse(key, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var index) &&
                          _controller.State.Draft.ExamplePairs.ElementAtOrDefault(index) is { } expected &&
                          _controller.RemoveExamplePair(index, expected));
                    if (accepted) _editEpoch++;
                });
            }
            return;
        }
        if (command == "space-builder.source.browse") { await RunSourceChoiceAsync(null, token).ConfigureAwait(false); return; }
        if (command == "space-builder.source.attach" && parameter is ISpaceBuilderOriginalBoardsSourceChoice sourceChoice)
        { await RunSourceChoiceAsync(sourceChoice, token).ConfigureAwait(false); return; }
        if (command is "space-builder.source.remove" or "space-builder.legacy-file.remove")
        {
            lock (_gate)
            {
                if (!CanMutate()) throw new InvalidOperationException("This Space draft cannot be edited now.");
                _work.RunSynchronous(original =>
                {
                    original.BindPublicationGuard(IsPresentationCurrent); original.DemandPublication();
                    Source(() => command == "space-builder.source.remove" && parameter is string id && Guid.TryParseExact(id, "D", out var contextId)
                        ? _controller.DetachContextReference(contextId)
                        : command == "space-builder.legacy-file.remove" && parameter is string path && _controller.DetachLegacyFileReference(path));
                });
            }
            return;
        }
        if (command is "space-builder.source.move-up" or "space-builder.source.move-down")
        {
            if (parameter is not SpaceBuilderContextOrderRequest order)
                throw new InvalidOperationException("The source order command did not carry its current draft identity.");
            lock (_gate)
            {
                if (!CanMutate()) throw new InvalidOperationException("This Space draft cannot be edited now.");
                _work.RunSynchronous(original =>
                {
                    original.BindPublicationGuard(IsPresentationCurrent);
                    original.DemandPublication();
                    var offset = command == "space-builder.source.move-up" ? -1 : 1;
                    var moved = Source(() => _controller.MoveContextReference(order.SpaceId, order.DraftRevision,
                        order.ContextId, order.ExpectedIndex, offset));
                    if (moved) _editEpoch++;
                    else _controller.ReportSourceStatus("The source order changed before this action was applied. Review the current order and try again.");
                });
            }
            RefreshBindings();
            return;
        }
        if (command == "space-builder.capability.remove" && parameter is string compositeKey)
        {
            var parts = compositeKey.Split(':', 2);
            if (parts.Length == 2 && Enum.TryParse<SpaceCapabilityKind>(parts[0], out var kind))
                TryDetachCapability(kind, parts[1]);
            return;
        }
        if (command == "space-builder.skill.attach" && parameter is string skillId)
        {
            var listed = _installedSkills.FirstOrDefault(skill => skill.SkillId == skillId);
            if (listed is null)
            {
                _controller.ReportSourceStatus("This Skill is no longer in the page's canonical catalog; no new binding was added and saved bindings were preserved.");
                return;
            }
            var (draftSnapshot, draftEditEpoch) = CaptureDraftSnapshot();
            var skill = await RunExtensionReadAsync("Skill", (extensions, cancellation) => extensions.GetSkillAsync(skillId, cancellation), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (skill is null) { _controller.ReportSourceStatus("This Skill is no longer installed; no new binding was added and saved bindings were preserved."); return; }
            lock (_gate)
            {
                var current = _controller.State;
                if (!CanMutate() || _editEpoch != draftEditEpoch || current.DraftRevision != draftSnapshot.DraftRevision || current.Draft != draftSnapshot.Draft)
                { _controller.ReportSourceStatus("The Space draft changed during Skill revalidation; no new binding was added."); return; }
                if (!TryAttachCapability(new SpaceCapabilityBinding(skill.SkillId, SpaceCapabilityKind.Skill,
                    SpaceCapabilityOverride.Inherit, skill.PackageVersion))) return;
                if (!skill.IsAvailable)
                    _controller.ReportSourceStatus("The Skill is currently unavailable; its canonical binding is an unsaved draft reference and does not install or enable it. Save to persist.");
                else if (!StringComparer.Ordinal.Equals(skill.PackageVersion, listed.PackageVersion))
                    _controller.ReportSourceStatus($"Skill catalog changed; current canonical version {skill.PackageVersion} is attached to the unsaved draft. Save to persist.");
            }
            return;
        }
        if (command == "space-builder.plugin.attach" && parameter is string pluginId)
        {
            var listed = _installedPlugins.FirstOrDefault(plugin => plugin.Manifest.PackageId == pluginId);
            if (listed is null)
            {
                _controller.ReportSourceStatus("This Plugin is no longer in the page's canonical catalog; no new binding was added and saved bindings were preserved.");
                return;
            }
            var (draftSnapshot, draftEditEpoch) = CaptureDraftSnapshot();
            var plugin = await RunExtensionReadAsync("Plugin", (extensions, cancellation) => extensions.GetPackageAsync(pluginId, cancellation), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (plugin is null || plugin.Manifest.PackageType is not (ExtensionPackageType.Plugin or ExtensionPackageType.PluginAndSkills))
            { _controller.ReportSourceStatus("This Plugin is no longer installed as a Plugin; no new binding was added and saved bindings were preserved."); return; }
            lock (_gate)
            {
                var current = _controller.State;
                if (!CanMutate() || _editEpoch != draftEditEpoch || current.DraftRevision != draftSnapshot.DraftRevision || current.Draft != draftSnapshot.Draft)
                { _controller.ReportSourceStatus("The Space draft changed during Plugin revalidation; no new binding was added."); return; }
                // Space attachment references the canonical package. Sidebar-only Plugins do
                // not need a tool capability; resolution/permissions remain with their owners.
                if (!TryAttachCapability(new SpaceCapabilityBinding(plugin.Manifest.PackageId, SpaceCapabilityKind.Plugin,
                    SpaceCapabilityOverride.Inherit, plugin.Manifest.Version))) return;
                if (!plugin.IsEnabled)
                    _controller.ReportSourceStatus($"Plugin is disabled (state {plugin.State}); the canonical draft binding does not enable it or grant permission. Save to persist.");
                else if (plugin.State != ExtensionInstallState.Enabled)
                    _controller.ReportSourceStatus($"Plugin reports state {plugin.State}; the canonical draft binding does not change package state or grant permission. Save to persist.");
                else if (!StringComparer.Ordinal.Equals(plugin.Manifest.Version, listed.Manifest.Version))
                    _controller.ReportSourceStatus($"Plugin catalog changed; current canonical version {plugin.Manifest.Version} is attached to the unsaved draft. Save to persist.");
            }
            return;
        }
        if (command == "space-builder.back")
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task back;
            lock (_gate)
            {
                if (!CanMutate() || _queueBack is null) throw new InvalidOperationException("This builder cannot return to Spaces.");
                back = _work.RunAsync(async original =>
                {
                    await start.Task.ConfigureAwait(false);
                    original.BindPublicationGuard(IsPresentationCurrent);
                    Task? pipeline = null;
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        original.DemandPublication();
                        pipeline = _host.CaptureOriginalActionSettlement(_show!);
                    }).GetTask()).ConfigureAwait(false);
                    var settlement = Task.WhenAll(pipeline!, original.Task);
                    if (!_queueBack(settlement)) throw new InvalidOperationException("The session refused the return to Spaces.");
                });
            }
            start.SetResult();
            await back.ConfigureAwait(false);
            return;
        }
        var actual = command switch
        {
            "space-builder.save" => SaveAsync(token),
            "space-builder.activate" => ActivateDraftAsync(token),
            "space-builder.discard" => DiscardChangesAsync(token),
            "space-builder.reload" => ReloadAsync(token),
            _ => throw new InvalidOperationException("This Space Builder command is not owned by this page.")
        };
        await actual.ConfigureAwait(false);
    }

    private bool TryUpdateExamplePair(int index, SpaceExamplePair expected, SpaceExamplePair updated)
    {
        lock (_gate)
        {
            if (!CanMutate()) return false;
            var accepted = false;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                original.DemandPublication();
                accepted = Source(() => _controller.UpdateExamplePair(index, expected, updated));
                if (accepted) _editEpoch++;
            });
            return accepted;
        }
    }

    private Task<T> RunExtensionReadAsync<T>(string resourceKind,
        Func<ExtensionManager, CancellationToken, Task<T>> read, CancellationToken caller)
    {
        lock (_gate)
        {
            if (!CanMutate() || _extensions is not { } extensions)
                throw new InvalidOperationException($"The live {resourceKind} catalog is unavailable.");
            _controllerOperations++;
            try
            {
                return _work.RunAsync(async original =>
                {
                    try
                    {
                        original.BindPublicationGuard(IsPresentationCurrent);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token);
                        try
                        {
                            var result = await original.AwaitAsync(Source(() => read(extensions, linked.Token))).ConfigureAwait(false);
                            original.DemandPublication();
                            return result;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error)
                        {
                            if (original.IsPublicationCurrent)
                                _controller.ReportSourceStatus($"Could not revalidate the canonical {resourceKind}; no binding was changed: {error.Message}");
                            throw;
                        }
                    }
                    finally { lock (_gate) _controllerOperations--; }
                });
            }
            catch { _controllerOperations--; throw; }
        }
    }

    private (SpaceBuilderState State, long EditEpoch) CaptureDraftSnapshot()
    {
        lock (_gate) return (_controller.State, _editEpoch);
    }

    private void OnStateChanged(SpaceBuilderState state)
    {
        if (_boardsChoicesRevision is { } revision && state.SpaceRevision != revision)
        { _boardsChoices = []; _boardsChoicesRevision = null; }
        RefreshBindings();
        if (Dispatcher.UIThread.CheckAccess()) UpdateGraphVisibility(state);
        else Dispatcher.UIThread.Post(() => UpdateGraphVisibility(state));
    }

    private void BuildGraphEditor(SpaceDefinition space)
    {
        _graphEditor?.Dispose();
        if (_graphEditor is not null) _contentGrid.Children.Remove(_graphEditor);
        _graphEditor = new SpaceLayoutEditorPage(space, OnGraphDraftChanged)
        {
            Height = 460,
            IsVisible = false
        };
        Grid.SetRow(_graphEditor, 1);
        _contentGrid.Children.Add(_graphEditor);
        UpdateGraphVisibility(_controller.State);
    }

    internal SpaceLayoutDocument? CurrentGraphDraft => _graphEditor is null
        ? null : SpaceLayoutEditorAdapter.FromEditor(_graphEditor.Editor.Document, _controller.State.Draft.LayoutDocument);

    private static SpaceDefinition ProjectCurrentDraft(SpaceDefinition saved, SpaceBuilderState state)
    {
        if (saved.Id != state.SpaceId || saved.Revision != state.SpaceRevision ||
            saved.DraftConfigurationRevision != state.DraftRevision ||
            saved.ActiveConfigurationRevision != state.ActiveRevision)
            throw new SpaceRevisionConflictException(state.SpaceId, state.SpaceRevision ?? 0, saved.Revision);
        return saved with { LayoutDocument = state.Draft.LayoutDocument };
    }

    private void OnGraphDraftChanged(SpaceLayoutDocument document)
    {
        lock (_gate)
        {
            if (!CanMutate()) return;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                original.DemandPublication();
                if (Source(() => _controller.SetLayoutDocument(document))) _editEpoch++;
            });
        }
    }

    private void UpdateGraphVisibility(SpaceBuilderState state)
    {
        if (_graphEditor is null) return;
        _graphEditor.IsVisible = state.EditorView == SpaceBuilderEditorView.Expert &&
            state.ExpertSection is SpaceBuilderExpertSection.HarnessLogic or
                SpaceBuilderExpertSection.ModelLogic or SpaceBuilderExpertSection.UserMessageLogic;
    }

    private void RefreshBindings() => _ = RefreshBindingsAsync();

    private Task RefreshBindingsAsync()
    {
        lock (_gate)
        {
            if (!_live || _work.IsRetiring) return Task.CompletedTask;
            return _work.RunAsync(async original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                    original.RunAcceptedPublicationCallback(() => _bindings.Refresh())).GetTask()).ConfigureAwait(false);
            });
        }
    }

    private T Source<T>(Func<T> acquire) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, acquire);

    public void DemandExternalOriginalRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        _work.DemandExternalClose();
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate) return _work.CloseAndDrainAsync();
    }
    public void RequestRetirement() { lock (_gate) _work.RequestRetirement(); }
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private Task StopAsync()
    {
        lock (_gate)
        {
            Volatile.Write(ref _live, false);
            _controller.StateChanged -= OnStateChanged;
            return _hostClose ??= Source(_host.CloseOriginalAsync);
        }
    }

    private async Task CleanupAsync()
    {
        List<Exception> failures = [];
        if (_hostClose is { } hostClose)
            try { await hostClose.ConfigureAwait(false); } catch (Exception error) { failures.Add(error); }
        await Source(() => Dispatcher.UIThread.InvokeAsync(() =>
        {
            _graphEditor?.Dispose();
            _graphEditor = null;
            Content = null;
        }).GetTask()).ConfigureAwait(false);
        if (failures.Count > 0) throw new AggregateException("The Space Builder surface did not close cleanly.", failures);
    }
}
