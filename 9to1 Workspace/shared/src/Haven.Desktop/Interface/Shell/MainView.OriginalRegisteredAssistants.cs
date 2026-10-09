#if !ANDROID
using Avalonia.Threading;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private Task OpenOriginalRegisteredAssistantsAsync() => _originalShellWork.RunAsync(original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        if (global::Avalonia.Application.Current is not global::Haven.Desktop.App owningApp)
            throw new InvalidOperationException("The actual Assistants App owner is unavailable.");
        return original.AwaitAsync(AcquireOriginalShellSynchronous(original,
            () => owningApp.OpenOriginalAssistantsForShellAsync(this, original.Token)));
    });
}
#endif
