using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.AIStudio;

public static class StudioNativeScene
{
    public static CuiNativeScene Create(ICuiBindingContext bindings,ICuiActionDispatcher actions,
        ICuiSceneReadiness authenticatedHomeReadiness)
    {
        using var stream=typeof(StudioNativeScene).Assembly.GetManifestResourceStream("HavenOS.AIStudio.UI.Studio.cui")
            ??throw new InvalidDataException("Canonical Studio CUI source is missing.");
        using var reader=new StreamReader(stream);
        return new("9to1.Studio","AI Studio","Studio",new CuiRichParser().Parse(reader.ReadToEnd()),
            bindings,actions,authenticatedHomeReadiness);
    }

    public static CuiNativeScene CreateUnavailable()
    {
        var model=new CuiViewModel();
        return Create(model,model,new RequiredAuthoringServices());
    }

    private sealed class RequiredAuthoringServices:ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                "studio_home_files_bridge_unavailable",
                "Connect the installed Home service and configure AI Studio storage in Files. Authoring requires the canonical Den and Dulche services."));
        }
    }
}
