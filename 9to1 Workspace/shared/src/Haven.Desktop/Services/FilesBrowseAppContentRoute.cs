using Avalonia.Threading;

namespace Haven.Desktop.Services;

internal sealed partial class NativeFilesDesktopRoute
{
    /// <summary>The request's profile label is an assertion checked against the actual admitted actor.</summary>
    internal void CheckOriginalAppContentProfile(string expectedProfile)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sync)
        {
            ThrowIfRetired();
            if (_originalActor is null || !string.Equals(_originalActor.ProfileId, expectedProfile, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("This Files tab belongs to a different authenticated Home profile.");
        }
    }
}
