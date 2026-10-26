using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Views.Pages.Home;

namespace Haven.Desktop.Tests.HuggingFace;

public sealed class HomeHuggingFaceBrowserTests
{
    [AvaloniaFact]
    public async Task Public_search_and_install_show_verified_details_without_claiming_runtime_readiness()
    {
        var service = new FakeService { TokenAvailable = false };
        using var browser = Create(service);
        var window = new Window { Content = browser, Width = 900, Height = 760 };
        window.Show();
        try
        {
            Find<TextBox>(browser, "hf-query").Text = "small instruct";
            Click(browser, "hf-search");
            await service.SearchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var repositories = Find<ListBox>(browser, "hf-repositories");
            Assert.Single(repositories.ItemsSource!.Cast<HuggingFaceModelSummary>());
            Assert.Equal("small instruct", service.LastQuery);

            repositories.SelectedIndex = 0;
            await service.DetailsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var fileList = Find<ListBox>(browser, "hf-files");
            Assert.Single(fileList.ItemsSource!.Cast<HuggingFaceModelFile>());
            Assert.Contains("MIT", Find<TextBlock>(browser, "hf-repository-details").Text);
            Assert.Contains("0123456789ab", Find<TextBlock>(browser, "hf-repository-details").Text);

            fileList.SelectedIndex = 0;
            Click(browser, "hf-install");
            await service.InstallCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var status = Find<TextBlock>(browser, "hf-install-status").Text;
            Assert.Contains("Awaiting Dulche engine observation", status);
            Assert.Contains("no inference was started", status);
            Assert.Equal("profile-a", service.LastInstallProfile);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Gated_install_requires_saved_token_and_token_input_is_masked_and_cleared()
    {
        var service = new FakeService { TokenAvailable = false, Gated = true };
        using var browser = Create(service);
        var window = new Window { Content = browser, Width = 900, Height = 760 };
        window.Show();
        try
        {
            await browser.ActivateAsync(CancellationToken.None);
            var query = Find<TextBox>(browser, "hf-query");
            query.Text = "private model";
            Click(browser, "hf-search");
            await service.SearchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Find<ListBox>(browser, "hf-repositories").SelectedIndex = 0;
            await service.DetailsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var files = Find<ListBox>(browser, "hf-files");
            files.SelectedIndex = 0;
            var install = Find<Button>(browser, "hf-install");
            Assert.False(install.IsEnabled);

            var token = Find<TextBox>(browser, "hf-token");
            Assert.Equal('•', token.PasswordChar);
            token.Text = "hf-secret-value";
            Click(browser, "hf-token-save");
            Assert.Equal(string.Empty, token.Text);
            await service.TokenSaved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("hf-secret-value", service.SavedToken);
            Assert.DoesNotContain("hf-secret-value", Find<TextBlock>(browser, "hf-token-status").Text);
            Assert.True(install.IsEnabled);

            Click(browser, "hf-token-remove");
            await service.TokenRemoved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(install.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Authentication_revision_change_cancels_install_even_when_profile_id_is_unchanged()
    {
        var service = new FakeService { HoldInstall = true };
        using var browser = Create(service);
        var window = new Window { Content = browser, Width = 900, Height = 760 };
        window.Show();
        try
        {
            Find<TextBox>(browser, "hf-query").Text = "small instruct";
            Click(browser, "hf-search");
            await service.SearchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Find<ListBox>(browser, "hf-repositories").SelectedIndex = 0;
            await service.DetailsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Find<ListBox>(browser, "hf-files").SelectedIndex = 0;
            Click(browser, "hf-install");
            await service.InstallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.CurrentActor = service.CurrentActor with { AuthenticationRevision = "revision-b" };
            await service.InstallCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(service.InstallToken.IsCancellationRequested);
            Assert.DoesNotContain("Installed org/tiny-instruct-GGUF", Find<TextBlock>(browser, "hf-install-status").Text);
        }
        finally { service.ReleaseInstall.TrySetResult(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Deactivation_cancels_in_flight_install_and_drops_late_ui_result()
    {
        var service = new FakeService { HoldInstall = true };
        using var browser = Create(service);
        var window = new Window { Content = browser, Width = 900, Height = 760 };
        window.Show();
        try
        {
            Find<TextBox>(browser, "hf-query").Text = "small instruct";
            Click(browser, "hf-search");
            await service.SearchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Find<ListBox>(browser, "hf-repositories").SelectedIndex = 0;
            await service.DetailsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Find<ListBox>(browser, "hf-files").SelectedIndex = 0;
            Click(browser, "hf-install");
            await service.InstallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            browser.Deactivate();
            Assert.True(service.InstallToken.IsCancellationRequested);
            service.ReleaseInstall.TrySetResult();
            await service.InstallCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain("Installed model", Find<TextBlock>(browser, "hf-install-status").Text);
        }
        finally { service.ReleaseInstall.TrySetResult(); window.Close(); }
    }

    private static HomeHuggingFaceBrowser Create(FakeService service) => new(service,
        _ => ValueTask.FromResult<AuthenticatedResourceActor?>(service.CurrentActor),
        async (body, token) => await body(token));

    private static void Click(Control root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == id);

    private sealed class FakeService : IHuggingFaceModelService
    {
        private readonly HuggingFaceModelSummary _summary = new("org/tiny-instruct-GGUF", 1200, 34, false);
        private readonly HuggingFaceModelDetails _details = new("org/tiny-instruct-GGUF", "0123456789abcdef", "MIT", false,
            [new("tiny-instruct.Q4_K_M.gguf", 1024, new string('a', 64))]);
        public bool TokenAvailable { get; set; }
        public bool Gated { get; set; }
        public bool HoldInstall { get; set; }
        public AuthenticatedResourceActor CurrentActor { get; set; } = new("actor-a", "profile-a", null, null, "revision-a");
        public string? LastQuery { get; private set; }
        public string? LastInstallProfile { get; private set; }
        public string? SavedToken { get; private set; }
        public CancellationToken InstallToken { get; private set; }
        public TaskCompletionSource SearchCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DetailsCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TokenSaved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TokenRemoved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InstallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InstallCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseInstall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<HuggingFaceModelSummary>> SearchAsync(string profileId, string query, CancellationToken cancellationToken)
        {
            Assert.Equal("profile-a", profileId);
            LastQuery = query;
            SearchCompleted.TrySetResult();
            return Task.FromResult<IReadOnlyList<HuggingFaceModelSummary>>([_summary with { Gated }]);
        }

        public Task<HuggingFaceModelDetails> GetDetailsAsync(string profileId, string repositoryId, CancellationToken cancellationToken)
        {
            Assert.Equal("profile-a", profileId);
            DetailsCompleted.TrySetResult();
            return Task.FromResult(_details with { Gated });
        }

        public async Task<HuggingFaceInstallResult> InstallAsync(string profileId, HuggingFaceModelDetails model,
            HuggingFaceModelFile file, IProgress<HuggingFaceInstallProgress>? progress, CancellationToken cancellationToken)
        {
            LastInstallProfile = profileId;
            InstallToken = cancellationToken;
            InstallStarted.TrySetResult();
            try
            {
                if (HoldInstall) await ReleaseInstall.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(HuggingFaceInstallPhase.Downloading, 1024, 1024));
                return new("org/tiny-instruct-GGUF/tiny-instruct.Q4_K_M.gguf", model.RepositoryId, model.CommitSha,
                    file.Path, file.Sha256, file.Size);
            }
            finally { InstallCompleted.TrySetResult(); }
        }

        public Task<bool> HasTokenAsync(string profileId, CancellationToken cancellationToken) => Task.FromResult(TokenAvailable);
        public Task SaveTokenAsync(string profileId, string token, CancellationToken cancellationToken)
        {
            SavedToken = token;
            TokenAvailable = true;
            TokenSaved.TrySetResult();
            return Task.CompletedTask;
        }
        public Task RemoveTokenAsync(string profileId, CancellationToken cancellationToken)
        {
            TokenAvailable = false;
            TokenRemoved.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
