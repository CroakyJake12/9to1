using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Browse;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Owning Windows Home admission controls. Compile-only on Linux; no simulated installed admission.</summary>
public sealed class FilesBrowseAppContentTests
{
    [AvaloniaFact]
    public async Task Actual_Home_factory_owns_distinct_Files_views_and_retains_thread_neutral_original_tasks()
    {
        var token = TestContext.Current.CancellationToken;
        await using var home = new WindowsHomeSameProcessCompositionTests.Control();
        await home.StartAsync();
        var chosen = Path.Combine(Path.GetDirectoryName(home.ProfileStatePath)!, "files"); Directory.CreateDirectory(chosen);
        var workspace = await home.Files.ConfigureNewAsync(chosen, home.Provider.GetRequiredService<HomeLocalStoreOwnership>(), token);
        var factory = FilesBrowseAppContentFactory.BindOriginalSameProcess(home.Provider, home.Home, home.AppLifetime.Token, home.WindowLifetime.Token);
        var address = FilesBrowseAppContentAddress.For(workspace.Configuration.StoreId, workspace.Configuration.AppFolders["write"].Value);
        BrowseAppContentRequest Request() => new(Guid.NewGuid(), address, workspace.Actor.ProfileId, BrowserTabPrivacy.Standard);
        var first = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(factory.CreateSession(Request()));
        var second = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(factory.CreateSession(Request()));
        Assert.Null(first.OriginalInitialization); Assert.Throws<InvalidOperationException>(() => first.OriginalNativeView);
        var firstWindow = new Window(); var secondWindow = new Window();
        try
        {
            var actual = await Task.Factory.StartNew(() => first.InitializeAsync(token), token,
                TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            Assert.Same(actual, first.OriginalInitialization); Assert.Same(actual, first.InitializeAsync(token));
            await actual; await second.InitializeAsync(token);
            var firstView = Assert.IsType<FilesNativeBrowserSurface>(first.OriginalNativeView);
            var secondView = Assert.IsType<FilesNativeBrowserSurface>(second.OriginalNativeView);
            Assert.NotSame(firstView, secondView); Assert.Equal(address, first.Address); Assert.Equal(address, second.Address);
            firstWindow.Content = firstView; secondWindow.Content = secondView; firstWindow.Show(); secondWindow.Show();
            firstWindow.UpdateLayout(); secondWindow.UpdateLayout();
            var name = firstView.GetVisualDescendants().OfType<TextBox>().Single(input => input.PlaceholderText == "Folder or item name");
            name.Text = "Unsaved intent";
            Assert.False(await Task.Run(() => first.TryPrepareToCloseAsync(token), token));
            Assert.Null(first.OriginalClose); Assert.True(first.IsPublicationCurrent); Assert.Same(firstView, first.OriginalNativeView);
            name.Text = "";
            Assert.True(await Task.Run(() => first.TryPrepareToCloseAsync(token), token));
            var close = first.CloseAndDrainAsync(); Assert.Same(close, first.CloseAndDrainAsync()); Assert.Same(close, first.OriginalClose);
            await close;
            Assert.True(first.CanRetireOriginalView); Assert.False(first.IsPublicationCurrent);
            Assert.True(second.IsPublicationCurrent); Assert.Same(secondView, second.OriginalNativeView);
            Assert.Null(home.Home.OriginalCloseTask);
        }
        finally
        {
            await first.CloseAndDrainAsync(); await second.CloseAndDrainAsync();
            firstWindow.Content = null; secondWindow.Content = null; firstWindow.Close(); secondWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task Caller_profile_and_foreign_store_are_refused_by_actual_Home_and_keep_failed_owner_tasks()
    {
        var token = TestContext.Current.CancellationToken;
        await using var home = new WindowsHomeSameProcessCompositionTests.Control(); await home.StartAsync();
        var chosen = Path.Combine(Path.GetDirectoryName(home.ProfileStatePath)!, "files"); Directory.CreateDirectory(chosen);
        var workspace = await home.Files.ConfigureNewAsync(chosen, home.Provider.GetRequiredService<HomeLocalStoreOwnership>(), token);
        var factory = FilesBrowseAppContentFactory.BindOriginalSameProcess(home.Provider, home.Home, home.AppLifetime.Token, home.WindowLifetime.Token);
        foreach (var request in new[]
        {
            new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://files"), "caller-claimed-profile", BrowserTabPrivacy.Standard),
            new BrowseAppContentRequest(Guid.NewGuid(), FilesBrowseAppContentAddress.For(Guid.NewGuid(), null), workspace.Actor.ProfileId, BrowserTabPrivacy.Standard)
        })
        {
            var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(factory.CreateSession(request));
            var actual = session.InitializeAsync(token);
            Assert.NotNull(await Record.ExceptionAsync(() => actual)); Assert.Same(actual, session.InitializeAsync(token));
            Assert.False(session.IsPublicationCurrent); Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
            var close = session.CloseAndDrainAsync();
            Assert.NotNull(await Record.ExceptionAsync(() => close)); Assert.Same(close, session.OriginalClose);
            Assert.False(session.CanRetireOriginalView); Assert.Null(home.Home.OriginalCloseTask);
        }
        Assert.Throws<NotSupportedException>(() => factory.CreateSession(new(Guid.NewGuid(), AppContentAddress.Parse("9to1://files"),
            workspace.Actor.ProfileId, BrowserTabPrivacy.Private)));
    }

    [AvaloniaFact]
    public async Task Closing_during_held_actual_profile_read_cannot_retire_before_original_work_settles()
    {
        var token = TestContext.Current.CancellationToken;
        await using var home = new WindowsHomeSameProcessCompositionTests.Control(); await home.StartAsync();
        var actor = await home.Home.Profiles.GetCurrentAsync(token); Assert.NotNull(actor);
        var factory = FilesBrowseAppContentFactory.BindOriginalSameProcess(home.Provider, home.Home, home.AppLifetime.Token, home.WindowLifetime.Token);
        var session = factory.CreateSession(new(Guid.NewGuid(), AppContentAddress.Parse("9to1://files"), actor.ProfileId, BrowserTabPrivacy.Standard));
        home.Principal.Calls = 0; home.Principal.HoldAt = 1;
        var actual = session.InitializeAsync(token);
        await home.Principal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        var raw = home.Principal.HeldOriginal!; var close = session.CloseAndDrainAsync();
        Assert.False(raw.IsCompleted); Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted); Assert.False(session.CanRetireOriginalView);
        home.Principal.Release.TrySetResult();
        Assert.NotNull(await Record.ExceptionAsync(() => actual)); Assert.NotNull(await Record.ExceptionAsync(() => close));
        Assert.True(raw.IsCompletedSuccessfully); Assert.False(session.CanRetireOriginalView); Assert.Null(home.Home.OriginalCloseTask);
    }
}
