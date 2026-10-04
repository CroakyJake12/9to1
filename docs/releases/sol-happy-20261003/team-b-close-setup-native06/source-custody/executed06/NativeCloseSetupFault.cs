using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CakeOS.Cui.Runtime;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;
using NineToOne.Web;
Console.WriteLine("EXPECTED=1; actualApp=true; actualNativeControl=true; browser=NOT_RUN; ownerAuthority=NOT_RUN");
await using var native=HeadlessUnitTestSession.StartNew(typeof(CloseSetupApplication));
await native.Dispatch(async()=>{
 var app=new BrowserApplication();
 var view=(ContentControl)typeof(BrowserApplication).GetField("_view",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;
 var owner=new CloseSetupOwner();var model=new CloseSetupModel();
 if(!app.Surfaces.Register(owner,_=>new BrowserCuiSurface(new CuiRichParser().Parse("<Cui version=\"1\"><TextBlock text=\"retained prior view\" /></Cui>"),model,model)).Succeeded)throw new InvalidOperationException("fixture registration failure");
 var surface=new BrowserCuiSurface(new CuiRichParser().Parse("<Cui version=\"1\"><TextBlock text=\"retained prior view\" /></Cui>"),model,model);
 if(!(bool)typeof(BrowserApplication).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,[surface,"#/app.retained"])!)throw new InvalidOperationException("fixture Render failure");
 var previous=view.Content;var callbacks=0;
 void Fault(object? sender,AvaloniaPropertyChangedEventArgs change){if(change.Property==InputElement.IsEnabledProperty && !view.IsEnabled){++callbacks;throw new InvalidOperationException("actual initial native-disable listener fault");}}
 view.PropertyChanged+=Fault;
 try{await app.CloseAsync();throw new InvalidOperationException("required native listener failure did not occur");}
 catch(InvalidOperationException error)when(error.Message=="actual initial native-disable listener fault"){Console.WriteLine("PASS actual initial native-disable failure preserved");}
 finally{view.PropertyChanged-=Fault;}
 var closing=(bool)typeof(BrowserApplication).GetField("_closing",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;
 var completion=(TaskCompletionSource?)typeof(BrowserApplication).GetField("_closeCompletion",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app);
 Console.WriteLine($"WITNESS ownerRetained={app.Surfaces.AvailableRoutes.Contains(owner.RouteId)} ownerRevokes={owner.Revokes} ownerDisposes={owner.Disposes} priorContentRetained={ReferenceEquals(previous,view.Content)} callbacks={callbacks} closing={closing} completionPending={completion is not null&&!completion.Task.IsCompleted} enabled={view.IsEnabled}");
 if(!app.Surfaces.AvailableRoutes.Contains(owner.RouteId)||owner.Revokes!=0||owner.Disposes!=0||!ReferenceEquals(previous,view.Content)||!view.IsEnabled||callbacks!=1 || closing || completion is not null&&!completion.Task.IsCompleted)throw new InvalidOperationException("Close must settle closing/waiter after initial native disable failure");
 Console.WriteLine("RESULT expected=1 executed=1 passed=1 failed=0 assertions=2; browser=NOT_RUN");return 2;
},CancellationToken.None);
public sealed class CloseSetupApplication:Application{
 public static AppBuilder BuildAvaloniaApp()=>CuiNativeHost.ConfigureFonts(AppBuilder.Configure<CloseSetupApplication>().UseSkia()).UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false});
 public override void Initialize(){CuiNativeHost.InitialisePrimitiveTheme(this,"Home");}
}

internal sealed class CloseSetupOwner:IHomeFeatureRouteHandler,IBrowserPrivateContextParticipant{
 public string RouteId=>"app.retained";internal int Revokes,Disposes;
 public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,CancellationToken token=default)=>throw new InvalidOperationException("unexpected owner command");
 public void RevokePrivateContext(){++Revokes;}public ValueTask DisposeAsync(){++Disposes;return ValueTask.CompletedTask;}
}
internal sealed class CloseSetupModel:ICuiBindingContext,ICuiActionDispatcher{
 public bool TryGetValue(string path,out object? value){value=null;return false;}
 public ValueTask DispatchAsync(string action,object? argument,CancellationToken token=default)=>ValueTask.CompletedTask;
}
