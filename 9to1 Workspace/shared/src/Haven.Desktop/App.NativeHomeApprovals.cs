#if !ANDROID
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private HomeNativeApprovalWindowOwner? _actualNativeHomeApprovals;
    private bool _nativeHomeApprovalBorrowerTransferred;

    private void ConfigureOriginalNativeHomeApprovalServices(IServiceCollection services)
    {
        if (_actualWindowsHome is not { } home) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            if (services.Any(row => row.ServiceType == typeof(HomeNativeApprovalWindowOwner)))
                throw new InvalidOperationException("The native Home review owner already exists.");
            services.AddSingleton<HomeNativeApprovalWindowOwner>(provider =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                // Genuine one-time decisions reuse canonical Home. Extended trust remains
                // unavailable because no real native warning-presentation issuer is supplied.
                return _actualNativeHomeApprovals = new(home.Runtime, home.Profiles, home.Permissions);
            });
            return true;
        }));
    }

    private object[] CaptureOriginalNativeHomeApprovalBorrowers(IServiceProvider provider)
    {
        if (_actualWindowsHome is not { } home) return [];
        HomeNativeApprovalWindowOwner? actual = null;
        _originalAppWork.RunSynchronous(original =>
        {
            actual = AcquireOriginalAppSynchronous(original, provider.GetRequiredService<HomeNativeApprovalWindowOwner>);
            if (!actual.IsBoundToOriginalComposition(home))
                throw new UnauthorizedAccessException("The native review owner must borrow the SAME original Home components.");
            original.DemandPublication();
        });
        return [new NativeHomeApprovalOriginalRetirementAdapter(actual!)];
    }

    private void DemandOriginalNativeHomeApprovalRetirementJoin() =>
        _actualNativeHomeApprovals?.DemandExternalOriginalRetirementJoin();

    private Task JoinOriginalUntransferredNativeHomeApprovalsAsync()
    {
        lock (_actualStartupAcquisitionGate)
            return _nativeHomeApprovalBorrowerTransferred ? Task.CompletedTask :
                _actualNativeHomeApprovals?.CloseAndDrainAsync() ?? Task.CompletedTask;
    }
}
#endif
