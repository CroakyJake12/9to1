using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Apps.Sites.Hosting;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class R2NativeSiteArtifactReaderTests
{
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class Transport(Dictionary<string,byte[]> objects) : HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            var response=new HttpResponseMessage(objects.TryGetValue(request.RequestUri!.AbsolutePath,out var bytes)?HttpStatusCode.OK:HttpStatusCode.NotFound)
                {RequestMessage=request,Content=new ByteArrayContent(bytes??[])};
            return Task.FromResult(response);
        }
    }
    [Theory]
    [InlineData("valid")][InlineData("corrupt-manifest")][InlineData("corrupt-output")]
    [InlineData("missing-output")][InlineData("duplicate")][InlineData("unsafe")]
    [InlineData("wrong-project")][InlineData("wrong-revision")][InlineData("wrong-source")]
    [InlineData("wrong-total")][InlineData("unscanned")][InlineData("unmapped")]
    public async Task VerifiesNativeManifestSemanticsAndRefusesChangedObjects(string mode)
    {
        byte[] output=[1,2,3]; var site=Guid.NewGuid();var project=Guid.NewGuid();var id=Guid.NewGuid();
        var file=new SiteOutputFile(mode=="unsafe"?"../secret":"index.html",Hash(output),output.Length);
        var files=mode=="duplicate"?new[]{file,file}:new[]{file};
        var manifest=new SiteArtifactManifest(1,site,mode=="wrong-project"?Guid.NewGuid():project,id,
            mode=="wrong-source"?"other":"source","config",files,mode=="wrong-revision"?2:1);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(manifest);
        var artifact=new SiteBuildArtifact(id,"source","config","9to1-native",Hash(bytes),
            mode=="unmapped"?"foreign-reference":"canonical-reference",mode=="wrong-total"?4:3,mode!="unscanned");
        var objects=new Dictionary<string,byte[]> {
            ["/bucket/retained/artifact.manifest.json"]=mode=="corrupt-manifest"?[0]:bytes,
            ["/bucket/retained/index.html"]=mode=="corrupt-output"?[3,2,1]:output
        };
        if(mode=="missing-output")objects.Remove("/bucket/retained/index.html");
        using var transport=new Transport(objects);
        using var reader=new R2NativeSiteArtifactReader(new("https://objects.example/bucket/"),
            new Dictionary<string,string>{{"canonical-reference","retained"}},65536,1024,transport);
        if(mode=="valid")
        {
            var verified=await reader.VerifyAsync(artifact,site,project,1);
            Assert.Equal(artifact.ArtifactId,verified.ArtifactID); Assert.Equal(3,verified.Files.Sum(f=>f.Bytes));
            Assert.NotEqual(artifact.SizeBytes,bytes.Length); Assert.Equal(2,transport.Calls);
        }
        else await Assert.ThrowsAsync<InvalidDataException>(()=>reader.VerifyAsync(artifact,site,project,1));
        if(mode is "unmapped" or "unscanned")Assert.Equal(0,transport.Calls);
    }
}
