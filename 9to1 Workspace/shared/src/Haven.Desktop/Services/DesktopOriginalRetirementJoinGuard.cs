namespace Haven.Desktop.Services;

/// <summary>Optional original-owner guard only; conveys no grant or completion receipt.
/// Hosts preflight the entire actual cohort before exposing any encompassing join Task.</summary>
internal interface IDesktopOriginalRetirementJoinGuard
{
    void DemandExternalOriginalRetirementJoin();
}
