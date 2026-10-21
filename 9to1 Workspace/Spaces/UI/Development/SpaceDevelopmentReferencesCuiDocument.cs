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
          <Page id="dev-saved-projects-page" role="main" accessible-name="Saved development projects">
            <Container id="dev-saved-projects-root" margin="16" Type="Vertical Stack" CellGap="12">
              <Text id="dev-title" text="Dev" font-size="24" />
              <Text text="Open a saved project and its existing task. Opening keeps the same task and run." text-wrapping="Wrap" />
              <Container id="dev-saved-project-controls" Type="Horizontal Stack" CellGap="8">
                <Button id="dev-refresh-saved-projects" action="dev.references.refresh" content="Refresh" Interactive="{Binding CanRefresh}" accessible-name="Refresh saved development projects" />
                <Button id="dev-open-selected-space" action="dev.references.open-space" content="Open Space" Interactive="{Binding CanOpenSpace}" accessible-name="Open the selected Space" />
              </Container>
              <Repeat id="dev-saved-projects" Source="{Binding Projects}" As="item" Key="{Binding item.SelectionKey}">
                <Container id="dev-saved-project-item" Type="Vertical Stack">
                  <Text id="dev-project-title" text="{Binding item.Title}" />
                  <Text text="{Binding item.Summary}" text-wrapping="Wrap" />
                  <Text text="{Binding item.TaskLabel}" foreground="{Resource CuiTextSoftBrush}" />
                  <Text text="{Binding item.CheckpointLabel}" foreground="{Resource CuiTextSoftBrush}" />
                  <Button id="dev-open-task" action="dev.references.open-task" action-argument="{Binding item.SelectionKey}" content="Open task and project" Interactive="{Binding item.CanOpen}" accessible-name="Open the existing task for {Binding item.Title}" />
                </Container>
              </Repeat>
              <Text id="dev-reference-coverage" text="{Binding Coverage}" foreground="{Resource CuiTextSoftBrush}" accessible-name="Saved project observation coverage" />
              <Text id="dev-reference-status" text="{Binding Status}" accessible-name="Development project status" />
            </Container>
          </Page>
        </Cui>
        """;
}
