namespace HavenOS.Apps.Dev;

/// <summary>First-party multi-file starter for a CUI source project. It creates editable source
/// files only; a CUI preview or application launch still uses its existing owning workflow.</summary>
public sealed class DeveloperCuiProjectTemplateProvider : IDeveloperProjectTemplateContentProvider
{
    public const string RegisteredProviderId = "9to1.cui";
    public const string RegisteredTemplateId = "cui-page-project";

    private static readonly DeveloperProjectTemplate Template = new(
        RegisteredTemplateId, "CUI page project", "cui-page-project", ["CUI"], [], [], ["Dev"], [],
        RegisteredProviderId, IsFirstParty: true);

    private static readonly IReadOnlyList<DeveloperProjectTemplateFile> Files =
    [
        new("Main.cui", """
            <Cui version="1">
              <Page id="main" role="main" accessible-name="Main page">
                <Container id="content" Type="Vertical Stack" margin="16" CellGap="12">
                  <Text id="title" text="New CUI project" font-size="24" />
                  <Text id="body" text="Replace this text with your page content." text-wrapping="Wrap" />
                </Container>
              </Page>
            </Cui>
            """),
        new("README.md", """
            # CUI page project

            Edit `Main.cui` in Build. CUI is the source format for 9to1 application interfaces.
            Use Build's current-draft preview to inspect the supported static CUI subset.
            """)
    ];

    public string ProviderId => RegisteredProviderId;

    public Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DeveloperProjectTemplate>>([Template]);
    }

    public Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(
        DeveloperProjectTemplate template, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(template.TemplateId, RegisteredTemplateId))
            throw new ArgumentException("The CUI provider only reports readiness for its own registered template.", nameof(template));
        return Task.FromResult<IReadOnlyList<DeveloperToolchainReadiness>>([]);
    }

    public Task<IReadOnlyList<DeveloperProjectTemplateFile>> GetFilesAsync(
        DeveloperProjectTemplate template, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(template.TemplateId, RegisteredTemplateId))
            throw new ArgumentException("The CUI provider only supplies files for its own registered template.", nameof(template));
        return Task.FromResult(Files);
    }
}
