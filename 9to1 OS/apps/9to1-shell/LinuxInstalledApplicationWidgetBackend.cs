using System.Globalization;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell;

/// <summary>Read-only projection of one existing canonical installed entry. The trusted owner
/// connection independently verifies the actual installed publisher; these reference fields
/// do not grant installation, launch, configuration or source access.</summary>
public sealed class LinuxInstalledApplicationWidgetBackend(
    IInstalledApplicationRegistry registry, IAuthenticatedResourceActorSource actors,
    ResourceAuthorizationService resources, HomeNativeWidgetReference originalOwner,
    Guid applicationId, long applicationRevision) : IHomeNativeWidgetOriginalActorRuntimeEndpoint
{
    public const string WidgetId = "installed-application-card";
    public const string DefinitionRevision = "1";
    public const string SurfaceReference = "installed-application-card.surface.v1";
    private readonly ResourceScope _scope = new("os.installed-application", applicationId.ToString("D"),
        applicationRevision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read);

    // Legacy capture cannot choose a replacement ambient profile.
    public ValueTask<HomeNativeWidgetSurface?> CaptureAsync(HomeNativeWidgetCaptureRequest request,
        CancellationToken cancellationToken) => ValueTask.FromResult<HomeNativeWidgetSurface?>(null);

    public async ValueTask<HomeNativeWidgetSurface?> CaptureForActorAsync(HomeNativeWidgetCaptureRequest request,
        AuthenticatedResourceActor expectedActor, CancellationToken ct)
    {
        if (request is null || expectedActor is null || applicationId == Guid.Empty || applicationRevision < 1 ||
            originalOwner.WidgetId != WidgetId || originalOwner.DefinitionRevision != DefinitionRevision ||
            request.Reference != originalOwner || request.SurfaceReference != SurfaceReference ||
            request.GridSize is null || request.GridSize.Columns is < 1 or > 4 || request.GridSize.Rows is < 1 or > 4 ||
            !double.IsFinite(request.ViewportWidth) || !double.IsFinite(request.ViewportHeight) ||
            request.ViewportWidth is <= 0 or > 16384 || request.ViewportHeight is <= 0 or > 16384 ||
            registry is not IInstalledApplicationOriginalReadRegistry originalRead ||
            expectedActor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return null;
        if (await resources.AuthorizeForActorAsync(expectedActor, "os.application.read", [_scope], ct).ConfigureAwait(false)
            != expectedActor) return null;
        var snapshot = await originalRead.ReadExistingForActorAsync(expectedActor, ct).ConfigureAwait(false);
        if (snapshot is null || expectedActor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return null;
        var app = snapshot.Applications.SingleOrDefault(a => a.ApplicationId == applicationId &&
            a.Revision == applicationRevision && a.HomeProfileId == expectedActor.ProfileId &&
            a.ProviderId == "linux.xdg-desktop" && a.Enabled && a.ProfileAccessible);
        if (app is null) return null;
        if (await resources.AuthorizeForActorAsync(expectedActor, "os.application.read", [_scope], ct).ConfigureAwait(false)
            != expectedActor || expectedActor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return null;
        // Markup is fixed. All actual labels/version values remain detached scalar binding data.
        return HomeNativeWidgetSurface.Capture(originalOwner, SurfaceReference,
            "<Cui><Stack orientation=\"vertical\"><Text role=\"title\">{Binding label}</Text>" +
            "<Text role=\"caption\">{Binding version}</Text></Stack></Cui>",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["label"] = JsonSerializer.SerializeToElement(app.Label),
                ["version"] = JsonSerializer.SerializeToElement(app.Version ?? "Version unavailable")
            });
    }

    public HomeNativeWidgetDefinition Declaration() => new(WidgetId, DefinitionRevision,
        "Installed application", new(1, 1), new(2, 1), new(4, 4),
        "installed-application-card.configuration.unavailable.v1", SurfaceReference,
        HomeNativeWidgetUpdateMode.Manual, null, [], [_scope]);
}
