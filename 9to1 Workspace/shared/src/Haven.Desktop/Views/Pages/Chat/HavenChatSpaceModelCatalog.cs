using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Adapts the installed local model inventory to the Chat Space's governed model catalog contract.
/// </summary>
public sealed class HavenChatSpaceModelCatalog(IOllamaClient models, UserPreferencesService preferences) : IChatSpaceModelCatalog
{
    private readonly IOllamaClient _models = models ?? throw new ArgumentNullException(nameof(models));
    private readonly UserPreferencesService _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));

    public async Task<ChatSpaceModelInventory> GetInventoryAsync(CancellationToken cancellationToken)
    {
        var descriptors = await _models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        return new ChatSpaceModelInventory(descriptors, _preferences.DefaultModel);
    }
}
