using System.Text.Json;
using Haven.Application;
using NineToOne.Cui.AI;

namespace Haven.Core.Tests;

internal sealed class TestComputerUseAdmission : IComputerUseAdmission
{
    public ValueTask<bool> AuthorizeAsync(ComputerUseRequest request, string toolName, JsonElement arguments, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.HasExplicitEligibleTarget);

    public static ComputerUseRequest Request()
    {
        var compose = new InvocationCompose();
        compose.Insert(InvocationCompose.ComputerUse, 0);
        compose.Insert(new(InvocationKind.App, "fixture.native-app", "Fixture native app", "revision-1",
            InteractionPath: AppInteractionPath.ComputerUseRequired, Classification: AppClassification.OrdinaryApplication), compose.Text.Length);
        return new(Guid.NewGuid().ToString("N"), "fixture.native-app", compose.Tokens);
    }

    public static ComputerToolRuntime Runtime(IComputerToolService tools, IComputerUseSessionController? sessions = null)
        => new(tools, sessions ?? new ComputerUseSessionController(), new TestComputerUseAdmission());
}
