#if !ANDROID
using Haven.Infrastructure.Automations;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private void ConfigureOriginalAutomationShippingRegistrations(IServiceCollection collection, HomeNativeWindowsComposition home)
    {
        // Real production caller for the durable Automations shipping composition: the SAME actual Home tuple
        // and the canonical repository/registered shared registry. This is the caller AddAutomationShipping
        // previously lacked. The Home run/effect resolver and action catalogue are wired into the composition
        // in ConfigureOriginalWindowsHomeRegistrations; here we register the authority, the canonical claim/
        // continuation/graph collaborators and the durable adapter into the shared registry adapter set.
        collection.AddAutomationShipping(home);
    }
}
#endif
