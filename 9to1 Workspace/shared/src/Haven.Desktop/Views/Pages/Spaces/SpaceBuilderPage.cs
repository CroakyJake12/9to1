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
/// SpaceBuilderCuiBindings. Incremental: Advanced/Expert are not yet implemented.</summary>
public sealed partial class SpaceBuilderPage : UserControl, IDesktopOriginalRetirementParticipant,
    IDesktopOriginalRetirementJoinGuard, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SpaceBuilderController _controller;
    private readonly ExtensionManager? _extensions;
    private readonly SpaceBuilderCuiBindings _bindings;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiSceneHost _host;
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
            DispatchAsync, CanMutate, () => _queueBack is not null);
        _controller.StateChanged += OnStateChanged;
        Document = document;
        Content = _host;
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
        lock (_gate) return _live && _activated && !_work.IsRetiring && _lease is null;
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
                await original.AwaitAsync(Source(() => _controller.LoadAsync(linked.Token))).ConfigureAwait(false);
                if (_extensions is not null)
                {
                    try
                    {
                        _installedSkills = await original.AwaitAsync(Source(() => _extensions.ListSkillsAsync(linked.Token))).ConfigureAwait(false);
                        _installedPlugins = await original.AwaitAsync(Source(() => _extensions.ListPluginsAsync(linked.Token))).ConfigureAwait(false);
                    }
                    catch { _installedSkills = []; _installedPlugins = []; }
                }
                Task<CuiSceneAvailability>? show = null;
                var dispatch = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    original.DemandPublication();
                    lock (_gate) _activated = true;
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
    public Task<bool> DiscardChangesAsync(CancellationToken token = default) => RunControllerAsync(_controller.DiscardChangesAsync, token);
    public Task<bool> ReloadAsync(CancellationToken token = default) => RunControllerAsync(_controller.LoadAsync, token);

    private Task<bool> RunControllerAsync(Func<CancellationToken, Task<bool>> operation, CancellationToken caller)
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
                    return await original.AwaitAsync(Source(() => operation(linked.Token))).ConfigureAwait(false);
                }
                finally { lock (_gate) _controllerOperations--; }
            }); }
            catch { _controllerOperations--; throw; }
        }
    }

    private async ValueTask DispatchAsync(string command, object? parameter, CancellationToken token)
    {
        if (command == "space-builder.capability.remove" && parameter is string compositeKey)
        {
            var parts = compositeKey.Split(':', 2);
            if (parts.Length == 2 && Enum.TryParse<SpaceCapabilityKind>(parts[0], out var kind))
                TryDetachCapability(kind, parts[1]);
            return;
        }
        if (command == "space-builder.skill.attach" && parameter is string skillId)
        {
            var skill = _installedSkills.FirstOrDefault(s => s.SkillId == skillId);
            if (skill is not null)
            {
                var binding = new SpaceCapabilityBinding(skill.SkillId, SpaceCapabilityKind.Skill, SpaceCapabilityOverride.Inherit, skill.PackageVersion);
                TryAttachCapability(binding);
            }
            return;
        }
        if (command == "space-builder.plugin.attach" && parameter is string pluginId)
        {
            var plugin = _installedPlugins.FirstOrDefault(p => p.Manifest.PackageId == pluginId);
            if (plugin is not null)
            {
                // Space attachment references the canonical package. Sidebar-only Plugins do
                // not need a tool capability; resolution/permissions remain with their owners.
                var binding = new SpaceCapabilityBinding(plugin.Manifest.PackageId, SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Inherit, plugin.Manifest.Version);
                TryAttachCapability(binding);
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
            "space-builder.discard" => DiscardChangesAsync(token),
            "space-builder.reload" => ReloadAsync(token),
            _ => throw new InvalidOperationException("This Space Builder command is not owned by this page.")
        };
        await actual.ConfigureAwait(false);
    }

    private void OnStateChanged(SpaceBuilderState state) => RefreshBindings();

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
        if (failures.Count > 0) throw new AggregateException("The Space Builder surface did not close cleanly.", failures);
        await Source(() => Dispatcher.UIThread.InvokeAsync(() => Content = null).GetTask()).ConfigureAwait(false);
    }
}
