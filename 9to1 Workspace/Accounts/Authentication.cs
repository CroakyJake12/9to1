using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NineToOne.Accounts;

public sealed record CakeProfile(Guid AccountID, string DisplayName);
public sealed record CakeSession(Guid SessionID, Guid AccountID, string DeviceName, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, string AccessTokenHash,string? RegisteredClientID=null);
public sealed record AuthorizationCode(string CodeHash, Guid AccountID, string ClientID, string RedirectURI,
    string Challenge, DateTimeOffset ExpiresAt);
public sealed record IssuedSession(string AccessToken, CakeSession Session);
public sealed record AuthenticationState(IReadOnlyList<CakeProfile> Profiles, IReadOnlyList<CakeSession> Sessions,
    IReadOnlyList<AuthorizationCode> Codes);

/// <summary>Public clients use one-time PKCE codes. Account identity is supplied only by a trusted login flow.</summary>
public sealed class CakeIdentityService
{
    private readonly object gate = new();
    private readonly string statePath;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> registeredClients;
    public CakeIdentityService(string statePath, IReadOnlyDictionary<string, IReadOnlySet<string>> registeredClients)
    {
        this.statePath = Path.GetFullPath(statePath);
        this.registeredClients = registeredClients;
        Directory.CreateDirectory(Path.GetDirectoryName(this.statePath)!);
    }
    public string AuthorizeAuthenticatedAccount(CakeProfile profile, string clientID, string redirectURI, string challenge)
    {
        if(profile is null||profile.AccountID==Guid.Empty||string.IsNullOrWhiteSpace(profile.DisplayName))throw new ArgumentException("invalid_trusted_profile");
        if (!registeredClients.TryGetValue(clientID, out var redirects) || !redirects.Contains(redirectURI))
            throw new UnauthorizedAccessException("unregistered_redirect");
        if (challenge.Length != 43 || challenge.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("invalid_s256_challenge");
        lock (gate)
        {
            using var lease = DurableState.Acquire(statePath);
            var state = Read();
            var code = RandomToken();
            var entry = new AuthorizationCode(Hash(code), profile.AccountID, clientID, redirectURI, challenge,
                DateTimeOffset.UtcNow.AddMinutes(2));
            Write(state with { Profiles = state.Profiles.Where(p => p.AccountID != profile.AccountID).Append(profile).ToArray(),
                Codes = state.Codes.Where(c => c.ExpiresAt > DateTimeOffset.UtcNow).Append(entry).ToArray() });
            return code;
        }
    }
    public IssuedSession Exchange(string code, string clientID, string redirectURI, string verifier, string deviceName)
    {
        if (verifier.Length is < 43 or > 128 || verifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.' and not '_' and not '~'))
            throw new UnauthorizedAccessException("invalid_verifier");
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName.Length > 120) throw new ArgumentException("invalid_device");
        lock (gate)
        {
            using var lease = DurableState.Acquire(statePath);
            var state = Read();
            var candidate = state.Codes.SingleOrDefault(c => FixedEquals(c.CodeHash, Hash(code)));
            if (candidate is null || candidate.ExpiresAt <= DateTimeOffset.UtcNow || candidate.ClientID != clientID ||
                candidate.RedirectURI != redirectURI || !FixedEquals(candidate.Challenge, Challenge(verifier)))
                throw new UnauthorizedAccessException("invalid_authorization_code");
            var token = RandomToken(); var now = DateTimeOffset.UtcNow;
            var session = new CakeSession(Guid.NewGuid(), candidate.AccountID, deviceName, now, now.AddMinutes(15), null, Hash(token),candidate.ClientID);
            Write(state with { Codes = state.Codes.Where(c => c != candidate).ToArray(), Sessions = state.Sessions.Append(session).ToArray() });
            return new(token, session);
        }
    }
    public CakeSession Authenticate(string token)
    {
        lock(gate){using var lease=DurableState.Acquire(statePath);return AuthenticateCurrent(Read(),token);}
    }
    internal T WithCurrentSession<T>(string token,Func<CakeSession,T> operation)
    {
        lock(gate){using var lease=DurableState.Acquire(statePath);return operation(AuthenticateCurrent(Read(),token));}
    }
    private static CakeSession AuthenticateCurrent(AuthenticationState state,string token)
    {
        if(string.IsNullOrWhiteSpace(token))throw new UnauthorizedAccessException("invalid_session");
        var hashed=Hash(token);var now=DateTimeOffset.UtcNow;
        return state.Sessions.SingleOrDefault(s=>s.RevokedAt is null&&s.ExpiresAt>now&&FixedEquals(s.AccessTokenHash,hashed))
            ??throw new UnauthorizedAccessException("invalid_session");
    }
    public CakeProfile GetCurrent(string token)
    {
        lock(gate){using var lease=DurableState.Acquire(statePath);var state=Read();var session=AuthenticateCurrent(state,token);return state.Profiles.Single(p=>p.AccountID==session.AccountID);}
    }
    public IReadOnlyList<CakeSession> ListSessions(string token)
    {
        lock(gate)
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var session=AuthenticateCurrent(state,token);var now=DateTimeOffset.UtcNow;
            return state.Sessions.Where(s=>s.AccountID==session.AccountID&&s.RevokedAt is null&&s.ExpiresAt>now).Select(s=>s with{AccessTokenHash=""}).ToArray();
        }
    }
    public void RevokeSession(string token,Guid sessionID)=>Revoke(token,sessionID,false);
    public void RevokeAllOtherSessions(string token)=>Revoke(token,null,true);
    public void SignOut(string token)=>Revoke(token,null,false);
    private void Revoke(string token,Guid? sessionID,bool allOthers)
    {
        lock(gate)
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var caller=AuthenticateCurrent(state,token);
            var target=sessionID??caller.SessionID;
            if(!allOthers&&!state.Sessions.Any(s=>s.SessionID==target&&s.AccountID==caller.AccountID))throw new UnauthorizedAccessException("session_not_owned");
            var now=DateTimeOffset.UtcNow;
            Write(state with{Sessions=state.Sessions.Select(s=>s.AccountID==caller.AccountID&&(allOthers?s.SessionID!=caller.SessionID:s.SessionID==target)?s with{RevokedAt=now}:s).ToArray()});
        }
    }
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string token) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private AuthenticationState Read()
    {
        var state=File.Exists(statePath)?DurableState.Read<AuthenticationState>(statePath):new([],[],[]);
        bool HashValue(string? value)=>value is {Length:43}&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
        if(state.Profiles is null||state.Sessions is null||state.Codes is null||
            state.Profiles.Any(p=>p is null||p.AccountID==Guid.Empty||string.IsNullOrWhiteSpace(p.DisplayName))||
            state.Profiles.Select(p=>p.AccountID).Distinct().Count()!=state.Profiles.Count||
            state.Sessions.Any(s=>s is null||s.SessionID==Guid.Empty||!state.Profiles.Any(p=>p.AccountID==s.AccountID)||string.IsNullOrWhiteSpace(s.DeviceName)||s.DeviceName.Length>120||
                s.CreatedAt==default||s.ExpiresAt<=s.CreatedAt||!HashValue(s.AccessTokenHash)||s.RegisteredClientID is not null&&string.IsNullOrWhiteSpace(s.RegisteredClientID))||
            state.Sessions.Select(s=>s.SessionID).Distinct().Count()!=state.Sessions.Count||state.Sessions.Select(s=>s.AccessTokenHash).Distinct(StringComparer.Ordinal).Count()!=state.Sessions.Count||
            state.Codes.Any(c=>c is null||!state.Profiles.Any(p=>p.AccountID==c.AccountID)||!HashValue(c.CodeHash)||!HashValue(c.Challenge)||string.IsNullOrWhiteSpace(c.ClientID)||string.IsNullOrWhiteSpace(c.RedirectURI)||c.ExpiresAt==default)||
            state.Codes.Select(c=>c.CodeHash).Distinct(StringComparer.Ordinal).Count()!=state.Codes.Count)
            throw new InvalidDataException("invalid_identity_state");
        return state;
    }
    private void Write(AuthenticationState state) => DurableState.Write(statePath, state);
}
