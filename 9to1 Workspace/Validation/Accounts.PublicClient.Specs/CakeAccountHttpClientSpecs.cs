using System.Net;
using System.Text;
using NineToOne.Accounts;

// Owning fixture-only tests; internal handler constructor must be compiled in same
// Accounts assembly or explicitly owning friend testassembly after metadata review.
static class CakeAccountHttpClientSpecs
{
    public static async Task RunAsync()
    {
        var handler=new FixtureHandler();using var client=new CakeAccountHttpClient(new Uri("https://fixture.invalid/"),handler);
        var token=await client.ExchangeAsync(new("fixture-code","desktop","http://127.0.0.1:48111/callback",new string('a',64),"fixture"));
        if(token.AccessToken!="fictional-bearer" || token.TokenType!="Bearer" || token.ExpiresIn!=900)throw new Exception("actual wire token response not preserved");
        var profile=await client.GetCurrentAsync(token.AccessToken);
        if(profile.AccountID!=handler.Account || handler.LastToken!=token.AccessToken)throw new Exception("authenticated HTTP request was not bound");
        handler.Failure=HttpStatusCode.Unauthorized;
        try { await client.GetCurrentAsync(token.AccessToken);throw new Exception("unauthenticated response became local identity"); }
        catch(CakeAccountTransportException e) when(e.Code=="Unauthenticated" && !e.OperationMayHaveApplied) { }
        handler.Failure=HttpStatusCode.InternalServerError;var before=handler.Calls;
        try { await client.ExchangeAsync(new("fixture-code","desktop","http://127.0.0.1:48111/callback",new string('a',64),"fixture"));throw new Exception("unknown exchange completion accepted"); }
        catch(CakeAccountTransportException e) when(e.OperationMayHaveApplied) { }
        if(handler.Calls!=before+1)throw new Exception("mutation automatically retried");
        handler.Failure=HttpStatusCode.TemporaryRedirect;before=handler.Calls;
        try { await client.SignOutAsync(token.AccessToken);throw new Exception("redirect implies rollback"); }
        catch(CakeAccountTransportException e) when(e.OperationMayHaveApplied) { }
        if(handler.Calls!=before+1)throw new Exception("unexpected redirect replay");
        handler.Failure=null;handler.LargeBody=true;
        try { await client.GetCurrentAsync(token.AccessToken);throw new Exception("oversized response parsed"); }
        catch(CakeAccountTransportException e) when(e.Code=="ResponseTooLarge" && !e.OperationMayHaveApplied) { }
        try { await client.ExchangeAsync(new("fixture-code","desktop","http://127.0.0.1:48111/callback",new string('a',64),"fixture"));throw new Exception("oversized exchange implies rollback"); }
        catch(CakeAccountTransportException e) when(e.Code=="ResponseTooLarge" && e.OperationMayHaveApplied) { }
        foreach(var lying in new[]{false,true})
        {
            handler.UnknownLength=true;handler.LyingLength=lying;
            try { await client.GetCurrentAsync(token.AccessToken);throw new Exception("unknown/lying oversized stream parsed"); }
            catch(CakeAccountTransportException e) when(e.Code=="ResponseTooLarge" && !e.OperationMayHaveApplied) { }
            if(handler.LastStreamingContent is null || handler.LastStreamingContent.Opened!=1)throw new Exception("streaming bound was not exercised");
        }
        try { using var invalid=new CakeAccountHttpClient(new Uri("http://production.invalid/"));throw new Exception("insecure backend admitted"); }
        catch(ArgumentException) { }
    }
    private sealed class NoLengthContent(byte[] bytes):HttpContent
    {
        public int Opened;
        protected override bool TryComputeLength(out long length) { length=0;return false; }
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)
            =>stream.WriteAsync(bytes.AsMemory()).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync()
        { Opened++;return Task.FromResult<Stream>(new MemoryStream(bytes,writable:false)); }
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested();return CreateContentReadStreamAsync(); }
    }
    private sealed class FixtureHandler:HttpMessageHandler
    {
        public Guid Account { get; }=Guid.NewGuid();public int Calls;public string? LastToken;public HttpStatusCode? Failure;public bool LargeBody;public bool UnknownLength;public bool LyingLength;public NoLengthContent? LastStreamingContent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;LastToken=request.Headers.Authorization?.Parameter;
            if(request.RequestUri!.Host!="fixture.invalid")throw new Exception("backend origin changed");
            var status=Failure??HttpStatusCode.OK;
            var json=request.RequestUri.AbsolutePath=="/oauth/token"?
                "{\"access_token\":\"fictional-bearer\",\"token_type\":\"Bearer\",\"expires_in\":900}":
                System.Text.Json.JsonSerializer.Serialize(new CakeProfile(Account,"Explicit fixture"));
            if(LargeBody)json=new string('x',1_048_577);
            HttpContent content;
            if(UnknownLength)
            {
                LastStreamingContent=new NoLengthContent(Encoding.UTF8.GetBytes(json));content=LastStreamingContent;
                if(LyingLength)content.Headers.ContentLength=1;
            }
            else content=new StringContent(json,Encoding.UTF8,"application/json");
            return Task.FromResult(new HttpResponseMessage(status){Content=content});
        }
    }
}
