using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Tests;

public sealed class DeveloperTemplateProjectEntryAdapterTests
{
    [Fact]
    public async Task Registration_RejectsForgedPayloadAndFolderNameThatDiffersFromReviewedProject()
    {
        var provider = new DeveloperCuiProjectTemplateProvider();
        var option = Assert.Single((await new DeveloperProjectCatalog([provider]).SearchAsync(new())).Value!);
        var materializer = new DeveloperProjectTemplateMaterializer([provider]);
        var prepared = await materializer.PrepareAsync(option, "Reviewed Project");
        Assert.True(prepared.Succeeded);
        var issued = prepared.Value!;
        var adapter = new DeveloperTemplateProjectEntryAdapter(null!, materializer);

        var forged = issued with { ProjectName = "Other Project" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.RegisterExistingFolderAsync(
            Guid.NewGuid(), Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "Other Project"), forged, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.RegisterExistingFolderAsync(
            Guid.NewGuid(), Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "Other Project"), issued, CancellationToken.None));
    }
}
