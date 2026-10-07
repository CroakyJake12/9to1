#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Accounts.Native;
using NineToOne.Web.Accounts;
namespace Haven.Desktop.Accounts;

internal sealed class NativeCakeAccountWindow:Window
{
 private readonly INativeCakeAccountSession session;private readonly CuiSceneHost host=new();
 private readonly CancellationTokenSource stop=new();private readonly DispatcherTimer clock=new(){Interval=TimeSpan.FromSeconds(1)};
 private readonly AccountBrowserBindings binding;private readonly NativeAccountBindingsAdapter adapter;
 private readonly List<Exception> ownErrors=[];private Task? close;private bool closing,closed;
 internal Task Initialization {get;private set;}=Task.CompletedTask;
 internal bool IsRetired=>closed;internal Task? OriginalClose=>close;
 internal NativeCakeAccountWindow(INativeCakeAccountSession originalSession)
 {
  session=originalSession;Title="CAKE ID account";Width=720;Height=820;
  binding=new(new NativeAccountTransport(session),PresentAsync,SignInAsync);
  adapter=new(binding,session,stop.Token);Content=host;
  clock.Tick+=OnContextTick;Closing+=OnClosing;
 }
 internal Task StartOriginalInitialization()
 {
  Dispatcher.UIThread.VerifyAccess();
  var start=new TaskCompletionSource();Initialization=InitializeAsync(start.Task);start.SetResult();return Initialization;
 }
 private async Task InitializeAsync(Task start)
 {
  await start.ConfigureAwait(true);stop.Token.ThrowIfCancellationRequested();
  using var stream=typeof(NativeCakeAccountWindow).Assembly.GetManifestResourceStream("Haven.Desktop.NativeCake.Accounts.cui")??throw new InvalidDataException("Account CUI resource is unavailable");
  using var reader=new StreamReader(stream);var parser=new CuiRichParser();var document=parser.Parse(reader.ReadToEnd(),"Accounts.cui");
  if(parser.Diagnostics.Diagnostics.Any(d=>d.Severity==CuiDiagnosticSeverity.Error))throw new InvalidDataException("Account CUI resource is incompatible");
  var scene=new CuiNativeScene("9to1.accounts","CAKE ID account","Home",document,adapter,adapter,new PresentationReadiness())
   {IsPublicationCurrent=()=>!closing&&!App.IsOriginalDesktopRetiring};
  var show=host.ShowAsync(scene,stop.Token); // Actual guarded Show retained by SAME host before callbacks.
  await show.ConfigureAwait(true);stop.Token.ThrowIfCancellationRequested();
  clock.Start();var refresh=adapter.DispatchAsync("Refresh",null,stop.Token).AsTask();await refresh.ConfigureAwait(true);
 }
 // This readiness concerns only loading the account presentation. It attests no Home handshake,
 // CAKE authentication, installed actor, permission, signer or account-service result.
 private sealed class PresentationReadiness:ICuiSceneReadiness
 {public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready,"AccountPresentation","Account presentation can be loaded; sign-in and server checks remain required."));}}
 private Task PresentAsync(Action write)
 {
  if(closing)return Task.CompletedTask;
  return Dispatcher.UIThread.InvokeAsync(()=>{if(!closing)write();}).GetTask();
 }
 private async Task SignInAsync(CancellationToken ct){var original=session.SignInAsync(ct);await original.ConfigureAwait(true);}
 private void OnContextTick(object? sender,EventArgs e)
 {
  if(closing)return;
  try{adapter.RefreshPresentationFence();}
  catch(Exception error){NativeAccountUiCauses.Add(ownErrors,error);_ = CloseAndDrainAsync();}
 }
 private void OnClosing(object? sender,WindowClosingEventArgs e)
 {if(closed)return;e.Cancel=true;_ = CloseAndDrainAsync();} // Request only; coalesced task stays strongly held.
 internal void DemandExternalOriginalRetirementJoin()=>adapter.DemandExternalOriginalJoin();
 internal Task CloseAndDrainAsync()
 {
  Dispatcher.UIThread.VerifyAccess();DemandExternalOriginalRetirementJoin();if(close is not null)return close;
  var start=new TaskCompletionSource();closing=true;close=CloseCoreAsync(start.Task);start.SetResult();return close;
 }
 private async Task CloseCoreAsync(Task start)
 {
  await start.ConfigureAwait(true);List<Exception> errors=[];var originals=new List<Task>{Initialization};
  // Close is already published/sealed. Start SAME host stop before joining any initiating action.
  try{adapter.Seal();}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  try{originals.Add(host.CloseOriginalAsync());}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  try{originals.Add(binding.DisposeAsync().AsTask());}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  try{stop.Cancel();}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  try{clock.Stop();clock.Tick-=OnContextTick;}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  foreach(var original in binding.BrokerOwnedContinuations)if(!originals.Any(t=>ReferenceEquals(t,original)))originals.Add(original);
  foreach(var original in originals)await NativeAccountUiCauses.JoinOneAsync(original,errors);
  foreach(var original in ownErrors)NativeAccountUiCauses.Add(errors,original);
  try{adapter.Dispose();}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  try{stop.Dispose();}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  NativeAccountUiCauses.Throw(errors); // Unknown originals never authorize destructive native cleanup.
  Content=null;Closing-=OnClosing;closed=true;
  if(!App.IsOriginalDesktopRetiring)Close(); // Root owns final HWND closure during application retirement.
 }
}

#endif
