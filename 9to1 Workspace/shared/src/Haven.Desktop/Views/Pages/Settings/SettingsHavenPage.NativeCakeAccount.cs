#if !ANDROID
using Avalonia.Controls;
using Haven.Desktop.Accounts;
using Haven.UI.Components;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Desktop.Views.Pages.Settings;
public sealed partial class SettingsHavenPage
{
 private Haven.UI.Components.Button? _nativeCakeAccountButton;
 private void InitializeNativeCakeAccount()
 {
  _nativeCakeAccountButton=new(){Name="Settings.CakeAccount",Content="CAKE ID account",Variant=ButtonVariant.Secondary};
  _nativeCakeAccountButton.Accessibility.AccessibleName="Open CAKE ID account sign-in, profile and sessions";
  _nativeCakeAccountButton.Invoked+=OnNativeCakeAccountInvoked;
  _route.Content.Add(_nativeCakeAccountButton);
 }
 private void OnNativeCakeAccountInvoked(object? sender,EventArgs e)
 {
  if(_disposed||App.IsOriginalDesktopRetiring)return;
  var owner=App.Services?.GetService<NativeCakeAccountUiOwner>()??throw new InvalidOperationException("Native account owner is unavailable");
  // Request-only callback. SAME Open task is published/retained by the app owner before native callbacks.
  _ = owner.OpenAsync(TopLevel.GetTopLevel(this) as Window);
 }
 private void DetachNativeCakeAccount()
 {if(_nativeCakeAccountButton is {} button)button.Invoked-=OnNativeCakeAccountInvoked;_nativeCakeAccountButton=null;}
}

#endif
