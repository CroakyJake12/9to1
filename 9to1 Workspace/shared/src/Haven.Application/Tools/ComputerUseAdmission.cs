using System.Text.Json;
using NineToOne.Cui.AI;

namespace Haven.Application;

public sealed record ComputerUseRequest(string RequestId, string TargetAppId, IReadOnlyList<InvocationToken> Invocations)
{
    public bool HasExplicitEligibleTarget => !string.IsNullOrWhiteSpace(RequestId) && !string.IsNullOrWhiteSpace(TargetAppId) &&
        Invocations is { Count: > 0 and <= 256 } && Invocations.All(token => token is not null && token.Resource is not null &&
            !string.IsNullOrWhiteSpace(token.TokenId) && !string.IsNullOrWhiteSpace(token.Resource.CanonicalId)) &&
        Invocations.Select(token => token.TokenId).Distinct(StringComparer.Ordinal).Count() == Invocations.Count &&
        Invocations.Count(token => token.Resource.Kind == InvocationKind.System &&
            token.Resource.CanonicalId == InvocationCompose.ComputerUseCapabilityId &&
            Guid.TryParse(token.ComputerUseInvocationId, out _)) == 1 &&
        Invocations.Count(token => token.Resource.Kind == InvocationKind.App && token.Resource.CanonicalId == TargetAppId &&
            !string.IsNullOrWhiteSpace(token.Resource.Revision) &&
            token.Resource.Available && token.Resource.Classification == AppClassification.OrdinaryApplication &&
            token.Resource.InteractionPath is AppInteractionPath.ComputerUseRequired or AppInteractionPath.TypedApiAndComputerUse) == 1;
}

/// <summary>Authenticated Home authority revalidates the canonical target and authorises each concrete action.</summary>
public interface IComputerUseAdmission
{
    ValueTask<bool> AuthorizeAsync(ComputerUseRequest request, string toolName, JsonElement arguments, CancellationToken cancellationToken);
}
