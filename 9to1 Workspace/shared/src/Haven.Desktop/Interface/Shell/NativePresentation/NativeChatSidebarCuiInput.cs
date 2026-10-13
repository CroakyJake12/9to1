using HavenOS.Apps.Spaces.Chat.Rendering;
using Avalonia.Controls;
namespace Haven.Desktop.Views.Shell.NativePresentation;
internal sealed class NativeChatSidebarCuiInput : IDisposable
{
    private readonly SpacesNavigationCuiInput _shared;
    public NativeChatSidebarCuiInput(Control root) => _shared = new(root);
    public void Dispose() => _shared.Dispose();
}
