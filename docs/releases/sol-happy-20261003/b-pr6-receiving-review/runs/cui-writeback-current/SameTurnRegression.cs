using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
var checks=0;
void Check(string name,bool condition){if(!condition)throw new InvalidOperationException("FAIL "+name);++checks;Console.WriteLine("PASS "+name);}
await using var session=HeadlessUnitTestSession.StartNew(typeof(WriteBackApplication));
await session.Dispatch(()=>{
 var model=new CuiViewModel();model.Set("Start","0");model.Set("Count",1d);
 using var loader=new CuiControlLoader();loader.SetBindingContext(model);loader.SetActionDispatcher(model);
 var document=new CuiRichParser().Parse("<Cui><StackPanel><TextBox id=\"start\" text=\"{Binding Start, mode=TwoWay, type=string}\"/><TextBox id=\"numeric\" text=\"{Binding Count, mode=TwoWay, type=number}\"/><Button id=\"capture\" action=\"Capture\" content=\"Capture latest native input\"/></StackPanel></Cui>");
 var (root,diagnostics)=loader.TryLoad(document);if(root is null||diagnostics.Any(x=>x.Severity==CuiDiagnosticSeverity.Error))throw new InvalidDataException("Actual CUI fixture failed to lower.");loader.WireBindings(root);
 var controls=root.GetLogicalDescendants().OfType<Control>().ToDictionary(x=>x.Name??Guid.NewGuid().ToString());
 var start=(TextBox)controls["start"];var numeric=(TextBox)controls["numeric"];var button=(Button)controls["capture"];
 var writes=0;model.PropertyChanged+=(_,args)=>{if(args.PropertyName=="Start")++writes;};
 string? captured=null;model.On("Capture",_=>{captured=(string?)model.Get("Start");loader.RefreshBindings();});
 Check("Initial actual owner binding lowers native Start",start.Text=="0"&&writes==0);
 ((IValueProvider)ControlAutomationPeer.CreatePeerForElement(start)).SetValue("0.5");
 Check("Actual native value provider sets textbox synchronously",start.Text=="0.5");
 ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(button)).Invoke();
 Console.WriteLine($"Same-turn actual owner action received Start={captured}; native Text={start.Text}.");
 Check("Same-turn actual CUI action receives latest native input",captured=="0.5");
 Check("Action-triggered actual host refresh preserves newest native input",start.Text=="0.5"&&writes==1);
 model.Set("Start","1.25");Check("Host binding refresh does not duplicate writeback",start.Text=="1.25"&&writes==2);
 var provider=(IValueProvider)ControlAutomationPeer.CreatePeerForElement(numeric);provider.SetValue("invalid");
 Check("Synchronous actual type validation rejects invalid numeric value",CuiInputValidationProperties.GetHasError(numeric)&&Equals(model.Get("Count"),1d));
 provider.SetValue("2.5");Check("Synchronous actual type conversion clears validation error",!CuiInputValidationProperties.GetHasError(numeric)&&Equals(model.Get("Count"),2.5d));
 loader.Dispose();((IValueProvider)ControlAutomationPeer.CreatePeerForElement(start)).SetValue("3.25");
 Check("Disposed owning loader stops native input writeback",Equals(model.Get("Start"),"1.25"));
},CancellationToken.None);
Console.WriteLine($"{checks}/8 actual native same-turn writeback checks PASS; owner proposal not accepted/shipped.");
public sealed class WriteBackApplication:Application{
 public static AppBuilder BuildAvaloniaApp()=>CuiNativeHost.ConfigureFonts(AppBuilder.Configure<WriteBackApplication>().UseSkia()).UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false});
 public override void Initialize()=>CuiNativeHost.InitialisePrimitiveTheme(this,"Home");
}
