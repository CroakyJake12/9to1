#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Migration;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Apps.Assistants.MiniComputer;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Assistants;

/// <summary>Native page over the actual scoped Assistant controller and SAME App/Home/Den owners.
/// The owning root creates the controller only after opening and verifying its actual personal Den.</summary>
internal sealed partial class NativeAssistantsDesktopPage : UserControl,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly OriginalAssistantPersonalDenHost _denHost;
    private readonly HomePersonalDenSession _den;
    private readonly AssistantsWorkspaceController _controller;
    private readonly CancellationToken _appLifetime;
    private readonly CancellationToken _originalWindowLifetime;
    private readonly Window _originalWindow;
    private readonly CancellationTokenSource _window;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly TaskCompletionSource _construction = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly OriginalReadiness _readiness;
    private readonly IAssistantsNativeOriginalDevelopmentRoute? _developmentRoute;
    private readonly ILegacyAgentMigrationController? _migration;
    private readonly string? _migrationUnavailableReason;
    private readonly IAssistantMemoryManagementController? _memoryManagement;
    private readonly string? _memoryUnavailableReason;
    private readonly IAssistantMiniComputerController? _miniComputerManagement;
    private readonly string? _miniComputerUnavailableReason;
    private readonly IAssistantGeneratedUiHost? _generatedUiHost;
    private AssistantsNativeCuiSurface? _surface;
    private CuiSceneAvailability? _lastAvailability;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private CloudflareOriginalTaskLedger? _originalCloseSources;
    private Exception? _constructionFailure;
    private int _originalWindowClosed;
    private bool _windowSubscribed;

    private NativeAssistantsDesktopPage(IServiceProvider provider, HomeNativeWindowsComposition home,
        OriginalAssistantPersonalDenHost denHost, HomePersonalDenSession den,
        AssistantsWorkspaceController controller, CancellationToken appLifetime,
        CancellationToken windowLifetime, Window actualWindow, Action<NativeAssistantsDesktopPage> captureOriginalOwner,
        IAssistantsNativeOriginalDevelopmentRoute? developmentRoute,
        ILegacyAgentMigrationController? migration, string? migrationUnavailableReason,
        IAssistantMemoryManagementController? memoryManagement, string? memoryUnavailableReason,
        IAssistantMiniComputerController? miniComputerManagement, string? miniComputerUnavailableReason,
        IAssistantGeneratedUiHost? generatedUiHost)
    {
        _provider = provider; _home = home; _denHost = denHost; _den = den; _controller = controller;
        _appLifetime = appLifetime;
        _originalWindowLifetime = windowLifetime; _originalWindow = actualWindow;
        _window = CancellationTokenSource.CreateLinkedTokenSource(appLifetime, windowLifetime);
        _work = new(StopOriginalAsync, CloseOriginalViewAsync);
        _readiness = new(this); _developmentRoute = developmentRoute;
        _migration = migration; _migrationUnavailableReason = migrationUnavailableReason;
        _memoryManagement = memoryManagement; _memoryUnavailableReason = memoryUnavailableReason;
        _miniComputerManagement = miniComputerManagement; _miniComputerUnavailableReason = miniComputerUnavailableReason;
        _generatedUiHost = generatedUiHost;
        try
        {
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                captureOriginalOwner(this); // Retain this actual partial page before any native publication.
                CaptureOriginalAttachmentPicker();
                if (!_originalWindow.IsVisible) Volatile.Write(ref _originalWindowClosed, 1);
                DemandAlive(); return true;
            });
            _originalWindow.Closed += OnOriginalWindowClosed; _windowSubscribed = true;
        }
        catch (Exception failure) { _constructionFailure = failure; throw; }
        finally { _construction.TrySetResult(); }
    }

    internal static NativeAssistantsDesktopPage BindOriginal(IServiceProvider provider,
        HomeNativeWindowsComposition home, OriginalAssistantPersonalDenHost actualDenHost,
        HomePersonalDenSession actualOpenedDen, AssistantsWorkspaceController actualScopedController,
        CancellationToken appLifetime, CancellationToken windowLifetime,
        Window actualWindow, Action<NativeAssistantsDesktopPage> captureOriginalOwner,
        IAssistantsNativeOriginalDevelopmentRoute? developmentRoute = null,
        ILegacyAgentMigrationController? migration = null, string? migrationUnavailableReason = null,
        IAssistantMemoryManagementController? memoryManagement = null, string? memoryUnavailableReason = null,
        IAssistantMiniComputerController? miniComputerManagement = null, string? miniComputerUnavailableReason = null,
        IAssistantGeneratedUiHost? generatedUiHost = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(actualDenHost); ArgumentNullException.ThrowIfNull(actualOpenedDen);
        ArgumentNullException.ThrowIfNull(actualScopedController); ArgumentNullException.ThrowIfNull(captureOriginalOwner);
        ArgumentNullException.ThrowIfNull(actualWindow);
        if (!actualWindow.IsVisible) throw new UnauthorizedAccessException("The actual native Assistants window is not open.");
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
        if (!appLifetime.CanBeCanceled || !windowLifetime.CanBeCanceled)
            throw new ArgumentException("Supply the actual App and native window original lifetimes.");
        appLifetime.ThrowIfCancellationRequested(); windowLifetime.ThrowIfCancellationRequested();
        if (!ReferenceEquals(provider.GetRequiredService<HomeLocalProfileIdentity>(), home.Profiles) ||
            !ReferenceEquals(provider.GetRequiredService<ResourceAuthorizationService>(), home.Resources) ||
            !ReferenceEquals(provider.GetRequiredService<HomeResourceOperationBroker>(), home.Broker) ||
            !ReferenceEquals(provider.GetRequiredService<OriginalAssistantPersonalDenHost>(), actualDenHost) ||
            !actualDenHost.HasAcquiredProvider || actualDenHost.OriginalClose is not null)
            throw new UnauthorizedAccessException("Retain the SAME opened personal Den and Windows Home composition.");
        return new(provider, home, actualDenHost, actualOpenedDen, actualScopedController,
            appLifetime, windowLifetime, actualWindow, captureOriginalOwner, developmentRoute, migration, migrationUnavailableReason,
            memoryManagement, memoryUnavailableReason, miniComputerManagement, miniComputerUnavailableReason, generatedUiHost);
    }

    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal AssistantsNativeCuiSurface? OriginalSurface => _surface;
    internal AssistantsWorkspaceController OriginalController => _controller;
    internal IAssistantsNativeOriginalDevelopmentRoute? OriginalDevelopmentRoute => _developmentRoute;
    internal ILegacyAgentMigrationController? OriginalMigration => _migration;
    internal IAssistantMemoryManagementController? OriginalMemoryManagementController => _memoryManagement;
    internal IAssistantMiniComputerController? OriginalMiniComputerManagementController => _miniComputerManagement;
    internal IAssistantGeneratedUiHost? OriginalGeneratedUiHost => _generatedUiHost;
    internal bool IsOriginalComposition(IServiceProvider provider, HomeNativeWindowsComposition home) =>
        ReferenceEquals(provider, _provider) && ReferenceEquals(home, _home);
    internal bool IsOriginalWindow(Window actual, CancellationToken appLifetime, CancellationToken windowLifetime) =>
        ReferenceEquals(actual, _originalWindow) && appLifetime == _appLifetime && windowLifetime == _originalWindowLifetime;
    internal bool IsOriginalClosePrepared => !_work.IsRetiring && !_window.IsCancellationRequested &&
        _initialization?.IsCompletedSuccessfully == true && _preparation is { IsCompletedSuccessfully: true } prepared &&
        prepared.Result && _surface?.IsOriginalClosePrepared == true;

    internal Task InitializeAsync(CancellationToken caller)
    {
        if (_initialization is not null) return _initialization;
        return _work.RunAsync(async original =>
        {
            Dispatcher.UIThread.VerifyAccess();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token, _window.Token);
            var token = linked.Token;
            await RequireReadyAsync(original, token);
            AssistantsNativeCuiSurface? surface = null;
            Acquire(original, () =>
            {
                surface = new(_controller, _readiness, _window.Token,
                    actual => _surface = actual, migration: _migration, migrationUnavailableReason: _migrationUnavailableReason,
                    developmentRoute: _developmentRoute, memoryManagement: _memoryManagement,
                    memoryUnavailableReason: _memoryUnavailableReason, miniComputerManagement: _miniComputerManagement,
                    miniComputerUnavailableReason: _miniComputerUnavailableReason); // SAME partial and borrowers precede native callbacks.
                return true;
            });
            var actualSurface = surface ?? throw new InvalidOperationException("No actual Assistant surface was acquired.");
            if (_generatedUiHost is { } generated) Acquire(original, () =>
            { actualSurface.BindOriginalGeneratedUiHost(generated); return true; });
            if (_attachmentPicker is { } picker) Acquire(original, () =>
            { actualSurface.BindOriginalAttachmentPicker(picker); return true; });
            await original.AwaitAsync(Acquire(original, () => actualSurface.InitializeAsync(token)));
            await RequireReadyAsync(original, token);
            DemandPublication(actualSurface); original.DemandPublication();
            Acquire(original, () => { Content = actualSurface; DemandPublication(actualSurface); return true; });
        }, actual => _initialization = actual);
    }

    internal Task<bool> PrepareToCloseAsync(CancellationToken caller = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_preparation is { IsCompleted: false } pending) return pending;
        var start = new TaskCompletionSource();
        Task<bool> actual;
        using (CloudflareOriginalExecutionGuard.EnterOriginal(this))
        {
            actual = _work.RunAsync(async original =>
            {
                await start.Task;
                DemandAlive();
                var surface = _surface ?? throw new InvalidOperationException("The actual Assistant surface is unavailable.");
                if (!await original.AwaitAsync(Acquire(original, () => surface.PrepareToCloseAsync(caller)))) return false;
                await RequireReadyAsync(original, caller);
                DemandPublication(surface); original.DemandPublication();
                return surface.IsOriginalClosePrepared;
            });
            _preparation = actual; // SAME actual Task before readiness/save callbacks can run.
        }
        start.TrySetResult();
        return actual;
    }

    internal void PublishOriginalTab(Action callback)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(callback);
        if (_initialization?.IsCompletedSuccessfully != true)
            throw new InvalidOperationException("Await the SAME actual native Assistant initialization before tab publication.");
        _work.RunSynchronous(original => Acquire(original, () =>
        {
            var surface = _surface ?? throw new InvalidOperationException("The actual native Assistant surface is unavailable.");
            DemandPublication(surface); original.DemandPublication(); callback();
            DemandPublication(surface); original.DemandPublication(); return true;
        }));
    }

    // Pure current native presentation proof. This grants no project/resource/Dev operation.
    internal void DemandOriginalDevelopmentPresentation(AssistantsNativeCuiSurface sameSurface,
        AssistantConversationBinding sameBinding, long originalGeneration)
    {
        Dispatcher.UIThread.VerifyAccess();
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            DemandPublication(sameSurface);
            if (!ReferenceEquals(sameSurface.CurrentConversationBinding, sameBinding) ||
                !sameSurface.IsPresentationCurrent(sameBinding, originalGeneration))
                throw new UnauthorizedAccessException("The actual Assistant conversation presentation changed before Dev acquisition.");
            return true;
        });
    }

    private T Acquire<T>(DesktopOriginalWorkLifetime.Original original, Func<T> source)
    {
        try { return CloudflareOriginalExecutionGuard.InvokeOriginal(this, source); }
        catch (Exception failure)
        {
            original.Retain(failure);
            if (failure is OperationCanceledException)
                throw new AggregateException("The actual Assistant page callback returned no canceled original Task.", failure);
            throw;
        }
    }

    private void DemandAlive()
    {
        _window.Token.ThrowIfCancellationRequested(); _work.DemandAdmission();
        if (Volatile.Read(ref _originalWindowClosed) != 0)
            throw new ObjectDisposedException("The actual native Assistant window is closed.");
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_provider, _home);
        if (_home.OriginalCloseTask is not null || _home.OriginalProcessRetirementRequestTask is not null ||
            _denHost.OriginalClose is not null || !ReferenceEquals(_controller, _surface?.OriginalController ?? _controller))
            throw new ObjectDisposedException("The actual Assistant Home/Den/presentation owner is retiring.");
    }

    private Task<AuthenticatedResourceActor?> ReadActualActorAsync(CancellationToken token)
    {
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        sources.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(_home,
            () => { DemandAlive(); token.ThrowIfCancellationRequested(); body(); return true; }));
        return sources.RunToOriginalSettlementAsync(async () =>
        {
            void Scope(Action source) => sources.Invoke(() =>
            { DemandAlive(); token.ThrowIfCancellationRequested(); source(); return true; });
            void Retain(Task actual) { _ = sources.Track(actual); }
            var actor = await sources.AwaitAsync(sources.Invoke(() => _home.Profiles.GetCurrentAsync(Scope, Retain, token).AsTask()));
            DemandAlive(); return actor;
        });
    }

    private Task<CuiSceneAvailability> CheckReadyAsync(CancellationToken caller) => _work.RunAsync(async original =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token, _window.Token);
        var token = linked.Token;
        DemandAlive();
        if (await original.AwaitAsync(Acquire(original, () => ReadActualActorAsync(token))) != _den.Actor)
            throw new UnauthorizedAccessException("The actual Home actor differs from the opened Den session.");
        void Scope(Action source) => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
            () => { DemandAlive(); token.ThrowIfCancellationRequested(); source(); return true; });
        var observed = await original.AwaitAsync(Acquire(original, () => WindowsHomeSameProcessRuntimeObservation.CheckAsync(
            _provider, _home, _appLifetime, _window.Token, Scope, DemandAlive, token)));
        if (await original.AwaitAsync(Acquire(original, () => ReadActualActorAsync(token))) != _den.Actor)
            throw new UnauthorizedAccessException("The original Home actor changed during the native observation.");
        DemandAlive(); original.DemandPublication();
        return Acquire(original, () => WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, observed));
    });

    private async Task RequireReadyAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var observed = await original.AwaitAsync(Acquire(original, () => CheckReadyAsync(token)));
        if (observed.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(observed.Message);
        _lastAvailability = observed; DemandAlive(); original.DemandPublication();
    }

    private void DemandPublication(AssistantsNativeCuiSurface surface)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandAlive();
        if (!_originalWindow.IsVisible)
            throw new ObjectDisposedException("The actual native Assistant window is closed.");
        if (!ReferenceEquals(_surface, surface) || !ReferenceEquals(surface.OriginalController, _controller) ||
            !ReferenceEquals(surface.OriginalMemoryManagementController, _memoryManagement) || _lastAvailability is null ||
            surface.OriginalInitialization?.IsCompletedSuccessfully != true ||
            (surface.CurrentConversationBinding is { } actual && !surface.IsPresentationCurrent(actual, surface.PresentationGeneration)))
            throw new UnauthorizedAccessException("The native Assistant surface or exact conversation binding changed.");
        var observed = WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, _lastAvailability);
        if (observed.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(observed.Message);
        DemandAlive();
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose();
        _surface?.DemandExternalOriginalRetirementJoin(); _miniComputerManagement?.DemandExternalOriginalRetirementJoin();
        _memoryManagement?.DemandExternalOriginalRetirementJoin();
        _migration?.DemandExternalOriginalRetirementJoin();
        _generatedUiHost?.DemandExternalOriginalRetirementJoin();
        _attachmentPicker?.DemandExternalOriginalJoin();
        _controller.DemandExternalOriginalRetirementJoin();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalAsync()
    {
        _window.Cancel();
        _attachmentPicker?.RequestOriginalRetirement();
        if (_surface is { } surface) surface.RequestRetirement();
        else
        {
            _generatedUiHost?.RequestRetirement();
            _miniComputerManagement?.RequestRetirement();
            _memoryManagement?.RequestRetirement();
            _migration?.RequestRetirement();
        }
        return Task.CompletedTask;
    }
    private void OnOriginalWindowClosed(object? sender, EventArgs args)
    {
        Volatile.Write(ref _originalWindowClosed, 1);
        RequestRetirement();
    }
    private Task CloseOriginalViewAsync()
    {
        var sources = _originalCloseSources = new CloudflareOriginalTaskLedger();
        sources.BindOriginalOwner(this); // Field custody precedes any actual cleanup callback.
        return sources.RunToOriginalSettlementAsync(async () =>
        {
            await sources.AwaitAsync(_construction.Task);
            if (_attachmentPicker is { } picker)
                await sources.AwaitAsync(sources.Invoke(picker.CloseAndDrainOriginalAsync));
            if (_surface is { } surface)
                await sources.AwaitAsync(sources.Invoke(surface.CloseAndDrainAsync));
            else
            {
                // Join both actual presentation borrowers independently. Any failure
                // retains the SAME Core/bridge and partial native page.
                var borrowerFailed = false;
                async Task JoinBorrower(Func<Task> close)
                {
                    Task? actual = null;
                    try { actual = sources.Invoke(close); }
                    catch (Exception cause) { sources.Retain(cause); borrowerFailed = true; }
                    if (actual is not null)
                        try { await sources.AwaitAsync(actual); }
                        catch (Exception cause) { sources.Capture(actual, cause); borrowerFailed = true; }
                }
                if (_generatedUiHost is { } generated) await JoinBorrower(generated.CloseAndDrainAsync);
                if (_miniComputerManagement is { } mini) await JoinBorrower(mini.CloseAndDrainAsync);
                if (_memoryManagement is { } memory) await JoinBorrower(memory.CloseAndDrainAsync);
                if (_migration is { } migration) await JoinBorrower(migration.CloseAndDrainAsync);
                if (borrowerFailed)
                    throw new AggregateException("The actual Assistant presentation borrowers have not settled.", sources.OriginalErrors);
                await sources.AwaitAsync(sources.Invoke(_controller.CloseAndDrainAsync));
            }
            if (_constructionFailure is { } originalFailure)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailure).Throw();
            var detach = sources.Invoke(() => Dispatcher.UIThread.InvokeAsync(() =>
            {
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                {
                    Content = null;
                    if (_windowSubscribed) { _originalWindow.Closed -= OnOriginalWindowClosed; _windowSubscribed = false; }
                    _window.Dispose(); return true;
                });
            }).GetTask());
            await sources.AwaitAsync(detach);
            // App/provider/ordinary Chat host/Den/Home/Tasks are borrowed here.
        });
    }
    private sealed class OriginalReadiness(NativeAssistantsDesktopPage page) : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => new(page.CheckReadyAsync(token)); }
}
#endif
