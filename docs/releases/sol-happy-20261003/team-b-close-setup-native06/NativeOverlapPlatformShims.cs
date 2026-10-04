namespace NineToOne.Web;
// Scripted observer counts, no fake registered service/transport/domain view.
internal sealed class OverlapBootstrapBoundary:Exception{}
internal static class Program
{
    internal static void ShowStatus(string code,string message){}
    internal static void Attach(BrowserApplication app)=>throw new OverlapBootstrapBoundary();
    internal static void WriteFragment(string fragment,bool replace)=>throw new OverlapBootstrapBoundary();
    internal static string ReadFragment()=>throw new OverlapBootstrapBoundary();
}
internal static class BrowserFeatureComposition
{
    internal static int Registrations;
    internal static void Register(BrowserSurfaceRegistry registry)=>throw new OverlapBootstrapBoundary();
    internal static void RegisterPrivateAccountSettings(BrowserSurfaceRegistry registry){++Registrations;}
}
