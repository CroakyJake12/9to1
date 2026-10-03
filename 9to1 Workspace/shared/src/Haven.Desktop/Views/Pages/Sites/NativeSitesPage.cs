using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;
using Haven.UI;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;

namespace Haven.Desktop.Views.Pages.Sites;

/// <summary>Platform preview adapter. Its document is derived output, never project state or an access grant.</summary>
internal interface ISitePreviewSurface : IDisposable
{
    Control Control { get; }
    void Display(string? document);
}

internal sealed class NativeSitePreviewSurface : ISitePreviewSurface
{
    private readonly NativeWebView web = new();
    private string? issuedDocument;
    private Uri? issuedUri;
    private bool disposed;
    public Control Control => web;
    public NativeSitePreviewSurface()
    {
        web.IsVisible = false;
        web.NavigationStarted += Navigation;
        web.NewWindowRequested += NewWindow;
        AutomationProperties.SetName(web, "Canonical website page preview");
    }
    public void Display(string? document)
    {
        if (disposed) return;
        if (document is null)
        {
            issuedDocument = null; issuedUri = null; web.IsVisible = false;
            web.Navigate(new Uri("about:blank")); return;
        }
        web.IsVisible = true;
        if (document == issuedDocument) return;
        issuedDocument = document;
        issuedUri = new Uri("data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(document)));
        web.Navigate(issuedUri);
    }
    private void Navigation(object? sender, WebViewNavigationStartingEventArgs args)
    {
        if (args.Request?.AbsoluteUri == "about:blank") return;
        if (issuedUri is not null && args.Request is { } target && target.GetLeftPart(UriPartial.Path) == issuedUri.GetLeftPart(UriPartial.Path)) return;
        args.Cancel = true;
    }
    private static void NewWindow(object? sender, WebViewNewWindowRequestedEventArgs args) => args.Handled = true;
    public void Dispose()
    {
        if (disposed) return;
        Display(null); disposed = true; issuedDocument = null; issuedUri = null;
        web.NavigationStarted -= Navigation; web.NewWindowRequested -= NewWindow;
    }
}

/// <summary>The mounted Sites CUI authoring page. Only its exact preview slot can receive the native WebView.</summary>
public sealed class NativeSitesPage : UserControl, IActivatablePage, IDisposable
{
    private readonly SiteNativeAuthoringSession session;
    private readonly SitesHavenScene scene = new();
    private readonly ISitePreviewSurface surface;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer authorityTimer;
    private SiteAuthoringView current = new([], null, null, null, "Open a website to begin.");
    private Guid? selectedComponent;
    private sealed record ComponentDraftContext(SiteNativeWorkspaceBinding Binding, Guid SiteID, Guid ProjectID, Guid? PageID, SiteSourceBinding Source);
    private ComponentDraftContext? draftContext;
    private string? operationMessage;
    private bool disposed;
    private bool active;
    private long generation;
    private int operation;
    private int checking;
    private int pending;
    private bool lifetimeDisposed;
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;
    internal SiteAuthoringView CurrentView => current;
    internal string? DisplayedDocument { get; private set; }

