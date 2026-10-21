#if !ANDROID
using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    /// <summary>
    /// Registers the typed first-run onboarding backend against the REAL Home state store and the
    /// shared model provider registry. The recommendation-manifest source and the 9to1.uk/auth
    /// browser launcher remain deliberately unconfigured (null): the exact manifest endpoint and
    /// issuer/registration contract are not specified by the canonical spec, so the flow surfaces an
    /// honest scoped unavailable state instead of fabricating auth/download. No production
    /// endpoint, list or credential is invented here.
    /// </summary>
    private void ConfigureOriginalOnboardingRegistrations(IServiceCollection collection)
    {
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            collection.AddSingleton<HomeOnboardingService>(provider => new HomeOnboardingService(
                provider.GetRequiredService<IHomeCoreStateStore>(),
                modelRegistry: provider.GetService<IModelProviderRegistry>(),
                recommendationSource: null,
                browserLauncher: null,
                modelEvidenceSource: new HomeDulchePackagedModelEvidenceSource()));
            return true;
        }));
    }
}
#endif
