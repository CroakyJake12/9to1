#if !ANDROID
using Haven.Application;
using HavenOS.Home.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop;

public sealed partial class App
{
    private OriginalAssistantPersonalDenHost? _actualAssistantPersonalDen;

    // Called inside existing configureOriginalStores, using the SAME paths and identity.Profiles.
    // Root retains this exact owner before returning its evidence/service registrations.
    private OriginalAssistantPersonalDenHost RetainOriginalAssistantPersonalDen(IAppPaths actualPaths,
        HomeLocalProfileIdentity actualProfiles)
    {
        if (_actualAssistantPersonalDen is not null)
            throw new InvalidOperationException("The original App already retained its actual personal Den owner.");
        return _actualAssistantPersonalDen = new(actualPaths, actualProfiles);
    }

    private void DemandOriginalAssistantPersonalDenRetirementJoin()
    {
        DemandOriginalModelCatalogueRetirementJoin();
        _actualAssistantPersonalDen?.DemandExternalOriginalRetirementJoin();
    }

    // Only after all actual scoped presentations AND ordinary Chat/Task business hosts have joined.
    private Task JoinOriginalAssistantPersonalDenAfterBorrowersAsync() =>
        JoinOriginalModelCataloguesThenDenAsync();
}
#endif
