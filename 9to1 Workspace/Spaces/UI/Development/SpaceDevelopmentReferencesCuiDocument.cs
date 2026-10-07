using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces.Development;

/// <summary>App-owned presentation for saved existing project references. The native
/// owner supplies bindings, current Home/actor checks, source joins and real launch actions.</summary>
public static class SpaceDevelopmentReferencesCuiDocument
{
    public static CuiDocument Load()
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(Markup, "SpaceDevelopmentReferences.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The saved development reference document is invalid.");
        return document;
    }

    public const string Markup = """
        <Cui id="dev.saved-projects" version="1">
          <Page id="dev-saved-projects-page" role="main" accessible-name="Saved development projects" font-family="Montserrat" layout="vertical" scalable-text="true" high-contrast="semantic" reduced-motion="respect">
            <Heading level="1">Dev</Heading>
            <Text>Open a saved project and its existing task. Opening keeps the same task and run.</Text>
            <Row id="dev-saved-project-controls" wrap="true" accessible-name="Saved project controls">
              <Button id="dev-refresh-saved-projects" action="dev.references.refresh" enabled="{CanRefresh}" accessible-name="Refresh saved development projects" touch-target="44">Refresh</Button>
              <Button id="dev-open-selected-space" action="dev.references.open-space" enabled="{CanOpenSpace}" accessible-name="Open the selected Space" touch-target="44">Open Space</Button>
            </Row>
            <List id="dev-saved-projects" binding="Projects" role="list" accessible-name="Saved projects and original tasks" empty-text="No matching saved project is available in the selected Spaces">
              <Article repeat="Projects" role="listitem" accessible-name="{Title}">
                <Heading level="2" binding="Title" />
                <Text binding="Summary" />
                <Text binding="TaskLabel" tone="muted" />
                <Text binding="CheckpointLabel" tone="muted" />
                <Button action="dev.references.open-task" action-argument="{SelectionKey}" enabled="{CanOpen}" accessible-name="Open the existing task for {Title}" touch-target="44">Open task and project</Button>
              </Article>
            </List>
            <Text id="dev-reference-coverage" binding="Coverage" tone="muted" accessible-name="Saved project observation coverage" />
            <Text id="dev-reference-status" binding="Status" role="status" live="polite" accessible-name="Development project status" />
          </Page>
        </Cui>
        """;
}
