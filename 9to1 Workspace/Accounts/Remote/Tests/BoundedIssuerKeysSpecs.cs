using System.Net;
using System.Net.Http;
using System.Text;
namespace NineToOne.Accounts.Oidc;
internal static class BoundedIssuerKeysSpecs
{
 public static async Task RunAsync()
 {
  var issuer=new Uri("https://issuer.example.invalid/");var discovery=new Uri(issuer,"discovery");var jwks=new Uri(issuer,"jwks");
  var policy=new IssuerFetchPolicy(issuer,discovery,jwks,512,TimeSpan.FromMinutes(1),TimeSpan.FromSeconds(2));
  int calls=0;var clock=new Clock(DateTimeOffset.UtcNow);
  using var good=new BoundedIssuerKeysSource(policy,clock,new Handler(_=>{calls++;return new(HttpStatusCode.OK){Content=new StringContent(calls==1?"{\"issuer\":\"https://issuer.example.invalid/\",\"jwks_uri\":\"https://issuer.example.invalid/jwks\"}":"{\"keys\":[]}")};}));
  Require(await good.ReadAsync(issuer.AbsoluteUri,default) is not null);Require(calls==2);
  Require(await good.ReadAsync(issuer.AbsoluteUri,default) is not null);Require(calls==2);
  Require(await good.ReadAsync("https://foreign.example.invalid/",default) is null);Require(calls==2);
  clock.Now=clock.Now.AddMinutes(2);Require(await good.ReadAsync(issuer.AbsoluteUri,default) is null);Require(calls==3); // malformed refreshed discovery cannot reuse stale key
  using var redirect=new BoundedIssuerKeysSource(policy,clock,new Handler(_=>new(HttpStatusCode.Redirect)));
  Require(await redirect.ReadAsync(issuer.AbsoluteUri,default) is null);
  using var lying=new BoundedIssuerKeysSource(policy,clock,new Handler(_=>new(HttpStatusCode.OK){Content=new UnknownLength(new string('x',513))}));
  Require(await lying.ReadAsync(issuer.AbsoluteUri,default) is null);
  using var stalled=new BoundedIssuerKeysSource(policy with {RequestTimeout=TimeSpan.FromMilliseconds(50)},clock,new Handler(_=>new(HttpStatusCode.OK){Content=new StallContent()}));
  Require(await stalled.ReadAsync(issuer.AbsoluteUri,default) is null);
  using var oversized=new BoundedIssuerKeysSource(policy,clock,new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent(new string('x',513))}));
  Require(await oversized.ReadAsync(issuer.AbsoluteUri,default) is null);
 }
 private sealed class Clock(DateTimeOffset now):TimeProvider {public DateTimeOffset Now=now;public override DateTimeOffset GetUtcNow()=>Now;}
 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> f):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(f(r));}
 private sealed class UnknownLength(string value):HttpContent
 {protected override bool TryComputeLength(out long length){length=0;return false;}protected override Task SerializeToStreamAsync(Stream stream,System.Net.TransportContext? context)=>stream.WriteAsync(Encoding.UTF8.GetBytes(value)).AsTask();}
 private sealed class StallContent:HttpContent
 {protected override bool TryComputeLength(out long length){length=0;return false;}protected override Task SerializeToStreamAsync(Stream s,System.Net.TransportContext? c)=>Task.CompletedTask;
 protected override Task<Stream> CreateContentReadStreamAsync()=>Task.FromResult<Stream>(new StallStream());
 protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken ct)=>Task.FromResult<Stream>(new StallStream());}
 private sealed class StallStream:MemoryStream
 {public override async ValueTask<int> ReadAsync(Memory<byte> bytes,CancellationToken ct=default){await Task.Delay(Timeout.Infinite,ct);return 0;}}
 private static void Require(bool value){if(!value)throw new InvalidOperationException("Bounded discovery transport fixture failed");}
}
