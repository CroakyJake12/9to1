namespace NineToOne.Cui.AI;

/// <summary>One shared Home/Dulche service composition; each host supplies its semantic context and typed actions.</summary>
public interface IAppAiCoordinatorFactory
{
    FloatingAiBarState Create(IAppAiContext context, IAppAiActions actions, IAppAiDatabaseMutationGuard? databaseGuard = null);
}

public sealed class AppAiCoordinatorFactory(
    IAppAiApprovalVerifier approvals,
    IDulcheAppClient dulche,
    IAppAiApprovalRequester approvalRequester,
    IAppAiActionGraph actionGraph,
    IAppAiModelPicker modelPicker,
    IInvocationResolver? invocationResolver = null) : IAppAiCoordinatorFactory
{
    public FloatingAiBarState Create(IAppAiContext context, IAppAiActions actions, IAppAiDatabaseMutationGuard? databaseGuard = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        return new(new AppAiCoordinator(context, actions, approvals, dulche, approvalRequester, databaseGuard, actionGraph, modelPicker, invocationResolver));
    }
}
