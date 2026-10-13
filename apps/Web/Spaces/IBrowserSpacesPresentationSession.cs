using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Spaces;

public interface IBrowserSpacesPresentationSession : ICuiWritableBindingContext, ICuiRepeatItemBindingContext,
    ICuiLifetimeAwareActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    Task PrepareAsync(CancellationToken cancellationToken);
    void Admit();
    void Deactivate();
    void RevokePrivateContext();
    bool HasUnsavedChanges { get; }
}
