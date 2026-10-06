using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>An immutable presentation wrapper with no producer or callback of its own.
/// Its captured content keeps its separate, actual owning lifetime.</summary>
public sealed class DesktopFloatingActivityContent : IFloatingActivityContent
{
    public DesktopFloatingActivityContent(object? content, string automationName)
    {
        Content = content!; // Literal null is static content, not a lifetime witness.
        AutomationName = automationName ?? throw new ArgumentNullException(nameof(automationName));
    }
    public object Content { get; }
    public string AutomationName { get; }
}
