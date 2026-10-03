using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual canonical DI source, without a synthetic Agent grant or new Home lease.
/// Full accepted-socket, Files receipt, selected Den and native shutdown journeys are separate.</summary>
public sealed class HomeOriginalAgentDiTests
{
    [Fact]
    public async Task Canonical_registration_resolves_one_same_admission_for_host_Chat_and_owning_dispatchers()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddHavenOriginalAgentExecution();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Exception? primary = null; List<Exception> errors = [];
        try
        {
            var host = provider.GetRequiredService<HomeAgentExecutionHost>();
            Assert.Same(host, provider.GetRequiredService<IChatExecutionAdmission>());
            Assert.Same(host, provider.GetRequiredService<IHomeAgentExecutionHost>());
            Assert.NotNull(provider.GetRequiredService<ChatSessionService>());
            Assert.NotNull(provider.GetRequiredService<AgentTaskRuntimeService>());
            Assert.NotNull(provider.GetRequiredService<IHomeAgentExecutionSessionComposer>());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.GetRequiredService<IChatExecutionAdmission>()
                .GetOriginalLifetimeAsync(new object(), CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.GetRequiredService<IChatExecutionAdmission>()
                .DemandOriginalCommitCurrentAsync(new object(), CancellationToken.None).AsTask());
            var close = host.CloseAndDrainAsync();
            Assert.Same(close, host.CloseAndDrainAsync());
            await close;
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { await provider.DisposeAsync(); }
            catch (Exception error) { if (!ReferenceEquals(primary, error)) errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual DI original work and provider cleanup failed.",
            primary is null ? errors : new[] { primary }.Concat(errors));
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }
}
