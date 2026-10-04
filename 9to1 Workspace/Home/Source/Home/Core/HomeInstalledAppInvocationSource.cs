using Haven.Application;
using Haven.Core;
using NineToOne.Cui.AI;

namespace HavenOS.Home.Core;

/// <summary>Only currently available, confidently classified native entrypoints enter the shared invocation catalogue.</summary>
public sealed class HomeInstalledAppInvocationSource(IInstalledApplicationRegistry registry) : IHomeInvocationResourceSource
{
    public async ValueTask<IReadOnlyList<InvocationResource>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var entries = await registry.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return entries.Where(app => app.Enabled && app.ProfileAccessible &&
            app.Operability.Classification == AppOperabilityClassification.OrdinaryApplication &&
            (app.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || app.ApplicationId.ToString("D").Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Select(app => new InvocationResource(InvocationKind.App, app.ApplicationId.ToString("D"), app.Label,
                app.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), "app",
                app.Operability.Path switch
                {
                    AppOperabilityPath.TypedApi => AppInteractionPath.TypedApi,
                    AppOperabilityPath.ComputerUseRequired => AppInteractionPath.ComputerUseRequired,
                    AppOperabilityPath.TypedApiAndComputerUse => AppInteractionPath.TypedApiAndComputerUse,
                    _ => null
                }, AppClassification.OrdinaryApplication)).Where(InvocationCompose.IsEligible).ToArray();
    }
}
