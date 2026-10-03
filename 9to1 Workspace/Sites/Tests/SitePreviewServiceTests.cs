using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Hosting;
using HavenOS.Apps.Sites.Runtime;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class SitePreviewServiceTests
{
    [Fact]
    public async Task Failed_canonical_edit_retains_exact_valid_document_with_current_diagnostics_then_recovers()
    {
        using var fixture = await PreviewFixture.CreateAsync();
        var first = await fixture.Preview.RefreshAsync(fixture.Request);
        Assert.Equal(SitePreviewState.Live, first.State);
        Assert.Contains("Original &lt;title&gt;", first.Document);
        var artifact = await fixture.BuildAsync();
        Assert.Equal(first.Document, await File.ReadAllTextAsync(Path.Combine(artifact, "index.html")));
        var stable = fixture.Project;

        await fixture.SetComponentAsync("code-defined", "Invalid edit");
        var failed = await fixture.Preview.RefreshAsync(fixture.Request);
        Assert.Equal(SitePreviewState.BuildFailed, failed.State);
        Assert.True(failed.IsRetainedLastValid);
        Assert.Equal(first.Document, failed.Document);
        Assert.Equal(stable.Revision, failed.RenderedProjectRevision);
        Assert.Equal(fixture.Project.Revision, failed.ProjectRevision);
        Assert.Contains(failed.Diagnostics, row => row.Code == "UnsupportedVisualConstruct" && row.IsError);
        await Assert.ThrowsAsync<SiteDeploymentException>(() => fixture.BuildAsync());
        Assert.Equal(failed, await fixture.Preview.GetCurrentAsync());

        await fixture.SetComponentAsync("heading", "Recovered title");
        var recovered = await fixture.Preview.RefreshAsync(fixture.Request);
        Assert.Equal(SitePreviewState.Live, recovered.State);
        Assert.False(recovered.IsRetainedLastValid);
        Assert.Empty(recovered.Diagnostics);
        Assert.Contains("Recovered title", recovered.Document);
        Assert.Equal(fixture.Project.Revision, recovered.RenderedProjectRevision);
        Assert.Equal(recovered.Document, await File.ReadAllTextAsync(Path.Combine(await fixture.BuildAsync(), "index.html")));
        var reopened = (await new SiteProjectService(new FileSiteWorkspaceStore(fixture.Root)).GetProjectAsync(stable.SiteId)).Value!;
        Assert.Equal(stable.ProjectId, reopened.ProjectId);
        Assert.Equal(stable.Source, reopened.Source);
        Assert.Equal(stable.Components[0].ComponentId, reopened.Components[0].ComponentId);
    }

    [Fact]
    public async Task Cached_document_requires_current_ambient_actor_and_current_owning_read_permission()
    {
        using var fixture = await PreviewFixture.CreateAsync();
        Assert.Equal(SitePreviewState.Live, (await fixture.Preview.RefreshAsync(fixture.Request)).State);
        fixture.Workspace.Binding = null;
        var revoked = await fixture.Preview.GetCurrentAsync();
        Assert.NotNull(revoked);
        Assert.Equal(SitePreviewState.Disconnected, revoked.State);
        Assert.Null(revoked.Document);
        Assert.Null(revoked.RenderedProjectRevision);
        Assert.Null(await fixture.Preview.GetCurrentAsync());

        fixture.Workspace.Binding = fixture.Binding;
        await fixture.SetComponentAsync("code-defined", "Private source");
        var invalid = await fixture.Preview.RefreshAsync(fixture.Request);
        Assert.Equal(SitePreviewState.BuildFailed, invalid.State);
        Assert.Null(invalid.Document); // restored access cannot resurrect the previously revoked cache
        Assert.False(invalid.IsRetainedLastValid);
    }

    [Fact]
    public async Task Actor_authentication_revision_change_never_inherits_another_actor_cached_page()
    {
        using var fixture = await PreviewFixture.CreateAsync();
        Assert.NotNull((await fixture.Preview.RefreshAsync(fixture.Request)).Document);
        fixture.Actors.Actor = fixture.Actors.Actor! with { AuthenticationRevision = "renewed-auth" };
        fixture.Workspace.Binding = fixture.Binding with { AuthenticationRevision = "renewed-auth" };
        await fixture.SetComponentAsync("code-defined", "New actor invalid edit");
        var changed = await fixture.Preview.RefreshAsync(fixture.Request);
        Assert.Equal(SitePreviewState.BuildFailed, changed.State);
        Assert.Null(changed.Document);
        Assert.False(changed.IsRetainedLastValid);
    }

    [Fact]
    public async Task Different_canonical_source_binding_or_page_does_not_reuse_last_valid_document()
    {
        using var fixture = await PreviewFixture.CreateAsync();
        Assert.NotNull((await fixture.Preview.RefreshAsync(fixture.Request)).Document);
        var changed = await fixture.Projects.UpdateProjectAsync(fixture.Project.SiteId, fixture.Project.Revision,
            project => project with { Source = project.Source with { StackDomainId = Guid.NewGuid(), SourceRevision = "another-branch" } });
        fixture.Project = changed.Value!;
        await fixture.SetComponentAsync("code-defined", "Different source");
        var source = await fixture.Preview.RefreshAsync(fixture.Request);
        Assert.Equal(SitePreviewState.BuildFailed, source.State);
        Assert.Null(source.Document);
        await fixture.SetComponentAsync("heading", "Valid changed source");
        Assert.NotNull((await fixture.Preview.RefreshAsync(fixture.Request)).Document);
        var missingPage = await fixture.Preview.RefreshAsync(fixture.Request with { PageID = Guid.NewGuid() });
        Assert.Equal(SitePreviewState.BuildFailed, missingPage.State);
        Assert.Null(missingPage.Document);
        Assert.Contains(missingPage.Diagnostics, row => row.Code == "PreviewRenderFailed");
    }

    [Fact]
    public async Task Older_real_source_read_cannot_publish_after_newer_preview_and_permission_revocation()
    {
        using var fixture = await PreviewFixture.CreateAsync();
        var held = new HeldCanonicalSource(fixture.Projects);
        var preview = new SitePreviewService(held, fixture.Authorization, new SiteDocumentRenderer());
        var old = preview.RefreshAsync(fixture.Request);
        await held.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.SetComponentAsync("heading", "Newer revision");
        var latest = await preview.RefreshAsync(fixture.Request);
        Assert.Equal(SitePreviewState.Live, latest.State);
        Assert.Contains("Newer revision", latest.Document);
        fixture.Workspace.Binding = null;
        var revoked = await preview.GetCurrentAsync();
        Assert.Equal(SitePreviewState.Disconnected, revoked!.State);
        Assert.Null(revoked.Document);
        held.Release.TrySetResult(true);
        var obsolete = await old.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SitePreviewState.Superseded, obsolete.State);
        Assert.Null(obsolete.Document);
        Assert.Null(await preview.GetCurrentAsync());
        fixture.Workspace.Binding = fixture.Binding;
        await fixture.SetComponentAsync("code-defined", "Bad later source");
        Assert.Null((await preview.RefreshAsync(fixture.Request)).Document);
    }

    [Fact]
    public async Task Publication_rechecks_owning_permission_after_actual_render_even_for_retained_document()
    {
        using var fixture = await PreviewFixture.CreateAsync();
        Assert.NotNull((await fixture.Preview.RefreshAsync(fixture.Request)).Document);
        await fixture.SetComponentAsync("code-defined", "Bad edit");
        fixture.Actors.Reads = 0;
        fixture.Actors.PauseRead = 3; // first authorization reads twice; next is publication authorization
        var pending = fixture.Preview.RefreshAsync(fixture.Request);
        await fixture.Actors.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Workspace.Binding = null;
        fixture.Actors.Release.TrySetResult(true);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SitePreviewState.Disconnected, result.State);
        Assert.Null(result.Document);
        Assert.False(result.IsRetainedLastValid);
        Assert.Null(await fixture.Preview.GetCurrentAsync());
    }

    private sealed class PreviewFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sites-preview-" + Guid.NewGuid().ToString("N"));
        public SiteProjectService Projects { get; }
        public SiteProject Project { get; set; } = null!;
        public AmbientActors Actors { get; } = new();
        public CurrentWorkspace Workspace { get; } = new();
        public SiteNativeWorkspaceBinding Binding { get; private set; } = null!;
        public ResourceAuthorizationService Authorization { get; }
        public SitePreviewService Preview { get; }
        public SitePreviewRequest Request => new(Project.SiteId, Project.Pages[0].PageId, Project.Revision);

        private PreviewFixture()
        {
            Directory.CreateDirectory(Root);
            Projects = new(new FileSiteWorkspaceStore(Root));
            Authorization = new(Actors, [new SiteNativeProjectAccessResolver(Workspace)]);
            Preview = new(Projects, Authorization, new SiteDocumentRenderer());
        }

        public static async Task<PreviewFixture> CreateAsync()
        {
            var fixture = new PreviewFixture();
            try
            {
                var folder = Guid.NewGuid();
                fixture.Actors.Actor = new("os-preview-viewer", "local-profile", null, null, "current-auth");
                fixture.Binding = new(fixture.Actors.Actor.ProfileId, fixture.Actors.Actor.ActorId,
                    fixture.Actors.Actor.AuthenticationRevision, folder, "folder-revision", fixture.Root);
                fixture.Workspace.Binding = fixture.Binding;
                var created = await fixture.Projects.CreateProjectAsync(new(folder, null, null, "source-a", "Preview project", "9to1-native", "Website"));
                Assert.Null(created.Error);
                fixture.Project = created.Value!;
                var edits = new SiteAuthoringService(fixture.Projects);
                fixture.Project = (await edits.CreatePageAsync(fixture.Project.SiteId, fixture.Project.Revision, "Home", "/")).Value!;
                fixture.Project = (await edits.AddComponentAsync(fixture.Project.SiteId, fixture.Project.Revision,
                    fixture.Project.Pages[0].PageId, null, "heading", Properties("Original <title>"))).Value!;
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        public async Task SetComponentAsync(string type, string text)
        {
            var updated = await Projects.UpdateProjectAsync(Project.SiteId, Project.Revision, project => project with
            {
                Components = project.Components.Select(component => component with { ComponentType = type, Properties = Properties(text) }).ToArray()
            });
            Assert.Null(updated.Error);
            Project = updated.Value!;
        }

        public async Task<string> BuildAsync()
        {
            var artifact = await new SiteArtifactAuthoringService(Projects, Path.Combine(Root, "builds"))
                .BuildAsync(Project.SiteId, Project.Revision, "config-preview");
            return artifact.ArtifactReference;
        }

        public void Dispose() => Directory.Delete(Root, true);
        private static Dictionary<string, JsonElement> Properties(string text) => new()
        {
            ["text"] = JsonSerializer.SerializeToElement(text), ["level"] = JsonSerializer.SerializeToElement(1)
        };
    }

    private sealed class AmbientActors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Actor { get; set; }
        public int Reads { get; set; }
        public int PauseRead { get; set; }
        public TaskCompletionSource<bool> Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            if (++Reads == PauseRead)
            {
                Paused.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return Actor;
        }
    }

    private sealed class CurrentWorkspace : ISiteNativeWorkspaceAuthority
    {
        public SiteNativeWorkspaceBinding? Binding { get; set; }
        public Task<SiteNativeWorkspaceBinding?> GetCurrentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Binding);
    }

    private sealed class HeldCanonicalSource(SiteProjectService projects) : ISitePreviewProjectSource
    {
        private int reads;
        public TaskCompletionSource<bool> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SiteApiResult<SiteProject>> ReadAsync(Guid siteID, CancellationToken cancellationToken)
        {
            var actual = await projects.GetProjectAsync(siteID, cancellationToken);
            if (Interlocked.Increment(ref reads) == 1)
            {
                Captured.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return actual;
        }
    }
}
