namespace Haven.Desktop.Services;

/// <summary>
/// A product owner requests permanent retirement, then an external host joins the
/// SAME actual originals and cleanup. This conveys no account, permission or grant.
/// </summary>
internal interface IDesktopOriginalRetirementParticipant
{
    void RequestRetirement();
    Task CloseAndDrainAsync();
}

/// <summary>An unclaimed producer must not be described as completely drained.</summary>
internal sealed class DesktopOriginalRetirementUnavailableException(Type actualOwnerType)
    : InvalidOperationException("No original retirement owner is registered for " +
        (actualOwnerType.FullName ?? actualOwnerType.Name) + ".")
{
    internal Type ActualOwnerType { get; } = actualOwnerType;
}
