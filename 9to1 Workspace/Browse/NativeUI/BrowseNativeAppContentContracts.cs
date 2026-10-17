using Avalonia.Controls;

namespace HavenOS.Apps.Browse;

/// <summary>The owning app supplies its actual, stable native view, never another application shell.</summary>
public interface IBrowseNativeAppContentSession : IBrowseAppContentSession
{
    Control OriginalNativeView { get; }
}
