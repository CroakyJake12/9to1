using System.Text.Json;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Hosting;

namespace HavenOS.Apps.Sites.Application;

/// <summary>Builds a revision-bound canonical project snapshot. The host supplies a Files-authorised build directory.</summary>
public sealed class SiteArtifactAuthoringService(SiteProjectService projects,string authorisedBuildDirectory,
    ISiteArtifactSecretScanner? secretScanner=null)
{
    public async Task<SiteBuildArtifact> BuildAsync(Guid siteID,long expectedRevision,string configurationRevision,
        CancellationToken cancellationToken=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRevision);
        var found=await projects.GetProjectAsync(siteID,cancellationToken);
        if(found.Error is not null)throw new InvalidOperationException(found.Error.Code);
        var project=found.Value!;
        if(project.Revision!=expectedRevision)throw new InvalidOperationException("RevisionConflict");
        var root=Path.GetFullPath(authorisedBuildDirectory);Directory.CreateDirectory(root);
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked build directories are unsupported.");
        var inputRoot=Path.Combine(root,"inputs");Directory.CreateDirectory(inputRoot);
        if((File.GetAttributes(inputRoot)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked build input directories are unsupported.");
        var source=Path.Combine(inputRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(source);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(source,"site.project.json"),JsonSerializer.Serialize(project),cancellationToken);
            var pipeline=new NativeSiteBuildPipeline(Path.Combine(root,"artifacts"),secretScanner);
            return await pipeline.BuildAndPackageAsync(new(siteID,Guid.NewGuid(),new(project.Source.SourceRevision,
                configurationRevision,project.Source.FrameworkId,source,true,true),Guid.NewGuid()),(_,_)=>ValueTask.CompletedTask,cancellationToken);
        }
        finally{Directory.Delete(source,true);}
    }
}
