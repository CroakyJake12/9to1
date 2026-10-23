using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationLibraryOriginalReadOwner
{
    internal sealed record OriginalCreationOrigin(IOriginalLibraryObservation Observation, VerifiedResourceStoreOwnership Ownership);
    // A genuine empty available page is sufficient origin. A row, caller UUID or
    // detached ownership receipt cannot replace this SAME producer-issued page.
    internal OriginalCreationOrigin ObserveOriginalCreationOrigin(ICanonicalAutomationLibraryOriginalObservation sameObservation)
    {
        if (!IsIssuedPortObservation(sameObservation) || ((PortObservation)sameObservation).Original is not Observation original ||
            original.State != LibraryState.Available || original.OriginalStoreIdentity is null || original.Receipt is null)
            throw new UnauthorizedAccessException("The SAME actual available protected library/store observation is required for creation.");
        return new(original, original.Receipt);
    }
    internal sealed record OriginalWriteSelection(IOriginalLibraryObservation Observation,
        AutomationOwnerRead<AutomationDefinition> Row, VerifiedResourceStoreOwnership Ownership);
    // Called only by the SAME concrete protected writer. No public matching row or
    // identifier can replace the exact selected row in the source-issued window.
    internal OriginalWriteSelection ObserveOriginalWriteSelection(
        ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<AutomationDefinition> sameRow)
    {
        if (!IsIssuedPortObservation(sameObservation) || ((PortObservation)sameObservation).Original is not Observation original ||
            original.State != LibraryState.Available || original.OriginalStoreIdentity is null || original.Receipt is null ||
            !original.Definitions.Any(row => ReferenceEquals(row, sameRow)))
            throw new UnauthorizedAccessException("Select the SAME actual protected library page and retained row.");
        return new(original, sameRow, original.Receipt);
    }
}
