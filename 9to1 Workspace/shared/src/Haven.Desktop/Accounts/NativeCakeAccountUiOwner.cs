#if !ANDROID
using Avalonia.Controls;
using Haven.Application;
using Haven.Desktop.Services;
using Avalonia.Threading;
using NineToOne.Accounts.Native;
namespace Haven.Desktop.Accounts;

/// <summary>Actual desktop owner of one account window and its original work; session is borrowed DI.
/// All joins occur outside accepted account/CUI actions, while the actual dispatcher lives.</summary>
public sealed class NativeCakeAccountUiOwner(INativeCakeAccountSession session, INativeCakeAccessCredentialSource? access=null):IAsyncDisposable, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
 private readonly List<NativeCakeAccountWindow> windows=[];private readonly List<Task> opens=[];
 private NativeCakeAccountWindow? active;private Task? close;private bool closing;
 private readonly InvalidOperationException capacity=new("Close the native account owner before retaining more failed presentations");
 private bool refused;
 public Task OpenAsync(Window? parent)
 {
  Dispatcher.UIThread.VerifyAccess();if(closing||App.IsOriginalDesktopRetiring)throw new ObjectDisposedException(nameof(NativeCakeAccountUiOwner));
  opens.RemoveAll(t=>t.IsCompletedSuccessfully);
  windows.RemoveAll(w=>w.IsRetired&&w.OriginalClose?.IsCompletedSuccessfully==true&&w.Initialization.IsCompletedSuccessfully);
  if(refused||windows.Count>=16||opens.Count>=16){refused=true;throw capacity;}
  var start=new TaskCompletionSource();var actual=OpenCoreAsync(start.Task,parent);opens.Add(actual);start.SetResult();return actual;
 }
 private async Task OpenCoreAsync(Task start,Window? parent)
 {
  await start.ConfigureAwait(true);using var originalScope=CloudflareOriginalExecutionGuard.EnterOriginal(this);if(closing)throw new ObjectDisposedException(nameof(NativeCakeAccountUiOwner));
  if(active is {} current&&!current.IsRetired&&current.OriginalClose is null){current.Activate();await current.Initialization.ConfigureAwait(true);return;}
  var acquired=new NativeCakeAccountWindow(session);windows.Add(acquired);active=acquired; // Custody before Show/native callbacks.
  var initialization=acquired.StartOriginalInitialization();
  if(parent is null)acquired.Show();else acquired.Show(parent);
  await initialization.ConfigureAwait(true);
 }
 public void DemandExternalOriginalRetirementJoin()
 {Dispatcher.UIThread.VerifyAccess();CloudflareOriginalExecutionGuard.DemandExternalJoin(this);foreach(var window in windows)window.DemandExternalOriginalRetirementJoin();}
 public void RequestRetirement(){DemandExternalOriginalRetirementJoin();closing=true;}
 public Task CloseAndDrainAsync()
 {
  DemandExternalOriginalRetirementJoin();if(close is not null)return close;
  var start=new TaskCompletionSource();closing=true;var originals=opens.ToArray();var acquired=windows.ToArray();
  close=CloseCoreAsync(start.Task,originals,acquired);start.SetResult();return close;
 }
 private async Task CloseCoreAsync(Task start,Task[] admitted,NativeCakeAccountWindow[] acquired)
 {
  await start.ConfigureAwait(true);List<Exception> errors=[];List<Task> originals=[..admitted];
  foreach(var window in acquired)try{originals.Add(window.CloseAndDrainAsync());}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  try{originals.Add(session.CloseAndDrainAsync());}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  if(access is not null)try{originals.Add(access.CloseAndDrainAsync());}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  foreach(var actual in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))await NativeAccountUiCauses.JoinOneAsync(actual,errors);
  if(refused)NativeAccountUiCauses.Add(errors,capacity);
  NativeAccountUiCauses.Throw(errors);active=null;
 }
 public ValueTask DisposeAsync()=>new(CloseAndDrainAsync());
}

#endif
