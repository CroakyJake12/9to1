using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Dev;

/// <summary>One maintained document for native/web Dev. Actual project, source and command
/// results are supplied by the canonical owner; controls never infer authority from a login.</summary>
public static class DeveloperWorkbenchCuiDocument
{
    public static CuiDocument Load()
    {
        var parser = new CuiRichParser(); var document = parser.Parse(Markup, "DeveloperWorkbench.cui");
        if (parser.Diagnostics.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The maintained Dev document is invalid.");
        return document;
    }
    public const string Markup = """
<Cui id="dev.project-workbench" version="1">
  <StackPanel id="dev-workbench" spacing="12" margin="16" accessible-name="Development project workspace">
    <TextBlock id="dev-project-title" text="{Binding ProjectTitle}" font-size="24" text-wrapping="Wrap" />
    <TextBlock text="{Binding ProjectSummary}" text-wrapping="Wrap" accessible-name="Existing project and task" />
    <WrapPanel>
      <Button id="dev-refresh" action="dev.refresh" content="Refresh project" />
      <Button id="dev-tree" action="dev.tree" content="Project files" />
      <Button id="dev-git-status" action="dev.git-status" content="Git status" />
      <Button id="dev-git-diff" action="dev.git-diff" content="Working-tree diff" />
      <Button id="dev-git-branches" action="dev.git-branches" content="Branches" />
    </WrapPanel>
    <TextBlock text="{Binding Files}" text-wrapping="Wrap" accessible-name="Observed project file tree" />
    <TextBox id="dev-source-path" text="{Binding SourcePath}" placeholdertext="Select an existing project file" accessible-name="Project-relative source file" />
    <Button id="dev-open-source" action="dev.open-source" content="Open source" IsEnabled="{Binding CanOpenSource}" />
    <TextBox id="dev-search-query" text="{Binding SearchQuery}" placeholdertext="Search project text" accessible-name="Project search query" />
    <Button id="dev-search" action="dev.search" content="Search files" />
    <TextBlock text="{Binding SearchResults}" text-wrapping="Wrap" accessible-name="Actual file search results" />
    <TextBlock text="{Binding EditorTitle}" text-wrapping="Wrap" accessible-name="Selected source and draft revision" />
    <TextBox id="dev-code-editor" text="{Binding DraftText}" accepts-return="true" font-family="monospace" min-height="280" accessible-name="Complete source editor" />
    <WrapPanel>
      <Button id="dev-preview-edit" action="dev.preview-edit" content="Review this edit" IsEnabled="{Binding CanPreview}" />
      <Button id="dev-apply-edit" action="dev.apply-edit" content="Apply reviewed edit" IsEnabled="{Binding CanApply}" />
      <Button id="dev-revert-pass" action="dev.revert-pass" content="Prepare last pass revert" IsEnabled="{Binding CanRevert}" />
    </WrapPanel>
    <TextBlock text="{Binding ReviewSummary}" text-wrapping="Wrap" accessible-name="This edit pass only" />
    <TextBlock text="Before this pass" font-size="18" />
    <Object type="DeveloperReadonlySource" id="dev-pass-before" text="{Binding BeforeText}" accepts-return="true" font-family="monospace" min-height="100" accessible-name="Exact source before this pass" />
    <TextBlock text="After this pass" font-size="18" />
    <Object type="DeveloperReadonlySource" id="dev-pass-after" text="{Binding AfterText}" accepts-return="true" font-family="monospace" min-height="100" accessible-name="Exact proposed source after this pass" />
    <TextBox id="dev-terminal-command" text="{Binding Command}" placeholdertext="Explicit command in this project" accessible-name="Terminal command" />
    <Button id="dev-terminal-run" action="dev.run-command" content="Run command" />
    <TextBox id="dev-test-command" text="{Binding TestCommand}" placeholdertext="Explicit test command" accessible-name="Test command" />
    <Button id="dev-test-run" action="dev.run-tests" content="Run tests" />
    <TextBox id="dev-stage-task" text="{Binding StageTaskId}" placeholdertext="Saved build task ID" accessible-name="Saved build task ID" />
    <TextBox id="dev-stage-id" text="{Binding StageId}" placeholdertext="Saved stage ID" accessible-name="Saved build stage ID" />
    <Button id="dev-build-stage" action="dev.run-stage" content="Run saved build stage" />
    <TextBox id="dev-branch-name" text="{Binding BranchName}" placeholdertext="Literal branch name" accessible-name="Git branch name" />
    <WrapPanel>
      <Button id="dev-create-branch" action="dev.create-branch" content="Create branch" />
      <Button id="dev-switch-branch" action="dev.switch-branch" content="Switch branch" />
    </WrapPanel>
    <TextBlock text="{Binding ProcessStatus}" text-wrapping="Wrap" accessible-name="Observed command exit and timeout" />
    <Object type="DeveloperReadonlySource" id="dev-terminal-output" text="{Binding ProcessOutput}" accepts-return="true" font-family="monospace" min-height="180" accessible-name="Actual standard output and error" />
    <TextBlock text="{Binding Status}" text-wrapping="Wrap" accessible-name="Development operation status" />
  </StackPanel>
</Cui>
""";
}
