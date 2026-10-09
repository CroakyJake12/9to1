using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NineToOne.Accounts.Native;
using NineToOne.Accounts.Oidc;
using NineToOne.Accounts.Remote;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NineToOne.Accounts.Remote.Specs;

// Synthetic issuer/Access data only. No live credentials, browser, Home actor or OS grant.
// Runs sequentially because production's registered redirect owns exact TCP port 43821.
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--cloudflared-only") return await CloudflaredLifetimeControls.RunAsync();
        var controls = new List<(string Name, Func<Task> Run)>
        {
            ("EdDSA_pair_real_loopback_PKCE_and_canonical_current", ValidPairAsync),
            ("canonical_profile_and_sessions_envelopes", EnvelopesAsync),
            ("memory_expiry_refuses_before_API", ExpiryAsync),
            ("server_revocation_clears_memory", RevokedAsync),
            ("server_permission_denial_is_not_login_success", DeniedAsync),
            ("signout_retires_memory_before_actual_reply", SignOutAsync),
            ("close_is_same_published_task_and_joins_two_original_faults", HeldCloseAsync),
            ("actual_signed_reader_preserves_compound_key_source_faults", ReaderCompoundAsync),
            ("exchange_unknown_completion_is_not_retried", ExchangeNotRetriedAsync),
            ("wrong_origin_Access_header_refused_before_acquisition", AccessWrongOriginAsync),
            ("Access_header_preserves_CAKE_Bearer_and_no_origin_assertion", AccessHeaderAsync),
            ("empty_Access_output_is_unavailable", EmptyAccessAsync),
            ("missing_Access_source_does_not_start_browser", MissingAccessAsync),
            ("loopback_wrong_host_refused", () => CallbackRefusedAsync("host")),
            ("loopback_wrong_state_refused_before_exchange", () => CallbackRefusedAsync("state")),
            ("loopback_duplicate_state_refused_before_exchange", () => CallbackRefusedAsync("duplicate")),
            ("loopback_wrong_method_refused_before_exchange", () => CallbackRefusedAsync("method")),
            ("originating_callback_resource_is_same_and_single_use", OriginatingSingleUseAsync)
        };
        foreach (var mutation in new[] { "kid", "signature", "issuer", "id-audience", "nonce", "subject", "access-subject", "sid", "id-sid", "azp", "client-id", "missing-client", "resource", "scope", "expired" })
        {
            var exact = mutation;
            controls.Add(("signed_pair_refuses_" + exact, () => SignedPairRefusedAsync(exact)));
        }
        var failed = 0;
        foreach (var control in controls)
        {
            try { await control.Run(); Console.WriteLine("PASS " + control.Name); }
            catch (Exception error)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + control.Name + "\n" + error);
            }
        }
        Console.WriteLine($"Native CAKE synthetic controls: {controls.Count - failed} passed, {failed} failed, 0 skipped.");
        return failed == 0 ? 0 : 1;
    }

    private static Task ValidPairAsync() => WithRigAsync(async rig =>
    {
        var original = rig.Track(rig.Session.SignInAsync(rig.Token));
        var snapshot = await original;
        Check.Equal(rig.Account, snapshot.AccountId);
        Check.Equal(rig.SessionId, snapshot.SessionId);
        Check.Equal("Synthetic owner", snapshot.DisplayName);
        Check.Equal(1, rig.ExchangeCount);
        Check.Equal(1, rig.CurrentCount);
        Check.True(rig.Session.TryGetCurrentSnapshot(out var current));
        Check.Equal(snapshot, current!);
        Check.Equal("S256", rig.Browser.Query["code_challenge_method"]);
        Check.Equal(rig.Options.ApiResource, rig.Browser.Query["resource"]);
        Check.Equal(NativeCakeClientOptions.RequiredRedirect, rig.Browser.Query["redirect_uri"]);
        Check.Equal("code", rig.Browser.Query["response_type"]);
        Check.True(NativeCakeClientOptions.Scopes.SetEquals(rig.Browser.Query["scope"].Split(' ')));
        Check.Equal(1, rig.Browser.Requests);
        var callback = rig.Browser.CallbackTask ?? throw new InvalidOperationException("No original callback task");
        await callback;
        Check.True(callback.IsCompletedSuccessfully);
    });

    private static Task EnvelopesAsync() => WithRigAsync(async rig =>
    {
        await rig.Track(rig.Session.SignInAsync(rig.Token));
        var profile = await rig.Track(rig.Session.ProfileAsync(rig.Token));
        Check.Equal(ApiFailure.None, profile.Failure);
        var actualProfile = profile.Value ?? throw new InvalidOperationException("No canonical profile");
        Check.Equal(rig.Account, actualProfile.AccountID);
        Check.Equal(7L, actualProfile.Revision);
        var sessions = await rig.Track(rig.Session.SessionsAsync(rig.Token));
        Check.Equal(ApiFailure.None, sessions.Failure);
        var actualSessions = sessions.Value ?? throw new InvalidOperationException("No canonical sessions");
        Check.Equal(1, actualSessions.Length);
        Check.Equal(rig.SessionId, actualSessions[0].SessionID);
        Check.Equal(rig.Account, actualSessions[0].AccountID);
    });

    private static Task ExpiryAsync() => WithRigAsync(async rig =>
    {
        await rig.Track(rig.Session.SignInAsync(rig.Token));
        var before = rig.CurrentCount;
        rig.Clock.Now = rig.Clock.Now.AddMinutes(6);
        Check.False(rig.Session.TryGetCurrentSnapshot(out _));
        var result = await rig.Track(rig.Session.CurrentAsync(rig.Token));
        Check.Equal(ApiFailure.InvalidToken, result.Failure);
        Check.Equal(before, rig.CurrentCount);
    });

    private static Task RevokedAsync() => WithRigAsync(async rig =>
    {
        await rig.Track(rig.Session.SignInAsync(rig.Token));
        rig.CurrentStatus = HttpStatusCode.Unauthorized;
        var result = await rig.Track(rig.Session.CurrentAsync(rig.Token));
        Check.Equal(ApiFailure.InvalidToken, result.Failure);
        Check.False(rig.Session.TryGetCurrentSnapshot(out _));
        var before = rig.CurrentCount;
        Check.Equal(ApiFailure.InvalidToken, (await rig.Track(rig.Session.CurrentAsync(rig.Token))).Failure);
        Check.Equal(before, rig.CurrentCount);
    });

    private static Task DeniedAsync() => WithRigAsync(async rig =>
    {
        rig.CurrentStatus = HttpStatusCode.Forbidden;
        await rig.ExpectRefusalAsync(rig.Track(rig.Session.SignInAsync(rig.Token)));
        Check.False(rig.Session.TryGetCurrentSnapshot(out _));
        Check.Equal(1, rig.CurrentCount);
    });

    private static Task SignedPairRefusedAsync(string mutation) => WithRigAsync(async rig =>
    {
        rig.Mutation = mutation;
        await rig.ExpectRefusalAsync(rig.Track(rig.Session.SignInAsync(rig.Token)));
        Check.False(rig.Session.TryGetCurrentSnapshot(out _));
        Check.Equal(1, rig.ExchangeCount);
        Check.Equal(0, rig.CurrentCount); // Refused signed pair never reaches application authority.
    });

    private static Task CallbackRefusedAsync(string mutation) => WithRigAsync(async rig =>
    {
        rig.Browser.Mutation = mutation;
        await rig.ExpectRefusalAsync(rig.Track(rig.Session.SignInAsync(rig.Token)));
        Check.False(rig.Session.TryGetCurrentSnapshot(out _));
        Check.Equal(0, rig.ExchangeCount);
        Check.Equal(0, rig.CurrentCount);
    });

    private static Task ExchangeNotRetriedAsync() => WithRigAsync(async rig =>
    {
        rig.ExchangeStatus = HttpStatusCode.BadGateway;
        await rig.ExpectRefusalAsync(rig.Track(rig.Session.SignInAsync(rig.Token)));
        Check.Equal(1, rig.ExchangeCount);
        Check.Equal(0, rig.CurrentCount);
        Check.False(rig.Session.TryGetCurrentSnapshot(out _));
    });

    private static Task SignOutAsync() => WithRigAsync(async rig =>
    {
        await rig.Track(rig.Session.SignInAsync(rig.Token));
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Release(() => reply.TrySetResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        rig.MutationReply = (_, _) => { acquired.TrySetResult(); return reply.Task; };
        var actual = rig.Track(rig.Session.SignOutAsync(rig.Token));
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check.False(actual.IsCompleted);
        Check.False(rig.Session.TryGetCurrentSnapshot(out _));
        Check.Equal(ApiFailure.InvalidToken, (await rig.Track(rig.Session.CurrentAsync(rig.Token))).Failure);
        reply.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        var result = await actual;
        Check.Equal(ApiFailure.None, result.Failure);
        Check.True(result.Value!.Acknowledged);
        Check.Equal(1, rig.MutationCount);
    });

    private static Task HeldCloseAsync() => WithRigAsync(async rig =>
    {
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new IOException("Synthetic original discovery cause one");
        var second = new InvalidOperationException("Synthetic original discovery cause two");
        Task? reentrant = null;
        var compound = new AggregateException("Synthetic held HTTP operation", first, second);
        rig.Release(() => response.TrySetException(compound));
        rig.DiscoveryReply = (_, ct) =>
        {
            rig.Own(ct.Register(() => { reentrant = rig.Session.CloseAndDrainAsync(); cancelled.TrySetResult(); }));
            acquired.TrySetResult();
            return response.Task; // Genuine original ignores cancellation until released.
        };
        var original = rig.Track(rig.Session.SignInAsync(rig.Token));
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = rig.Track(rig.Session.CloseAndDrainAsync());
        Check.Same(close, rig.Session.CloseAndDrainAsync());
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check.Same(close, reentrant);
        Check.False(close.IsCompleted);
        Check.False(original.IsCompleted);
        response.SetException(compound);
        var originalError = await rig.ExpectFailureAsync(original);
        var closeError = await rig.ExpectFailureAsync(close);
        Check.ContainsSame(first, originalError);
        Check.ContainsSame(second, originalError);
        Check.ContainsSame(first, closeError);
        Check.ContainsSame(second, closeError);
        Check.Same(close, rig.Session.CloseAndDrainAsync());
        Check.Equal(0, rig.ExchangeCount);
        Check.Throws<ObjectDisposedException>(() => { rig.Session.SignInAsync(rig.Token); });
    });

    private static async Task ReaderCompoundAsync()
    {
        var first = new IOException("Synthetic original JWKS cause one");
        var second = new InvalidOperationException("Synthetic original JWKS cause two");
        var original = new TaskCompletionSource<ApprovedIssuerKeys?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keys = new HeldKeys(original.Task);
        var reader = new Ed25519IssuerReader(keys, new FixedClock());
        var verify = reader.VerifyAsync("synthetic-token-before-key-read", new(Rig.Issuer, Rig.Origin,
            TokenPurpose.ApiAccessToken, null, new HashSet<string>(), new HashSet<string> { "EdDSA" }), CancellationToken.None).AsTask();
        Check.False(verify.IsCompleted);
        original.SetException(new Exception[] { first, second });
        Exception? captured = null;
        try { await verify; } catch (Exception error) { captured = error; }
        Check.True(captured is AggregateException);
        Check.ContainsSame(first, captured!);
        Check.ContainsSame(second, captured!);
        Check.Equal(1, keys.Calls);
    }

    private static Task OriginatingSingleUseAsync()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
        var flow = new OidcOriginatingFlow(Rig.Issuer, Rig.ClientId, NativeCakeClientOptions.RequiredRedirect, "1", expiry, Rig.Origin);
        var authorization = flow.AuthorizationParameters();
        Check.Equal(Rig.Origin, authorization["resource"]);
        Check.True(flow.ConsumeCallback(authorization["state"], NativeCakeClientOptions.RequiredRedirect, "synthetic-code", "2", DateTimeOffset.UtcNow) is null);
        var exchange = flow.ConsumeCallback(authorization["state"], NativeCakeClientOptions.RequiredRedirect, "synthetic-code", "1", DateTimeOffset.UtcNow);
        Check.True(exchange is not null);
        Check.Equal(Rig.Origin, exchange!["resource"]);
        Check.Equal(authorization["code_challenge"], Encode(SHA256.HashData(Encoding.ASCII.GetBytes(exchange["code_verifier"]))));
        Check.True(flow.ConsumeCallback(authorization["state"], NativeCakeClientOptions.RequiredRedirect, "synthetic-code", "1", DateTimeOffset.UtcNow) is null);
        return Task.CompletedTask;
    }

    private static async Task AccessWrongOriginAsync()
    {
        var source = new AccessSource("synthetic-access");
        var wire = new DelegateHandler((_, _) => throw new InvalidOperationException("Wrong destination reached transport"));
        using var client = AccessClient(source, wire);
        using var other = new HttpRequestMessage(HttpMethod.Post, "https://different.invalid/api/auth/oauth2/token");
        await Check.ThrowsAsync<HttpRequestException>(() => client.SendAsync(other));
        using var injected = new HttpRequestMessage(HttpMethod.Get, Rig.Origin + "/api/account/current");
        injected.Headers.Add("Cf-Access-Token", "caller-supplied");
        await Check.ThrowsAsync<HttpRequestException>(() => client.SendAsync(injected));
        Check.Equal(0, source.Calls);
        Check.Equal(0, wire.Calls);
    }

    private static async Task AccessHeaderAsync()
    {
        var source = new AccessSource("synthetic-access");
        var wire = new DelegateHandler((request, _) =>
        {
            Check.Equal("synthetic-access", request.Headers.GetValues("Cf-Access-Token").Single());
            var authorization = request.Headers.Authorization ?? throw new InvalidOperationException("CAKE bearer lost");
            Check.Equal("Bearer", authorization.Scheme);
            Check.Equal("synthetic-cake-access", authorization.Parameter);
            Check.False(request.Headers.Contains("Cf-Access-Jwt-Assertion"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        using var client = AccessClient(source, wire);
        using var request = new HttpRequestMessage(HttpMethod.Get, Rig.Origin + "/api/account/current");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-cake-access");
        using var response = await client.SendAsync(request);
        Check.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Check.Equal(1, source.Calls);
        Check.Equal(1, wire.Calls);
    }

    private static async Task EmptyAccessAsync()
    {
        var source = new AccessSource("");
        var wire = new DelegateHandler((_, _) => throw new InvalidOperationException("Empty credential reached transport"));
        using var client = AccessClient(source, wire);
        await Check.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Rig.Origin + "/api/account/current"));
        Check.Equal(1, source.Calls);
        Check.Equal(0, wire.Calls);
    }

    private static async Task MissingAccessAsync()
    {
        var session = new NativeCakeAccountSession(Rig.CreateOptions());
        Check.False(session.IsAvailable);
        Check.Throws<InvalidOperationException>(() => { session.SignInAsync(CancellationToken.None); });
        var original = session.CloseAndDrainAsync();
        Check.Same(original, session.CloseAndDrainAsync());
        await original;
    }

    private static HttpClient AccessClient(AccessSource source, DelegateHandler wire)
    {
        var owner = new NativeCakeAccessHandler(new Uri(Rig.Origin), source);
        var replaced = owner.InnerHandler;
        owner.InnerHandler = wire;
        replaced?.Dispose();
        return new HttpClient(owner) { Timeout = TimeSpan.FromSeconds(10) };
    }

    private static async Task WithRigAsync(Func<Rig, Task> control)
    {
        var errors = new List<Exception>();
        Rig? rig = null;
        try { rig = new Rig(); await control(rig); }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (rig is not null)
            {
                try { await rig.CloseAsync(); }
                catch (Exception error) { Add(errors, error); }
            }
        }
        Throw(errors);
    }

    internal static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Split('=', 2)).ToDictionary(value => Uri.UnescapeDataString(value[0]), value => Uri.UnescapeDataString(value[1].Replace('+', ' ')), StringComparer.Ordinal);
    internal static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(original => ReferenceEquals(original, error))) errors.Add(error); }
    internal static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Synthetic body and original fixture teardown failed", errors);
    }
}

