using System.Text.Json;
using NineToOne.Accounts;
using NineToOne.Web;
using HavenOS.Files;

var builder = WebApplication.CreateBuilder(args);
var stateRoot = builder.Configuration["StateRoot"] ?? Path.Combine(AppContext.BaseDirectory, "state");
var clientRedirects = builder.Configuration.GetSection("Identity:Clients").GetChildren().ToDictionary(
    c => c.Key, c => (IReadOnlySet<string>)c.GetChildren().Select(r => r.Value!).Where(v => v is not null).ToHashSet(StringComparer.Ordinal));
var identity = new CakeIdentityService(Path.Combine(stateRoot, "identity.json"), clientRedirects);
var ledger = new AccountLedger(Path.Combine(stateRoot, "accounts"));
Guid? verifiedJacob=Guid.TryParse(builder.Configuration["Identity:VerifiedJacobAccountID"],out var provisionedJacob)?provisionedJacob:null;
var profiles=new ProfileService(Path.Combine(stateRoot,"profiles.json"),verifiedJacob);
var organisations=new OrganisationService(Path.Combine(stateRoot,"organisations.json"),profiles);
var domains = new WorkspaceWebDomains();
var filesLocations=builder.Configuration.GetSection("Files:AccountLocations").Get<FilesAccountLocation[]>()??[];
var filesProviders=filesLocations.ToDictionary(l=>l.AccountID,l=>(IFilesProvider)new DurableDriveProvider(l.StatePath,new FilesLocationId(l.LocationID),l.AccountID.ToString("N")));
IFilesProvider? Provider(Guid accountID)=>filesProviders.GetValueOrDefault(accountID);
var filesResolver=new FilesWorkspaceDirectoryResolver(Path.Combine(stateRoot,"files-workspace-bindings.json"),Provider);
domains.Register(new FilesWebDomain(Provider));
domains.Register(new SitesWebDomain(filesResolver));
domains.Register(new AdminWebDomain(organisations));
var costModel=builder.Configuration.GetSection("SubscriptionCosts").Get<ApprovedCostModel>()
    ?? new ApprovedCostModel("","GBP",null,null,null,null,null,null,null,null,null,null,null,null,null,null,false);
var calculator=new SubscriptionBuilder(costModel);
SubscriptionQuotes Quotes(Guid accountID)=>new(Path.Combine(stateRoot,"quotes",accountID.ToString("N")+".json"),calculator,accountID);
builder.Services.AddSingleton(identity); builder.Services.AddSingleton(ledger); builder.Services.AddSingleton(domains);
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    try { await next(); }
    catch (UnauthorizedAccessException) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "PermissionDenied" }); }
    catch (KeyNotFoundException) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new { error = "CapabilityUnavailable" }); }
    catch (FileNotFoundException) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new { error = "AccountNotFound" }); }
    catch (ArgumentException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "InvalidInput" }); }
    catch (InvalidOperationException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "Conflict" }); }
});
app.MapGet("/health", () => new { status = "ready", identity = "CAKE-ID", modelBandVersion = SubscriptionPolicy.ModelBandVersion });
app.MapPost("/oauth/token", (TokenExchange request) =>
{
    var issued = identity.Exchange(request.Code, request.ClientID, request.RedirectURI, request.Verifier, request.DeviceName);
    return Results.Ok(new { access_token = issued.AccessToken, token_type = "Bearer", expires_in = 900 });
});
app.MapGet("/api/account/current", (HttpContext c) => identity.GetCurrent(Token(c)));
app.MapGet("/api/account/profile", (HttpContext c) => profiles.Get(identity.Authenticate(Token(c)).AccountID));
app.MapPatch("/api/account/profile", (HttpContext c,ProfilePatch request) => profiles.Update(identity.Authenticate(Token(c)).AccountID,request.ExpectedRevision,request.Fields));
app.MapGet("/api/account/username-available", (HttpContext c,string username) => new {available=profiles.UsernameAvailable(identity.Authenticate(Token(c)).AccountID,username)});
app.MapGet("/api/account/sessions", (HttpContext c) => identity.ListSessions(Token(c)));
app.MapPost("/api/account/signout", (HttpContext c) => { identity.SignOut(Token(c)); return Results.NoContent(); });
app.MapDelete("/api/account/sessions/{sessionID:guid}", (HttpContext c, Guid sessionID) =>
    { identity.RevokeSession(Token(c), sessionID); return Results.NoContent(); });
