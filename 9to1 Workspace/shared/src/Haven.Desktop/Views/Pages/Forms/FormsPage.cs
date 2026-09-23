using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenPage = Haven.UI.Components.Page;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Forms;

/// <summary>Forms surface that submits through the trusted structured-form runtime and stores responses locally.</summary>
public sealed class FormsPage : UserControl, IDisposable
{
    private readonly IFormsSubmissionStore _submissions;
    private readonly StructuredFormTemplateRuntime _template;
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly HavenGenUiSceneSurface _formSurface;
    private readonly FormsHavenScene _scene;
    private readonly HavenSceneControl _sceneHost;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Guid _threadId = Guid.NewGuid();
    private bool _saving;
    private bool _disposed;

    public FormsPage(
        IFormsSubmissionStore submissions,
        StructuredFormTemplateRuntime template,
        GenerativeUiEventRouter router,
        GenUiInstanceStore instances)
    {
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));

        _formSurface = new HavenGenUiSceneSurface(_router, _instances);
        _formSurface.ActionCompleted += OnFormActionCompleted;
        _formSurface.Present(_template.Create(_threadId, "forms", BuildFeedbackInputs()));
        _scene = new FormsHavenScene(_formSurface.Root);
        _scene.OpenDataRequested += OnOpenDataRequested;
        _sceneHost = new HavenSceneControl { Root = _scene.Root };
        _sceneHost.InputSubmitted += OnInputSubmitted;
        Content = _sceneHost;
        AutomationProperties.SetAutomationId(this, "HavenFormsPage");
        AutomationProperties.SetName(this, "Haven Forms");
        AutomationProperties.SetAutomationId(_sceneHost, "HavenFormsScene");
        AutomationProperties.SetName(_sceneHost, "Feedback form and locally stored responses");
        _ = RefreshResponsesAsync();
    }

    internal HavenSceneControl Scene => _sceneHost;
    internal FormsHavenScene HavenScene => _scene;
    public event Action<Guid>? DataWorkbookRequested;

    private static IReadOnlyDictionary<string, JsonElement> BuildFeedbackInputs() =>
        new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["title"] = JsonSerializer.SerializeToElement("Feedback"),
            ["schema"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { id = "name", label = "Your name", type = "text", placeholder = "Name (optional when anonymous)" },
                new { id = "topic", label = "Topic", type = "select", options = new[] { "Feedback", "Issue", "Idea" } },
                new { id = "message", label = "Message", type = "text", placeholder = "What would you like us to know?" },
                new { id = "anonymous", label = "Submit anonymously", type = "toggle" }
            })
        };

    private void OnInputSubmitted(Input input)
    {
        if (!_disposed && _formSurface.OwnsInput(input))
            _ = _formSurface.SubmitInputAsync(input, _lifetime.Token);
    }

    private void OnOpenDataRequested(object? sender, EventArgs e) => DataWorkbookRequested?.Invoke(_submissions.DataWorkbookId);

    private void OnFormActionCompleted(object? sender, GenUiActionResult result)
    {
        if (result.ActionId == "structured-form.submit" && result.Status == GenUiActionStatus.Completed)
            _ = SaveResponseAsync(result);
    }

    private async Task SaveResponseAsync(GenUiActionResult result)
    {
        if (_disposed || Interlocked.Exchange(ref _saving, true)) return;
        try
        {
            if (!TryCreateFeedbackSubmission(result.StructuredResult, DateTimeOffset.UtcNow, out var submission, out var validationError))
            {
                _scene.SetStatus(validationError ?? "The response needs more information.");
                return;
            }
            await _submissions.SaveAsync(submission!, _lifetime.Token).ConfigureAwait(false);
            await RefreshResponsesAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus("Response saved on this device."));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus($"Response could not be saved: {failure.Message}"));
        }
        finally
        {
            Interlocked.Exchange(ref _saving, false);
        }
    }

    private async Task RefreshResponsesAsync()
    {
        try
        {
            var responses = await _submissions.GetLatestAsync(_lifetime.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed) _scene.SetResponses(responses);
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus($"Saved responses could not load: {failure.Message}"));
        }
    }

    internal static bool TryCreateFeedbackSubmission(
        JsonElement structuredResult,
        DateTimeOffset submittedAt,
        out FormsSubmission? submission,
        out string? validationError)
    {
        submission = null;
        validationError = null;
        if (structuredResult.ValueKind != JsonValueKind.Object)
        {
            validationError = "The form response could not be read.";
            return false;
        }

        var name = ReadValue(structuredResult, "name");
        var topic = ReadValue(structuredResult, "topic");
        var message = ReadValue(structuredResult, "message");
        var anonymous = string.Equals(ReadValue(structuredResult, "anonymous"), "true", StringComparison.OrdinalIgnoreCase);
        if (!anonymous && string.IsNullOrWhiteSpace(name))
        {
            validationError = "Add your name or select anonymous submission.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            validationError = "Enter a message before submitting.";
            return false;
        }

        submission = new FormsSubmission(
            Guid.NewGuid().ToString("N"),
            "feedback",
            "Feedback",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = anonymous ? string.Empty : name.Trim(),
                ["topic"] = string.IsNullOrWhiteSpace(topic) ? "Feedback" : topic.Trim(),
                ["message"] = message.Trim(),
                ["anonymous"] = anonymous ? "true" : "false"
            },
            submittedAt);
        submission = FormsSubmissionLogic.Normalise(submission);
        return true;
    }

    private static string ReadValue(JsonElement values, string fieldId)
    {
        if (!values.TryGetProperty($"structured-form.input.{fieldId}", out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sceneHost.InputSubmitted -= OnInputSubmitted;
        _scene.OpenDataRequested -= OnOpenDataRequested;
        DataWorkbookRequested = null;
        _formSurface.ActionCompleted -= OnFormActionCompleted;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _sceneHost.Root = null;
        _formSurface.Dispose();
        if (_formSurface.Document is { } document) _instances.Remove(document.Origin.InstanceId);
    }
}

internal sealed class FormsHavenScene
{
    private readonly Container _responseList = new() { Layout = HavenLayout.Vertical };
    private readonly HavenText _status = new(string.Empty) { Level = TextLevel.Paragraph };

    public FormsHavenScene(Container formRoot)
    {
        Root = new HavenPage { Name = "Forms.Root", Layout = HavenLayout.Vertical };
        Set(Root, HavenProperties.Padding, HavenThickness.Parse("28px 32px"));
        Set(Root, HavenProperties.Gap, HavenLength.Px(14));
        Set(Root, HavenProperties.Background, "Transparent");
        Set(Root, HavenProperties.Overflow, HavenOverflow.Scroll);

        var header = new Container { Layout = HavenLayout.Vertical };
        Set(header, HavenProperties.Gap, HavenLength.Px(4));
        header.Add(new HavenText("Forms") { Name = "Forms.Title", Level = TextLevel.H1 });
        header.Add(Muted("Submit a structured feedback form. Responses stay on this device."));
        Root.Add(header);

        var formCard = Card("Forms.Feedback.Card");
        formCard.Add(new HavenText("Feedback form") { Level = TextLevel.H3 });
        formCard.Add(formRoot);
        Root.Add(formCard);

        var responsesCard = Card("Forms.Responses.Card");
        responsesCard.Add(new HavenText("Saved responses") { Level = TextLevel.H3 });
        OpenDataButton = new HavenButton { Name = "Forms.Responses.OpenInData", Content = "Open responses in Data", Variant = ButtonVariant.Secondary };
        OpenDataButton.Invoked += (_, _) => OpenDataRequested?.Invoke(this, EventArgs.Empty);
        OpenDataButton.Accessibility.AccessibleName = "Open Forms responses in the Data app";
        responsesCard.Add(OpenDataButton);
        responsesCard.Add(_responseList);
        Root.Add(responsesCard);

        _status.Accessibility.AccessibleName = "Forms status";
        Set(_status, HavenProperties.Foreground, "TextSecondary");
        Root.Add(_status);
    }

    public HavenPage Root { get; }
    internal HavenButton OpenDataButton { get; }
    internal event EventHandler? OpenDataRequested;
    internal void SetStatus(string message) => _status.Content = message;

    internal void SetResponses(IReadOnlyList<FormsSubmission> submissions)
    {
        foreach (var child in _responseList.Children.ToArray()) _responseList.Remove(child);
        if (submissions.Count == 0)
        {
            _responseList.Add(Muted("No responses yet."));
            return;
        }

        foreach (var response in submissions.Take(10))
        {
            var name = response.Values.TryGetValue("name", out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : "Anonymous";
            var topic = response.Values.TryGetValue("topic", out var category) ? category : "Feedback";
            var message = response.Values.TryGetValue("message", out var details) ? details : string.Empty;
            var excerpt = message.Length > 100 ? message[..100] + "…" : message;
            _responseList.Add(Muted($"{response.SubmittedAt.ToLocalTime():g} · {topic} · {name}: {excerpt}"));
        }
    }

    private static Container Card(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        Set(card, HavenProperties.Width, HavenLength.Percent(100));
        Set(card, HavenProperties.Background, "SurfaceRaised");
        Set(card, HavenProperties.BorderColor, "Border");
        Set(card, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(card, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(card, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(card, HavenProperties.Gap, HavenLength.Px(9));
        Set(card, HavenProperties.Shadow, "Card");
        return card;
    }

    private static HavenText Muted(string content)
    {
        var text = new HavenText(content) { Level = TextLevel.Paragraph };
        Set(text, HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) => element.SetValue(property, value);
}