internal sealed class Rig
{
    internal const string Origin = "https://synthetic-cake.invalid";
    internal const string Issuer = Origin + "/api/auth";
    internal const string ClientId = "synthetic-native-client";
    internal readonly Guid Account = Guid.Parse("76cf1461-1394-4c32-9c62-cc315aac2ef9");
    internal readonly Guid SessionId = Guid.Parse("7be0d553-cbb9-4d92-b098-3c4a630ba00b");
    internal readonly FixedClock Clock = new();
    internal readonly Browser Browser;
    internal readonly NativeCakeClientOptions Options = CreateOptions();
    internal readonly NativeCakeAccountSession Session;
    internal string Mutation = "";
    internal HttpStatusCode CurrentStatus = HttpStatusCode.OK;
    internal HttpStatusCode ExchangeStatus = HttpStatusCode.OK;
    internal int ExchangeCount, CurrentCount, MutationCount;
    internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? DiscoveryReply, MutationReply;
    private readonly Ed25519PrivateKeyParameters privateKey = new(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), 0);
    private readonly Ed25519PrivateKeyParameters otherKey = new(Enumerable.Range(33, 32).Select(i => (byte)i).ToArray(), 0);
    private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
    private readonly List<Task> originals = [];
    private readonly List<Action> releases = [];
    private readonly List<IDisposable> borrowedRegistrations = [];
    private readonly HashSet<Exception> expected = new(ReferenceEqualityComparer.Instance);
    private readonly HttpClient http;
    private readonly WorkerAccountApiClient api;
    internal CancellationToken Token => deadline.Token;

    internal static NativeCakeClientOptions CreateOptions() => new(Issuer, ClientId, "native", "none",
        NativeCakeClientOptions.RequiredRedirect, Origin, new Uri(Origin), new Uri(Issuer + "/.well-known/openid-configuration"),
        new Uri(Issuer + "/oauth2/authorize"), new Uri(Issuer + "/oauth2/token"), new Uri(Issuer + "/jwks"));

    internal Rig()
    {
        Browser = new Browser();
        var keys = new FixtureKeys(Issuer, JsonSerializer.Serialize(new { keys = new[] { new { kty = "OKP", crv = "Ed25519", kid = "synthetic-ed25519", alg = "EdDSA", use = "sig", x = Program.Encode(privateKey.GeneratePublicKey().GetEncoded()) } } }), Clock);
        var reader = new Ed25519IssuerReader(keys, Clock);
        var policy = new TokenPolicy(Issuer, Origin, TokenPurpose.ApiAccessToken, null,
            NativeCakeClientOptions.Scopes.Where(scope => scope.StartsWith("cake:", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal), new HashSet<string> { "EdDSA" });
        api = new WorkerAccountApiClient(new Uri(Origin), new OidcResourceConsumer(reader, false), policy, Clock, new DelegateHandler(RespondAsync));
        http = new HttpClient(new DelegateHandler(RespondAsync)) { Timeout = TimeSpan.FromSeconds(15) };
        var flow = new NativeCakeBrowserFlow(Options, reader, api, http, Browser, Clock);
        Session = new NativeCakeAccountSession(flow, api, Clock);
    }

    internal T Track<T>(T original) where T : Task { originals.Add(original); return original; }
    internal void Release(Action release) => releases.Add(release);
    internal void Own(IDisposable registration) => borrowedRegistrations.Add(registration);
    internal async Task<Exception> ExpectFailureAsync(Task original)
    {
        try { await original; }
        catch (Exception error) { Expect(error); return error; }
        throw new InvalidOperationException("The original was expected to fail");
    }
    internal async Task ExpectRefusalAsync(Task original)
    {
        var error = await ExpectFailureAsync(original);
        static bool Refusal(Exception value) => value is IOException ||
            value is AggregateException group && group.InnerExceptions.Count > 0 && group.InnerExceptions.All(Refusal);
        Check.True(Refusal(error)); // Cancellation, NullReference and arbitrary failures cannot satisfy refusal.
    }
    private void Expect(Exception error)
    {
        expected.Add(error);
        if (error is AggregateException compound) foreach (var inner in compound.InnerExceptions) Expect(inner);
    }
    private bool Expected(Exception error) => expected.Contains(error) ||
        error is AggregateException group && group.InnerExceptions.Count > 0 && group.InnerExceptions.All(Expected);

    internal async Task CloseAsync()
    {
        var errors = new List<Exception>();
        Task? close = null;
        try { close = Session.CloseAndDrainAsync(); }
        catch (Exception error) { Program.Add(errors, error); }
        foreach (var release in releases) try { release(); } catch (Exception error) { Program.Add(errors, error); }
        foreach (var actual in originals.ToArray())
        {
            try { await actual; }
            catch (Exception error) { if (!Expected(error)) Program.Add(errors, error); }
        }
        if (close is not null)
        {
            try { await close; }
            catch (Exception error) { if (!Expected(error)) Program.Add(errors, error); }
        }
        if (Browser.CallbackTask is { } callback)
        {
            try { await callback; }
            catch (Exception error) { Program.Add(errors, error); }
        }
        foreach (var registration in borrowedRegistrations) try { registration.Dispose(); } catch (Exception error) { Program.Add(errors, error); }
        try { http.Dispose(); } catch (Exception error) { Program.Add(errors, error); }
        try { api.Dispose(); } catch (Exception error) { Program.Add(errors, error); }
        try { deadline.Dispose(); } catch (Exception error) { Program.Add(errors, error); }
        Program.Throw(errors);
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing synthetic destination");
        Check.Equal("https", uri.Scheme);
        Check.Equal(new Uri(Origin).Authority, uri.Authority);
        var path = uri.AbsolutePath;
        if (path == "/api/auth/.well-known/openid-configuration")
        {
            if (DiscoveryReply is { } held) return await held(request, ct);
            return Json(new { issuer = Issuer, authorization_endpoint = Options.AuthorizationUri.AbsoluteUri,
                token_endpoint = Options.TokenUri.AbsoluteUri, jwks_uri = Options.JwksUri.AbsoluteUri });
        }
        if (path == "/api/auth/oauth2/token")
        {
            ExchangeCount++;
            Check.Equal(HttpMethod.Post, request.Method);
            var form = Program.Query(new Uri("https://synthetic.invalid/?" + await request.Content!.ReadAsStringAsync(ct)));
            Check.Equal("authorization_code", form["grant_type"]);
            Check.Equal(ClientId, form["client_id"]);
            Check.Equal(NativeCakeClientOptions.RequiredRedirect, form["redirect_uri"]);
            Check.Equal(Origin, form["resource"]);
            Check.Equal("synthetic-code", form["code"]);
            Check.Equal(Browser.Query["code_challenge"], Program.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))));
            if (ExchangeStatus != HttpStatusCode.OK) return new HttpResponseMessage(ExchangeStatus);
            return Json(new { token_type = "Bearer", expires_in = 300, id_token = Jwt(true), access_token = Jwt(false) });
        }
        var authorization = request.Headers.Authorization ?? throw new InvalidOperationException("Missing CAKE bearer");
        Check.Equal("Bearer", authorization.Scheme);
        Check.Equal(Jwt(false), authorization.Parameter);
        if (path == "/api/account/current")
        {
            CurrentCount++;
            if (CurrentStatus != HttpStatusCode.OK) return new HttpResponseMessage(CurrentStatus);
            return Json(new { accountId = Account.ToString("D"), displayName = "Synthetic owner" });
        }
        if (path == "/api/account/profile") return Json(new { profile = new { accountId = Account.ToString("D"), name = "Synthetic owner", username = "synthetic", icon = (string?)null, pronouns = (string?)null, job = (string?)null, revision = 7 } });
        if (path == "/api/account/sessions") return Json(new { sessions = new[] { new { sessionId = SessionId.ToString("D"), accountId = Account.ToString("D"), deviceName = "Synthetic Windows", createdAt = Clock.Now.AddMinutes(-1), expiresAt = Clock.Now.AddMinutes(5), revokedAt = (DateTimeOffset?)null, registeredClientId = ClientId } } });
        if (path == "/api/account/signout")
        {
            MutationCount++;
            Check.Equal(HttpMethod.Post, request.Method);
            return MutationReply is { } mutation ? await mutation(request, ct) : new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        throw new InvalidOperationException("Unexpected synthetic provider route " + path);
    }

    private string Jwt(bool identity)
    {
        var now = Clock.Now.ToUnixTimeSeconds();
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = Issuer, ["sub"] = Account.ToString("D"), ["iat"] = now, ["exp"] = now + 300,
            ["aud"] = identity ? ClientId : Origin
        };
        if (identity) payload["nonce"] = Browser.Query["nonce"];
        else
        {
            payload["sid"] = SessionId.ToString("D");
            payload["azp"] = ClientId; payload["client_id"] = ClientId;
            payload["scope"] = string.Join(' ', NativeCakeClientOptions.Scopes.Order(StringComparer.Ordinal));
        }
        switch (Mutation)
        {
            case "issuer" when identity: payload["iss"] = "https://different.invalid/api/auth"; break;
            case "id-audience" when identity: payload["aud"] = "different-client"; break;
            case "nonce" when identity: payload["nonce"] = "different-nonce"; break;
            case "subject" when identity: payload["sub"] = "not-a-uuid"; break;
            case "access-subject" when !identity: payload["sub"] = "c3285f0a-138f-4f58-a366-05a811b98207"; break;
            case "sid" when !identity: payload["sid"] = "not-a-session"; break;
            case "id-sid" when identity: payload["sid"] = "36d66c4c-e79a-47d9-8b49-1d0b3c27e749"; break;
            case "azp" when !identity: payload["azp"] = "different-client"; break;
            case "client-id" when !identity: payload["client_id"] = "different-client"; break;
            case "missing-client" when !identity: payload.Remove("azp"); payload.Remove("client_id"); break;
            case "resource" when !identity: payload["aud"] = "https://different-resource.invalid"; break;
            case "scope" when !identity: payload["scope"] = "openid profile"; break;
            case "expired" when identity: payload["iat"] = now - 300; payload["exp"] = now; break;
        }
        var kid = identity && Mutation == "kid" ? "unknown-key" : "synthetic-ed25519";
        var header = Program.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "EdDSA", kid }));
        var data = header + "." + Program.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signer = new Ed25519Signer();
        signer.Init(true, identity && Mutation == "signature" ? otherKey : privateKey);
        var bytes = Encoding.ASCII.GetBytes(data);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return data + "." + Program.Encode(signer.GenerateSignature());
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}

