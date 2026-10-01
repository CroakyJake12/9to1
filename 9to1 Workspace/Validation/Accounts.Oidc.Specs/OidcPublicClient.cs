using System.Security.Cryptography;
using System.Text;
namespace NineToOne.Accounts.Oidc;

// Private originating flow owns state/nonce/verifier; never serialize this as an authority grant.
public sealed class OidcOriginatingFlow
{
    private readonly string state=Random(), nonce=Random(), verifier=Random();
    private readonly string issuer,clientId,redirectUri, originalActorRevision;
    private readonly DateTimeOffset expiresAt;
    private int consumed; private int canceled;
    public void Cancel()=>Interlocked.Exchange(ref canceled,1);
    public OidcOriginatingFlow(string issuer,string clientId,string redirectUri,string originalActorRevision,DateTimeOffset expiresAt)
    { if(!Uri.TryCreate(issuer,UriKind.Absolute,out var uri)||uri.Scheme!="https"||string.IsNullOrWhiteSpace(clientId)
      ||string.IsNullOrWhiteSpace(originalActorRevision))throw new ArgumentException("Invalid originating configuration");
      this.issuer=issuer;this.clientId=clientId;this.redirectUri=redirectUri;this.originalActorRevision=originalActorRevision;this.expiresAt=expiresAt; }
    public IReadOnlyDictionary<string,string> AuthorizationParameters() => new Dictionary<string,string>
    { ["client_id"]=clientId,["redirect_uri"]=redirectUri,["response_type"]="code",["scope"]="openid profile",
      ["state"]=state,["nonce"]=nonce,["code_challenge_method"]="S256",
      ["code_challenge"]=Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) };
    public IReadOnlyDictionary<string,string>? ConsumeCallback(string returnedState,string exactRedirect,string code,
        string currentActorRevision,DateTimeOffset now)
    {
        if(canceled!=0||now>=expiresAt||currentActorRevision!=originalActorRevision||exactRedirect!=redirectUri
            ||returnedState!=state||string.IsNullOrWhiteSpace(code)||code.Length>4096)return null;
        if(Interlocked.CompareExchange(ref consumed,1,0)!=0)return null;
        return new Dictionary<string,string>{["grant_type"]="authorization_code",["client_id"]=clientId,
          ["redirect_uri"]=redirectUri,["code"]=code,["code_verifier"]=verifier};
    }
    public bool AcceptIdToken(VerifiedToken token,string currentActorRevision,DateTimeOffset now) => canceled==0&&now<expiresAt&&currentActorRevision==originalActorRevision&&consumed==1&&token.Issuer==issuer
        &&Guid.TryParseExact(token.Subject,"D",out var subject)&&subject!=Guid.Empty&&subject.ToString("D")==token.Subject
        &&token.IssuedAt is not null&&token.IssuedAt<=now&&token.IssuedAt<token.ExpiresAt&&token.Audiences.Contains(clientId)&&token.Nonce==nonce&&token.ExpiresAt>now&&token.NotBefore<=now
        &&(token.Audiences.Count==1 ? token.AuthorizedParty is null||token.AuthorizedParty==clientId : token.AuthorizedParty==clientId);
    private static string Random()=>Encode(RandomNumberGenerator.GetBytes(32));
    private static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
}
