using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Source-compatible page name for the SAME internal Desktop original owner.
/// No second registry or operation store is created; inherited APIs and nested Original
/// retain the exact instance-specific original/close/cause identity.</summary>
internal sealed class NewChatOriginalWorkLifetime(Func<Task> stop, Func<Task> cleanup)
    : DesktopOriginalWorkLifetime(stop, cleanup);