internal sealed class Browser : INativeCakeBrowserRequestSource
{
    internal string Mutation = "";
    internal int Requests;
    internal IReadOnlyDictionary<string, string> Query = new Dictionary<string, string>();
    internal Task? CallbackTask;
    public void Request(Uri authorization, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Requests++;
        Query = Program.Query(authorization);
        CallbackTask = CallbackAsync(token); // Actual Task captured before Request returns.
    }
    private async Task CallbackAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // This synthetic browser transport is separately owned; provider completion may
        // cancel its linked login token after receiving the callback. Drain the actual
        // TCP response under its own finite deadline instead of manufacturing cancellation.
        using var transport = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        token = transport.Token;
        using var socket = new TcpClient(AddressFamily.InterNetwork);
        await socket.ConnectAsync(IPAddress.Loopback, 43821, token);
        await using var stream = socket.GetStream();
        var state = Mutation == "state" ? "wrong-state" : Query["state"];
        var target = "/cake-id/callback/?code=synthetic-code&state=" + Uri.EscapeDataString(state);
        if (Mutation == "duplicate") target += "&state=duplicate";
        var host = Mutation == "host" ? "different.invalid:43821" : "127.0.0.1:43821";
        var method = Mutation == "method" ? "POST" : "GET";
        var bytes = Encoding.ASCII.GetBytes(method + " " + target + " HTTP/1.1\r\nHost: " + host + "\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
        socket.Client.Shutdown(SocketShutdown.Send); // Actual request half-close; retain response receive task.
        if (Mutation is "host" or "method" or "duplicate") return;
        // Refused headers may close without a response. This is not success evidence.
        var buffer = new byte[1024];
        while (await stream.ReadAsync(buffer, token) != 0) { }
    }
}

