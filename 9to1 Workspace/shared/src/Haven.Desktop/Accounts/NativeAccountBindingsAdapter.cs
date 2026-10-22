#if !ANDROID
using System.ComponentModel;
using Haven.Application;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Accounts.Native;
using NineToOne.Web.Accounts;
namespace Haven.Desktop.Accounts;

/// <summary>Read-only native view adapter over the existing platform-neutral binding owner.
/// Snapshot fences are privacy observations; the Worker remains the account/session authority.</summary>
internal sealed class NativeAccountBindingsAdapter:ICuiWritableBindingContext,ICuiRepeatItemBindingContext,
 ICuiLifetimeAwareActionDispatcher,ICuiActionAvailability,INotifyPropertyChanged,IDisposable
{
 private readonly AccountBrowserBindings binding;private readonly INativeCakeAccountSession session;
 private readonly CancellationToken ownerStop;private bool sealedOwner;private Guid? visibleSession;
 public event PropertyChangedEventHandler? PropertyChanged;
 internal NativeAccountBindingsAdapter(AccountBrowserBindings original,INativeCakeAccountSession originalSession,CancellationToken originalOwnerStop)
 {binding=original;session=originalSession;ownerStop=originalOwnerStop;binding.PropertyChanged+=Changed;}
 internal void DemandExternalOriginalJoin()=>CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
 internal void Seal(){sealedOwner=true;visibleSession=null;}
 internal void RefreshPresentationFence()
 {
  if(sealedOwner)return;
  if(visibleSession is {} old&&(!session.TryGetCurrentSnapshot(out var snapshot)||snapshot?.SessionId!=old))
  {visibleSession=null;binding.ClearAccountObservations("The account context changed. Sign in or refresh using the current session.");PropertyChanged?.Invoke(this,new(null));}
 }
 private bool Visible()=>!sealedOwner&&session.IsAvailable&&visibleSession is {} id&&session.TryGetCurrentSnapshot(out var s)&&s?.SessionId==id;
 private bool CanSignIn()=>!sealedOwner&&session.IsAvailable&&binding.IsActionAvailable("RequestSignIn")==true;
 private bool HasObservedAccount()=>Visible()&&binding.TryGetValue("HasAccount",out var observed)&&observed is true;
 public bool TryGetValue(string path,out object? value)
 {
  value=null;
  if(path is "CanEdit" or "CanSave" or "HasChanges" or "Conflict"){value=false;return true;}
  if(path=="CanSignIn"){value=CanSignIn();return true;}
  if(path=="UnsupportedServices"){value="Profile edits are unavailable for this native client. Plan, billing, username availability and icon upload are not connected.";return true;}
  if(path=="Status"&&!session.IsAvailable){value="Native CAKE sign-in is unavailable until its genuine owner Access transport is configured.";return true;}
  if(path is "ConnectionSummary" or "ConnectionRecovery")
  {
   // Sealed/retired owner never reports a connected account.
   if(sealedOwner){value=path=="ConnectionSummary"?"This native account session has been retired.":"The retired session is closed. Start a new session to manage your account again.";return true;}
   // Unavailable host/auth service: primary visible guidance, never a nonexistent button.
   if(!session.IsAvailable){value=path=="ConnectionSummary"?"Native CAKE sign-in is unavailable.":"This native client's account owner Access transport is not configured. You can keep using local apps and retry later.";return true;}
   // Native checks are read-only, including while the browser binding retains its last
   // observation. Never inherit the browser editor's promise that profile changes are saved.
   if(binding.TryGetValue("Busy",out var busyV)&&busyV is true)
   {value=path=="ConnectionSummary"?"Checking your account…":"This native client is read-only. Account details are shown only for the checked current session; profile edits cannot be saved.";return true;}
   // A matching native session alone does not prove that the account service was observed.
   if(HasObservedAccount()){value=path=="ConnectionSummary"?"Your 9to1 account is connected (read-only).":"This native client is read-only: profile edits cannot be saved. Observed account and session information is shown without modification.";return true;}
   // Guidance must name only an action that this actual host can offer.
   value=CanSignIn()
    ?path=="ConnectionSummary"?"Sign in to manage your profile and sessions.":"Use Sign in to connect with your genuine account session."
    :path=="ConnectionSummary"?"This native client is not connected to your 9to1 account.":"Sign-in is not available in this session. You can keep using local apps and retry later.";
   return true;
  }
  if(path is "HasAccount" or "HasProfile" or "CanSessionActions" or "HasConfirmation" or "CanConfirm")
  {if(!Visible()){value=false;return true;}}
  if(path is "AccountId" or "DisplayName" or "ProfileRevision" or "ConflictRevision" or "Sessions" or "Confirmation" or "SessionSummary"||path.StartsWith("Draft.",StringComparison.Ordinal))
  {if(!Visible()){value=path=="Sessions"?Array.Empty<JsonElement>():"";return true;}}
  if(sealedOwner){value=path=="Busy"?false:path=="CanRefresh"?false:null;return value is not null;}
  return binding.TryGetValue(path,out value);
 }
 public bool TrySetValue(string path,object? value)=>false;
 public bool TryGetItemValue(object item,string path,out object? value){value=null;return Visible()&&binding.TryGetItemValue(item,path,out value);}
 public bool TrySetItemValue(object item,string path,object? value)=>false;
 public bool? IsActionAvailable(string command)
 {
  if(sealedOwner||command is "SaveProfile" or "ClearIcon" or "ClearPronouns" or "ClearJob")return false;
  if(command=="RequestSignIn"&&!session.IsAvailable)return false;
  if((command is "SelectSession" or "RequestSignOut" or "RequestRevokeOthers" or "ConfirmSessionMutation" or "CancelSessionMutation")&&!Visible())return false;
  return binding.IsActionAvailable(command);
 }
 public ValueTask DispatchAsync(string command,object? parameter,CancellationToken ct=default)=>new(DispatchCoreAsync(command,parameter,new(default,ct)));
 public ValueTask DispatchWithLifetimeAsync(string command,object? parameter,CuiActionDispatchLifetime lifetime)=>new(DispatchCoreAsync(command,parameter,lifetime));
 private async Task DispatchCoreAsync(string command,object? parameter,CuiActionDispatchLifetime lifetime)
 {
  using var originalScope=CloudflareOriginalExecutionGuard.EnterOriginal(this);
  if(IsActionAvailable(command)!=true)return;
  List<Exception> errors=[];CancellationTokenSource? caller=null;
  try
  {
   caller=CancellationTokenSource.CreateLinkedTokenSource(lifetime.CallerCancellation,ownerStop);caller.Token.ThrowIfCancellationRequested();
   if(command=="RequestSignIn"){visibleSession=null;binding.ClearAccountObservations("The account context changed. Sign in or refresh using the current session.");}
   // For confirmation the binding must preserve its captured selected operation: its own
   // MutateSessionAsync clears observations before transport. Do not recapture wire state.
   var supplied=binding.DispatchWithLifetimeAsync(command,parameter,new(lifetime.ViewCancellation,caller.Token));
   var actual=supplied.AsTask();await NativeAccountUiCauses.JoinOneAsync(actual,errors);
   if(errors.Count==0&&command=="RequestSignIn"&&!sealedOwner)
   {
    caller.Token.ThrowIfCancellationRequested();
    var refresh=binding.DispatchAsync("Refresh",null,caller.Token).AsTask();await NativeAccountUiCauses.JoinOneAsync(refresh,errors);
   }
   if(errors.Count==0&&!sealedOwner&&(command is "Refresh" or "DiscardAndReload" or "RequestSignIn")&&session.TryGetCurrentSnapshot(out var snapshot)&&snapshot is not null)
    visibleSession=snapshot.SessionId;
   if(!sealedOwner)PropertyChanged?.Invoke(this,new(null));
  }
  catch(Exception error){NativeAccountUiCauses.Add(errors,error);}
  finally{try{caller?.Dispose();}catch(Exception error){NativeAccountUiCauses.Add(errors,error);}}
  NativeAccountUiCauses.Throw(errors);
 }
 private void Changed(object? sender,PropertyChangedEventArgs e){if(!sealedOwner)PropertyChanged?.Invoke(this,e);}
 public void Dispose(){Seal();binding.PropertyChanged-=Changed;PropertyChanged=null;}
}

#endif
