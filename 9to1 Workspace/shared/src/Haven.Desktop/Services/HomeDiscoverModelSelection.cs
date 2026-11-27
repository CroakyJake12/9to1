using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home;
using HavenOS.Home.Discover;

namespace Haven.Desktop.Services;

/// <summary>Uses the same compact-picker action and guarded route save as an explicit
/// picker selection. Provider, model and artifact identity are never substituted.</summary>
internal static class HomeDiscoverModelSelection
{
    internal static async Task<HomeDiscoverModelUseResult> UseAsync(HomeModelPickerCuiController controller,
        HomeDiscoverIdentity identity, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(controller); ArgumentNullException.ThrowIfNull(identity);
        var result = await controller.ExecuteAsync(new(HomeModelPickerAction.ChooseCompactModel,
            Category: "chat", ProviderId: identity.ProviderId, ModelId: identity.ModelId,
            ArtifactRevision: identity.ArtifactRevision), token);
        return new(result.Succeeded, result.Code, result.Message);
    }
}
