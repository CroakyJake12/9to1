#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private OriginalAssistantSpacesDependencyOwner? _actualAssistantSpacesDependency;

    private async Task<OriginalAssistantsDependencyStatus> ObserveOriginalAssistantSpacesDependencyAsync(
        DesktopOriginalWorkLifetime.Original original, IServiceProvider provider,
        HomeNativeWindowsComposition home, CancellationToken token)
    {
        void DemandCurrent()
        {
            token.ThrowIfCancellationRequested(); original.DemandPublication();
            if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
                home.OriginalStartTask?.IsCompletedSuccessfully != true || home.OriginalCloseTask is not null ||
                home.OriginalProcessRetirementRequestTask is not null)
                throw new UnauthorizedAccessException("Use the SAME started App/Home dependency composition.");
        }
        DemandCurrent();
        var owner = AcquireOriginalAppSynchronous(original, () => provider.GetService<OriginalAssistantSpacesDependencyOwner>());
        // A normal host without the genuine cold Root composition shows setup
        // before any personal Den access, creation or controller.
        if (owner is null) return OriginalAssistantsDependencyStatus.SetupRequired();
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualAssistantSpacesDependency is { } previous && !ReferenceEquals(previous, owner))
                throw new UnauthorizedAccessException("The original installed Spaces dependency owner changed.");
            _actualAssistantSpacesDependency = owner; // Retain before source callbacks can run.
        }
        if (!ReferenceEquals(owner.OriginalHome, home))
            throw new UnauthorizedAccessException("Use the SAME actual Home installed Spaces dependency owner.");
        void Scope(Action source) => AcquireOriginalAppSynchronous(original,
            () => { DemandCurrent(); source(); return true; });
        void Retain(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            lock (_actualStartupAcquisitionGate) _actualAssistantAcquisitionSources.Add(actual);
        }
        var actorTask = AcquireOriginalAppSynchronous(original,
            () => home.Profiles.GetCurrentAsync(Scope, Retain, token).AsTask());
        var actor = await original.AwaitAsync(actorTask)
            ?? throw new UnauthorizedAccessException("A current real personal Home actor is required for installation observation.");
        DemandCurrent();
        var observation = AcquireOriginalAppSynchronous(original,
            () => owner.ObserveOriginalAsync(actor, Scope, Retain, token));
        var status = await original.AwaitAsync(observation);
        DemandCurrent();
        if (status.IsObserved && !status.IsIssuedBy(owner))
            throw new UnauthorizedAccessException("Use the SAME actual installed Spaces source observation.");
        return status;
    }
}
#endif
