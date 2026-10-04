using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace NineToOne.Web.Write;

/// <summary>Registers mechanically extracted owner resources, never an app-private palette.</summary>
public static class WriteRetainedSceneResources
{
    public static void Register(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var resources = AvaloniaXamlLoader.Load(new Uri("avares://Haven/WriteOwnerResources/DefaultTheme.Resources.axaml"));
        if (resources is not IResourceDictionary dictionary)
            throw new InvalidDataException("The owning Write scene resources are unavailable.");
        application.Resources.MergedDictionaries.Add(dictionary);
    }
}
