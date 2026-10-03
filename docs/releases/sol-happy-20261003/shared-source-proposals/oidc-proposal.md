# Proposed OIDC public-client protocol compatibility patch

This isolated proposal is based on a63d77fe5a9dfea56c938eaa85678a9e170368ec.
Team A acknowledged Team C ownership of these bounded client-protocol corrections.
It is not a new identity authority or a verified production login integration.

The existing explicitly operator-configured `ResourceAudience` is carried as the
RFC 8707 `resource` at authorization and code exchange. Both values come from the
private immutable originating flow, never callback/browser request fields. The
legacy generic originating constructor is retained; the Web composition always
passes its required configured resource. Client ID still supplies ID-token
audience, while API verification still uses resource audience. Issuer/discovery/
JWKS, algorithm/key policy, nonce, state, S256 verifier, scopes, sid,
auth_revision, one-use callback, private bearer cache, browser generation and
admission checks are preserved. No defaults or new claims are introduced.

The retained hosted Worker `cloud/cake-id-auth/src/resource-api.ts` responds with
`{profile:{...}}` for profile GET/PATCH and `{sessions:[...]}` for session listing;
current account remains direct JSON and revocation returns 204. The existing
client now unwraps only those actual envelopes and retains byte/depth limits,
account ownership, unique nonempty session IDs, revision+1, scope and ambiguous
mutation checks. Missing/null/wrong-shape/duplicate envelopes fail safely.

Existing controlled tests incorrectly supplied flattened profile/session bodies.
Their response fixtures now mirror the retained Worker envelope. Assertions and
security checks are retained; new protocol negatives strengthen them. Tests use
actual maintained client/host types with isolated controlled HTTP and signed test
keys, not a replacement client. This does not establish an actual issuer journey.

Before production changes, envelope fixtures failed at the session assertion and
the Web signed flow failed because authorization omitted `resource`. Raw failures
are retained in Team C evidence. Updated tests check both requests and preserve
PKCE/nonce/code binding plus immutable resource under returned-dictionary mutation.
Malformed/missing/null/wrong-shape/foreign envelopes, null/duplicate/empty sessions,
wrong patch revision, denied tokens, redirects and uncertain mutations stay denied.

Actual configured HTTPS issuer/browser/code exchange remains BLOCKED and explicitly
UNVERIFIED. Production RSA/algorithm policy and genuine auth_revision contracts
remain OPEN. Controlled signed fixture claims are labelled synthetic test evidence;
they are not fabricated production auth_revision. Deployment/merchant/session
configuration is unchanged. The owning team and independent coordinator must
review this exact proposed patch and exercise a genuinely configured issuer
before integration or product acceptance.
