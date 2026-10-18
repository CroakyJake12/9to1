namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Truthful model availability observed from the actual discovery read.
/// <see cref="Unavailable"/> is a terminal degraded outcome: the read completed and
/// failed, or completed with no usable model. It is never a retirement failure and
/// must survive Fresh/Restore instead of being cleared with transient projection
/// status.</summary>
public enum ChatModelAvailability
{
    Unknown,
    Discovering,
    Available,
    Unavailable,
}
