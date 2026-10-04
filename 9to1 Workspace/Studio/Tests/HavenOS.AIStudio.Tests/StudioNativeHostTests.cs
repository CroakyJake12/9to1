using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioNativeHostTests
{
    [Fact]
    public Task Actual_configuration_scene_creates_owned_Den_and_explicit_Agent_then_mounts_editor() => ExerciseHostAsync(false);
    [Fact]
    public Task Actual_picker_import_preserves_bytes_in_owned_Den_and_native_authoring_saves_reference() => ExerciseHostAsync(true);
    [Fact]
    public Task Actual_background_Den_disposal_retires_native_editor_and_workspace_before_returning() => ExerciseHostAsync(false, true);
    private static async Task ExerciseHostAsync(bool importAvatar, bool retireFromBackground = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-native-host-" + Guid.NewGuid().ToString("N"));
        var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
        var avatarBytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
        var avatarPath = Path.Combine(root, "picked-avatar.gif");
        await File.WriteAllBytesAsync(avatarPath, avatarBytes);
        try
        {
            await using var native = HeadlessUnitTestSession.StartNew(typeof(StudioTestApplication));
            await native.Dispatch(async () =>
            {
                var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var actors = new Actors(profiles);
                var permissions = new HomePermissionTrustService(home, (app, action) => null);
                StudioNativeWindow? window = null;
                await using var den = new StudioDenLifetime(actors, ct => window?.RetireWorkspaceAsync(ct) ?? Task.CompletedTask);
                var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([den]), permissions);
                var receipts = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
                await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                var services = new ServiceCollection();
                services.AddSingleton(runtime); services.AddSingleton(profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(actors);
                services.AddSingleton(permissions); services.AddSingleton(den); services.AddSingleton(ownership);
                services.AddSingleton<IResourceStoreOwnershipReceiptAuthority>(receipts);
                services.AddSingleton(SelectedFolder(chosen, avatarPath));
                await using var provider = services.BuildServiceProvider();
                window = new StudioNativeWindow(provider); window.Show();
                try
                {
                    await window.Initialization;
                    FindButton(window, "Create a Den in an empty folder…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(async () => { try { await den.OpenBoundSessionAsync(receipts); return true; } catch (UnauthorizedAccessException) { return false; } catch (InvalidOperationException) { return false; } });
                    var session = await den.OpenBoundSessionAsync(receipts);
                    Assert.Empty(await session.Den.ListAsync<AgentDefinitionRecord>("personal"));
                    var name = All(window).OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "New Agent name");
                    name.Text = "Native authored Agent";
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                    FindButton(window, "Create Agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(async () => (await session.Den.ListAsync<AgentDefinitionRecord>("personal")).Count == 1);
                    await Until(() => Task.FromResult(All(window).OfType<Button>().Any(item => item.Content?.ToString() == "Save to Agent")));
                    var agent = Assert.Single(await session.Den.ListAsync<AgentDefinitionRecord>("personal"));
                    Assert.Equal("Native authored Agent", agent.DisplayName);
                    Assert.Null(agent.Presentation);
                    if (importAvatar)
                    {
                        FindButton(window, "Import an avatar image into this Agent…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var assetField = All(window).OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "Static fallback asset reference");
                        try { await Until(() => Task.FromResult(!string.IsNullOrEmpty(assetField.Text))); }
                        catch (OperationCanceledException error)
                        {
                            var observed = string.Join(" | ", All(window).OfType<TextBlock>().Select(item => item.Text));
                            throw new TimeoutException("The original native avatar import did not complete. Observed UI: " + observed, error);
                        }
                        var reference = (await session.Den.GetAsync<BlobReferenceRecord>("personal", assetField.Text!))!;
                        Assert.Equal(agent.Id, reference.OwnerId);
                        Assert.Equal(DenAgentPresentationAssets.AgentOwnerKind, reference.OwnerKind);
                        Assert.Equal("image/gif", reference.MediaType);
                        var imported = await session.Den.ReadAttachmentAsync("personal", reference.Id);
                        try { Assert.Equal(avatarBytes, imported); } finally { Array.Clear(imported); }
                        var accessible = All(window).OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "Accessible avatar name");
                        accessible.Text = "Imported native avatar";
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                        FindButton(window, "Apply avatar identity").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        FindButton(window, "Save to Agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Until(async () => (await session.Den.GetAsync<AgentDefinitionRecord>("personal", agent.Id))!.Presentation is not null);
                        Assert.Equal(reference.Id, (await session.Den.GetAsync<AgentDefinitionRecord>("personal", agent.Id))!.Presentation!.StaticFallbackAssetReference);
                    }
                    Assert.False(await session.Den.AccessPolicy.IsAllowedAsync(session.Actor.ActorId, "personal", agent.Id, DenPermission.Execute));
                    if (retireFromBackground)
                    {
                        await Task.Run(async () =>
                        {
                            Assert.False(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
                            await den.DisposeAsync();
                        }).WaitAsync(TimeSpan.FromSeconds(10));
                        await Assert.ThrowsAsync<ObjectDisposedException>(() => den.OpenBoundSessionAsync(receipts));
                    }
                    else
                    {
                        actors.Changed = session.Actor with { AuthenticationRevision = "changed-native-fixture-session" };
                        var denied = await Record.ExceptionAsync(() => window.ValidateWorkspaceAsync(default));
                        Assert.True(denied is UnauthorizedAccessException or DenException { Code: DenErrorCode.Forbidden });
                    }
                    Assert.Empty(All(window).OfType<ComboBox>().Single(item => AutomationProperties.GetName(item) == "Choose Agent").Items);
                    Assert.DoesNotContain(All(window).OfType<Button>(), item => item.Content?.ToString() == "Save to Agent");
                }
                finally { window.Close(); }
                return true;
            }, default);
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Actors(HomeLocalProfileIdentity profiles) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Changed { get; set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) =>
            Changed is null ? profiles.GetCurrentAsync(ct) : ValueTask.FromResult<AuthenticatedResourceActor?>(Changed);
    }
    private static IEnumerable<Control> All(Control root) => root.GetLogicalDescendants().OfType<Control>().Prepend(root);
    private static Button FindButton(Control root, string name) => All(root).OfType<Button>().Single(item => item.Content?.ToString() == name);
    private static async Task Until(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition()) await Task.Delay(10, timeout.Token);
    }
    private static IStorageProvider SelectedFolder(string path, string avatarPath)
    {
        var folder = DispatchProxy.Create<IStorageFolder, StudioStorageTestProxy>();
        ((StudioStorageTestProxy)(object)folder).Handler = (method, _) => method.Name switch
        {
            "get_Path" => new Uri(path + Path.DirectorySeparatorChar),
            "get_Name" => Path.GetFileName(path),
            "Dispose" => null,
            _ => throw new NotSupportedException(method.Name)
        };
        var file = DispatchProxy.Create<IStorageFile, StudioStorageTestProxy>();
        ((StudioStorageTestProxy)(object)file).Handler = (method, _) => method.Name switch
        {
            "get_Path" => new Uri(avatarPath),
            "get_Name" => Path.GetFileName(avatarPath),
            "OpenReadAsync" => Task.FromResult<Stream>(File.OpenRead(avatarPath)),
            "Dispose" => null,
            _ => throw new NotSupportedException(method.Name)
        };
        var picker = DispatchProxy.Create<IStorageProvider, StudioStorageTestProxy>();
        ((StudioStorageTestProxy)(object)picker).Handler = (method, args) => method.Name switch
        {
            "get_CanPickFolder" => true,
            "get_CanOpen" => true,
            "OpenFilePickerAsync" when args![0] is FilePickerOpenOptions { AllowMultiple: false } => Task.FromResult<IReadOnlyList<IStorageFile>>([file]),
            "OpenFolderPickerAsync" when args![0] is FolderPickerOpenOptions { AllowMultiple: false } => Task.FromResult<IReadOnlyList<IStorageFolder>>([folder]),
            _ => throw new NotSupportedException(method.Name)
        };
        return picker;
    }
}
public class StudioStorageTestProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
}
