using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
namespace HavenOS.Apps.Spaces.Chat.Rendering;

/// <summary>Borrowed observation and commands of the current application's
/// original navigation owner. A workspace does not acquire its disposal authority.</summary>
public interface ISpacesShellNavigation : ICuiWritableBindingContext,
    ICuiRepeatItemBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
{
    double PreferredWidth { get; }
}
