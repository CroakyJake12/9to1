namespace Haven.Application;

/// <summary>Request-only source retirement and external join of the SAME actual Home READ
/// originals. Files/kernel selection and capture are independent owners and must also drain.</summary>
public interface IDeveloperProjectOriginalReadRetirementSource : IDeveloperProjectOriginalReadAdmissionJoinGuard
{
    void RequestOriginalReadRetirement();
    Task CloseAndDrainOriginalReadsAsync();
}