app.MapPost("/api/account/revoke-other-sessions", (HttpContext c) =>
    { identity.RevokeAllOtherSessions(Token(c)); return Results.NoContent(); });
app.MapGet("/api/subscription/plan", (HttpContext c) => ledger.Get(identity.Authenticate(Token(c)).AccountID).Subscription);
app.MapGet("/api/account/entitlements", (HttpContext c) => SubscriptionPolicy.Evaluate(ledger.Get(identity.Authenticate(Token(c)).AccountID).Subscription));
app.MapGet("/api/subscription/usage", (HttpContext c) => ledger.Get(identity.Authenticate(Token(c)).AccountID).Usage);
app.MapGet("/api/subscription/dust", (HttpContext c) =>
{
    var account = ledger.Get(identity.Authenticate(Token(c)).AccountID);
    return new { allocated = account.Subscription.Resources.AIDustAllocated,
        available = account.Subscription.Resources.AIDustAllocated - account.Usage.DustSpent - account.Usage.DustReserved,
        spent = account.Usage.DustSpent, reserved = account.Usage.DustReserved };
});
app.MapGet("/api/subscription/presets", (HttpContext c) => { identity.Authenticate(Token(c)); return SubscriptionPolicy.ListPresets(); });
app.MapGet("/api/subscription/personal-api", (HttpContext c) =>
    new { enabled = SubscriptionPolicy.Evaluate(ledger.Get(identity.Authenticate(Token(c)).AccountID).Subscription).PersonalAPI,
        usagePool = "account-dust", resaleAllowed = false });
app.MapPost("/api/subscription/manage", (HttpContext c) =>
{
    identity.Authenticate(Token(c));
    var configured = builder.Configuration["Billing:ManageURI"];
    if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        return Results.Problem("Trusted billing is not configured.", statusCode: 503);
    return Results.Ok(new { url = uri.AbsoluteUri });
});
app.MapGet("/subscription-builder", () => Results.Content(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"SubscriptionBuilder.html")),"text/html"));
app.MapPost("/api/subscription/preview", (HttpContext c, BuilderSelection selection) => Quotes(identity.Authenticate(Token(c)).AccountID).Preview(selection));
app.MapPost("/api/subscription/options", (HttpContext c,BuilderSelection selection) =>
{
    identity.Authenticate(Token(c));
    return new { ai=calculator.AIOptionQuotes(selection.StorageBytes),storage=calculator.StorageOptionQuotes(selection.AIMultiplier,selection.CustomDust) };
});
app.MapPost("/api/subscription/checkout/{quoteID:guid}", (HttpContext c,Guid quoteID) =>
{
    var result=Quotes(identity.Authenticate(Token(c)).AccountID).Checkout(quoteID);
    if(result.Quote is null)return Results.Conflict(result);
    var configured=builder.Configuration["Billing:CheckoutURI"];
    if(!Uri.TryCreate(configured,UriKind.Absolute,out var uri)||uri.Scheme!="https")return Results.Problem("Trusted billing checkout is not configured.",statusCode:503);
    return Results.Ok(new {quote=result.Quote,url=uri.AbsoluteUri+(string.IsNullOrEmpty(uri.Query)?"?":"&")+"quote="+quoteID.ToString("N")});
});
app.MapGet("/api/catalogue", (HttpContext c) => { identity.Authenticate(Token(c)); return domains.Catalogue; });
app.MapPost("/api/apps/{domain}/{action}", async (HttpContext c, string domain, string action, JsonElement arguments, CancellationToken ct) =>
    await domains.InvokeAsync(domain, action, arguments, identity.Authenticate(Token(c)).AccountID, ct));
app.Run();

static string Token(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || header.Length <= 7)
        throw new UnauthorizedAccessException("missing_token");
    return header[7..];
}
public sealed record TokenExchange(string Code, string ClientID, string RedirectURI, string Verifier, string DeviceName);
public sealed record ProfilePatch(long ExpectedRevision,JsonElement Fields);
public partial class Program;
