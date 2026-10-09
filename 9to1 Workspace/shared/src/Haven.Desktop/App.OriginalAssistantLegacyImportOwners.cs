#if !ANDROID
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Migration;
using HavenOS.Home.Core;

namespace Haven.Desktop;

public sealed partial class App
{
    private HomeOriginalLocalStoreImportSession? _actualAssistantLegacyImportSession;

    private HomeOriginalLocalStoreImportSession AcquireOriginalAssistantLegacyImportSession(
        IServiceProvider provider, HomeNativeWindowsComposition home, LegacySavedAgentSqliteSource legacy)
    {
        HomeOriginalLocalStoreImportSession actual = null!;
        _originalAppWork.RunSynchronous(original =>
        {
            original.DemandPublication();
            actual = AcquireOriginalAppSynchronous(original, () =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
                    !ReferenceEquals(legacy, _actualAssistantLegacySource) || _actualAssistantSqliteStore is null ||
                    !legacy.IsOriginalSource(_actualAssistantSqliteStore, home.Profiles))
                    throw new UnauthorizedAccessException("Use the SAME actual legacy source/protected store/Home profile owners.");
                // This session performs no constructor IO/import. Retain it once
                // across presentations; closing a view cannot retire borrowed
                // Home/source owners or discard a pending individual import.
                var same = _actualAssistantLegacyImportSession ??= new(LegacySavedAgentSqliteSource.LegacyResourceKind,
                    legacy, home.Profiles, home.LocalStoreOwnership, legacy, home.Permissions, home.Ownership);
                if (same.OriginalClose is not null || !legacy.IsOriginalImportSession(same, home.Ownership))
                    throw new InvalidOperationException("Retain the SAME live process-owned Home legacy import session before reopening a migration view.");
                return same;
            });
            original.DemandPublication();
        });
        return actual;
    }
}
#endif
