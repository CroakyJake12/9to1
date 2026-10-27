#if !ANDROID
using Haven.Application;
using Haven.Desktop.Accounts;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Accounts.Native;

namespace Haven.Desktop;

public sealed partial class App
{
    /// <summary>
    /// Registers the typed first-run onboarding backend against the REAL Home state store and the
    /// shared model provider registry and native CAKE ID session. The recommendation-manifest
    /// endpoint remains unconfigured until its authoritative versioned contract is supplied.
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
                browserLauncher: new NativeCakeOnboardingAdapter(provider.GetRequiredService<INativeCakeAccountSession>()),
                modelEvidenceSource: new HomeDulchePackagedModelEvidenceSource()));
            return true;
        }));
    }
}
#endif
