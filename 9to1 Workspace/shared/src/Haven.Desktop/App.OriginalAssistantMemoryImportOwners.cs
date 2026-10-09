#if !ANDROID
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Home.Core;

namespace Haven.Desktop;

public sealed partial class App
{
    private HomeOriginalLocalStoreImportSession? _actualAssistantMemoryImportSession;

    private void BindOriginalAssistantMemoryImportSession(IServiceProvider provider,
        HomeNativeWindowsComposition home, AssistantOriginalMemorySource memory)
    {
        _originalAppWork.RunSynchronous(original =>
        {
            original.DemandPublication();
            AcquireOriginalAppSynchronous(original, () =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
                    !ReferenceEquals(memory, _actualAssistantMemorySource) || memory.OriginalClose is not null ||
                    _actualAssistantMemoryEvidence is not { } evidence || _actualAssistantSqliteStore is not { } store ||
                    !evidence.HasOriginalComposition(store, AssistantOriginalMemorySource.ResourceKind))
                    throw new UnauthorizedAccessException("Use the SAME actual memory source/protected store/Home profile/evidence owners.");
                // No constructor IO or import. The App retains one session across
                // views; source and presentation borrowers cannot retire this owner.
                var same = _actualAssistantMemoryImportSession ??= new(AssistantOriginalMemorySource.ResourceKind,
                    memory, home.Profiles, home.LocalStoreOwnership, evidence, home.Permissions, home.Ownership);
                if (same.OriginalClose is not null ||
                    !same.IsOriginalSource(memory, evidence, home.Profiles, home.Ownership))
                    throw new InvalidOperationException("Retain the SAME live process-owned Home memory import session.");
                memory.BindOriginalMemoryImportSession(same, evidence);
                if (!memory.HasOriginalMemoryImportSession(same))
                    throw new InvalidOperationException("The actual memory source did not retain its SAME Home import session.");
                return true;
            });
            original.DemandPublication();
        });
    }
}
#endif
