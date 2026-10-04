using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Settings;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class SettingsHavenSceneTests
{
    [Fact]
    public void Scene_uses_persistent_haven_ui_categories()
    {
        using var scene = new SettingsHavenScene();

        Assert.IsType<Page>(scene.Root);
        Assert.Equal("home", scene.ActiveSection);
        Assert.NotEmpty(scene.Sidebar.Conditions);
        Assert.NotEmpty(scene.CompactNavigation.Conditions);
        scene.NavigateTo("permissions");

        Assert.Equal("permissions", scene.ActiveSection);
        Assert.Equal("Permissions & Sandboxing", scene.PageTitle.Content);
        Assert.NotNull(scene.FilePermissionSelect);
        Assert.NotNull(scene.SavePermissionsButton);
    }

    [Fact]
    public void Search_routes_to_real_settings_metadata()
    {
        using var scene = new SettingsHavenScene();
        scene.SearchInput.Text = "tool permissions";

        Assert.True(scene.RunSearch());
        Assert.Equal("permissions", scene.ActiveSection);
        Assert.Contains("Opened Permissions & Sandboxing", scene.StatusText.Content);
    }

    [Fact]
    public void Provider_secret_search_routes_to_integration_transparency_surface()
    {
        using var scene = new SettingsHavenScene();
        scene.SearchInput.Text = "api key";

        Assert.True(scene.RunSearch());
        Assert.Equal("integrations", scene.ActiveSection);
    }

    [Fact]
    public void Destructive_model_removal_has_a_second_confirmation_surface()
    {
        using var scene = new SettingsHavenScene();
        scene.SetModels(["qwen3:4b"], "qwen3:4b");

        Assert.Equal(HavenVisibility.Collapsed, scene.DeleteConfirmation.GetValue(HavenProperties.Visibility));
        scene.SetDeleteConfirmation(true);
        Assert.Equal(HavenVisibility.Visible, scene.DeleteConfirmation.GetValue(HavenProperties.Visibility));
        Assert.NotNull(scene.ConfirmDeleteButton);
        Assert.NotNull(scene.CancelDeleteButton);
    }

    [Fact]
    public void Connections_render_live_state_with_secure_secret_entry()
    {
        using var scene = new SettingsHavenScene();
        scene.NavigateTo("integrations");
        var service = new ServiceConnectionSnapshot(CalendarProviderKind.Google, "Google Calendar", "Read and manage calendar events", "Connected", "student@example.com", "Synced just now", "Ready", true, true, false);
        var provider = new ProviderConnectionSnapshot("openai", "OpenAI", "OpenAI-compatible", "https://api.openai.com/v1", "Connected", "Healthy", "GPT models available", true, true, false);
        scene.SetConnections([service], [provider], [], "No MCP connections configured yet.", "Connection status is up to date.");
        var all = scene.Root.DescendantsAndSelf().ToArray();
        var serviceCard = Assert.Single(all, item => item.Name == "Settings.Integrations.Service.Google");
        var providerCard = Assert.Single(all, item => item.Name == "Settings.Integrations.Provider.openai");
        var endpoint = Assert.IsType<Input>(Assert.Single(providerCard.DescendantsAndSelf(), item => item.Name == "Settings.Integrations.Provider.openai.Endpoint"));
        Assert.Equal("https://api.openai.com/v1", endpoint.Text);
        Assert.True(endpoint.GetValue(HavenProperties.Enabled));
        var connect = Assert.IsType<Button>(Assert.Single(serviceCard.DescendantsAndSelf(), item => item.Name == "Settings.Integrations.Service.Google.Connect"));
        var disconnect = Assert.IsType<Button>(Assert.Single(serviceCard.DescendantsAndSelf(), item => item.Name == "Settings.Integrations.Service.Google.Disconnect"));
        Assert.False(connect.GetValue(HavenProperties.Enabled));
        Assert.True(disconnect.GetValue(HavenProperties.Enabled));
        Assert.Contains(providerCard.DescendantsAndSelf().OfType<Button>(), b => b.Content == "Update connection");
        Assert.Contains(providerCard.DescendantsAndSelf().OfType<Button>(), b => b.Content == "Test connection");
        Assert.Contains(providerCard.DescendantsAndSelf().OfType<Button>(), b => b.Content == "Disconnect");
        var secret = Assert.Single(providerCard.DescendantsAndSelf().OfType<Input>(), input => input.IsSecret);
        Assert.Equal("Leave blank to keep the saved API key", secret.Placeholder);
        Assert.False(secret.CanExposeSecretToClipboard);
    }

    [Fact]
    public void Unconfigured_provider_exposes_secure_connect_input_but_not_unsafe_actions()
    {
        using var scene = new SettingsHavenScene();
        scene.NavigateTo("integrations");
        var provider = new ProviderConnectionSnapshot("anthropic", "Anthropic", "Anthropic", "https://api.anthropic.com", "Not connected", string.Empty, string.Empty, false, false, false);
        scene.SetConnections([], [provider], [], "No MCP connections configured yet.", "Provider setup requires secure secret entry.");
        var card = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "Settings.Integrations.Provider.anthropic");
        var buttons = card.DescendantsAndSelf().OfType<Button>().ToArray();
        Assert.True(Assert.Single(buttons, button => button.Content == "Connect").GetValue(HavenProperties.Enabled));
        Assert.False(Assert.Single(buttons, button => button.Content == "Test connection").GetValue(HavenProperties.Enabled));
        Assert.False(Assert.Single(buttons, button => button.Content == "Disconnect").GetValue(HavenProperties.Enabled));
        Assert.True(Assert.Single(card.DescendantsAndSelf().OfType<Input>(), input => input.IsSecret).GetValue(HavenProperties.Enabled));
    }

    [Fact]
    public async Task Suggested_mcp_connection_requires_access_review_before_connect_event()
    {
        using var scene = new SettingsHavenScene();
        scene.NavigateTo("integrations");
        scene.SetMcpSuggestions([
            new McpSuggestionSnapshot(
                "custom-mcp", "Custom MCP Server", "Connect another server.",
                "https://mcp.example.com:8443/mcp?mode=readonly", "MCP - Streamable HTTP - OAuth 2.1 browser sign-in", "Connect MCP Server")
        ]);

        var connectRequest = new TaskCompletionSource<(string Key, string Name, string Endpoint)>(TaskCreationOptions.RunContinuationsAsynchronously);
        scene.ConnectSuggestedMcpRequested += (key, name, endpoint) =>
        {
            connectRequest.TrySetResult((key, name, endpoint));
            return Task.CompletedTask;
        };

        var nameInput = Assert.Single(scene.Root.DescendantsAndSelf().OfType<Input>(), input => input.Name == "Settings.Integrations.Mcp.Suggest.custom-mcp.Name");
        nameInput.Text = "Team MCP";
        Invoke(FindButton(scene, "Settings.Integrations.Mcp.Suggest.custom-mcp.Connect"));

        Assert.False(connectRequest.Task.IsCompleted);
        var review = Assert.Single(scene.Root.DescendantsAndSelf(), element => element.Name == "Settings.Integrations.Mcp.Suggest.custom-mcp.Review");
        var reviewText = string.Join("\n", review.DescendantsAndSelf().OfType<Text>().Select(text => text.Content?.ToString() ?? string.Empty));
        Assert.Contains("Destination host and port: mcp.example.com:8443", reviewText);
        Assert.Contains("OAuth 2.1 in your browser", reviewText);
        Assert.Contains("does not contact the proposed endpoint", reviewText);
        Assert.Contains("tool inputs are sent only when an attached tool is invoked", reviewText);
        Assert.Contains("existing permission checks still apply", reviewText);
        Assert.DoesNotContain("mode=readonly", reviewText);
        Assert.False(nameInput.GetValue(HavenProperties.Enabled));

        Invoke(FindButton(scene, "Settings.Integrations.Mcp.Suggest.custom-mcp.Review.Cancel"));
        Assert.False(connectRequest.Task.IsCompleted);
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element.Name == "Settings.Integrations.Mcp.Suggest.custom-mcp.Review");
        Assert.True(nameInput.GetValue(HavenProperties.Enabled));

        Invoke(FindButton(scene, "Settings.Integrations.Mcp.Suggest.custom-mcp.Connect"));
        Invoke(FindButton(scene, "Settings.Integrations.Mcp.Suggest.custom-mcp.Review.Continue"));
        var request = await connectRequest.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal("custom-mcp", request.Key);
        Assert.Equal("Team MCP", request.Name);
        Assert.Equal("https://mcp.example.com:8443/mcp?mode=readonly", request.Endpoint);
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element.Name == "Settings.Integrations.Mcp.Suggest.custom-mcp.Review");
    }

    [Fact]
    public void Suggested_mcp_access_review_blocks_insecure_remote_endpoint_locally()
    {
        using var scene = new SettingsHavenScene();
        scene.SetMcpSuggestions([
            new McpSuggestionSnapshot(
                "custom-mcp", "Custom MCP Server", "Connect another server.",
                "http://mcp.example.com/mcp", "MCP - Streamable HTTP - OAuth 2.1 browser sign-in", "Connect MCP Server")
        ]);

        Invoke(FindButton(scene, "Settings.Integrations.Mcp.Suggest.custom-mcp.Connect"));

        var review = Assert.Single(scene.Root.DescendantsAndSelf(), element => element.Name == "Settings.Integrations.Mcp.Suggest.custom-mcp.Review");
        var continueButton = Assert.Single(review.DescendantsAndSelf().OfType<Button>(), button => button.Name == "Settings.Integrations.Mcp.Suggest.custom-mcp.Review.Continue");
        Assert.False(continueButton.GetValue(HavenProperties.Enabled));
        Assert.Contains(review.DescendantsAndSelf().OfType<Text>(), text => text.Content?.ToString()?.Contains("Remote MCP servers must use HTTPS.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Suggested_mcp_access_review_identifies_loopback_as_local_and_oauth_free()
    {
        using var scene = new SettingsHavenScene();
        scene.SetMcpSuggestions([
            new McpSuggestionSnapshot(
                "custom-mcp", "Custom MCP Server", "Connect another server.",
                "http://127.0.0.1:8000/mcp", "MCP - Streamable HTTP - local/no OAuth", "Connect MCP Server")
        ]);

        Invoke(FindButton(scene, "Settings.Integrations.Mcp.Suggest.custom-mcp.Connect"));

        var review = Assert.Single(scene.Root.DescendantsAndSelf(), element => element.Name == "Settings.Integrations.Mcp.Suggest.custom-mcp.Review");
        var reviewText = string.Join("\n", review.DescendantsAndSelf().OfType<Text>().Select(text => text.Content?.ToString() ?? string.Empty));
        var continueButton = Assert.Single(review.DescendantsAndSelf().OfType<Button>(), button => button.Name == "Settings.Integrations.Mcp.Suggest.custom-mcp.Review.Continue");
        Assert.Contains("127.0.0.1:8000", reviewText);
        Assert.Contains("Sign-in: none", reviewText);
        Assert.True(continueButton.GetValue(HavenProperties.Enabled));
    }

    [Fact]
    public void Privacy_section_exposes_persistable_controls_and_loads_store_values()
    {
        using var scene = new SettingsHavenScene();
        scene.LoadPrivacyPreferences(new PrivacyPreferences(
            LocalOnlyMode: true,
            BackgroundLearningEnabled: true,
            ModelImprovementSharingEnabled: false,
            DateTimeOffset.UtcNow));

        scene.NavigateTo("privacy");

        Assert.True(scene.LocalOnlyToggle.IsChecked);
        Assert.True(scene.BackgroundLearningToggle.IsChecked);
        Assert.False(scene.ModelImprovementSharingToggle.IsChecked);
        Assert.NotNull(scene.SavePrivacyButton);
        Assert.Equal("Privacy & Memory", scene.PageTitle.Content);
    }

    [Fact]
    public void Privacy_memory_exposes_real_background_learning_management()
    {
        using var scene = new SettingsHavenScene();
        var categories = Enum.GetValues<Haven.Core.KnowledgeCategory>().ToDictionary(category => category, _ => true);
        var snapshot = new Haven.Application.BackgroundLearningSchedulerSnapshot(
            true, Haven.Core.BackgroundLearningMode.Balanced, categories, [], DateTimeOffset.UtcNow);
        var storage = new Haven.Core.KnowledgeStorageSnapshot(
            1024, Haven.Core.KnowledgeStorageLimits.BackgroundLearningBytes, 1, 1,
            2048, Haven.Core.KnowledgeStorageLimits.ApiBankBytes, 2, 1);

        scene.SetLearningSnapshot(snapshot, storage, [], []);
        scene.NavigateTo("privacy");

        Assert.True(scene.BackgroundLearningToggle.IsChecked);
        Assert.Equal("Balanced", scene.BackgroundModeSelect.SelectedItem);
        Assert.Equal(categories.Count, scene.LearningCategoryToggles.Count);
        Assert.Contains("512 MB", scene.LearningStorageText.Content);
        Assert.Contains("1 GB", scene.LearningStorageText.Content);
        Assert.NotNull(scene.LearnMeCorrectButton);
        Assert.NotNull(scene.LearnMeRejectButton);
        Assert.NotNull(scene.ApiBankRemoveButton);
        Assert.NotNull(scene.LearningTaskCancelButton);
    }

    private static Button FindButton(SettingsHavenScene scene, string name) =>
        Assert.Single(scene.Root.DescendantsAndSelf().OfType<Button>(), button => button.Name == name);

    private static void Invoke(Button button)
    {
        var method = typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(button, null);
    }
}
