using Haven.Application;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Infrastructure;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class SiteNativeProjectAccessTests
{
    [Fact]
    public async Task Actual_project_revision_and_current_folder_binding_are_required()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-native-authority-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var folder=Guid.NewGuid();var service=new SiteProjectService(new FileSiteWorkspaceStore(root));
            var created=await service.CreateProjectAsync(new(folder,null,null,"fixture-source","Fixture","9to1-native","Fixture"));
            Assert.Null(created.Error);var project=Assert.IsType<HavenOS.Apps.Sites.Domain.SiteProject>(created.Value);
            var actor=new AuthenticatedResourceActor("fixture-os-actor","fixture-profile",null,null,"fixture-auth-revision");
            var binding=new SiteNativeWorkspaceBinding(actor.ProfileId,actor.ActorId,actor.AuthenticationRevision,folder,"fixture-folder-revision",root);
            var authority=new FixtureWorkspace{Binding=binding};var resolver=new SiteNativeProjectAccessResolver(authority);
            var scope=new ResourceScope("sites.project",project.SiteId.ToString("D"),"1",ResourceAccess.Write);
            Assert.True((await resolver.EvaluateAsync(actor,"sites.project.save",scope,default)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor,"sites.project.save",scope with{Revision="2"},default)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor,"sites.project.unknown",scope,default)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor with{AccountId=Guid.NewGuid()},"sites.project.save",scope,default)).Allowed);
            authority.Binding=binding with{FilesFolderId=Guid.NewGuid()};
            Assert.False((await resolver.EvaluateAsync(actor,"sites.project.save",scope,default)).Allowed);
            authority.Binding=binding;authority.RevokeAfterFirstRead=true;authority.Reads=0;
            Assert.False((await resolver.EvaluateAsync(actor,"sites.project.save",scope,default)).Allowed);
            Assert.Equal(2,authority.Reads);
        }
        finally{Directory.Delete(root,true);}
    }

    private sealed class FixtureWorkspace:ISiteNativeWorkspaceAuthority
    {
        public SiteNativeWorkspaceBinding? Binding {get;set;}
        public bool RevokeAfterFirstRead {get;set;}
        public int Reads {get;set;}
        public Task<SiteNativeWorkspaceBinding?> GetCurrentAsync(CancellationToken cancellationToken=default)
            =>Task.FromResult(++Reads>1&&RevokeAfterFirstRead?null:Binding);
    }
}
