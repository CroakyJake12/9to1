using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    // Actual compiled-CUI landing/catalog with the existing genuine fixture graph. The saved
    // metadata census and final open callback below are component controls; they confer no Files,
    // source, process or native-frame admission and do not claim a full project-open acceptance.
    [AvaloniaFact]
    public async Task Saved_project_filter_pages_every_root_and_opens_the_exact_selected_repository_reference()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken; var landing = BuildLanding(rig);
            var local = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("local-root"));
            var remote = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("remote-root"), "container:dev");
            var workspace = DeveloperWorkspace.Create([local, remote]) with
            {
                Projects = Enumerable.Range(0, 101).Select(index => new DeveloperProject(Guid.NewGuid(), "code", $"Project {index:D3}",
                    [local.RootId, remote.RootId], "cui", null, "custom", [], [], [], [], null)).ToArray(),
                SourceControlBindings = [new("selected-remote", remote.RootId, "git", "repository:remote")]
            };
            var store = new NavigationMetadataStore([workspace]); DeveloperProjectReference? opened = null;
            using var connection = new CancellationTokenSource(); using var window = new CancellationTokenSource();
            DeveloperSavedProjectsPage? retained = null;
            var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
                view => Task.CompletedTask, (space, settlement) => Task.CompletedTask, store,
                reference => { opened = reference; return Task.CompletedTask; }, connection.Token, window.Token, actual => retained = actual);
            try
            {
                Assert.Same(page, retained); await page.ActivateAsync(token);
                object[] Rows()
                {
                    Assert.True(page.TryGetValue("Projects", out var value)); return Assert.IsType<object[]>(value);
                }
                string Field(object row, string name)
                {
                    Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, name, out var value)); return Assert.IsType<string>(value);
                }
                Assert.Equal(100, Rows().Length); Assert.True(page.IsActionAvailable("dev.references.next-projects"));
                var notified = 0; page.PropertyChanged += (_, _) => notified++;
                Assert.True(page.TrySetValue("ProjectQuery", "REMOTE-ROOT")); Assert.True(notified > 0);
                Assert.Equal(100, Rows().Length);
                await page.DispatchAsync("dev.references.next-projects", null, token);
                var selected = Assert.Single(Rows()); var oldKey = Field(selected, "SelectionKey");
                Assert.Equal("Project 100", Field(selected, "Title")); Assert.Equal("Open project", Field(selected, "OpenLabel"));
                Assert.Contains("container:dev", Field(selected, "CheckpointLabel")); Assert.Contains("repository:remote", Field(selected, "CheckpointLabel"));
                Assert.False(page.IsActionAvailable("dev.references.next-projects")); Assert.True(page.IsActionAvailable("dev.references.previous-projects"));
                await page.DispatchAsync("dev.references.open-task", oldKey, token);
                Assert.NotNull(opened); Assert.Equal(remote.RootId, opened!.RootId); Assert.Equal("selected-remote", opened.RepositoryBindingId);
                Assert.Equal(workspace.Projects[100].ProjectId, opened.ProjectId);
                Assert.True(page.TrySetValue("ProjectQuery", "Project 000")); Assert.Equal(2, Rows().Length);
                Assert.False(page.IsActionAvailable("dev.references.previous-projects"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => page.DispatchAsync("dev.references.open-task", oldKey, token).AsTask());
                Assert.Equal(1, store.ListCalls); Assert.Equal(0, store.Writes);
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    [AvaloniaFact]
    public async Task Saved_manual_census_failure_is_visible_and_cannot_enable_duplicate_setup_from_an_empty_looking_list()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken; var landing = BuildLanding(rig);
            var store = new NavigationMetadataStore([]); var setupCalls = 0;
            using var connection = new CancellationTokenSource(); using var window = new CancellationTokenSource();
            var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
                view => Task.CompletedTask, (space, settlement) => Task.CompletedTask, store, reference => Task.CompletedTask,
                connection.Token, window.Token, actual => { }, (folder, cancellation) => { setupCalls++; throw new InvalidOperationException("Must not create during unavailable census."); });
            try
            {
                await page.ActivateAsync(token); Assert.True(page.TrySetValue("ManualFolderPath", Path.GetFullPath("existing-folder")));
                Assert.True(page.IsActionAvailable("dev.references.create-manual-project"));
                store.Unavailable = true; await page.DispatchAsync("dev.references.refresh", null, token);
                Assert.True(page.TryGetValue("Projects", out var rows)); Assert.Empty(Assert.IsType<object[]>(rows));
                Assert.True(page.TryGetValue("Coverage", out var coverage)); Assert.Contains("catalog storage unavailable", Assert.IsType<string>(coverage));
                Assert.False(page.IsActionAvailable("dev.references.create-manual-project")); Assert.False(page.IsActionAvailable("dev.references.open-task"));
                Assert.Equal(0, setupCalls); Assert.Equal(0, store.Writes);
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    [AvaloniaFact]
    public async Task Template_catalogue_uses_typed_facets_and_provider_scoped_selection_keys()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig);
            var web = new NavigationTemplateProvider("provider.web", new("shared-template", "CUI Web", "web",
                ["CUI"], [], ["Browser"], ["Build"], [], "provider.web", true, ["browser"], "org.web"));
            var desktop = new NavigationTemplateProvider("provider.desktop", new("shared-template", "CUI Desktop", "desktop",
                ["CUI"], [".NET"], ["Linux"], ["Build"],
                [new("dotnet", "C#", ".NET SDK", null, null, true)], "provider.desktop", false, ["net10.0"], "org.desktop"),
                [new("dotnet", DeveloperToolchainState.MissingCompilerOrRuntime, ">=10", null, "install-dotnet", "Install the required SDK.")]);
            var providers = new IDeveloperProjectTemplateContentProvider[] { web, desktop };
            var catalog = new DeveloperProjectCatalog(providers);
            var materializer = new DeveloperProjectTemplateMaterializer(providers);
            using var connection = new CancellationTokenSource(); using var window = new CancellationTokenSource();
            var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
                view => Task.CompletedTask, (space, settlement) => Task.CompletedTask, null, null,
                connection.Token, window.Token, actual => { }, templateCatalog: catalog, templateMaterializer: materializer,
                registerTemplateProject: (folder, payload, cancellation) => Task.FromResult(new DeveloperProjectReference(
                    Guid.NewGuid(), 0, Guid.NewGuid(), 0, Guid.NewGuid())),
                openTemplateProject: (reference, payload) => Task.CompletedTask);
            try
            {
                await page.ActivateAsync(token);
                Assert.True(page.TryGetValue("Templates", out var unfilteredObject));
                var unfilteredRows = Assert.IsAssignableFrom<object[]>(unfilteredObject);
                Assert.Equal(2, unfilteredRows.Length);
                var selectionKeys = unfilteredRows.Select(row =>
                {
                    Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "SelectionKey", out var key));
                    return Assert.IsType<string>(key);
                }).ToArray();
                Assert.Equal(2, selectionKeys.Distinct(StringComparer.Ordinal).Count());
                Assert.True(page.TryGetValue("TemplateProviderOptions", out var providersObject));
                var providerOptions = Assert.IsType<string[]>(providersObject);
                var providerIndex = Array.IndexOf(providerOptions, "provider.desktop");
                Assert.True(providerIndex > 0);
                Assert.True(page.TrySetValue("TemplateProviderIndex", providerIndex));

                Assert.True(page.TryGetValue("TemplateProjectTypeOptions", out var projectTypesObject));
                var projectTypeIndex = Array.IndexOf(Assert.IsType<string[]>(projectTypesObject), "desktop");
                Assert.True(projectTypeIndex > 0);
                Assert.True(page.TrySetValue("TemplateProjectTypeIndex", projectTypeIndex));
                Assert.True(page.TryGetValue("TemplateLanguageOptions", out var languagesObject));
                var languageIndex = Array.IndexOf(Assert.IsType<string[]>(languagesObject), "CUI");
                Assert.True(languageIndex > 0);
                Assert.True(page.TrySetValue("TemplateLanguageIndex", languageIndex));
                Assert.True(page.TryGetValue("TemplateFrameworkOptions", out var frameworksObject));
                var frameworkIndex = Array.IndexOf(Assert.IsType<string[]>(frameworksObject), ".NET");
                Assert.True(frameworkIndex > 0);
                Assert.True(page.TrySetValue("TemplateFrameworkIndex", frameworkIndex));
                Assert.True(page.TryGetValue("TemplateRuntimeOptions", out var runtimesObject));
                var runtimeIndex = Array.IndexOf(Assert.IsType<string[]>(runtimesObject), "net10.0");
                Assert.True(runtimeIndex > 0);
                Assert.True(page.TrySetValue("TemplateRuntimeIndex", runtimeIndex));
                Assert.True(page.TryGetValue("TemplatePlatformOptions", out var platformsObject));
                var platformIndex = Array.IndexOf(Assert.IsType<string[]>(platformsObject), "Linux");
                Assert.True(platformIndex > 0);
                Assert.True(page.TrySetValue("TemplatePlatformIndex", platformIndex));
                Assert.True(page.TryGetValue("TemplateOrganisationOptions", out var organisationObject));
                var organisationIndex = Array.IndexOf(Assert.IsType<string[]>(organisationObject), "org.desktop");
                Assert.True(organisationIndex > 0);
                Assert.True(page.TrySetValue("TemplateOrganisationIndex", organisationIndex));
                Assert.True(page.TrySetValue("TemplateReadinessIndex", 2));

                Assert.True(page.TryGetValue("Templates", out var templatesObject));
                var rows = Assert.IsAssignableFrom<object[]>(templatesObject);
                var row = Assert.Single(rows);
                Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "ProviderId", out var actualProvider));
                Assert.Equal("provider.desktop", actualProvider);
                Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "SelectionKey", out var keyObject));
                Assert.True(page.TrySetValue("ManualFolderPath", Path.GetFullPath("template-project")));
                await page.DispatchAsync("dev.templates.select", keyObject, token);
                Assert.True(page.TryGetValue("SelectedTemplate", out var selectedObject));
                Assert.Contains("provider.desktop", Assert.IsType<string>(selectedObject));
                Assert.True(page.TryGetValue("TemplateFilterStatus", out var statusObject));
                Assert.Contains("1 of 2", Assert.IsType<string>(statusObject));
                Assert.True(page.IsActionAvailable("dev.templates.review"));
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    [AvaloniaFact]
    public async Task Atlas_Build_keyboard_navigation_retains_the_exact_folder_and_review_without_creating_a_project()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig); var store = new NavigationMetadataStore([]);
            var provider = new NavigationTemplateProvider("installed.cui", new("page", "CUI page", "cui",
                ["CUI"], [], [], ["Build"], [], "installed.cui", true));
            var createCalls = 0; var openCalls = 0;
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var windowLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
                _ => { openCalls++; return Task.CompletedTask; }, (_, _) => { openCalls++; return Task.CompletedTask; },
                store, _ => { openCalls++; return Task.CompletedTask; }, connection.Token, windowLifetime.Token, _ => { },
                createManualProject: (_, _) => { createCalls++; throw new InvalidOperationException("Navigation must not create a project."); },
                templateCatalog: new([provider]), templateMaterializer: new([provider]),
                registerTemplateProject: (_, _, _) => { createCalls++; throw new InvalidOperationException("Navigation must not register a template."); },
                openTemplateProject: (_, _) => { openCalls++; return Task.CompletedTask; });
            var window = new Window { Width = 1440, Height = 900, Content = page };
            try
            {
                window.Show(); await page.ActivateAsync(token); StartLayout(window);
                await PressStartButtonAsync(page, window, "dev-start-new", "StartIsNew", token);
                var folder = Path.Combine(rig.Root, "Current project draft");
                Assert.True(page.TrySetValue("ManualFolderPath", folder));
                Assert.True(page.TryGetValue("Templates", out var templates));
                var template = Assert.Single(Assert.IsAssignableFrom<object[]>(templates));
                Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(template, "SelectionKey", out var key));
                await page.DispatchAsync("dev.templates.select", key, token);
                await page.DispatchAsync("dev.templates.review", null, token);
                Assert.True(page.IsActionAvailable("dev.templates.create"));
                Assert.True(page.TryGetValue("TemplateReviewStatus", out var originalReview));

                await PressStartButtonAsync(page, window, "dev-start-open", "StartIsOpen", token);
                Assert.True(StartControl<Button>(page, "dev-create-manual").IsEffectivelyVisible);
                await PressStartButtonAsync(page, window, "dev-start-projects", "StartIsProjects", token);
                Assert.False(StartControl<Button>(page, "dev-create-manual").IsEffectivelyVisible);
                await PressStartButtonAsync(page, window, "dev-start-new", "StartIsNew", token);
                Assert.True(page.TryGetValue("ManualFolderPath", out var retainedFolder)); Assert.Equal(folder, retainedFolder);
                Assert.True(page.TryGetValue("TemplateReviewStatus", out var retainedReview)); Assert.Equal(originalReview, retainedReview);
                Assert.True(page.IsActionAvailable("dev.templates.create"));
                Assert.Equal(0, createCalls); Assert.Equal(0, openCalls); Assert.Equal(0, store.Writes);

                await page.DispatchAsync("dev.start.extension", null, token); StartLayout(window);
                Assert.True(StartControl<Control>(page, "dev-extension-unavailable").IsEffectivelyVisible);
                Assert.False(StartControl<Button>(page, "dev-create-template-project").IsEffectivelyVisible);
                Assert.Equal(0, createCalls);
            }
            finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
        });
    }

    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Atlas_Build_start_uses_actual_compiled_controls_at_720_and_1440(string themeName)
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken; var landing = BuildLanding(rig);
            var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.Combine(rig.Root, "source"));
            var workspace = DeveloperWorkspace.Create([root]) with
            { Projects = [new(Guid.NewGuid(), "code", "Existing source project", [root.RootId], "CUI", null, "none", [], [], [], [], null)] };
            var provider = new NavigationTemplateProvider("installed.cui", new("page", "CUI page", "cui",
                ["CUI"], [], [], ["Build"], [], "installed.cui", true));
            var store = new NavigationMetadataStore([workspace]);
            var source = new BuildThemeSource(); if (themeName == "Professional") source.SelectProfessional();
            using var theme = new CuiThemePreferenceContext(source);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var windowLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
                _ => Task.CompletedTask, (_, _) => Task.CompletedTask, store, _ => Task.CompletedTask,
                connection.Token, windowLifetime.Token, _ => { }, templateCatalog: new([provider]), templateMaterializer: new([provider]),
                registerTemplateProject: (_, _, _) => throw new InvalidOperationException("Rendering must not register a project."),
                openTemplateProject: (_, _) => throw new InvalidOperationException("Rendering must not open a project."));
            page.BindInterfaceTheme(theme);
            var window = new Window { Width = 1440, Height = 900, Content = page };
            try
            {
                window.Show(); await page.ActivateAsync(token);
                foreach (var size in new[] { new Size(1440, 900), new Size(720, 650) })
                {
                    window.Width = size.Width; window.Height = size.Height; StartLayout(window);
                    await page.DispatchAsync("dev.start.projects", null, token); StartLayout(window);
                    Assert.Equal(size.Width == 1440 ? 210 : 72, StartControl<Control>(page, "dev-start-sidebar").Bounds.Width);
                    Assert.True(StartControl<Button>(page, "dev-start-new-card").IsEffectivelyVisible);
                    AssertStartFits(page, "dev-project-query", "dev-start-new-card", "dev-start-open-card", "dev-start-extension-card");
                    Assert.True(page.TryGetValue("Projects", out var value));
                    var row = Assert.Single(Assert.IsAssignableFrom<object[]>(value));
                    Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "DisplaySummary", out var location));
                    Assert.Equal(root.Location, location);
                    Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "DetailsVisible", out var details));
                    Assert.Equal(false, details);
                    CaptureStartFrame(window, $"build-projects-{themeName}-{size.Width:0}x{size.Height:0}");

                    await page.DispatchAsync("dev.start.new", null, token); StartLayout(window);
                    Assert.True(StartControl<Control>(page, "dev-setup-summary").IsEffectivelyVisible);
                    Assert.False(StartControl<Control>(page, "dev-landing-saved-section").IsEffectivelyVisible);
                    AssertStartFits(page, "dev-manual-folder", "dev-template-query", "dev-setup-summary");
                    CaptureStartFrame(window, $"build-new-project-{themeName}-{size.Width:0}x{size.Height:0}");

                    await page.DispatchAsync("dev.start.open", null, token); StartLayout(window);
                    Assert.True(StartControl<Button>(page, "dev-create-manual").IsEffectivelyVisible);
                    Assert.False(StartControl<Control>(page, "dev-setup-summary").IsEffectivelyVisible);
                    AssertStartFits(page, "dev-manual-folder", "dev-project-query");
                    CaptureStartFrame(window, $"build-open-project-{themeName}-{size.Width:0}x{size.Height:0}");
                }
                Assert.Equal(0, store.Writes);
            }
            finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
        });
    }

    private static T StartControl<T>(Control root, string id) where T : Control => Assert.Single(
        AuthoredCuiControls.Enumerate(root).OfType<T>(), control => control.Name == id || AutomationProperties.GetAutomationId(control) == id);

    private static void StartLayout(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private static async Task PressStartButtonAsync(DeveloperSavedProjectsPage page, Window window, string id,
        string expectedBinding, CancellationToken token)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { if (page.TryGetValue(expectedBinding, out var value) && value is true) changed.TrySetResult(); }
        page.PropertyChanged += Observe;
        try
        {
            var button = StartControl<Button>(page, id); Assert.True(button.IsEffectivelyEnabled); Assert.True(button.Focus());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), token); StartLayout(window);
        }
        finally { page.PropertyChanged -= Observe; }
    }

    private static void AssertStartFits(Control page, params string[] ids)
    {
        foreach (var id in ids)
        {
            var control = StartControl<Control>(page, id); var point = control.TranslatePoint(default, page);
            Assert.True(control.IsEffectivelyVisible, id); Assert.NotNull(point); Assert.True(control.Bounds.Width > 0, id);
            Assert.InRange(point.Value.X, -1, page.Bounds.Width);
            Assert.True(point.Value.X + control.Bounds.Width <= page.Bounds.Width + 1, id + " exceeds the horizontal viewport.");
        }
    }

    private static void CaptureStartFrame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") ?? Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    private sealed class NavigationTemplateProvider(string providerId, DeveloperProjectTemplate template,
        IReadOnlyList<DeveloperToolchainReadiness>? readiness = null) : IDeveloperProjectTemplateContentProvider
    {
        public string ProviderId => providerId;
        public Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<DeveloperProjectTemplate>>([template]); }
        public Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(DeveloperProjectTemplate selected, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(readiness ?? Array.Empty<DeveloperToolchainReadiness>()); }
        public Task<IReadOnlyList<DeveloperProjectTemplateFile>> GetFilesAsync(DeveloperProjectTemplate selected, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<DeveloperProjectTemplateFile>>([new("Main.cui", "<Cui version=\"1\" />")]); }
    }

    private sealed class NavigationMetadataStore(IReadOnlyList<DeveloperWorkspace> workspaces) : IDeveloperWorkspaceStore
    {
        internal int ListCalls, Writes;
        internal bool Unavailable;
        public Task<DeveloperOperationResult<IReadOnlyList<DeveloperWorkspace>>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++; cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Unavailable
                ? DeveloperOperationResult<IReadOnlyList<DeveloperWorkspace>>.Failure(DeveloperOperationErrorCode.StorageUnavailable, "catalog storage unavailable", "catalog")
                : DeveloperOperationResult<IReadOnlyList<DeveloperWorkspace>>.Success(workspaces));
        }
        public Task<DeveloperOperationResult<DeveloperWorkspace>> CreateAsync(DeveloperWorkspace workspace, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Enumeration does not create workspaces."); }
        public Task<DeveloperOperationResult<DeveloperWorkspace>> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeveloperOperationResult<DeveloperWorkspace>> SaveAsync(DeveloperWorkspace workspace, long expectedRevision, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Enumeration does not save workspaces."); }
    }
}
