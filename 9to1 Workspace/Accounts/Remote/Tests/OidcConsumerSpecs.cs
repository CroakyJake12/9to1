namespace NineToOne.Accounts.Oidc;
internal static class OidcConsumerSpecs
{
    public static async Task RunAsync()
    {
        var now=DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var flow=new OidcOriginatingFlow("https://issuer.example.invalid","fictional-client","https://client.example.invalid/callback","original-actor",now.AddMinutes(5));
        var parameters=flow.AuthorizationParameters();
        Require(parameters["code_challenge_method"]=="S256"&&parameters["response_type"]=="code");
        Require(flow.ConsumeCallback(parameters["state"],parameters["redirect_uri"],"fictional-code","changed-actor",now) is null);
        var exchange=flow.ConsumeCallback(parameters["state"],parameters["redirect_uri"],"fictional-code","original-actor",now);
        Require(exchange is not null&&exchange["grant_type"]=="authorization_code"&&!exchange.ContainsKey("ClientID"));
        Require(flow.ConsumeCallback(parameters["state"],parameters["redirect_uri"],"fictional-code","original-actor",now) is null);
        var id=new VerifiedToken("https://issuer.example.invalid","550e8400-e29b-41d4-a716-446655440000",new HashSet<string>{"fictional-client"},now.AddMinutes(10),now,parameters["nonce"],null,null,null,new HashSet<string>(),now);
        Require(flow.AcceptIdToken(id,"original-actor",now));
        Require(!flow.AcceptIdToken(id,"changed-actor",now));
        Require(!flow.AcceptIdToken(id,"original-actor",now.AddMinutes(6)));
        flow.Cancel();Require(!flow.AcceptIdToken(id,"original-actor",now));
        var resource="https://worker.example.invalid/api";
        var resourceFlow=new OidcOriginatingFlow("https://issuer.example.invalid","fictional-client","https://client.example.invalid/callback","original-actor",now.AddMinutes(5),resource);
        var resourceParameters=resourceFlow.AuthorizationParameters();
        Require(resourceParameters["resource"]==resource);
        // Returned parameter dictionaries are untrusted copies, not the originating authority.
        ((Dictionary<string,string>)resourceParameters)["resource"]="https://foreign.example.invalid/api";
        Require(resourceFlow.AuthorizationParameters()["resource"]==resource);
        var resourceExchange=resourceFlow.ConsumeCallback(resourceParameters["state"],resourceParameters["redirect_uri"],"fictional-code","original-actor",now);
        Require(resourceExchange is not null&&resourceExchange["resource"]==resource&&resourceExchange["client_id"]=="fictional-client");
        Require(resourceFlow.ConsumeCallback(resourceParameters["state"],resourceParameters["redirect_uri"],"fictional-code","original-actor",now) is null);
        Console.WriteLine("PASS C3_OIDC_PROTOCOL_IMMUTABLE_RESOURCE_AUTHORIZATION_AND_EXCHANGE");
        var policy=new TokenPolicy("https://issuer.example.invalid","fictional-resource",TokenPurpose.ApiAccessToken,null,new HashSet<string>{"cake:account:read"},new HashSet<string>{"RS256"});
        Require(await new OidcResourceConsumer(new UnavailableIssuerTokenReader()).ObserveAsync("fictional",policy,now,default) is null);
        // Controlled verified-reader boundary, NOT actual JWT/JWKS/issuer verification.
        var claims=new VerifiedToken(policy.Issuer,"550e8400-e29b-41d4-a716-446655440000",new HashSet<string>{policy.Audience},now.AddMinutes(1),now,null,null,"fictional-session","server-revision",policy.RequiredScopes,now);
        Require(await new OidcResourceConsumer(new FixtureReader(claims)).ObserveAsync("fictional",policy,now,default) is not null);
        Require(await new OidcResourceConsumer(new FixtureReader(claims)).ObserveAsync("fictional",policy with {Audience="other"},now,default) is null);
        Require(await new OidcResourceConsumer(new FixtureReader(claims)).ObserveAsync("fictional",policy,now.AddMinutes(2),default) is null);
        Require(await new OidcResourceConsumer(new FixtureReader(claims)).ObserveAsync("fictional",policy with {Purpose=TokenPurpose.IdToken},now,default) is null);
    }
    private sealed class FixtureReader(VerifiedToken token):IVerifiedIssuerTokenReader
    { public ValueTask<VerifiedToken?> VerifyAsync(string raw,TokenPolicy policy,CancellationToken ct)=>ValueTask.FromResult<VerifiedToken?>(token); }
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Controlled OIDC fixture assertion");}
}
