using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Sites.NativeUI;

/// <summary>Thin native CUI owner of the actual original Sites page and staged Home review.
/// Root supplies its same authenticated composition; this surface creates no Home/Files store.</summary>
public sealed class SitesNativeAuthoringSurface : UserControl, IAsyncDisposable, IDisposable,
    ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly SiteNativeAuthoringSession _session;
    private readonly AuthenticatedResourceActor _actor;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CancellationTokenSource _stop;
    private readonly CancellationToken _hostLifetime;
    private CancellationTokenRegistration _hostRetirement;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _tasksGate = new();
    private readonly HashSet<Task> _tasks = [];
    private readonly List<Exception> _failures = [];
    private readonly ListBox _projects = new() { MinHeight = 120 };
    private readonly ListBox _pages = new() { MinHeight = 120 };
    private readonly ListBox _components = new() { MinHeight = 120 };
    private readonly TextBox _name = new() { PlaceholderText = "Project or page name", MaxLength = 120, Width = 220 };
    private readonly TextBox _pageRenameName = new() { PlaceholderText = "Selected page name", MaxLength = 120, Width = 220 };
    private readonly TextBox _path = new() { PlaceholderText = "Relative project folder or absolute page route", MaxLength = 256, Width = 220 };
    private readonly TextBox _text = new() { PlaceholderText = "Heading or text", MaxLength = 65536, AcceptsReturn = true, MinHeight = 64 };
    private readonly TextBox _breakpoint = new() { PlaceholderText = "Breakpoint", MaxLength = 80, Width = 140 };
    private readonly TextBox _responsiveWidth = new() { PlaceholderText = "Width (e.g. 100%)", MaxLength = 128, Width = 160 };
    private readonly TextBox _responsivePadding = new() { PlaceholderText = "Padding (e.g. 16px)", MaxLength = 128, Width = 160 };
    private readonly TextBox _seoTitle = new() { PlaceholderText = "SEO title (optional)", MaxLength = 65536, Width = 220 };
    private readonly TextBox _metaDescription = new() { PlaceholderText = "Meta description (optional)", MaxLength = 65536, Width = 320 };
    private readonly StackPanel _preview = new();
    private readonly SitePreviewBuffer _previewBuffer = new();
    private readonly TextBox _html = new() { IsReadOnly = true, AcceptsReturn = true, MaxHeight = 160 };
    private readonly string _reviewSession = Guid.NewGuid().ToString("N");
    private SiteNativeAuthoringPage? _page;
    private SiteNativeAuthoringSession.PreparedEdit? _pending;
    private CuiSceneHost? _scene;
    private bool _busy;
    private bool _initialized;
    private bool _closing;
    private bool _preservePreviewDuringRefresh;
    private Task? _close;
    private string _status = "Opening Sites";
    public new event PropertyChangedEventHandler? PropertyChanged;

    public SitesNativeAuthoringSurface(SiteNativeAuthoringSession session, AuthenticatedResourceActor originalActor,
        ICuiSceneReadiness readiness, CancellationToken hostLifetime)
    {
        _session = session; _actor = originalActor; _readiness = readiness; _hostLifetime = hostLifetime;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
        _projects.ItemTemplate = new FuncDataTemplate<SiteProject>((item, _) => new TextBlock { Text = item?.Name ?? "" });
        _pages.ItemTemplate = new FuncDataTemplate<SitePage>((item, _) => new TextBlock { Text = item?.Name ?? "" });
        _components.ItemTemplate = new FuncDataTemplate<SiteComponent>((item, _) => new TextBlock
        { Text = item is null ? "" : item.ComponentType + ": " + Text(item) });
        _projects.SelectionChanged += (_, _) =>
        {
            PublishNativeChange(() =>
            {
                _pages.ItemsSource = Project?.Pages; _pages.SelectedItem = null;
                if(!_preservePreviewDuringRefresh){_pageRenameName.Text="";_seoTitle.Text="";_metaDescription.Text="";}
                _components.ItemsSource = null; _components.SelectedItem = null;
                if(!_preservePreviewDuringRefresh)ClearPreview(); Changed();
            });
        };
        _pages.SelectionChanged += (_, _) =>
        {
            PublishNativeChange(() =>
            {
                if(!_preservePreviewDuringRefresh)
                {
                    _pageRenameName.Text=SelectedPage?.Name??"";
                    _seoTitle.Text=SelectedPage?.SeoTitle??"";_metaDescription.Text=SelectedPage?.MetaDescription??"";
                }
                if(Project is { } project&&SelectedPage is { } page)
                {
                    var reachable=SiteAuthoringService.Reachable(project,page.RootComponentIds);
                    _components.ItemsSource=project.Components.Where(item=>reachable.Contains(item.ComponentId)).ToArray();
                }
                else _components.ItemsSource=null;
                _components.SelectedItem = null;
                if(!_preservePreviewDuringRefresh)ClearPreview(); Changed();
            });
        };
        _components.SelectionChanged += (_, _) =>
        { PublishNativeChange(() => { if (Component is { } component && _pending is null) _text.Text = Text(component); Changed(); }); };
        _name.TextChanged += (_, _) => { PublishNativeChange(Changed); };
        _pageRenameName.TextChanged += (_, _) => { PublishNativeChange(Changed); };
        _path.TextChanged += (_, _) => { PublishNativeChange(Changed); };
        _text.TextChanged += (_, _) => { PublishNativeChange(Changed); };
        _breakpoint.TextChanged += (_, _) => { PublishNativeChange(Changed); };
        _responsiveWidth.TextChanged += (_, _) => { PublishNativeChange(Changed); };
        _responsivePadding.TextChanged += (_, _) => { PublishNativeChange(Changed); };
        _seoTitle.TextChanged += (_, _) => { PublishNativeChange(Changed); };
        _metaDescription.TextChanged += (_, _) => { PublishNativeChange(Changed); };
    }

    public Task InitializeAsync(CancellationToken token = default) => TrackAsync(async () =>
    {
        if (_initialized) throw new InvalidOperationException("The original Sites view is already mounted.");
        _initialized = true;
        _hostRetirement = _hostLifetime.Register(static state => _ = ((SitesNativeAuthoringSurface)state!).CloseAndDrainAsync(), this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        await RequireReadyAsync(linked.Token);
        var page = await _session.ReadAsync(_actor, linked.Token);
        var registry = new CuiControlRegistry();
        registry.BindAuthoredControl("sites-name-input", _ => _name); registry.BindAuthoredControl("sites-path-input", _ => _path);
        registry.BindAuthoredControl("sites-page-rename-input", _ => _pageRenameName);
        registry.BindAuthoredControl("sites-text-input", _ => _text); registry.BindAuthoredControl("sites-projects", _ => _projects);
        registry.BindAuthoredControl("sites-breakpoint-input", _ => _breakpoint);
        registry.BindAuthoredControl("sites-responsive-width-input", _ => _responsiveWidth);
        registry.BindAuthoredControl("sites-responsive-padding-input", _ => _responsivePadding);
        registry.BindAuthoredControl("sites-seo-title-input", _ => _seoTitle);
        registry.BindAuthoredControl("sites-meta-description-input", _ => _metaDescription);
        registry.BindAuthoredControl("sites-pages", _ => _pages); registry.BindAuthoredControl("sites-components", _ => _components);
        registry.BindAuthoredControl("sites-preview", _ => _preview); registry.BindAuthoredControl("sites-html-preview", _ => _html);
        _scene = new CuiSceneHost(registry);
        var available = await _scene.ShowAsync(new("sites", "Sites", "Authoring", Load(), this, this, _readiness), linked.Token);
        if (available.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(available.Message);
        await _session.RevalidateAsync(page, _actor, linked.Token); await RequireReadyAsync(linked.Token);
        _page = page; _projects.ItemsSource = page.Projects; RequireAlive(linked.Token); Content = _scene;
        _status = $"{page.Projects.Count} projects"; Changed(); RequireAlive(linked.Token);
    });

    // Native host protocol only: the original privately issued page and source
    // still own all resource/revision checks; these methods confer no authority.
    public Task RevalidateOriginalOwnerAsync(CancellationToken token = default) => TrackAsync(async () =>
    {
        var originalPage = _page ?? throw new InvalidOperationException("The original Sites page is unavailable.");
        await RequireReadyAsync(token);
        await _session.RevalidateAsync(originalPage, _actor, token);
        await RequireReadyAsync(token);
        if (!ReferenceEquals(originalPage, _page))
            throw new UnauthorizedAccessException("The original Sites observation changed before publication.");
        CheckOriginalPublicationAlive();
    });
    public void CheckOriginalPublicationAlive()
    {
        Dispatcher.UIThread.VerifyAccess(); RequireAlive(CancellationToken.None);
    }
    public void DemandExternalOriginalRetirementJoin() =>
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);

    private SiteProject? Project => _projects.SelectedItem as SiteProject;
    private SitePage? SelectedPage => _pages.SelectedItem as SitePage;
    private SiteComponent? Component => _components.SelectedItem as SiteComponent;
    private bool Available => Alive() && !_busy && _scene is not null;
    private bool Editing => Available && _pending is null && _page is not null;
    public bool? IsActionAvailable(string action) => action switch
    {
        "9to1.Sites.Refresh" => Available && _pending is null,
        "9to1.Sites.CreateProject" => Editing && !string.IsNullOrWhiteSpace(_name.Text) && !string.IsNullOrWhiteSpace(_path.Text),
        "9to1.Sites.CreatePage" => CanMutateSelectedProject() && !string.IsNullOrWhiteSpace(_name.Text) && !string.IsNullOrWhiteSpace(_path.Text),
        "9to1.Sites.AddRoute" => AddRouteActionAvailable(),
        "9to1.Sites.RenamePage" => RenamePageActionAvailable(),
        "9to1.Sites.SetPageSeo" => PageSeoActionAvailable(),
        "9to1.Sites.AddHeading" or "9to1.Sites.AddText" => CanMutateSelectedProject() && SelectedPage is not null,
        "9to1.Sites.SaveText" => CanMutateSelectedProject() && Component is { } editableComponent
            && (string.Equals(editableComponent.ComponentType,"heading",StringComparison.OrdinalIgnoreCase)
                ||string.Equals(editableComponent.ComponentType,"text",StringComparison.OrdinalIgnoreCase)),
        "9to1.Sites.SetResponsiveWidth" => ResponsiveActionAvailable(_responsiveWidth.Text),
        "9to1.Sites.SetResponsivePadding" => ResponsiveActionAvailable(_responsivePadding.Text),
        "9to1.Sites.ResetResponsiveOverrides" => ResetResponsiveActionAvailable(),
        "9to1.Sites.Preview" => Editing && Project is not null && SelectedPage is not null,
        "9to1.Sites.Apply" => Available && _pending is { HasAuditRecovery: false } pending
            && (pending.Approval.IsAllowed || pending.Approval.State == HomePermissionRequestState.PendingApproval),
        "9to1.Sites.CancelReview" => Available && _pending is { HasAuditRecovery: false, ObservedResult: null },
        "9to1.Sites.RetryAudit" => Available && _pending is { HasAuditRecovery: true },
        _ => false
    };
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Selection" => ProjectSelectionStatus(),
            "SourceEditStatus" => SourceEditStatus(),
            "SelectedPageRoute" => SelectedPageRouteStatus(),
            "Breakpoints" => BreakpointStatus(),
            "ResponsiveWidthState" => ResponsivePropertyStatus("width"),
            "ResponsivePaddingState" => ResponsivePropertyStatus("padding"),
            "PendingChange" => _pending?.Description ?? "", "Status" => _status,
            "CanRefresh" => IsActionAvailable("9to1.Sites.Refresh"), "CanCreateProject" => IsActionAvailable("9to1.Sites.CreateProject"),
            "CanCreatePage" => IsActionAvailable("9to1.Sites.CreatePage"), "CanAdd" => IsActionAvailable("9to1.Sites.AddText"),
            "CanAddRoute" => IsActionAvailable("9to1.Sites.AddRoute"),
            "CanRenamePage" => IsActionAvailable("9to1.Sites.RenamePage"),
            "CanSetPageSeo" => IsActionAvailable("9to1.Sites.SetPageSeo"),
            "CanSaveText" => IsActionAvailable("9to1.Sites.SaveText"), "CanPreview" => IsActionAvailable("9to1.Sites.Preview"),
            "CanSetResponsiveWidth" => IsActionAvailable("9to1.Sites.SetResponsiveWidth"),
            "CanSetResponsivePadding" => IsActionAvailable("9to1.Sites.SetResponsivePadding"),
            "CanResetResponsiveOverrides" => IsActionAvailable("9to1.Sites.ResetResponsiveOverrides"),
            "CanApply" => IsActionAvailable("9to1.Sites.Apply"), "CanCancel" => IsActionAvailable("9to1.Sites.CancelReview"),
            "CanRetryAudit" => IsActionAvailable("9to1.Sites.RetryAudit"), _ => null
        };
        return path is "Selection" or "SourceEditStatus" or "SelectedPageRoute" or "Breakpoints" or "ResponsiveWidthState" or "ResponsivePaddingState" or "PendingChange" or "Status" or "CanRefresh" or "CanCreateProject" or "CanCreatePage"
            or "CanAdd" or "CanAddRoute" or "CanRenamePage" or "CanSaveText" or "CanPreview" or "CanSetResponsiveWidth" or "CanSetResponsivePadding" or "CanResetResponsiveOverrides"
            or "CanSetPageSeo" or "CanApply" or "CanCancel" or "CanRetryAudit";
    }
    public ValueTask DispatchAsync(string action, object? parameter, CancellationToken token = default)
    {
        if (parameter is not null) throw new ArgumentException("Sites actions use the original native selection, not caller identity or paths.");
        return new(TrackAsync(() => InvokeAsync(action, token)));
    }
    private async Task InvokeAsync(string action, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); if (IsActionAvailable(action) != true) return;
        var originalPage = _page; var project = Project; var selectedPage = SelectedPage; var component = Component;
        var name = _name.Text ?? ""; var path = _path.Text ?? ""; var text = _text.Text ?? "";
        var pageRenameName=_pageRenameName.Text??"";
        var breakpoint = _breakpoint.Text ?? ""; var responsiveWidth = _responsiveWidth.Text ?? ""; var responsivePadding = _responsivePadding.Text ?? "";
        var seoTitle=_seoTitle.Text;var metaDescription=_metaDescription.Text;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        await _operations.WaitAsync(linked.Token);
        List<Exception> terminal = [];
        try { await ExecuteOwnedAsync(); }
        catch (Exception error)
        {
            lock (_tasksGate) Add(_failures, error);
            _status = error.Message;
            IEnumerable<Exception> causes = error is AggregateException group ? group.Flatten().InnerExceptions : [error];
            if (causes.Any(cause => cause is UnauthorizedAccessException or ObjectDisposedException) || !Alive())
            { lock (_tasksGate) _closing = true; Add(terminal, error); }
            // Keep the SAME original failed edit available for its exact audit recovery.
            // Unknown effects never cause a replacement mutation.
        }
        finally
        {
            _busy = false;
            try { Changed(); } catch (Exception error) { Add(terminal, error); }
            try { _operations.Release(); } catch (Exception error) { Add(terminal, error); }
        }
        if (terminal.Count != 0) throw new AggregateException("Original Sites action and publication retained failures.", terminal);
        async Task ExecuteOwnedAsync()
        {
            if (IsActionAvailable(action) != true) return;
            _busy = true; Changed(); await RequireReadyAsync(linked.Token); Retained();
            if (action is "9to1.Sites.CancelReview")
            { _session.RetireReview(_pending!); _pending = null; _status = "Change cancelled before execution."; return; }
            if (action == "9to1.Sites.Preview")
            {
                var previewProject=project!;var previewPage=selectedPage!;
                var rendered = await _session.PreviewAsync(originalPage!, previewProject, previewPage, _actor, linked.Token);
                Retained();
                var errors = rendered.Diagnostics.Where(item => item.IsError).Select(item => item.Message).ToArray();
                if (!_previewBuffer.TryReplace(rendered))
                {
                    // A failed render is diagnostic state, not a replacement for the last valid preview.
                    _status = string.Join("; ", errors); return;
                }
                ClearPreviewVisuals(); _html.Text = _previewBuffer.Current!.Document;
                var reachable=SiteAuthoringService.Reachable(previewProject,previewPage.RootComponentIds);
                foreach (var item in previewProject.Components.Where(item => reachable.Contains(item.ComponentId)
                    && (string.Equals(item.ComponentType,"heading",StringComparison.OrdinalIgnoreCase)
                        ||string.Equals(item.ComponentType,"text",StringComparison.OrdinalIgnoreCase))))
                    _preview.Children.Add(new TextBlock { Text = (string.Equals(item.ComponentType,"heading",StringComparison.OrdinalIgnoreCase) ? "Heading: " : "") + Text(item),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                _status = "Saved page preview ready."; return;
            }
            var operation = action switch { "9to1.Sites.CreateProject" => "create", "9to1.Sites.CreatePage" => "page",
                "9to1.Sites.AddRoute" => "route-create",
                "9to1.Sites.RenamePage" => "page-rename",
                "9to1.Sites.SetPageSeo" => "page-seo",
                "9to1.Sites.AddHeading" => "heading", "9to1.Sites.AddText" => "text", "9to1.Sites.SaveText" => "save-text",
                "9to1.Sites.SetResponsiveWidth" => "responsive-width", "9to1.Sites.SetResponsivePadding" => "responsive-padding",
                "9to1.Sites.ResetResponsiveOverrides" => "reset-responsive", _ => null };
            if (operation is not null)
            {
                var responsive = operation is "responsive-width" or "responsive-padding" or "reset-responsive";
                _pending = await _session.PrepareAsync(originalPage!, project, selectedPage, component, _actor,
                    operation, operation=="page-rename"?pageRenameName:name, responsive ? breakpoint : path,
                    operation switch { "responsive-width" => responsiveWidth, "responsive-padding" => responsivePadding, _ => text },
                    Alive, _reviewSession, linked.Token,seoTitle,metaDescription);
                Retained(); _status = _pending.Approval.IsAllowed ? "Ready to apply this edit."
                    : _pending.Approval.State == HomePermissionRequestState.PendingApproval
                        ? "Review this edit in Home, then choose Apply." : _pending.Approval.Message;
                return;
            }
            Guid? retain = project?.SiteId;
            Guid? retainPage = selectedPage?.PageId;
            var appliedSuccessfully=false;
            if (action == "9to1.Sites.Apply")
            {
                var outcome = await _session.ApplyAsync(_pending!, linked.Token); Retained(); _status = outcome.Message;
                if (outcome.AwaitingHomeReview) return;
                appliedSuccessfully=outcome.Result?.IsSuccess==true;
                if (outcome.AuditRecorded) _pending = null;
                if (outcome.Result?.Value is { } saved) retain = saved.SiteId;
            }
            if (action == "9to1.Sites.RetryAudit")
            {
                var result = await _session.RetryAuditAsync(_pending!, linked.Token); Retained();
                if (result?.Value is { } saved) retain = saved.SiteId;
                _pending = null; _status = "Home audit recorded.";
            }
            var fresh = await _session.ReadAsync(_actor, linked.Token);
            await _session.RevalidateAsync(fresh, _actor, linked.Token); await RequireReadyAsync(linked.Token); Retained();
            var restoredProject=fresh.Projects.SingleOrDefault(item=>item.SiteId==retain);
            _preservePreviewDuringRefresh=retainPage is not null&&action!="9to1.Sites.Refresh"
                &&restoredProject?.Pages.Any(item=>item.PageId==retainPage)==true;
            try
            {
                _page = fresh; _projects.ItemsSource = fresh.Projects;
                _projects.SelectedItem = restoredProject;
                if (Project is { } current) _pages.SelectedItem = current.Pages.SingleOrDefault(item => item.PageId == retainPage);
                RequireAlive(linked.Token);
                if(!_preservePreviewDuringRefresh)ClearPreview();
                else if(appliedSuccessfully)_status="Saved. Keeping the last valid preview until you render again.";
            }
            finally { _preservePreviewDuringRefresh=false; }
            if (action == "9to1.Sites.Refresh") _status = $"{fresh.Projects.Count} projects";
        }
        void Retained()
        {
            RequireAlive(linked.Token);
            if (!ReferenceEquals(_page, originalPage) || !ReferenceEquals(Project, project)
                || !ReferenceEquals(SelectedPage, selectedPage) || !ReferenceEquals(Component, component))
                throw new InvalidOperationException("The original Sites selection changed before publication.");
        }
    }
    private void PublishNativeChange(Action change)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        TaskCompletionSource original;
        lock (_tasksGate)
        {
            if (!Alive()) return;
            _tasks.RemoveWhere(task => task.IsCompletedSuccessfully);
            if (_tasks.Count >= 256) throw new InvalidOperationException("Drain original Sites publications before admitting more changes.");
            original = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _tasks.Add(original.Task);
        }
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { change(); return true; }); original.SetResult(); }
        catch (Exception error)
        {
            lock (_tasksGate) { Add(_failures, error); _closing = true; }
            original.SetException(error); throw;
        }
    }
    private void ClearPreview() { _previewBuffer.Clear(); ClearPreviewVisuals(); }
    private void ClearPreviewVisuals() { _preview.Children.Clear(); _html.Text = ""; }
    private string ResponsivePropertyStatus(string property)
    {
        var project=Project;var component=Component;var breakpoint=_breakpoint.Text??"";
        if(project is null||component is null||!project.DesignSystem.Breakpoints.TryGetValue(breakpoint,out var activeWidth)||activeWidth<=0)
            return "Choose a selected component and declared breakpoint.";
        if(component.Layout.BreakpointOverrides.TryGetValue(breakpoint,out var current)
            && current.ChangedProperties.TryGetValue(property,out var currentValue))
            return $"{property} defined at {breakpoint}: {CssValue(currentValue)}";
        foreach(var candidate in component.Layout.BreakpointOverrides
            .Where(pair=>project.DesignSystem.Breakpoints.TryGetValue(pair.Key,out var width)&&width>=activeWidth)
            .OrderBy(pair=>project.DesignSystem.Breakpoints[pair.Key]).ThenByDescending(pair=>pair.Key,StringComparer.Ordinal))
            if(candidate.Value.ChangedProperties.TryGetValue(property,out var inherited))
                return $"{property} inherited from {candidate.Key}: {CssValue(inherited)}";
        return component.Layout.Properties.TryGetValue(property,out var baseValue)
            ? $"Inherited base {property}: {CssValue(baseValue)}" : $"No explicit {property}; using the layout default.";
    }
    private bool ResponsiveActionAvailable(string? value)
    {
        var project=Project;
        return CanMutateSelectedProject()&&project is not null&&Component is not null
            &&project.DesignSystem.Breakpoints.TryGetValue(_breakpoint.Text??"",out var width)&&width>0
            &&!string.IsNullOrWhiteSpace(value);
    }
    private bool RenamePageActionAvailable()
    {
        var page=SelectedPage;var name=_pageRenameName.Text;
        return CanMutateSelectedProject()&&page is not null&&!string.IsNullOrWhiteSpace(name)
            &&!string.Equals(name.Trim(),page.Name,StringComparison.Ordinal);
    }
    private bool AddRouteActionAvailable()
    {
        var project=Project;var page=SelectedPage;var path=_path.Text;
        if(!CanMutateSelectedProject()||project is null||page is null||string.IsNullOrWhiteSpace(path))return false;
        string normalized;
        try{_ = SiteAddressRules.NormalizeRoutePath(path.Trim());normalized=path.Trim();}
        catch(SiteOperationException){return false;}
        return !project.Routes.Any(route=>route.Pattern==normalized);
    }
    private bool PageSeoActionAvailable()
    {
        var page=SelectedPage;
        if(!CanMutateSelectedProject()||page is null)return false;
        var rawTitle=_seoTitle.Text;var rawDescription=_metaDescription.Text;
        var title=string.IsNullOrWhiteSpace(rawTitle)?null:rawTitle.Trim();
        var description=string.IsNullOrWhiteSpace(rawDescription)?null:rawDescription.Trim();
        return !string.Equals(title,page.SeoTitle,StringComparison.Ordinal)
            ||!string.Equals(description,page.MetaDescription,StringComparison.Ordinal);
    }
    private bool ResetResponsiveActionAvailable()
    {
        var project=Project;var component=Component;var breakpoint=_breakpoint.Text??"";
        return CanMutateSelectedProject()&&project is not null&&component is not null
            &&project.DesignSystem.Breakpoints.TryGetValue(breakpoint,out var width)&&width>0
            &&component.Layout.BreakpointOverrides.ContainsKey(breakpoint);
    }
    private string BreakpointStatus()
    {
        var project=Project;
        if(project is null)return "No project selected.";
        var values=project.DesignSystem.Breakpoints.Where(pair=>pair.Value>0).OrderBy(pair=>pair.Value)
            .Select(pair=>$"{pair.Key} ({pair.Value}px)").ToArray();
        return values.Length==0?"No responsive breakpoints are declared in this project.":string.Join(", ",values);
    }
    private string SelectedPageRouteStatus()
    {
        var project=Project;var page=SelectedPage;
        if(project is null||page is null)return "Select a page to view its route.";
        var routes=project.Routes.Where(item=>item.PageId==page.PageId).Select(item=>item.Pattern).ToArray();
        return page.Name+" · "+(routes.Length==0?"No route":string.Join(", ",routes));
    }
    private string ProjectSelectionStatus()
    {
        var project=Project;
        if(project is null)return "Select a project.";
        var domain=project.Source.StackDomainId?.ToString("D")??"No Build domain bound";
        var sourceBinding=project.Source.StackProjectId?.ToString("D")??"local Files source";
        return $"{project.Name} · SiteID {project.SiteId:D} · ProjectID {project.ProjectId:D} · source binding {sourceBinding} · DomainID {domain} · source revision {project.Source.SourceRevision} · Sites revision {project.Revision}";
    }
    private bool CanMutateSelectedProject()=>Editing&&Project is { } project
        &&!project.Source.StackProjectId.HasValue&&!project.Source.StackDomainId.HasValue;
    private string SourceEditStatus()=>Project is { } project&&(project.Source.StackProjectId.HasValue||project.Source.StackDomainId.HasValue)
        ? "Source-backed project: this is the saved Sites snapshot at the displayed source revision. Edit/update it through Build; Sites-only writes are disabled until that writer is available."
        : "Sites edits are revision-checked and reviewed in Home.";
    private static string CssValue(System.Text.Json.JsonElement value) => value.ValueKind==System.Text.Json.JsonValueKind.String
        ? value.GetString()??"" : value.ToString();
    private void Changed()
    {
        var editing = Editing;
        _projects.IsEnabled = editing; _pages.IsEnabled = editing; _components.IsEnabled = editing;
        _name.IsEnabled = editing; _pageRenameName.IsEnabled=editing;_path.IsEnabled = editing; _text.IsEnabled = editing;
        _breakpoint.IsEnabled = editing; _responsiveWidth.IsEnabled = editing; _responsivePadding.IsEnabled = editing;
        _seoTitle.IsEnabled=editing;_metaDescription.IsEnabled=editing;
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { PropertyChanged?.Invoke(this, new(null)); return true; });
    }
    private bool Alive() { lock (_tasksGate) return !_closing && !_stop.IsCancellationRequested && !_hostLifetime.IsCancellationRequested; }
    private void RequireAlive(CancellationToken token) { token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(!Alive(), this); }
    private async Task RequireReadyAsync(CancellationToken token)
    { RequireAlive(token); if ((await CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => _readiness.CheckAsync(token))).State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException("The original Home owner cannot admit this Sites view."); RequireAlive(token); }
    private Task TrackAsync(Func<Task> operation)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_tasksGate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _tasks.RemoveWhere(task => task.IsCompletedSuccessfully);
            if (_tasks.Count >= 256) throw new InvalidOperationException("Drain original Sites work before admitting more actions.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = RunAsync(start.Task, operation); _tasks.Add(actual); start.SetResult(); return actual;
        }
        async Task RunAsync(Task start, Func<Task> original)
        {
            await start;
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            Task? actual = null;
            try { actual = CloudflareOriginalExecutionGuard.InvokeOriginal(this, original); await actual; }
            catch (Exception originalFailure)
            {
                Exception error = actual?.IsFaulted == true ? actual.Exception! : originalFailure;
                lock (_tasksGate) { Add(_failures, error); _closing = true; }
                throw error;
            }
        }
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_tasksGate)
        {
            if (_close is not null) return _close;
            _closing = true; var originals = _tasks.ToArray();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseAsync(start.Task, originals); start.SetResult(); return _close;
        }
    }
    private async Task CloseAsync(Task start, Task[] originals)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        List<Exception> failures = [];
        try { _stop.Cancel(); } catch (Exception error) { Add(failures, error); }
        foreach (var actual in originals)
            try { await actual; } catch (Exception error) { Add(failures, actual.IsFaulted ? actual.Exception! : error); }
        lock (_tasksGate) foreach (var error in _failures) Add(failures, error);
        async Task OnUi(Action action)
        {
            void Original() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { action(); return true; });
            if (Dispatcher.UIThread.CheckAccess()) Original(); else await Dispatcher.UIThread.InvokeAsync(Original);
        }
        try { await OnUi(() => { _page = null; _projects.ItemsSource = null; _pages.ItemsSource = null; _components.ItemsSource = null; ClearPreview(); Content = null; Changed(); }); }
        catch (Exception error) { Add(failures, error); }
        try { await OnUi(() => _scene?.Dispose()); } catch (Exception error) { Add(failures, error); }
        try { _hostRetirement.Dispose(); } catch (Exception error) { Add(failures, error); }
        try { _operations.Dispose(); } catch (Exception error) { Add(failures, error); }
        try { _stop.Dispose(); } catch (Exception error) { Add(failures, error); }
        if (failures.Count != 0) throw new AggregateException("Original Sites work and close retained failures.", failures);
    }
    private static void Add(List<Exception> rows, Exception error) { if (!rows.Any(item => ReferenceEquals(item, error))) rows.Add(error); }
    public void Dispose() => _ = CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static string Text(SiteComponent component) => component.Properties.TryGetValue("text", out var value)
        && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : "";
    private static CuiDocument Load()
    {
        const string name = "HavenOS.Sites.NativeUI.UI.SitesAuthoring.cui";
        using var stream = typeof(SitesNativeAuthoringSurface).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException("The owning Sites CUI source is missing.");
        using var reader = new StreamReader(stream); var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), name);
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
}
