using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Haven.Desktop;
using Haven.Application;
using Haven.Application.Go;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Cui.AI;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private AlertDialog? _launcherDulcheDialog;
    private bool _launcherDulcheOpening;
    private bool _launcherDulcheVisible;
    private FloatingAiBarState? _launcherDulcheState;
    private LauncherAppAiActions? _launcherDulcheActions;
    private CancellationTokenSource? _launcherDulcheLifetime;
    private LauncherSessionSnapshot? _launcherDulcheSession;
    private EventHandler? _launcherDulcheChanged;
    private TextView? _launcherDulcheResponse;

    private async Task ShowLauncherDulcheAsync()
    {
        if (!_activityStarted || _launcherDulcheDialog is not null || _launcherDulcheOpening) return;
        _launcherDulcheOpening = true;
        try
        {
            var session = await WidgetSessions.ReadAsync(_launcherLifetime.Token)
                ?? throw new InvalidOperationException("Open the current Home launcher profile first.");
            if (!_activityStarted || _launcherLifetime.IsCancellationRequested) return;
            var services = App.Services ?? throw new InvalidOperationException("Home is unavailable.");
            var semanticOwner = services.GetRequiredService<LauncherSemanticFeatureProvider>();
            var actions = _launcherDulcheActions = new LauncherAppAiActions(WidgetSessions, semanticOwner, session);
            var state = services.GetRequiredService<IAppAiCoordinatorFactory>().Create(new LauncherAppAiContext(WidgetSessions, services.GetRequiredService<GoService>(), services.GetRequiredService<IInstalledApplicationRegistry>(), session), actions);
            _launcherDulcheActions = actions; _launcherDulcheState = state; _launcherDulcheSession = session;
            var lifetime = _launcherDulcheLifetime = CancellationTokenSource.CreateLinkedTokenSource(_launcherLifetime.Token);
            var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
            form.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
            var context = new TextView(this) { Text = "Reading current launcher context…" }; form.AddView(context);
            var mode = new Switch(this) { Text = "Allow typed layout edits · Home review required", Checked = false };
            mode.CheckedChange += (_, args) => state.SetAccessMode(args.IsChecked ? AppAiAccessMode.Write : AppAiAccessMode.ReadOnly);
            form.AddView(mode);
            var model = new Button(this) { Text = "Use current Home model" };
            model.Click += async (_, _) =>
            {
                if (!ReferenceEquals(_launcherDulcheState, state) || lifetime.IsCancellationRequested) return;
                try { if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return; await state.SelectNextModelAsync(lifetime.Token); await RequireLauncherDulcheSessionAsync(state, lifetime.Token); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error) { ReportLauncherDulcheError(state, error); }
            };
            form.AddView(model);
            var prompt = new EditText(this) { Hint = "Ask about this layout, or describe a change", ContentDescription = "Launcher Dulche request" };
            prompt.SetMinLines(2); form.AddView(prompt);
            var response = _launcherDulcheResponse = new TextView(this) { Text = "Read-only mode is on." };
            var output = new ScrollView(this); output.AddView(response); form.AddView(output, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(180)));
            var status = new TextView(this); form.AddView(status);
            var send = new Button(this) { Text = "Send" }; form.AddView(send);
            send.Click += async (_, _) =>
            {
                if (!ReferenceEquals(_launcherDulcheState, state) || lifetime.IsCancellationRequested) return;
                try
                {
                    if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                    state.Prompt = prompt.Text ?? string.Empty; await state.SubmitAsync(lifetime.Token);
                    if (await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) LoadAppsAsync();
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error) { ReportLauncherDulcheError(state, error); }
            };
            var review = new Button(this) { Text = "Review exact change in Home", Enabled = false }; form.AddView(review);
            review.Click += async (_, _) =>
            {
                try
                {
                    if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token) || actions.PendingReviewRequestId is not { } requestId) return;
                    state.Cancel();
                    var intent = new Intent(this, typeof(MainActivity));
                    intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                    intent.PutExtra("haven_home_review_request", requestId);
                    StartActivity(intent); // Navigation only; return here and retry after Home review.
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error) { ReportLauncherDulcheError(state, error); }
            };
            var retry = new Button(this) { Text = "Retry unchanged reviewed change", Enabled = false }; form.AddView(retry);
            retry.Click += async (_, _) =>
            {
                if (!ReferenceEquals(_launcherDulcheState, state) || lifetime.IsCancellationRequested || actions.PendingActionRequest is not { } request) return;
                try
                {
                    if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                    await state.ExecuteActionAsync(request, lifetime.Token);
                    if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                    LoadAppsAsync(); await state.RefreshContextAsync(lifetime.Token);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error) { ReportLauncherDulcheError(state, error); }
            };
            var audit = new Button(this) { Text = "Finish recording the result", Enabled = false, Visibility = ViewStates.Gone }; form.AddView(audit);
            async Task RefreshAuditRecoveryAsync()
            {
                if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                var ids = await semanticOwner.ReadPendingAuditIdsAsync(session, lifetime.Token);
                if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                audit.Enabled = ids.Count > 0; audit.Visibility = ids.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
            }
            audit.Click += async (_, _) =>
            {
                try
                {
                    if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                    var ids = await semanticOwner.ReadPendingAuditIdsAsync(session, lifetime.Token);
                    var completed = true;
                    foreach (var id in ids)
                    {
                        if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                        // Home retries only the original fixed audit outcome; no layout edit is submitted here.
                        completed &= await semanticOwner.RetryAuditAsync(session, id, lifetime.Token);
                    }
                    if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                    await RefreshAuditRecoveryAsync();
                    status.Text = completed ? "The retained result was recorded in Home." : "Home recording is still pending. The edit was not repeated.";
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error) { ReportLauncherDulcheError(state, error); }
            };
            _launcherDulcheChanged = (_, _) => RunOnUiThread(async () =>
            {
                if (!ReferenceEquals(_launcherDulcheState, state) || !_activityStarted || !_launcherDulcheVisible) return;
                try
                {
                    if (!await RequireLauncherDulcheSessionAsync(state, lifetime.Token)) return;
                    context.Text = state.ContextLabel; response.Text = state.Response;
                    status.Text = state.Error ?? state.RequestStateLabel;
                    model.Text = state.ModelPickerLabel;
                    model.Enabled = state.ModelPickerAvailable;
                    review.Enabled = actions.PendingReviewRequestId is not null;
                    retry.Enabled = state.IsWriteMode && actions.PendingActionRequest is not null;
                    await RefreshAuditRecoveryAsync();
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch { CloseLauncherDulche(); }
            });
            state.Changed += _launcherDulcheChanged;
            var scroll = new ScrollView(this); scroll.AddView(form);
            var builder = new AlertDialog.Builder(this); builder.SetTitle("Dulche · Launcher"); builder.SetView(scroll);
            builder.SetNegativeButton("Close", (_, _) => CloseLauncherDulche());
            var dialog = _launcherDulcheDialog = builder.Create();
            if (dialog is null) { CloseLauncherDulche(); return; }
            dialog.DismissEvent += (_, _) => { if (ReferenceEquals(_launcherDulcheDialog, dialog)) CloseLauncherDulche(); };
            _launcherDulcheVisible = true; dialog.Show();
            await state.RefreshContextAsync(lifetime.Token); await state.RefreshModelsAsync(lifetime.Token);
            await RefreshAuditRecoveryAsync();
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { CloseLauncherDulche(); }
        catch (Exception error) { CloseLauncherDulche(); Toast.MakeText(this, error.Message, ToastLength.Long)?.Show(); }
        finally { _launcherDulcheOpening = false; }
    }
    private async Task<bool> RequireLauncherDulcheSessionAsync(FloatingAiBarState state, CancellationToken token)
    {
        if (!ReferenceEquals(_launcherDulcheState, state) || !_activityStarted || !_launcherDulcheVisible || token.IsCancellationRequested) return false;
        var expected = _launcherDulcheSession;
        if (expected is null || !await WidgetSessions.IsCurrentAsync(expected, token))
        {
            if (ReferenceEquals(_launcherDulcheState, state)) CloseLauncherDulche();
            return false;
        }
        return ReferenceEquals(_launcherDulcheState, state) && ReferenceEquals(_launcherDulcheSession, expected) &&
            _activityStarted && _launcherDulcheVisible && !token.IsCancellationRequested;
    }
    private void ReportLauncherDulcheError(FloatingAiBarState state, Exception error)
    {
        if (!ReferenceEquals(_launcherDulcheState, state) || !_activityStarted || !_launcherDulcheVisible) return;
        Toast.MakeText(this, error.Message, ToastLength.Long)?.Show();
    }
    private async Task RevalidateLauncherDulcheAsync()
    {
        if (_launcherDulcheSession is not { } expected) return;
        try
        {
            if (!await WidgetSessions.IsCurrentAsync(expected, _launcherLifetime.Token)) { CloseLauncherDulche(); return; }
            _launcherDulcheVisible = true;
            if (_launcherDulcheState is { } state) { await state.RefreshContextAsync(_launcherLifetime.Token); await state.RefreshModelsAsync(_launcherLifetime.Token); }
        }
        catch { CloseLauncherDulche(); }
    }
    private void PauseLauncherDulche()
    {
        _launcherDulcheVisible = false;
        _launcherDulcheState?.Cancel();
        if (_launcherDulcheResponse is not null) _launcherDulcheResponse.Text = string.Empty;
    }
    private void CloseLauncherDulche()
    {
        _launcherDulcheVisible = false;
        var dialog = _launcherDulcheDialog; _launcherDulcheDialog = null;
        _launcherDulcheLifetime?.Cancel(); _launcherDulcheLifetime?.Dispose(); _launcherDulcheLifetime = null;
        if (_launcherDulcheState is { } state)
        { if (_launcherDulcheChanged is not null) state.Changed -= _launcherDulcheChanged; state.Dispose(); }
        _launcherDulcheActions?.Dispose(); _launcherDulcheActions = null; _launcherDulcheState = null;
        _launcherDulcheSession = null; _launcherDulcheChanged = null; _launcherDulcheResponse = null;
        dialog?.Dismiss();
    }
}
