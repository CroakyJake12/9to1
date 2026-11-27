using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [AvaloniaFact]
    public async Task Build_setup_save_update_reopen_and_missing_template_keep_one_draft_without_source_effects()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var store = new VersionedAtomicSettingsStore(files);
            var preferences = new BuildProjectStartPreferences(store, "original-build-profile", BuildSetupAdmit);
            var census = new NavigationMetadataStore([]); var probe = new BuildSetupProbe();
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, preferences, probe, connection.Token, window.Token);
            var folder = Path.Combine(rig.Root, "Uncreated setup destination"); Guid id;
            try
            {
                await page.ActivateAsync(token);
                await EnterBuildSetupAsync(page, "Original setup", folder, token);
                await page.DispatchAsync("dev.templates.review", null, token);
                Assert.True(page.IsActionAvailable("dev.templates.create"));
                Assert.Equal(1, probe.FileReads);
                await page.DispatchAsync("dev.setup.save", null, token);
                id = Assert.Single((await preferences.ReadAsync(token)).List.Drafts).Id;
                Assert.True(page.TrySetValue("SetupDraftName", "Updated saved choices"));
                await page.DispatchAsync("dev.setup.save", null, token);
                Assert.Equal(id, Assert.Single((await preferences.ReadAsync(token)).List.Drafts).Id);
                Assert.False(Directory.Exists(folder)); Assert.Equal(0, census.Writes);
            }
            finally { await page.CloseAndDrainAsync(); }

            probe.Installed = false;
            var reopened = BuildSetupPage(rig, census,
                new(new VersionedAtomicSettingsStore(files), "original-build-profile", BuildSetupAdmit), probe, connection.Token, window.Token);
            try
            {
                await reopened.ActivateAsync(token);
                await reopened.DispatchAsync("dev.setup.drafts", null, token);
                var row = Assert.Single(BuildSetupRows(reopened, "SetupDrafts"));
                Assert.Equal(id.ToString("D"), BuildSetupField(reopened, row, "SelectionKey"));
                await reopened.DispatchAsync("dev.setup.resume", id.ToString("D"), token);
                Assert.Equal("Updated saved choices", BuildSetupValue<string>(reopened, "SetupDraftName"));
                Assert.Equal(folder, BuildSetupValue<string>(reopened, "ManualFolderPath"));
                Assert.True(BuildSetupValue<bool>(reopened, "StartIsNew"));
                Assert.Contains("Saved template unavailable", BuildSetupValue<string>(reopened, "SelectedTemplate"));
                Assert.False(reopened.IsActionAvailable("dev.templates.review"));
                Assert.False(reopened.IsActionAvailable("dev.templates.create"));
                probe.Installed = true;
                await reopened.DispatchAsync("dev.references.refresh", null, token);
                Assert.True(reopened.IsActionAvailable("dev.templates.review"));
                Assert.False(reopened.IsActionAvailable("dev.templates.create")); // No old issued review is restored.
                Assert.Equal(1, probe.FileReads); Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.Opens);
                Assert.Equal(0, census.Writes); Assert.False(Directory.Exists(folder));
            }
            finally { await reopened.CloseAndDrainAsync(); }
        });
    }

    [AvaloniaFact]
    public async Task Build_setup_recent_and_pinned_use_current_canonical_roots_and_never_replace_a_missing_project()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var preferences = new BuildProjectStartPreferences(new VersionedAtomicSettingsStore(files), "profile", BuildSetupAdmit);
            var firstRoot = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.Combine(rig.Root, "Root one"));
            var secondRoot = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.Combine(rig.Root, "Root two"));
            var project = new DeveloperProject(Guid.NewGuid(), "code", "Existing project", [firstRoot.RootId, secondRoot.RootId], "CUI", null, "none", [], [], [], [], null);
            var workspace = DeveloperWorkspace.Create([firstRoot, secondRoot]) with { Projects = [project] };
            var workspaces = new[] { workspace }; var census = new NavigationMetadataStore(workspaces); var probe = new BuildSetupProbe();
            var secondReference = new DeveloperProjectReference(workspace.WorkspaceId, workspace.Revision, project.ProjectId, project.Revision, secondRoot.RootId);
            // The session gate separately proves successful-open admission. This fixture supplies its observed receipt metadata.
            await preferences.RecordOpenedAsync(BuildProjectStartIdentity.ForManual(secondReference), token);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, preferences, probe, connection.Token, window.Token);
            try
            {
                await page.ActivateAsync(token);
                var rows = BuildSetupRows(page, "Projects"); Assert.Equal(2, rows.Length);
                Assert.Equal(secondRoot.Location, BuildSetupField(page, rows[0], "DisplaySummary"));
                await page.DispatchAsync("dev.setup.pin-project", BuildSetupField(page, rows[0], "SelectionKey"), token);
                await page.DispatchAsync("dev.setup.pinned", null, token);
                Assert.Single(BuildSetupRows(page, "Projects"));
                workspaces[0] = workspace with { Revision = workspace.Revision + 1, Projects = [project with { Revision = project.Revision + 1 }] };
                await page.DispatchAsync("dev.references.refresh", null, token);
                var current = Assert.Single(BuildSetupRows(page, "Projects"));
                await page.DispatchAsync("dev.references.open-task", BuildSetupField(page, current, "SelectionKey"), token);
                Assert.NotNull(probe.OpenedReference);
                Assert.Equal(secondRoot.RootId, probe.OpenedReference.RootId);
                Assert.Equal(workspaces[0].Revision, probe.OpenedReference.WorkspaceRevision);
                Assert.Equal(project.Revision + 1, probe.OpenedReference.ProjectRevision);
                workspaces[0] = workspaces[0] with { Projects = [project with { ProjectId = Guid.NewGuid() }] };
                await page.DispatchAsync("dev.references.refresh", null, token);
                Assert.Empty(BuildSetupRows(page, "Projects"));
                Assert.Contains("unavailable", BuildSetupValue<string>(page, "ProjectViewStatus"));
                Assert.False(page.IsActionAvailable("dev.references.open-task"));
                Assert.Equal(1, probe.Opens); Assert.Equal(0, census.Writes); Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.FileReads);
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    [AvaloniaFact]
    public async Task Build_setup_save_captures_the_clicked_snapshot_and_preserves_newer_form_edits()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var controlled = new BuildStartControlledSettings(new(files)) { HoldCommit = true };
            var preferences = new BuildProjectStartPreferences(controlled, "profile", BuildSetupAdmit);
            var probe = new BuildSetupProbe(); var census = new NavigationMetadataStore([]);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, preferences, probe, connection.Token, window.Token);
            Task? save = null;
            try
            {
                await page.ActivateAsync(token);
                await EnterBuildSetupAsync(page, "Clicked snapshot", Path.Combine(rig.Root, "Original folder"), token);
                save = page.DispatchAsync("dev.setup.save", null, token).AsTask();
                await controlled.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                Assert.True(page.TrySetValue("SetupDraftName", "Newer unsaved choices"));
                Assert.True(page.TrySetValue("ManualFolderPath", Path.Combine(rig.Root, "Newer folder")));
                controlled.CommitRelease.TrySetResult(); await save;
                var captured = Assert.Single((await preferences.ReadAsync(token)).List.Drafts);
                Assert.Equal("Clicked snapshot", captured.Name);
                Assert.Equal("Newer unsaved choices", BuildSetupValue<string>(page, "SetupDraftName"));
                Assert.Contains("Newer setup choices remain unsaved", BuildSetupValue<string>(page, "SetupStatus"));
                controlled.HoldCommit = false;
                await page.DispatchAsync("dev.setup.save", null, token);
                var updated = Assert.Single((await preferences.ReadAsync(token)).List.Drafts);
                Assert.Equal(captured.Id, updated.Id); Assert.Equal("Newer unsaved choices", updated.Name);
                Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.Opens); Assert.Equal(0, probe.FileReads); Assert.Equal(0, census.Writes);
            }
            finally
            {
                controlled.CommitRelease.TrySetResult();
                if (save is not null) await save;
                await page.CloseAndDrainAsync();
            }
        });
    }

    [AvaloniaFact]
    public async Task Build_setup_resume_does_not_replace_choices_changed_while_its_original_read_is_held()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var controlled = new BuildStartControlledSettings(new(files));
            var preferences = new BuildProjectStartPreferences(controlled, "profile", BuildSetupAdmit);
            var probe = new BuildSetupProbe(); var census = new NavigationMetadataStore([]);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, preferences, probe, connection.Token, window.Token);
            Task? resume = null;
            try
            {
                await page.ActivateAsync(token);
                await EnterBuildSetupAsync(page, "Stored setup", Path.Combine(rig.Root, "Stored folder"), token);
                await page.DispatchAsync("dev.setup.save", null, token);
                await page.DispatchAsync("dev.start.projects", null, token);
                await page.DispatchAsync("dev.setup.drafts", null, token);
                var key = BuildSetupField(page, Assert.Single(BuildSetupRows(page, "SetupDrafts")), "SelectionKey");
                controlled.HoldRead = true;
                resume = page.DispatchAsync("dev.setup.resume", key, token).AsTask();
                await controlled.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                await page.DispatchAsync("dev.start.new", null, token);
                Assert.True(page.TrySetValue("SetupDraftName", "Keep these choices"));
                Assert.True(page.TrySetValue("ManualFolderPath", Path.Combine(rig.Root, "Keep this folder")));
                controlled.ReadRelease.TrySetResult(); await resume;
                Assert.Equal("Keep these choices", BuildSetupValue<string>(page, "SetupDraftName"));
                Assert.Equal(Path.Combine(rig.Root, "Keep this folder"), BuildSetupValue<string>(page, "ManualFolderPath"));
                Assert.Contains("current choices were retained", BuildSetupValue<string>(page, "SetupStatus"));
                Assert.Equal(0, probe.Opens); Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.FileReads); Assert.Equal(0, census.Writes);
            }
            finally
            {
                controlled.ReadRelease.TrySetResult();
                if (resume is not null) await resume;
                await page.CloseAndDrainAsync();
            }
        });
    }

    [AvaloniaFact]
    public async Task Build_setup_resume_rechecks_the_saved_draft_and_requires_a_new_selection_after_an_external_edit()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var preferences = new BuildProjectStartPreferences(new VersionedAtomicSettingsStore(files), "profile", BuildSetupAdmit);
            var probe = new BuildSetupProbe(); var census = new NavigationMetadataStore([]);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, preferences, probe, connection.Token, window.Token);
            try
            {
                await page.ActivateAsync(token);
                await EnterBuildSetupAsync(page, "Original saved choices", Path.Combine(rig.Root, "Original folder"), token);
                await page.DispatchAsync("dev.setup.save", null, token);
                Assert.True(page.TrySetValue("SetupDraftName", "My unsaved choices"));
                await page.DispatchAsync("dev.start.projects", null, token);
                await page.DispatchAsync("dev.setup.drafts", null, token);
                var key = BuildSetupField(page, Assert.Single(BuildSetupRows(page, "SetupDrafts")), "SelectionKey");
                var current = await preferences.ReadAsync(token);
                var changed = Assert.Single(current.List.Drafts) with { Name = "Edited in another tab" };
                Assert.True((await preferences.SaveAsync(current, current.List with { Drafts = [changed] }, token)).Saved);
                await page.DispatchAsync("dev.setup.resume", key, token);
                Assert.True(BuildSetupValue<bool>(page, "StartIsProjects"));
                Assert.Equal("My unsaved choices", BuildSetupValue<string>(page, "SetupDraftName"));
                Assert.Contains("changed or was removed", BuildSetupValue<string>(page, "SetupStatus"));
                Assert.Equal(changed.Name, BuildSetupField(page, Assert.Single(BuildSetupRows(page, "SetupDrafts")), "Title"));
                await page.DispatchAsync("dev.setup.resume", key, token);
                Assert.Equal(changed.Name, BuildSetupValue<string>(page, "SetupDraftName"));
                Assert.Equal(0, probe.Opens); Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.FileReads); Assert.Equal(0, census.Writes);
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    [AvaloniaTheory]
    [InlineData("edited")]
    [InlineData("removed")]
    [InlineData("unrelated")]
    public async Task Build_setup_old_resume_read_cannot_overrule_a_newer_published_preference_snapshot(string change)
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var actual = new VersionedAtomicSettingsStore(files);
            var controlled = new BuildStartControlledSettings(actual);
            var preferences = new BuildProjectStartPreferences(controlled, "profile", BuildSetupAdmit);
            var writer = new BuildProjectStartPreferences(actual, "profile", BuildSetupAdmit);
            var probe = new BuildSetupProbe(); var census = new NavigationMetadataStore([]);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, preferences, probe, connection.Token, window.Token);
            Task? resume = null;
            try
            {
                await page.ActivateAsync(token);
                await EnterBuildSetupAsync(page, "Original saved choices", Path.Combine(rig.Root, "Original folder"), token);
                await page.DispatchAsync("dev.setup.save", null, token);
                Assert.True(page.TrySetValue("SetupDraftName", "My retained choices"));
                await page.DispatchAsync("dev.start.projects", null, token);
                await page.DispatchAsync("dev.setup.drafts", null, token);
                var key = BuildSetupField(page, Assert.Single(BuildSetupRows(page, "SetupDrafts")), "SelectionKey");
                controlled.HoldRead = true; controlled.HoldReadNumber = controlled.ReadCalls + 1;
                resume = page.DispatchAsync("dev.setup.resume", key, token).AsTask();
                await controlled.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                var current = await writer.ReadAsync(token);
                var originalDraft = Assert.Single(current.List.Drafts);
                var next = change switch
                {
                    "edited" => current.List with { Drafts = [originalDraft with { Name = "Newer saved choices" }] },
                    "removed" => current.List with { Drafts = [] },
                    _ => current.List with { Drafts = [originalDraft, originalDraft with { Id = Guid.NewGuid(), Name = "Unrelated draft" }] }
                };
                Assert.True((await writer.SaveAsync(current, next, token)).Saved);
                await page.DispatchAsync("dev.references.refresh", null, token); // Publishes the newer revision while the old read is still held.
                Assert.Equal(next.Drafts.Count, BuildSetupRows(page, "SetupDrafts").Length);
                controlled.ReadRelease.TrySetResult(); await resume;
                if (change == "unrelated")
                {
                    Assert.True(BuildSetupValue<bool>(page, "StartIsNew"));
                    Assert.Equal(originalDraft.Name, BuildSetupValue<string>(page, "SetupDraftName"));
                }
                else
                {
                    Assert.True(BuildSetupValue<bool>(page, "StartIsProjects"));
                    Assert.Equal("My retained choices", BuildSetupValue<string>(page, "SetupDraftName"));
                    Assert.Contains("changed or was removed", BuildSetupValue<string>(page, "SetupStatus"));
                }
                Assert.Equal(0, probe.Opens); Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.FileReads); Assert.Equal(0, census.Writes);
            }
            finally
            {
                controlled.ReadRelease.TrySetResult();
                if (resume is not null) await resume;
                await page.CloseAndDrainAsync();
            }
        });
    }

    [AvaloniaFact]
    public async Task Build_setup_extension_draft_keeps_unsupported_target_metadata_unset_and_creates_nothing()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var preferences = new BuildProjectStartPreferences(new VersionedAtomicSettingsStore(files), "profile", BuildSetupAdmit);
            var probe = new BuildSetupProbe(); var census = new NavigationMetadataStore([]);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, preferences, probe, connection.Token, window.Token);
            try
            {
                await page.ActivateAsync(token);
                await EnterBuildSetupAsync(page, "Extension setup", Path.Combine(rig.Root, "Uncreated extension"), token);
                await page.DispatchAsync("dev.start.extension", null, token);
                await page.DispatchAsync("dev.setup.save", null, token);
                var draft = Assert.Single((await preferences.ReadAsync(token)).List.Drafts);
                Assert.Equal("extension", draft.Flow); Assert.Null(draft.ProviderId); Assert.Null(draft.TemplateId);
                await page.DispatchAsync("dev.start.projects", null, token);
                await page.DispatchAsync("dev.setup.drafts", null, token);
                await page.DispatchAsync("dev.setup.resume", draft.Id.ToString("D"), token);
                Assert.True(BuildSetupValue<bool>(page, "StartIsExtension"));
                Assert.False(page.IsActionAvailable("dev.templates.create"));
                Assert.Equal(0, probe.Opens); Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.FileReads); Assert.Equal(0, census.Writes);
                Assert.False(Directory.Exists(draft.Destination));
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    [AvaloniaFact]
    public async Task Build_setup_close_joins_a_cancelled_save_and_retains_its_actual_late_storage_failure()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var failure = new IOException("Original Build setup commit failed after retirement.");
            var controlled = new BuildStartControlledSettings(new(files)) { HoldCommit = true, CommitFailure = failure };
            var probe = new BuildSetupProbe(); var census = new NavigationMetadataStore([]);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, new(controlled, "profile", BuildSetupAdmit), probe, connection.Token, window.Token);
            Task? save = null; Task? close = null;
            try
            {
                await page.ActivateAsync(token);
                await EnterBuildSetupAsync(page, "Retained original", Path.Combine(rig.Root, "Never created"), token);
                save = page.DispatchAsync("dev.setup.save", null, token).AsTask();
                await controlled.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                close = page.CloseAndDrainAsync();
                Assert.False(close.IsCompleted); Assert.False(save.IsCompleted);
                controlled.CommitRelease.TrySetResult();
                var saveError = await Record.ExceptionAsync(() => save);
                var closeError = await Record.ExceptionAsync(() => close);
                Assert.NotNull(saveError); Assert.NotNull(closeError);
                Assert.Contains(BuildSetupCauses(saveError), error => ReferenceEquals(error, failure));
                Assert.Contains(BuildSetupCauses(closeError), error => ReferenceEquals(error, failure));
                Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.Opens); Assert.Equal(0, census.Writes);
            }
            finally
            {
                controlled.CommitRelease.TrySetResult();
                if (save is not null) await Record.ExceptionAsync(() => save);
                close ??= page.CloseAndDrainAsync();
                await Record.ExceptionAsync(() => close);
            }
        });
    }

    [AvaloniaTheory]
    [InlineData("Glow", 400, 650)]
    [InlineData("Professional", 400, 650)]
    [InlineData("Glow", 720, 650)]
    [InlineData("Professional", 720, 650)]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Professional", 1440, 900)]
    public async Task Build_setup_authored_views_resume_pin_and_save_without_horizontal_overflow_or_source_IO(string themeName, int width, int height)
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            using var files = new BuildStartTestFiles();
            var preferences = new BuildProjectStartPreferences(new VersionedAtomicSettingsStore(files), "profile", BuildSetupAdmit);
            var probe = new BuildSetupProbe(); var census = new NavigationMetadataStore([]);
            var source = new BuildThemeSource(); if (themeName == "Professional") source.SelectProfessional();
            using var theme = new CuiThemePreferenceContext(source);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var windowLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = BuildSetupPage(rig, census, preferences, probe, connection.Token, windowLifetime.Token);
            page.BindInterfaceTheme(theme);
            var window = new Window { Width = width, Height = height, Content = page };
            try
            {
                window.Show(); await page.ActivateAsync(token);
                await EnterBuildSetupAsync(page, "My saved CUI setup", Path.Combine(rig.Root, "Uncreated CUI project"), token);
                StartLayout(window);
                var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                { if (BuildSetupValue<string>(page, "SetupSaveLabel") == "Save draft changes" && page.IsActionAvailable("dev.setup.save") == true) saved.TrySetResult(); }
                page.PropertyChanged += Observe;
                try
                {
                    var save = StartControl<Button>(page, "dev-save-setup-draft");
                    save.BringIntoView(); StartLayout(window);
                    AssertStartFits(page, "dev-setup-draft-name", "dev-save-setup-draft");
                    Assert.True(save.IsEffectivelyEnabled);
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await saved.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                }
                finally { page.PropertyChanged -= Observe; }
                await page.DispatchAsync("dev.start.projects", null, token); StartLayout(window);
                await PressStartButtonAsync(page, window, "dev-projects-setup-drafts", "ProjectViewDrafts", token);
                StartControl<Button>(page, "dev-resume-setup-draft").BringIntoView(); StartLayout(window);
                AssertStartFits(page, "dev-project-query", "dev-projects-recent", "dev-projects-pinned", "dev-projects-setup-drafts", "dev-resume-setup-draft");
                Assert.Single(BuildSetupRows(page, "SetupDrafts"));
                CaptureStartFrame(window, $"build-saved-setup-{themeName}-{width}x{height}");
                var row = Assert.Single(BuildSetupRows(page, "SetupDrafts"));
                var key = BuildSetupField(page, row, "SelectionKey");
                await page.DispatchAsync("dev.setup.pin-draft", key, token);
                await PressStartButtonAsync(page, window, "dev-projects-pinned", "ProjectViewPinned", token);
                Assert.Single(BuildSetupRows(page, "SetupDrafts"));
                await PressStartButtonAsync(page, window, "dev-resume-setup-draft", "StartIsNew", token); StartLayout(window);
                Assert.Equal("My saved CUI setup", BuildSetupValue<string>(page, "SetupDraftName"));
                var actualSave = StartControl<Button>(page, "dev-save-setup-draft");
                actualSave.BringIntoView(); StartLayout(window);
                var point = actualSave.TranslatePoint(default, window);
                Assert.NotNull(point); Assert.InRange(point.Value.Y, -1, height);
                Assert.True(point.Value.Y + actualSave.Bounds.Height <= height + 1, "The resumed setup save action is reachable inside the viewport.");
                AssertStartFits(page, "dev-setup-draft-form", "dev-setup-draft-name", "dev-save-setup-draft");
                CaptureStartFrame(window, $"build-resumed-setup-{themeName}-{width}x{height}");
                Assert.Equal(0, probe.Opens); Assert.Equal(0, probe.Creates); Assert.Equal(0, probe.FileReads); Assert.Equal(0, census.Writes);
                Assert.False(Directory.Exists(Path.Combine(rig.Root, "Uncreated CUI project")));
            }
            finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
        });
    }

    private static DeveloperSavedProjectsPage BuildSetupPage(Rig rig, NavigationMetadataStore census,
        BuildProjectStartPreferences preferences, BuildSetupProbe probe, CancellationToken connection, CancellationToken window)
    {
        var landing = BuildLanding(rig);
        var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
            _ => { probe.Opens++; return Task.CompletedTask; }, (_, _) => { probe.Opens++; return Task.CompletedTask; }, census,
            reference => { probe.Opens++; probe.OpenedReference = reference; return Task.CompletedTask; }, connection, window, _ => { },
            createManualProject: (_, _) => { probe.Creates++; throw new InvalidOperationException("Setup metadata cannot register a project."); },
            templateCatalog: new([probe]), templateMaterializer: new([probe]),
            registerTemplateProject: (_, _, _) => { probe.Creates++; throw new InvalidOperationException("Setup metadata cannot register a template project."); },
            openTemplateProject: (_, _) => { probe.Opens++; return Task.CompletedTask; });
        page.ConfigureStartPreferences(preferences);
        return page;
    }

    private static async Task EnterBuildSetupAsync(DeveloperSavedProjectsPage page, string name, string folder, CancellationToken token)
    {
        await page.DispatchAsync("dev.start.new", null, token);
        Assert.True(page.TrySetValue("SetupDraftName", name)); Assert.True(page.TrySetValue("ManualFolderPath", folder));
        var template = Assert.Single(BuildSetupRows(page, "Templates"));
        await page.DispatchAsync("dev.templates.select", BuildSetupField(page, template, "SelectionKey"), token);
    }
    private static Task BuildSetupAdmit(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    private static T BuildSetupValue<T>(DeveloperSavedProjectsPage page, string name)
    { Assert.True(page.TryGetValue(name, out var value)); return Assert.IsType<T>(value); }
    private static object[] BuildSetupRows(DeveloperSavedProjectsPage page, string name)
    { Assert.True(page.TryGetValue(name, out var value)); return Assert.IsAssignableFrom<IEnumerable<object>>(value).ToArray(); }
    private static string BuildSetupField(DeveloperSavedProjectsPage page, object item, string name)
    { Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(item, name, out var value)); return Assert.IsType<string>(value); }
    private static IEnumerable<Exception> BuildSetupCauses(Exception error)
    {
        yield return error;
        if (error is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var cause in BuildSetupCauses(child)) yield return cause;
        else if (error.InnerException is { } child)
            foreach (var cause in BuildSetupCauses(child)) yield return cause;
    }
    private sealed class BuildSetupProbe : IDeveloperProjectTemplateContentProvider
    {
        public string ProviderId => "installed.setup.cui";
        internal bool Installed = true;
        internal int FileReads, Creates, Opens;
        internal DeveloperProjectReference? OpenedReference;
        private DeveloperProjectTemplate Template => new("native-page", "Native CUI page", "cui", ["CUI"], [], [], ["Build"], [], ProviderId, true);
        public Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<DeveloperProjectTemplate>>(Installed ? [Template] : []); }
        public Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(DeveloperProjectTemplate template, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<DeveloperToolchainReadiness>>([]); }
        public Task<IReadOnlyList<DeveloperProjectTemplateFile>> GetFilesAsync(DeveloperProjectTemplate template, CancellationToken token)
        { token.ThrowIfCancellationRequested(); FileReads++; return Task.FromResult<IReadOnlyList<DeveloperProjectTemplateFile>>([new("Main.cui", "<Cui version=\"1\" />")]); }
    }
}
