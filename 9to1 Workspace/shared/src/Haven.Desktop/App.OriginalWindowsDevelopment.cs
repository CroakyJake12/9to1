#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private NativePersonalTaskColdRecoveryConfiguration? _windowsColdConfiguration;
    private HomeDeveloperProjectReadResourceResolver? _windowsDeveloperReadResolver;
    private FilesDeveloperOriginalSetupDestinationResolver? _windowsDeveloperDestinationResolver;
    private HomeDeveloperProjectReadActionPolicySource? _windowsDeveloperReadPolicy;
    private HomeDeveloperProjectSetupActionPolicySource? _windowsDeveloperSetupPolicy;
    private HomeColdProjectReadResourceResolver? _windowsColdProjectResolver;
    private HomeColdProjectReadActionPolicySource? _windowsColdProjectPolicy;
    private FilesDeveloperOriginalCurrentProjectExecutionResolver? _windowsExecutionResolver;
    private HomeDeveloperWorkspaceExecutionActionPolicySource? _windowsExecutionPolicy;
    private HomeDeveloperProjectReadAdmissionSource? _windowsDeveloperReads;
    private HomeDeveloperProjectSetupJournal? _windowsDeveloperJournal;
    private HomeDeveloperProjectSetupPermissionSource? _windowsDeveloperPermissions;
    private FilesDeveloperOriginalSourceSelection? _windowsDeveloperSelections;
    private IDeveloperProjectOriginalPhysicalCaptureSource? _windowsDeveloperPhysical;
    private FilesDeveloperOriginalSetupScopeSource? _windowsDeveloperScopes;
    private FilesDeveloperOriginalFolderSetupProducer? _windowsDeveloperSteps;
    private HomeColdProjectReadReconciliation? _windowsColdProjects;
    private FilesDeveloperOriginalCurrentProjectSelection? _windowsCurrentProjects;
    private IDeveloperOriginalCurrentProjectNativeSource? _windowsCurrentProjectNative;
    private FilesDeveloperOriginalCurrentProjectExecutionBridge? _windowsCurrentExecution;
    private HomeDeveloperWorkspaceExecutionConsentSource? _windowsExecutionConsent;
    private bool _windowsDeveloperBorrowersTransferred;
    private Task? _actualWindowsDeveloperDrain;

    private bool WindowsColdProjectRequested => _windowsColdConfiguration?.OriginalStatus.Kind ==
        NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified;

    // These exact lazy resolvers/policies precede the genuine Home constructor. Captured
    // owners are filled before startup; evaluating an unbound resolver explicitly refuses.
    private void PrepareOriginalWindowsDeveloperPolicies()
    {
        _windowsColdConfiguration = NativePersonalTaskColdRecoveryConfiguration.ReadExplicitEnvironment();
        _windowsDeveloperReadResolver = new(() => _windowsDeveloperReads
            ?? throw new InvalidOperationException("The SAME Windows Home READ owner is not captured."));
        _windowsDeveloperDestinationResolver = new(() => _windowsDeveloperScopes
            ?? throw new InvalidOperationException("The SAME Windows Files destination owner is not captured."));
        _windowsDeveloperReadPolicy = new(); _windowsDeveloperSetupPolicy = new();
        if (!WindowsColdProjectRequested) return;
        _windowsColdProjectResolver = new(() => _windowsColdProjects
            ?? throw new InvalidOperationException("The SAME Windows cold project source is not captured."));
        _windowsColdProjectPolicy = new();
        _windowsExecutionResolver = new(() => _windowsCurrentExecution
            ?? throw new InvalidOperationException("The SAME Windows project execution bridge is not captured."));
        _windowsExecutionPolicy = new();
    }
    private ICanonicalResourceAccessResolver[] OriginalWindowsDeveloperResolvers() => WindowsColdProjectRequested
        ? [_windowsDeveloperReadResolver!, _windowsDeveloperDestinationResolver!, _windowsColdProjectResolver!, _windowsExecutionResolver!]
        : [_windowsDeveloperReadResolver!, _windowsDeveloperDestinationResolver!];
    private IHomeActionPolicySource[] OriginalWindowsDeveloperPolicies() => WindowsColdProjectRequested
        ? [_windowsDeveloperReadPolicy!, _windowsDeveloperSetupPolicy!, _windowsColdProjectPolicy!, _windowsExecutionPolicy!]
        : [_windowsDeveloperReadPolicy!, _windowsDeveloperSetupPolicy!];

    private void ConfigureOriginalWindowsDeveloperRegistrations(IServiceCollection services)
    {
        var home = _actualWindowsHome;
        if (home is null) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            services.AddHavenOwnedNativeTaskColdRecovery(_windowsColdConfiguration
                ?? throw new InvalidOperationException("The explicit Windows recovery configuration was not captured."));
            services.AddHavenOwnedDeveloperSourceReads(home,
                provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(),
                _windowsDeveloperReadResolver!, _windowsDeveloperReadPolicy!);
            services.AddHavenOwnedDeveloperSetups(home,
                provider => provider.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>(),
                provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(),
                provider => provider.GetRequiredService<FilesDeveloperOriginalFolderSetupProducer>(), _windowsDeveloperSetupPolicy!);
            services.AddFilesOriginalDeveloperSetups(_windowsDeveloperDestinationResolver!, provider =>
                (provider.GetRequiredService<IWorkspaceToolService>() as WorkspaceToolService
                    ?? throw new InvalidOperationException("The SAME actual Windows workspace kernel is required."))
                .CreateOriginalDeveloperCaptureSource(provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>(),
                    () => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(),
                    () => provider.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>(),
                    () => provider.GetRequiredService<FileDeveloperWorkspaceStore>(),
                    () => provider.GetRequiredService<FilesDeveloperOriginalFolderSetupProducer>()),
                provider => provider.GetRequiredService<HomeDeveloperProjectSetupJournal>(),
                provider => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>(),
                provider => provider.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>(),
                provider => provider.GetRequiredService<FileDeveloperWorkspaceStore>());
            if (WindowsColdProjectRequested)
            {
                services.AddHavenOwnedNativeColdProjectResources(home);
                services.AddHavenOwnedCurrentProjectExecution(home, _windowsExecutionResolver!, _windowsExecutionPolicy!);
            }
            original.DemandPublication();
            return true;
        }));
    }

    private void CaptureOriginalWindowsDeveloperBorrowers(IServiceProvider provider)
    {
        if (_actualWindowsHome is not { } home) return;
        _originalAppWork.RunSynchronous(original =>
        {
            _ = provider.RequireOriginalWindowsHomeComponents(home);
            T Capture<T>(Action<T> retain) where T : class
            {
                original.DemandPublication();
                return AcquireOriginalAppSynchronous(original, () =>
                {
                    var actual = provider.GetRequiredService<T>(); retain(actual); return actual;
                });
            }
            Capture<HomeDeveloperProjectReadAdmissionSource>(actual => _windowsDeveloperReads = actual);
            Capture<HomeDeveloperProjectSetupJournal>(actual => _windowsDeveloperJournal = actual);
            Capture<HomeDeveloperProjectSetupPermissionSource>(actual => _windowsDeveloperPermissions = actual);
            Capture<FilesDeveloperOriginalSourceSelection>(actual => _windowsDeveloperSelections = actual);
            Capture<IDeveloperProjectOriginalPhysicalCaptureSource>(actual => _windowsDeveloperPhysical = actual);
            Capture<FilesDeveloperOriginalSetupScopeSource>(actual => _windowsDeveloperScopes = actual);
            Capture<FilesDeveloperOriginalFolderSetupProducer>(actual => _windowsDeveloperSteps = actual);
            if (WindowsColdProjectRequested)
            {
                var journal = Capture<SqliteTaskRunColdRecoveryJournal>(_ => { });
                Capture<HomeColdProjectReadReconciliation>(actual => _windowsColdProjects = actual);
                Capture<FilesDeveloperOriginalCurrentProjectSelection>(actual => _windowsCurrentProjects = actual);
                Capture<IDeveloperOriginalCurrentProjectNativeSource>(actual => _windowsCurrentProjectNative = actual);
                Capture<FilesDeveloperOriginalCurrentProjectExecutionBridge>(actual => _windowsCurrentExecution = actual);
                Capture<HomeDeveloperWorkspaceExecutionConsentSource>(actual => _windowsExecutionConsent = actual);
                if (!ReferenceEquals(journal.RequireOriginalProjectResourceSource(), _windowsColdProjects) ||
                    !journal.HasOriginalProjectResourceSource(_windowsColdProjects!) ||
                    !_windowsColdProjects!.HasOriginalColdProjectComposition(journal, provider.GetRequiredService<HostLocalTaskActorSource>()) ||
                    !ReferenceEquals(provider.GetRequiredService<IDeveloperOriginalProjectCommandReadSource>(), _windowsColdProjects) ||
                    !ReferenceEquals(provider.GetRequiredService<IDeveloperWorkspaceTrustService>(), _windowsCurrentExecution) ||
                    !_windowsCurrentExecution!.IsBoundToOriginalToolOwner(provider.GetRequiredService<ITaskRunToolActionOwner>()) ||
                    !ReferenceEquals(provider.GetRequiredService<IWorkspaceOriginalProcessStartConsentSource>(), _windowsExecutionConsent))
                    throw new InvalidOperationException("The SAME Windows cold Task/project/command/tool/execution composition is required.");
            }
            original.DemandPublication();
        });
    }

    private void DemandOriginalWindowsDeveloperRetirementJoin()
    {
        DemandOriginalAssistantDevelopmentInputsJoin();
        _windowsDeveloperPermissions?.DemandExternalOriginalSetupJoin();
        _windowsDeveloperSteps?.DemandExternalOriginalSetupStepOutcomeJoin();
        _windowsDeveloperScopes?.DemandExternalOriginalSetupScopeJoin();
        _windowsDeveloperSelections?.DemandExternalOriginalCaptureJoin();
        _windowsDeveloperPhysical?.DemandExternalOriginalJoin();
        _windowsDeveloperJournal?.DemandExternalOriginalRetirementJoin();
        _windowsDeveloperReads?.DemandExternalOriginalReadAdmissionJoin();
        _windowsCurrentExecution?.DemandExternalOriginalExecutionTrustJoin();
        _windowsExecutionConsent?.DemandExternalOriginalProcessStartConsentJoin();
        _windowsColdProjects?.DemandExternalOriginalJoin();
        _windowsCurrentProjects?.DemandExternalOriginalCurrentProjectJoin();
        _windowsCurrentProjectNative?.DemandExternalOriginalJoin();
    }

    // Canonical business must already be sealed. Only admission is sealed now; final
    // completion/audit uses its still-live original scope/capture/outcome/journal/READ.
    private void RequestOriginalWindowsDeveloperRetirement(List<Exception> failures)
    {
        void Request(Action request)
        { try { _originalAppWork.RunCloseCallback(request); } catch (Exception error) { AddAppCause(failures, error); } }
        if (_windowsDeveloperPermissions is { } permissions) Request(permissions.RequestOriginalSetupRetirement);
        if (_windowsCurrentExecution is { } execution) Request(execution.RequestOriginalExecutionRetirement);
        if (_windowsExecutionConsent is { } consent) Request(consent.RequestOriginalExecutionRetirement);
    }

    private Task JoinOriginalWindowsDeveloperBorrowersAsync()
    {
        DemandOriginalWindowsDeveloperRetirementJoin();
        TaskCompletionSource start; Task actual;
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualWindowsDeveloperDrain is { } retained) return retained;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _actualWindowsDeveloperDrain = JoinOriginalWindowsDeveloperBorrowersCoreAsync(start.Task);
        }
        start.SetResult();
        return actual;
    }
    private async Task JoinOriginalWindowsDeveloperBorrowersCoreAsync(Task start)
    {
        await start;
        await JoinOriginalAssistantDevelopmentInputsAsync();
        var failures = new List<Exception>(); var completions = new List<Task>();
        var completionAcquisitionFailed = false;
        var completionPhase = true;
        void Close(Func<Task> close, List<Task> targets)
        {
            try { _originalAppWork.RunCloseCallback(() => targets.Add(close())); }
            catch (Exception error)
            { AddAppCause(failures, error); if (completionPhase) completionAcquisitionFailed = true; }
        }
        if (_windowsDeveloperPermissions is { } permissions) Close(permissions.CloseAndDrainOriginalSetupsAsync, completions);
        if (_windowsCurrentExecution is { } execution) Close(execution.CloseAndDrainOriginalExecutionAsync, completions);
        if (_windowsExecutionConsent is { } consent) Close(consent.CloseAndDrainOriginalExecutionsAsync, completions);
        foreach (var original in completions.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(original, failures);

        if (completionAcquisitionFailed) ThrowAppCauses(failures);
        completionPhase = false;
        // Each completion above really settled, including faults. Retire dependencies
        // independently, preserving every sibling failure; no ACK is a completion lease.
        void Request(Action request)
        { try { _originalAppWork.RunCloseCallback(request); } catch (Exception error) { AddAppCause(failures, error); } }
        if (_windowsDeveloperSteps is { } steps) Request(steps.RequestOriginalFolderSetupRetirement);
        if (_windowsDeveloperScopes is { } scopes) Request(scopes.RequestOriginalSetupScopeRetirement);
        if (_windowsDeveloperSelections is { } selections) Request(selections.RequestOriginalSelectionRetirement);
        if (_windowsDeveloperPhysical is { } physical) Request(physical.RequestOriginalCaptureRetirement);
        if (_windowsDeveloperJournal is { } journal) Request(journal.RequestRetirement);
        if (_windowsDeveloperReads is { } reads) Request(reads.RequestOriginalReadRetirement);
        if (_windowsColdProjects is { } cold) Request(cold.RequestOriginalRetirement);
        if (_windowsCurrentProjects is { } projects) Request(projects.RequestOriginalCurrentProjectRetirement);
        if (_windowsCurrentProjectNative is { } native) Request(native.RequestOriginalRetirement);
        var dependencies = new List<Task>();
        if (_windowsDeveloperSteps is { } s) Close(s.CloseAndDrainOriginalFolderSetupsAsync, dependencies);
        if (_windowsDeveloperScopes is { } d) Close(d.CloseAndDrainOriginalSetupScopesAsync, dependencies);
        if (_windowsDeveloperSelections is { } a) Close(a.CloseAndDrainOriginalSelectionsAsync, dependencies);
        if (_windowsDeveloperPhysical is { } p) Close(p.CloseAndDrainOriginalCapturesAsync, dependencies);
        if (_windowsDeveloperJournal is { } j) Close(j.CloseAndDrainAsync, dependencies);
        if (_windowsDeveloperReads is { } r) Close(r.CloseAndDrainOriginalReadsAsync, dependencies);
        if (_windowsColdProjects is { } c) Close(c.CloseAndDrainOriginalAsync, dependencies);
        if (_windowsCurrentProjects is { } f) Close(f.CloseAndDrainOriginalCurrentProjectsAsync, dependencies);
        if (_windowsCurrentProjectNative is { } n) Close(n.CloseAndDrainOriginalAsync, dependencies);
        foreach (var original in dependencies.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(original, failures);
        ThrowAppCauses(failures);
        await JoinOriginalAssistantSqliteAfterBorrowersAsync();
    }
    private Task JoinOriginalUntransferredWindowsDeveloperBorrowersAsync()
    {
        lock (_actualStartupAcquisitionGate)
            return _windowsDeveloperBorrowersTransferred ? Task.CompletedTask : JoinOriginalWindowsDeveloperBorrowersAsync();
    }
}
#endif
