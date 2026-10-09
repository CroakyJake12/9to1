using System.Net;
using System.Security.Cryptography;
using HavenOS.Apps.Sites.Hosting;
using Xunit;
public class Challenge {
 sealed class Wire : HttpMessageHandler {
  public bool Foreign; public bool Encoding;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t) {
   var c=new ByteArrayContent(new byte[]{2}); if(Encoding)c.Headers.ContentEncoding.Add("gzip");
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=c,RequestMessage=Foreign?new HttpRequestMessage(r.Method,"https://foreign.example/secret"):r});
  }
 }
 static SiteBuildArtifact A()=>new(Guid.NewGuid(),"source","config","static",Convert.ToHexString(SHA256.HashData(new byte[]{1})),"ref",1,true);
 static R2SiteArtifactReader R(Wire w)=>new(new Uri("https://objects.example/"),new Dictionary<string,string>{{"ref","x"}},16,"sha256-hex",w);
 [Fact] public async Task HeadDoesNotProveCorruptedContent(){using var r=R(new()); await r.HeadAsync(A()); await Assert.ThrowsAsync<InvalidDataException>(()=>r.ReadAsync(A()));}
 [Fact] public async Task ForeignFinalResponseUriRejected(){using var r=R(new(){Foreign=true});await Assert.ThrowsAsync<InvalidDataException>(()=>r.ReadAsync(A()));}
 [Fact] public async Task EncodedHeadAndGetRejected(){using var r=R(new(){Encoding=true});await Assert.ThrowsAsync<InvalidDataException>(()=>r.HeadAsync(A()));await Assert.ThrowsAsync<InvalidDataException>(()=>r.ReadAsync(A()));}
}
