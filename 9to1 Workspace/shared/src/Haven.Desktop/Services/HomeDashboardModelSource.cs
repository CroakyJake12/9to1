using Haven.Application;

namespace Haven.Desktop.Services;

public sealed record HomeDashboardModels(LocalModelHardware Hardware, IReadOnlyList<LocalModelRecommendation> Models, string Status,
    AuthenticatedResourceActor Actor);

/// <summary>Borrows Home's profile/credential/catalogue owners. Hardware stays local; only a public
/// catalogue query and repository identities leave the device. No model download or inference.</summary>
public sealed class HomeDashboardModelSource(ILocalModelHardwareProbe hardware, IHuggingFaceModelService catalogue,
    Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>> profile, Func<bool> networkAllowed)
{
    public IHuggingFaceModelService Catalogue => catalogue;
    public Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>> Profile => async token =>
        networkAllowed() ? await profile(token) : null;

    public async Task<HomeDashboardModels> ReadAsync(CancellationToken token)
    {
        var actor = await profile(token);
        if (actor is null) throw new UnauthorizedAccessException("The active Home profile is unavailable.");
        var device = await hardware.ReadAsync(token).ConfigureAwait(false);
        if (!Equals(actor, await profile(token))) throw new UnauthorizedAccessException("The active Home profile changed.");
        if (!networkAllowed()) return new(device, [], "Local-only mode is on. Open Discover to inspect your installed models.", actor);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var observed = new List<(HuggingFaceModelSummary, HuggingFaceModelDetails)>();
        var failures = 0;
        try
        {
            var repositories = await catalogue.SearchAsync(actor.ProfileId, string.Empty, deadline.Token).ConfigureAwait(false);
            foreach (var item in repositories.Take(12))
            {
                if (!networkAllowed()) return new(device, [], "Local-only mode is on. Online recommendations were cleared.", actor);
                if (!Equals(actor, await profile(deadline.Token))) throw new UnauthorizedAccessException("The active Home profile changed.");
                try
                {
                    var details = await catalogue.GetDetailsAsync(actor.ProfileId, item.RepositoryId, deadline.Token).ConfigureAwait(false);
                    observed.Add((item, details));
                }
                catch (HuggingFaceModelException) { failures++; }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { failures++; }
        catch (HuggingFaceModelException) { failures++; }
        token.ThrowIfCancellationRequested();
        if (!Equals(actor, await profile(token))) throw new UnauthorizedAccessException("The active Home profile changed.");
        if (!networkAllowed()) return new(device, [], "Local-only mode is on. Online recommendations were cleared.", actor);
        var models = LocalModelMemoryEstimator.Rank(device, observed);
        var status = models.Count == 0
            ? device.AvailableRamBytes is null ? "Available RAM could not be measured, so hardware fit is unknown. Browse Discover to inspect models."
                : failures > 0 ? "Model recommendations could not be loaded. Retry or browse Discover." : "No inspected model fits the measured available memory. Browse Discover for smaller models."
            : $"Memory-fit estimates from {observed.Count} Hugging Face repositories, ordered by fit then downloads. " +
                "File size × 1.25 + 1 GiB overhead; context needs vary. Compatibility is checked at load time.";
        if (models.Count > 0 && failures > 0) status += " Some repositories were unavailable.";
        return new(device, models, status, actor);
    }
}
