using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Hosting;
using HavenOS.Apps.Sites.Infrastructure;
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
    [InlineData("valid")][InlineData("valid-domain")][InlineData("legacy-artifact-identity")][InlineData("wrong-artifact-domain")][InlineData("corrupt-manifest")][InlineData("corrupt-output")]
    [InlineData("missing-output")][InlineData("duplicate")][InlineData("unsafe")]
    [InlineData("wrong-project")][InlineData("wrong-revision")][InlineData("wrong-source")][InlineData("wrong-domain")]
    [InlineData("blank-source")][InlineData("blank-config")][InlineData("wrong-total")][InlineData("unscanned")][InlineData("unmapped")]
    public async Task VerifiesNativeManifestSemanticsAndRefusesChangedObjects(string mode)
    {
        byte[] output=[1,2,3]; var site=Guid.NewGuid();var project=Guid.NewGuid();var id=Guid.NewGuid();
        var file=new SiteOutputFile(mode=="unsafe"?"../secret":"index.html",Hash(output),output.Length);
        var files=mode=="duplicate"?new[]{file,file}:new[]{file};
        var domain=(mode is "valid-domain" or "wrong-artifact-domain")?Guid.NewGuid():null;
        var artifactDomain=mode=="wrong-artifact-domain"?Guid.NewGuid():domain;
        var manifest=new SiteArtifactManifest(1,site,mode=="wrong-project"?Guid.NewGuid():project,id,
            mode=="wrong-source"?"other":mode=="blank-source"?" ":"source",mode=="blank-config"?" ":"config",files,mode=="wrong-revision"?2:1,
            mode=="wrong-domain"?Guid.NewGuid():domain);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(manifest);
        var artifact=new SiteBuildArtifact(id,mode=="blank-source"?" ":"source",mode=="blank-config"?" ":"config","9to1-native",Hash(bytes),
            mode=="unmapped"?"foreign-reference":"canonical-reference",mode=="wrong-total"?4:3,mode!="unscanned",
            mode=="legacy-artifact-identity"?(Guid?)null:site,mode=="legacy-artifact-identity"?(Guid?)null:project,
            mode=="legacy-artifact-identity"?(long?)null:1L,mode=="legacy-artifact-identity"?(Guid?)null:artifactDomain);
        var objects=new Dictionary<string,byte[]> {
            ["/bucket/retained/artifact.manifest.json"]=mode=="corrupt-manifest"?[0]:bytes,
            ["/bucket/retained/index.html"]=mode=="corrupt-output"?[3,2,1]:output
        };
        if(mode=="missing-output")objects.Remove("/bucket/retained/index.html");
        using var transport=new Transport(objects);
        using var reader=new R2NativeSiteArtifactReader(new("https://objects.example/bucket/"),
            new Dictionary<string,string>{{"canonical-reference","retained"}},65536,1024,transport);
        if(mode is "valid" or "valid-domain" or "legacy-artifact-identity")
        {
            var verified=await reader.VerifyWithDomainAsync(artifact,site,project,1,domain);
            Assert.Equal(artifact.ArtifactId,verified.ArtifactID); Assert.Equal(3,verified.Files.Sum(f=>f.Bytes));
            Assert.NotEqual(artifact.SizeBytes,bytes.Length); Assert.Equal(2,transport.Calls);
        }
        else await Assert.ThrowsAsync<InvalidDataException>(()=>reader.VerifyAsync(artifact,site,project,1));
        if(mode is "unmapped" or "unscanned" or "blank-source" or "blank-config" or "wrong-artifact-domain")Assert.Equal(0,transport.Calls);
    }

    [Fact]
    public async Task Native_build_validation_binds_prepared_artifact_to_exact_project_identity_and_revision()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-native-build-"+Guid.NewGuid().ToString("N"));
        var source=Path.Combine(root,"source");var artifacts=Path.Combine(root,"artifacts");
        Directory.CreateDirectory(source);
        try
        {
            var siteId=Guid.NewGuid();var projectId=Guid.NewGuid();var domainId=Guid.NewGuid();
            var page=new SitePage(Guid.NewGuid(),"Home",null,[],"Home",null,1);
            var project=new SiteProject(siteId,projectId,"Example",new(Guid.NewGuid(),Guid.NewGuid(),domainId,"source-1","9to1-native","site"),
                7,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,[page],[new(Guid.NewGuid(),page.PageId,"/",SiteRouteKind.Static)],[],[],
                new(new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,string>(),
                    new Dictionary<string,string>(),new Dictionary<string,int>(),"system",1),[],[],[],[],[],new Dictionary<string,JsonElement>());
            await File.WriteAllTextAsync(Path.Combine(source,"site.project.json"),JsonSerializer.Serialize(project));
            var pipeline=new NativeSiteBuildPipeline(artifacts);
            var artifact=await pipeline.BuildAndPackageAsync(new(siteId,Guid.NewGuid(),new("source-1","config-1","9to1-native",source,true,true),Guid.NewGuid(),projectId,7,domainId),
                (_,_)=>ValueTask.CompletedTask,CancellationToken.None);

            Assert.Equal<Guid?>(siteId,artifact.SiteId);Assert.Equal<Guid?>(projectId,artifact.ProjectId);Assert.Equal(7L,artifact.ProjectRevision!.Value);
            Assert.Equal<Guid?>(domainId,artifact.DomainId);
            Assert.True((await pipeline.ValidateArtifactAsync(artifact,CancellationToken.None)).IsValid);
            var legacyArtifact=artifact with { SiteId=null,ProjectId=null,ProjectRevision=null,DomainId=null };
            var legacyValidation=await pipeline.ValidateArtifactAsync(legacyArtifact,CancellationToken.None);
            Assert.True(legacyValidation.IsValid);
            Assert.Equal<Guid?>(siteId,legacyValidation.SiteId);Assert.Equal<Guid?>(projectId,legacyValidation.ProjectId);
            Assert.Equal<Guid?>(domainId,legacyValidation.DomainId);Assert.Equal<long?>(7L,legacyValidation.ProjectRevision);
            Assert.False((await pipeline.ValidateArtifactAsync(artifact with { ProjectId=Guid.NewGuid() },CancellationToken.None)).IsValid);
            Assert.False((await pipeline.ValidateArtifactAsync(artifact with { SiteId=Guid.NewGuid() },CancellationToken.None)).IsValid);
            Assert.False((await pipeline.ValidateArtifactAsync(artifact with { ProjectRevision=8 },CancellationToken.None)).IsValid);
            Assert.False((await pipeline.ValidateArtifactAsync(artifact with { DomainId=Guid.NewGuid() },CancellationToken.None)).IsValid);
            Assert.False((await pipeline.ValidateArtifactAsync(artifact with { FrameworkId="external" },CancellationToken.None)).IsValid);
            await Assert.ThrowsAsync<SiteDeploymentException>(()=>pipeline.BuildAndPackageAsync(
                new(siteId,Guid.NewGuid(),new("source-1","config-1","9to1-native",source,true,true),Guid.NewGuid()),
                (_,_)=>ValueTask.CompletedTask,CancellationToken.None));
            await Assert.ThrowsAsync<SiteDeploymentException>(()=>pipeline.BuildAndPackageAsync(
                new(siteId,Guid.NewGuid(),new("source-1","config-1","9to1-native",source,true,true),Guid.NewGuid(),Guid.NewGuid(),7,domainId),
                (_,_)=>ValueTask.CompletedTask,CancellationToken.None));
            await Assert.ThrowsAsync<SiteDeploymentException>(()=>pipeline.BuildAndPackageAsync(
                new(siteId,Guid.NewGuid(),new("source-1","config-1","9to1-native",source,true,true),Guid.NewGuid(),projectId,8,domainId),
                (_,_)=>ValueTask.CompletedTask,CancellationToken.None));
            await Assert.ThrowsAsync<SiteDeploymentException>(()=>pipeline.BuildAndPackageAsync(
                new(siteId,Guid.NewGuid(),new("source-1","config-1","9to1-native",source,true,true),Guid.NewGuid(),projectId,7,Guid.NewGuid()),
                (_,_)=>ValueTask.CompletedTask,CancellationToken.None));
            var manifestPath=Path.Combine(artifact.ArtifactReference,"artifact.manifest.json");
            var manifest=JsonSerializer.Deserialize<SiteArtifactManifest>(await File.ReadAllBytesAsync(manifestPath))!;
            await File.WriteAllTextAsync(manifestPath,JsonSerializer.Serialize(manifest with { ProjectID=Guid.NewGuid() }));
            var rewritten=await File.ReadAllBytesAsync(manifestPath);
            Assert.False((await pipeline.ValidateArtifactAsync(artifact with { ContentHash=Hash(rewritten) },CancellationToken.None)).IsValid);
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }

    [Fact]
    public async Task Authoring_preparation_rejects_stale_revision_without_files_and_reopens_exact_project_artifact()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-authoring-preparation-"+Guid.NewGuid().ToString("N"));
        var files=Path.Combine(root,"files");var build=Path.Combine(root,"build");
        Directory.CreateDirectory(files);
        try
        {
            var siteId=Guid.NewGuid();var projectId=Guid.NewGuid();var domainId=Guid.NewGuid();var pageId=Guid.NewGuid();
            var project=new SiteProject(siteId,projectId,"Example",new(Guid.NewGuid(),Guid.NewGuid(),domainId,"source-1","9to1-native","site"),
                3,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,[new(pageId,"Home",null,[],"Home",null,1)],
                [new(Guid.NewGuid(),pageId,"/",SiteRouteKind.Static)],[],[],
                new(new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,string>(),
                    new Dictionary<string,string>(),new Dictionary<string,int>(),"system",1),[],[],[],[],[],new Dictionary<string,JsonElement>());
            var store=new FileSiteWorkspaceStore(files);
            await store.MutateAsync(state=>(state with { Projects=[project] },true));
            var authoring=new SiteArtifactAuthoringService(new SiteProjectService(store),build);

            await Assert.ThrowsAsync<InvalidOperationException>(()=>authoring.BuildAsync(siteId,2,"config-1"));
            Assert.False(Directory.Exists(build));

            var prepared=await authoring.BuildAsync(siteId,3,"config-1");
            Assert.Equal<Guid?>(siteId,prepared.SiteId);Assert.Equal<Guid?>(projectId,prepared.ProjectId);
            Assert.Equal(3L,prepared.ProjectRevision!.Value);
            Assert.Equal<Guid?>(domainId,prepared.DomainId);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(build,"inputs")));
            var reopenedManifest=JsonSerializer.Deserialize<SiteArtifactManifest>(
                await File.ReadAllBytesAsync(Path.Combine(prepared.ArtifactReference,"artifact.manifest.json")))!;
            Assert.Equal(siteId,reopenedManifest.SiteID);Assert.Equal(projectId,reopenedManifest.ProjectID);
            Assert.Equal(3L,reopenedManifest.ProjectRevision);
            Assert.Equal<Guid?>(domainId,reopenedManifest.DomainID);
            Assert.True((await new NativeSiteBuildPipeline(Path.Combine(build,"artifacts"))
                .ValidateArtifactAsync(prepared,CancellationToken.None)).IsValid);

            await store.MutateAsync(state=>(state with { Projects=state.Projects.Select(current=>current.SiteId==siteId?current with { Routes=[] }:current).ToArray() },true));
            await Assert.ThrowsAsync<SiteDeploymentException>(()=>authoring.BuildAsync(siteId,4,"config-2"));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(build,"inputs")));
            Assert.Equal(new[] { prepared.ArtifactReference },Directory.GetDirectories(Path.Combine(build,"artifacts")));
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }

    private sealed class Responses(Func<HttpRequestMessage,HttpResponseMessage> create) : HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(create(request)); }
    }
    [Theory]
    [InlineData("redirect")][InlineData("foreign-uri")][InlineData("encoding")]
    [InlineData("truncated")][InlineData("extra")][InlineData("cancel")][InlineData("null")]
    public async Task RefusesUnsafeTransportOrInvalidAdmission(string mode)
    {
        byte[] bytes=[1];
        using var transport=new Responses(request=>{
            var content=new ByteArrayContent(mode=="truncated"?[]:mode=="extra"?[1,2]:bytes);
            content.Headers.ContentLength=1;
            if(mode=="encoding")content.Headers.ContentEncoding.Add("gzip");
            return new(mode=="redirect"?HttpStatusCode.Redirect:HttpStatusCode.OK){Content=content,
                RequestMessage=mode=="foreign-uri"?new HttpRequestMessage(HttpMethod.Get,"https://foreign.example/secret"):request};
        });
        using var reader=new R2NativeSiteArtifactReader(new("https://objects.example/"),
            new Dictionary<string,string>{{"ref","prefix"}},1024,1024,transport);
        var artifact=new SiteBuildArtifact(Guid.NewGuid(),"source","config","9to1-native",Hash(bytes),"ref",1,true);
        using var cancel=new CancellationTokenSource(); if(mode=="cancel")cancel.Cancel();
        if(mode=="null")await Assert.ThrowsAsync<ArgumentNullException>(()=>reader.VerifyAsync(null!,Guid.NewGuid(),Guid.NewGuid(),1));
        else if(mode=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reader.VerifyAsync(artifact,Guid.NewGuid(),Guid.NewGuid(),1,cancel.Token));
        else await Assert.ThrowsAsync<InvalidDataException>(()=>reader.VerifyAsync(artifact,Guid.NewGuid(),Guid.NewGuid(),1));
        if(mode is "null" or "cancel")Assert.Equal(0,transport.Calls);
    }

}
