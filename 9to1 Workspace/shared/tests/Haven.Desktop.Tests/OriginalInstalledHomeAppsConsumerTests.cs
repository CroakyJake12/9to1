#if !ANDROID
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class OriginalInstalledHomeAppsConsumerTests
{
    private static readonly List<object[]> FailedOriginalOwners = [];

    [Fact]
    public async Task Actual_shown_unconfigured_installed_Apps_page_has_no_launch_rows_and_joins_its_view()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var appLifetime = new CancellationTokenSource(); using var windowLifetime = new CancellationTokenSource();
            var window = new Window { Width = 900, Height = 640 }; var captured = new CaptureBox();
            Task? initialize = null, close = null; var failures = new List<Exception>();
            window.Show();
            try
            {
                var type = typeof(App).Assembly.GetType("Haven.Desktop.Views.Pages.Home.OriginalInstalledHomeAppsPage", throwOnError: true)!;
                var capture = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(type), captured,
                    typeof(CaptureBox).GetMethod(nameof(CaptureBox.Capture))!);
                var page = CallStatic(type, "BindOriginal", null,
                    new Func<HomeNativeStartupObservation?>(() => null), window, appLifetime.Token, windowLifetime.Token, capture);
                Assert.Same(captured.Page, page);
                window.Content = Assert.IsAssignableFrom<Control>(page); window.UpdateLayout();
                initialize = Call<Task>(page, "InitializeAsync");
                await initialize.WaitAsync(timeout.Token); window.UpdateLayout();
                Assert.Equal(0, Property<int>(page, "OriginalRowCount"));
                Assert.Contains("Installed Apps is unavailable", Property<string>(page, "Detail"));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Launch"));
                Assert.Null(Property<Task?>(page, "OriginalClose"));
                close = Call<Task>(page, "CloseAndDrainAsync");
                Assert.Same(close, Call<Task>(page, "CloseAndDrainAsync"));
                await close.WaitAsync(timeout.Token);
                Assert.True(close.IsCompletedSuccessfully);
                Assert.Null(Assert.IsAssignableFrom<UserControl>(page).Content);
                Assert.False(appLifetime.IsCancellationRequested); Assert.False(windowLifetime.IsCancellationRequested);
            }
            catch (Exception cause) { failures.Add(cause); }
            finally
            {
                if (captured.Page is { } page)
                    try { close ??= Call<Task>(page, "CloseAndDrainAsync"); }
                    catch (Exception cause) { failures.Add(cause); }
                // Root before any failure inspection; independently join every
                // actual admitted task even after a failed assertion or callback.
                FailedOriginalOwners.Add([window, captured, initialize!, close!, failures]);
                foreach (var raw in new[] { initialize, close }.Where(raw => raw is not null).Cast<Task>().Distinct<Task>(ReferenceEqualityComparer.Instance))
                    try { await raw.WaitAsync(timeout.Token); }
                    catch (Exception cause) { failures.Add(raw.Exception ?? cause); }
                if (failures.Count == 0) window.Close();
            }
            if (failures.Count != 0) throw new AggregateException("Actual unconfigured Apps page and its view-close receipts remain retained.", failures);
            return true;
        }, timeout.Token));
    }

    [Fact]
    public void Actual_locator_capture_refuses_foreign_host_or_argument_service_authority()
    {
        var unchanged = new[] { "--ordinary-argument" };
        Assert.Same(unchanged, CallStatic(typeof(App), "CaptureOriginalNativeHomeLaunchLocators", (object)unchanged));
        foreach (var arguments in new[]
        {
            new[] { "--home-root-host", "foreign.host", "--home-machine-state", Path.GetFullPath("unused-machine-state") },
            new[] { "--home-root-host", "9to1.root.home-host.v1", "--home-machine-state", "relative-state" },
            new[] { "--home-root-host", "9to1.root.home-host.v1", "--home-machine-state", Path.GetFullPath("unused-machine-state"), "--required-service", "home.state" }
        })
            Assert.Throws<InvalidDataException>(() => CallStatic(typeof(App), "CaptureOriginalNativeHomeLaunchLocators", (object)arguments));
        var actualMetadata = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        var product = CallStatic(typeof(App), "ValidateOriginalInitialProductDeclaration", (object)actualMetadata);
        if (product is null)
            Assert.Throws<InvalidDataException>(() => CallStatic(typeof(App), "CaptureOriginalNativeHomeRequirements", typeof(App).Assembly));
        else
        {
            var request = Assert.IsType<HomeCompatibilityRequest>(CallStatic(typeof(App), "CaptureOriginalNativeHomeRequirements", typeof(App).Assembly));
            Assert.Equal(product, request.AppId); Assert.Equal(typeof(App).Assembly.GetName().Version!.ToString(), request.AppVersion);
            var requirement = Assert.Single(request.RequiredServices);
            Assert.Equal("home.state", requirement.ServiceId);
            Assert.Equal(HomeCoreServiceCatalog.CurrentContractVersion.Major, requirement.MajorVersion);
            Assert.Equal(HomeCoreServiceCatalog.CurrentContractVersion.Minor, requirement.MinimumMinorVersion);
        }
        // These are locators and compiled structural declarations only. No
        // connection, installed identity, service consent or child is created.
    }
    public sealed class CaptureBox
    {
        public object? Page { get; private set; }
        public void Capture(object page)
        { if (Page is not null && !ReferenceEquals(Page, page)) throw new InvalidOperationException("Different actual page captured."); Page = page; }
    }
    private static object CallStatic(Type type, string name, params object?[] args) => Invoke(null,
        type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException(type.FullName, name), args)!;
    private static T Call<T>(object target, string name) => (T)Invoke(target,
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(target.GetType().FullName, name), [])!;
    private static T Property<T>(object target, string name) => (T)(target.GetType().GetProperty(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target)
        ?? (typeof(T).IsValueType ? throw new MissingMemberException(name) : null))!;
    private static object? Invoke(object? target, MethodInfo method, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException cause) when (cause.InnerException is { } original)
        { ExceptionDispatchInfo.Capture(original).Throw(); throw; }
    }
}
#endif
