using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Home-scoped Computer Use authority for the authenticated host. The canonical invocation tokens
/// establish the eligible target, and only a grant for that exact target authorises dispatch.
/// Model text, tool descriptions and generic mode flags never create authority.
/// </summary>
public sealed class HomeComputerUseAdmission(IPermissionDecisionEngine engine) : IComputerUseAdmission
{
    public ValueTask<bool> AuthorizeAsync(ComputerUseRequest request, string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null || !request.HasExplicitEligibleTarget || string.IsNullOrWhiteSpace(toolName))
            return new ValueTask<bool>(false);

        var decision = engine.Evaluate(Scope(request.TargetAppId), RiskFor(toolName), requiresPermission: true,
            "Home must grant Computer Use for this exact canonical target before dispatch.");
        return new ValueTask<bool>(decision.Kind == PermissionDecisionKind.Allowed);
    }

    private static string Scope(string targetAppId) => "computeruse:" + targetAppId.Trim().ToLowerInvariant();

    private static CapabilityRiskClass RiskFor(string toolName) => toolName switch
    {
        "computer_snapshot" or "computer_list_windows" => CapabilityRiskClass.ReadOnly,
        "computer_close_window" => CapabilityRiskClass.Restricted,
        "computer_launch_app" or "computer_invoke" or "computer_click" or "computer_type" or "computer_press_key" => CapabilityRiskClass.Consequential,
        _ => CapabilityRiskClass.Restricted
    };
}
