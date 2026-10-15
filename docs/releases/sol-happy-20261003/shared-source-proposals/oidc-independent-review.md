# Independent C1 OIDC proposal review

Reviewed immutable commit 542aca10f0bc93e77b5a70b0ced0d6d9ad68376a against parent a63d77fe5a9dfea56c938eaa85678a9e170368ec. Every changed line in all eight files reviewed alongside originating flow, resource consumer, configured Web composition, HTTP exchange, and retained Worker resource-api.ts/auth.ts. No actionable patch regression found.

Configured ResourceAudience is captured in the immutable originating flow and emitted unchanged at authorization and exchange. Returned dictionary mutation cannot alter it. Legacy generic constructor remains compatible. API audience containment and ID-token client audience/multi-audience azp checks are preserved, as are nonce/state/S256, callback single-use, actor/browser generation, expiry/admission, approved discovery/JWKS/key/algorithm policy, private bearer handling and no ambiguous exchange retry.

Profile GET/PATCH unwrap only the actual profile object; sessions unwrap only the actual sessions array. Current account stays direct and successful revocations require 204. Account matching, patch revision+1, session ownership/unique nonempty IDs, scope verification, bounded 64KiB/64-depth parsing, denial and uncertain mutation behavior remain. Null session elements now fail safely. Duplicate envelope properties fail. Malformed JSON read expectation Unavailable agrees with unchanged JsonException catch; mutation CompletionUnknown is preserved. Existing assertions were retained while fixtures corrected actual protocol shape.

Independent execution on clean detached exact commit /workspace/team-c/c1-review-oidc, SDK 10.0.401, isolated outputs:

- DOTNET_CLI_HOME=/tmp/c1-oidc-cli DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 /workspace/.tools/dotnet/dotnet run --project '9to1 Workspace/Accounts/Remote/Tests/NineToOne.Accounts.Remote.Specs.csproj' --artifacts-path /tmp/c1-oidc-remote-artifacts: exit 0; remote.log reports zero-skips synthetic suite and new protocol checks.
- Same environment, dotnet run --project '9to1 Workspace/Web/Tests/Oidc/Web.Oidc.Specs.csproj' --artifacts-path /tmp/c1-oidc-web-artifacts: exit 0; web.log reports production host controlled specs passed.

These execute actual selected .NET source with controlled transport and synthetic signed keys, not the real Worker issuer journey. Actual HTTPS browser/issuer/code exchange, genuine auth_revision producer contract, approved production signing algorithm/key compatibility, full host and deployed provider acceptance remain BLOCKED/UNVERIFIED. Selected normal-30 owner bodies remain a distinct unverified scope. No production configuration, account, credentials or source was changed by C1. Coordinator owns integration decision.