    public NativeSitesPage(SiteNativeAuthoringSession session) : this(session, new NativeSitePreviewSurface()) { }
    internal NativeSitesPage(SiteNativeAuthoringSession session, ISitePreviewSurface surface)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        Scene = new HavenSceneControl(new HavenDesktopImageResolver(), new PreviewResolver(scene.PreviewHost, surface.Control)) { Root = scene.Root };
        Content = Scene; Scene.IsVisible = false;
        AutomationProperties.SetAutomationId(this, "NativeSitesPage"); AutomationProperties.SetName(this, "Sites authoring and preview");
        scene.Refresh.Invoked += Refresh;
        scene.Create.Invoked += Create;
        scene.AddPage.Invoked += AddPage;
        scene.AddComponent.Invoked += AddComponent;
        scene.Save.Invoked += Save;
        scene.RetryAudit.Invoked += RetryAudit;
        scene.SiteSelected += SelectSite;
        scene.PageSelected += SelectPage;
        scene.ComponentSelected += SelectComponent;
        authorityTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (active && !disposed && operation == 0 && Interlocked.CompareExchange(ref checking, 1, 0) == 0) _ = OwnAsync(CheckCurrentAsync);
        });
        authorityTimer.Stop();
    }
    public HavenSceneControl Scene { get; }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed) return;
        active = true; Scene.IsVisible = false; authorityTimer.Start();
        await RefreshAndDisplayAsync(cancellationToken);
    }
    public void Deactivate()
    {
        Dispatcher.UIThread.VerifyAccess(); active = false; authorityTimer.Stop(); generation++;
        // Home approval may activate another tab. The bound derived cache survives, but is never displayed without a new read check.
        session.SuspendPreview(); Scene.IsVisible = false; HideDocument();
    }

    internal Task RefreshAndDisplayAsync(CancellationToken ct = default, Guid? siteID = null, Guid? pageID = null)
        => OwnAsync(() => RefreshCoreAsync(ct, siteID, pageID));

    private async Task RefreshCoreAsync(CancellationToken ct, Guid? siteID, Guid? pageID)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed) return;
        var selected = ++generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        if (siteID is { } site && site != current.Project?.SiteId || pageID is { } page && page != current.PageID) HideDocument();
        await session.RefreshAsync(siteID, pageID, linked.Token);
        if (disposed || selected != generation) return;
        await ApplyAuthorizedAsync(selected, linked.Token);
    }

    private async Task ApplyAuthorizedAsync(long selected, CancellationToken ct, Guid? loadComponentID = null)
    {
        // Every actual mount/read is authorised by the owning session immediately before its dispatcher publication.
        var authorized = await session.ReadCurrentAsync(ct);
        if (disposed || selected != generation || ct.IsCancellationRequested) return;
        var context = authorized.Project is { } project && authorized.WorkspaceBinding is { } binding
            ? new ComponentDraftContext(binding, project.SiteId, project.ProjectId, authorized.PageID, project.Source) : null;
        if (context != draftContext) ClearComponentDraft();
        draftContext = context;
        current = operationMessage is null ? authorized : authorized with { Message = operationMessage };
        var currentPage = authorized.Project?.Pages.SingleOrDefault(row => row.PageId == authorized.PageID);
        var reachable = currentPage is not null && authorized.Project is { } pageOwner
            ? SiteAuthoringService.Reachable(pageOwner, currentPage.RootComponentIds) : new HashSet<Guid>();
        if (loadComponentID is { } selectedID && authorized.Project is { } owner)
        {
            var loaded = owner.Components.SingleOrDefault(row => row.ComponentId == selectedID && reachable.Contains(row.ComponentId));
            if (loaded is null)
            {
                ClearComponentDraft(); draftContext = context; current = authorized;
            }
            else
            {
                selectedComponent = loaded.ComponentId;
                scene.ComponentType.Text = loaded.ComponentType;
                scene.Properties.Text = JsonSerializer.Serialize(loaded.Properties, new JsonSerializerOptions { WriteIndented = true });
            }
        }
        if (selectedComponent is { } component && !reachable.Contains(component))
        {
            ClearComponentDraft(); draftContext = context; current = authorized;
        }
        scene.Apply(current, selectedComponent, session.HasPendingAudit);
        if (operation != 0) scene.SetBusy(true, session.HasPendingAudit);
        if (active)
        {
            DisplayedDocument = authorized.Preview?.Document;
            surface.Display(DisplayedDocument); Scene.IsVisible = true;
        }
        else { Scene.IsVisible = false; HideDocument(); }
        Scene.InvalidateMeasure(); Scene.InvalidateVisual();
    }

    private async Task CheckCurrentAsync()
    {
        try
        {
            var selected = generation;
            await session.ReadCurrentAsync(lifetime.Token);
            if (!active || disposed || selected != generation) return;
            await ApplyAuthorizedAsync(selected, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (IsExpected(error))
        {
            if (!disposed) { current = new([], null, null, null, "Current Sites access is unavailable. Refresh to reopen the project."); ClearComponentDraft(); scene.Apply(current, null, session.HasPendingAudit); HideDocument(); }
        }
        finally { Interlocked.Exchange(ref checking, 0); }
    }

    private void Refresh(object? sender, EventArgs e) => Start(ct => session.RefreshAsync(cancellationToken: ct));
    private void Create(object? sender, EventArgs e)
    {
        var name = scene.ProjectName.Text; var path = scene.ProjectPath.Text;
        Start(ct => session.CreateProjectAsync(name, path, ct));
    }
    private void AddPage(object? sender, EventArgs e)
    {
        var name = scene.PageName.Text; var path = scene.PagePath.Text;
        Start(ct => session.CreatePageAsync(name, path, ct));
    }
    private void AddComponent(object? sender, EventArgs e)
    {
        var type = scene.ComponentType.Text;
        try { var properties = CaptureProperties(); Start(ct => session.AddComponentAsync(type, properties, ct)); }
        catch (JsonException) { SetOperationMessage("Component properties must be a JSON object. Your draft remains editable."); }
    }
    private void Save(object? sender, EventArgs e)
    {
        if (selectedComponent is not { } component) return;
        try { var properties = CaptureProperties(); Start(ct => session.UpdatePropertiesAsync(component, properties, ct)); }
        catch (JsonException) { SetOperationMessage("Component properties must be a JSON object. Your draft remains editable."); }
    }
    private void RetryAudit(object? sender, EventArgs e) => Start(ct => session.RetryAuditAsync(ct));
    private void SelectSite(Guid siteID) { ClearComponentDraft(); HideDocument(); Start(ct => session.RefreshAsync(siteID, cancellationToken: ct)); }
    private void SelectPage(Guid pageID) { ClearComponentDraft(); HideDocument(); Start(ct => session.RefreshAsync(current.Project?.SiteId, pageID, ct)); }
    private void SelectComponent(Guid componentID)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed || Interlocked.CompareExchange(ref operation, 1, 0) != 0) return;
        PendingOperation = OwnAsync(async () =>
        {
            try
            {
                var selected = ++generation;
                await session.ReadCurrentAsync(lifetime.Token);
                if (disposed || selected != generation || lifetime.IsCancellationRequested) return;
                await ApplyAuthorizedAsync(selected, lifetime.Token, componentID);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (IsExpected(error))
            {
                if (!disposed) { current = new([], null, null, null, "Current Sites access is unavailable. Refresh to reopen the project."); ClearComponentDraft(); scene.Apply(current, null, session.HasPendingAudit); HideDocument(); }
            }
            finally { Interlocked.Exchange(ref operation, 0); if (!disposed) scene.Apply(current, selectedComponent, session.HasPendingAudit); }
        });
    }
    private IReadOnlyDictionary<string, JsonElement> CaptureProperties()
    {
        using var parsed = JsonDocument.Parse(scene.Properties.Text, new JsonDocumentOptions { MaxDepth = 32 });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Object required.");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in parsed.RootElement.EnumerateObject())
            if (!properties.TryAdd(row.Name, row.Value.Clone())) throw new JsonException("Duplicate property.");
        return properties;
    }
    private void Start(Func<CancellationToken, Task<SiteAuthoringView>> action)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed || Interlocked.CompareExchange(ref operation, 1, 0) != 0) return;
        operationMessage = null;
        scene.SetBusy(true, session.HasPendingAudit);
        PendingOperation = OwnAsync(() => RunAsync(action));
    }
    private async Task RunAsync(Func<CancellationToken, Task<SiteAuthoringView>> action)
    {
        try
        {
            var selected = ++generation;
            await action(lifetime.Token);
            if (!disposed && selected == generation) await ApplyAuthorizedAsync(selected, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (IsExpected(error))
        {
            if (disposed) return;
            if (error is UnauthorizedAccessException) { current = new([], null, null, null, "Sites access or approval is unavailable."); ClearComponentDraft(); scene.Apply(current, null, session.HasPendingAudit); HideDocument(); }
            SetOperationMessage(session.HasPendingAudit ? "The edit requires Home audit recovery. Retry the audit before making another edit." : error is JsonException ? "Properties are invalid; the draft is still editable." : "The edit could not complete. Review Home approval and refresh the current source.");
        }
        finally
        {
            Interlocked.Exchange(ref operation, 0);
            if (!disposed) scene.Apply(current, selectedComponent, session.HasPendingAudit);
        }
    }
    // All asynchronous reads, selections and writes retain the same cancellation source through settlement.
    private async Task OwnAsync(Func<Task> action)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed) return;
        pending++;
        try { await action(); }
        finally { pending--; DisposeLifetimeWhenSettled(); }
    }
    private void DisposeLifetimeWhenSettled()
    {
        if (disposed && pending == 0 && !lifetimeDisposed) { lifetimeDisposed = true; lifetime.Dispose(); }
    }
    private void SetOperationMessage(string message)
    {
        operationMessage = message; current = current with { Message = message };
        scene.Status.Content = message;
    }
    private void ClearComponentDraft()
    {
        selectedComponent = null; draftContext = null; operationMessage = null;
        scene.Properties.Text = ""; scene.ComponentType.Text = "heading";
    }
    private void HideDocument() { DisplayedDocument = null; surface.Display(null); }
    private static bool IsExpected(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or SiteNativeWriteAuditPendingException or SiteNativeAdmissionAuditPendingException or SiteOperationException;
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed) return;
        disposed = true; active = false; generation++; authorityTimer.Stop(); lifetime.Cancel();
        scene.Refresh.Invoked -= Refresh; scene.Create.Invoked -= Create; scene.AddPage.Invoked -= AddPage;
        scene.AddComponent.Invoked -= AddComponent; scene.Save.Invoked -= Save; scene.RetryAudit.Invoked -= RetryAudit;
        scene.SiteSelected -= SelectSite; scene.PageSelected -= SelectPage; scene.ComponentSelected -= SelectComponent;
        session.Dispose(); current = new([], null, null, null, "Sites is closed."); ClearComponentDraft(); Scene.IsVisible = false;
        HideDocument(); surface.Dispose(); Scene.Root = null; Content = null;
        DisposeLifetimeWhenSettled();
    }
    private sealed class PreviewResolver(Haven.UI.Components.NativeHost host, Control control) : IHavenAvaloniaNativeControlResolver
    {
        public bool TryCreate(HavenElement element, out Control? result) { result = ReferenceEquals(element, host) ? control : null; return result is not null; }
    }
}
