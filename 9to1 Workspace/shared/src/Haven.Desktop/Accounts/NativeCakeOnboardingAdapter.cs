#if !ANDROID
using HavenOS.Home.Core;
using NineToOne.Accounts.Native;

namespace Haven.Desktop.Accounts;

/// <summary>Onboarding borrows the same profile-owned native account session as Home.
/// The session owns PKCE, loopback reception, verified identity and retirement.</summary>
public sealed class NativeCakeOnboardingAdapter(INativeCakeAccountSession session) : IHomeOnboardingBrowserLauncher
{
    public bool IsAvailable => session.IsAvailable;
    public bool IsSignedIn => session.TryGetCurrentSnapshot(out _);

    public async Task<bool> LaunchAuthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.IsAvailable) return false;
        if (session.TryGetCurrentSnapshot(out _)) return true;
        var authenticated = await session.SignInAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return session.TryGetCurrentSnapshot(out var current) && current == authenticated;
    }
}
#endif
