#if !ANDROID
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private BoardsOriginalStoreSetupBackend? _actualBoardsStoreSetup;

    private void RegisterOriginalBoardsStoreSetup(IServiceCollection collection, HomeNativeWindowsComposition home,
        NotesRepository notes, NotesLocalStoreEvidenceProvider evidence)
    {
        if (collection.Any(row => row.ServiceType == typeof(BoardsOriginalStoreSetupBackend)))
            throw new InvalidOperationException("The original local-library setup owner already exists.");
        collection.AddSingleton<BoardsOriginalStoreSetupBackend>(provider =>
        {
            BoardsOriginalStoreSetupBackend actual = null!;
            _originalAppWork.RunSynchronous(original =>
            {
                original.DemandPublication();
                actual = AcquireOriginalAppSynchronous(original, () =>
                {
                    _ = provider.RequireOriginalWindowsHomeComponents(home);
                    if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
                        !ReferenceEquals(provider.GetRequiredService<NotesRepository>(), notes) ||
                        !ReferenceEquals(provider.GetRequiredService<NotesLocalStoreEvidenceProvider>(), evidence))
                        throw new UnauthorizedAccessException("Retain the SAME original provider, Notes evidence and Home setup composition.");
                    var approvals = provider.GetRequiredService<HomeNativeApprovalWindowOwner>();
                    // Retain the real process owner before any identity/registration check
                    // following allocation can fail. Views borrow it; constructors perform no IO.
                    var same = _actualBoardsStoreSetup ??= new(notes, evidence, home, approvals, original.Token);
                    if (same.OriginalClose is not null || !same.HasOriginalComposition(notes, evidence, home, approvals))
                        throw new InvalidOperationException("The original local-library setup owner changed or retired.");
                    return same;
                });
                original.DemandPublication();
            });
            return actual;
        });
    }

    private void DemandOriginalBoardsStoreSetupRetirementJoin() =>
        _actualBoardsStoreSetup?.DemandExternalOriginalRetirementJoin();

    private Task JoinOriginalBoardsStoreSetupAfterBorrowersAsync() =>
        _actualBoardsStoreSetup?.CloseAndDrainAsync() ?? Task.CompletedTask;
}
#endif