internal sealed class FixedClock : TimeProvider
{
    internal DateTimeOffset Now = new(2026, 10, 5, 17, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
internal sealed class FixtureKeys(string issuer, string json, FixedClock clock) : IApprovedIssuerKeysSource
{
    public ValueTask<ApprovedIssuerKeys?> ReadAsync(string exactIssuer, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<ApprovedIssuerKeys?>(exactIssuer == issuer ? new(issuer, json, clock.Now.AddMinutes(1)) : null); }
}
internal sealed class HeldKeys(Task<ApprovedIssuerKeys?> original) : IApprovedIssuerKeysSource
{
    internal int Calls;
    public ValueTask<ApprovedIssuerKeys?> ReadAsync(string exactIssuer, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Calls++; return new(original); }
}
internal sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    internal int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Calls++; return respond(request, cancellationToken); }
}
internal sealed class AccessSource(string? token) : INativeCakeAccessCredentialSource
{
    internal int Calls;
    public ValueTask<string?> AcquireForRequestAsync(Uri exactApplicationOrigin, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Check.Equal(Rig.Origin + "/", exactApplicationOrigin.AbsoluteUri); Calls++; return ValueTask.FromResult(token); }
    public Task CloseAndDrainAsync() => Task.CompletedTask; // Explicit synthetic source has no process/network work.
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
internal static class Check
{
    internal static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true"); }
    internal static void False(bool value) => True(!value);
    internal static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", actual " + actual); }
    internal static void Same(object? expected, object? actual)
    { if (!ReferenceEquals(expected, actual)) throw new InvalidOperationException("Original object identity changed"); }
    internal static void ContainsSame(Exception expected, Exception actual)
    {
        if (ReferenceEquals(expected, actual)) return;
        if (actual is AggregateException group && group.InnerExceptions.Any(inner => Contains(expected, inner))) return;
        throw new InvalidOperationException("Exact original cause is absent", actual);
    }
    private static bool Contains(Exception expected, Exception actual) => ReferenceEquals(expected, actual) || actual is AggregateException group && group.InnerExceptions.Any(inner => Contains(expected, inner));
    internal static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    internal static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
