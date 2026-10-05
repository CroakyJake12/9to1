using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Runtime;

namespace HavenOS.Apps.Sites.Hosting;

public sealed record SiteOutputFile(string RelativePath,string SHA256,long Bytes);
public sealed record SiteArtifactManifest(int SchemaVersion,Guid SiteID,Guid ProjectID,Guid ArtifactID,string SourceRevision,
    string ConfigurationRevision,IReadOnlyList<SiteOutputFile> Files,long ProjectRevision=0);

public interface ISiteArtifactSecretScanner
{
    Task<bool> ScanAsync(string artifactDirectory,IReadOnlyList<SiteOutputFile> publicFiles,CancellationToken cancellationToken);
}

/// <summary>Compiles canonical native Sites projects into independent static-host artifacts.</summary>
public sealed class NativeSiteBuildPipeline(string artifactRoot,ISiteArtifactSecretScanner? secretScanner=null) : ISiteBuildPipeline
{
    public async Task<SiteBuildArtifact> BuildAndPackageAsync(SiteBuildRequest request,
        Func<SitePipelineUpdate,CancellationToken,ValueTask> progress,CancellationToken ct)
    {
        if(request.Source.FrameworkId!="9to1-native")throw Failure("CapabilityUnavailable","Select the registered framework build provider.");
        await progress(new(SiteDeploymentStageKind.Build,SiteStageState.Running),ct);
        var projectPath=Path.Combine(request.Source.RootReference,"site.project.json");
        await using var source=File.OpenRead(projectPath);
        var project=await JsonSerializer.DeserializeAsync<SiteProject>(source,cancellationToken:ct)
            ??throw Failure("BuildFailed","Native site metadata is invalid.");
        if(project.SchemaVersion!=SiteProjectFormat.CurrentSchemaVersion)throw Failure("SchemaVersionUnsupported","Native site schema is unsupported.");
        if(project.SiteId!=request.SiteId || project.Source.SourceRevision!=request.Source.SourceRevision)
            throw Failure("RevisionConflict","Build source is not the requested site/revision.");
        var outputRoot=Path.GetFullPath(artifactRoot);Directory.CreateDirectory(outputRoot);
        if((File.GetAttributes(outputRoot)&FileAttributes.ReparsePoint)!=0)throw Failure("ArtifactCorrupt","Linked artifact directories are unsupported.");
        var artifactID=Guid.NewGuid();var directory=Path.Combine(outputRoot,artifactID.ToString("N"));
        Directory.CreateDirectory(directory);var renderer=new SiteDocumentRenderer();var files=new List<SiteOutputFile>();
        try
        {
            foreach(var route in project.Routes)
            {
                ct.ThrowIfCancellationRequested();
                if(route.Kind!=SiteRouteKind.Static)throw Failure("CapabilityUnavailable","Dynamic routes require their registered server runtime provider.");
                var segments=SiteAddressRules.NormalizeRoutePath(route.Pattern);
                var relative=segments.Count==0 ? "index.html" : string.Join('/',segments)+"/index.html";
                if(files.Any(f=>f.RelativePath==relative))throw Failure("RouteConflict","Two routes produce the same output.");
                var rendered=renderer.Render(project,route.PageId,SiteRenderContext.PublicRoute);
                if(rendered.Diagnostics.Any(d=>d.IsError))throw Failure(rendered.Diagnostics.First(d=>d.IsError).Code,rendered.Diagnostics.First(d=>d.IsError).Message);
                var output=Path.Combine(directory,relative);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output,rendered.Document,new UTF8Encoding(false),ct);
                files.Add(await DescribeAsync(directory,relative,ct));
            }
            if(files.Count==0)throw Failure("BuildFailed","Site has no routes.");
            var manifest=new SiteArtifactManifest(1,project.SiteId,project.ProjectId,artifactID,request.Source.SourceRevision,request.Source.ConfigurationRevision,files,project.Revision);
            var manifestPath=Path.Combine(directory,"artifact.manifest.json");
            await File.WriteAllTextAsync(manifestPath,JsonSerializer.Serialize(manifest),ct);
            var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(manifestPath,ct))).ToLowerInvariant();
            await progress(new(SiteDeploymentStageKind.Build,SiteStageState.Succeeded),ct);
            await progress(new(SiteDeploymentStageKind.Package,SiteStageState.Succeeded),ct);
            return new(artifactID,manifest.SourceRevision,manifest.ConfigurationRevision,"9to1-native",hash,directory,
                files.Sum(f=>f.Bytes),secretScanner is not null && await secretScanner.ScanAsync(directory,files,ct));
        }
        catch { Directory.Delete(directory,true);throw; }
    }
    public async Task<SiteBuildValidation> ValidateArtifactAsync(SiteBuildArtifact artifact,CancellationToken ct)
    {
        try
        {
            var path=Path.Combine(artifact.ArtifactReference,"artifact.manifest.json");var bytes=await File.ReadAllBytesAsync(path,ct);
            if(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=artifact.ContentHash)throw Failure("ArtifactCorrupt","Artifact manifest hash does not match.");
            var manifest=JsonSerializer.Deserialize<SiteArtifactManifest>(bytes)??throw Failure("ArtifactCorrupt","Artifact manifest is invalid.");
            if(manifest.ProjectRevision<1||manifest.SchemaVersion!=1||manifest.ArtifactID!=artifact.ArtifactId||manifest.SourceRevision!=artifact.SourceRevision||manifest.ConfigurationRevision!=artifact.ConfigurationRevision)
                throw Failure("ArtifactCorrupt","Artifact provenance does not match.");
            if(manifest.Files.Select(f=>f.RelativePath).Distinct(StringComparer.Ordinal).Count()!=manifest.Files.Count)
                throw Failure("ArtifactCorrupt","Artifact contains duplicate file entries.");
            var listed=manifest.Files.Select(f=>f.RelativePath).Append("artifact.manifest.json").ToHashSet(StringComparer.Ordinal);
            foreach(var output in Directory.EnumerateFileSystemEntries(artifact.ArtifactReference,"*",SearchOption.AllDirectories))
            {
                if((File.GetAttributes(output)&FileAttributes.ReparsePoint)!=0)throw Failure("ArtifactCorrupt","Artifact contains linked output.");
                if(File.Exists(output)&&!listed.Contains(Path.GetRelativePath(artifact.ArtifactReference,output).Replace('\\','/')))
                    throw Failure("ArtifactCorrupt","Artifact contains unlisted output.");
            }
            foreach(var file in manifest.Files)
            {
                if(file.RelativePath.StartsWith('/')||file.RelativePath.Contains('\\')||file.RelativePath.Split('/').Any(s=>s is ".." or "." or ""))throw Failure("ArtifactCorrupt","Artifact contains an unsafe path.");
                var actual=await DescribeAsync(artifact.ArtifactReference,file.RelativePath,ct);
                if(actual!=file)throw Failure("ArtifactCorrupt","Output hash/length does not match manifest.");
            }
            return new(true,[]);
        }
        catch(SiteDeploymentException error){return new(false,[error.Error]);}
        catch(IOException){return new(false,[new("ArtifactUnavailable","Artifact output is unavailable.","artifact",true)]);}
    }
    private static async Task<SiteOutputFile> DescribeAsync(string root,string relative,CancellationToken ct)
    {
        var bytes=await File.ReadAllBytesAsync(Path.Combine(root,relative),ct);
        return new(relative,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),bytes.LongLength);
    }
    private static SiteDeploymentException Failure(string code,string message)=>new(new(code,message,"Sites.Build",false));
}
