using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Unsaved form state only. A draft never grants access or replaces the canonical definition.</summary>
public sealed partial class AssistantConfigurationDraft
{
    private AssistantConfiguration _saved;
    private long _generation;

    public AssistantConfigurationDraft(AssistantDefinitionSnapshot? saved = null)
    {
        if (saved is not null && saved.Kind != ConfiguredIdentityKind.Assistant)
            throw new InvalidOperationException("This form edits configured Assistants only.");
        Identity = saved?.Identity;
        ExpectedRevision = saved?.Revision ?? 0;
        _saved = Copy(saved?.Configuration ?? new AssistantConfiguration { Name = "" });
        Configuration = Copy(_saved);
    }

    public AssistantIdentity? Identity { get; private set; }
    public long ExpectedRevision { get; private set; }
    public AssistantConfiguration Configuration { get; private set; }
    public bool IsDirty => _generation != _savedGeneration;
    internal long OriginalGeneration => _generation;
    private long _savedGeneration;

    public bool TrySetText(string field, string? value)
    {
        value ??= "";
        var replacement = field switch
        {
            "DraftName" => Configuration with { Name = value },
            "DraftDescription" => Configuration with { Description = value },
            "DraftPurpose" => Configuration with { Purpose = value },
            "DraftRole" => Configuration with { Role = value },
            "DraftInstructions" => Configuration with { Instructions = value },
            _ => null
        };
        if (replacement is null) return false;
        if (replacement == Configuration) return true;
        Configuration = replacement;
        _generation = checked(_generation + 1);
        return true;
    }

    public bool TrySetBoolean(string field, bool value)
    {
        var replacement = field switch
        {
            "DraftAllowCloud" => Configuration with { Model = Configuration.Model with { AllowCloud = value } },
            "DraftAllowFallback" => Configuration with { Model = Configuration.Model with { AllowFallback = value } },
            _ => MemoryPreferenceReplacement(field, value) ?? ProactivityPreferenceReplacement(field, value)
        };
        if (replacement is null) return false;
        if (replacement == Configuration) return true;
        Configuration = replacement;
        _generation = checked(_generation + 1);
        return true;
    }

    public void SetConfiguration(AssistantConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var captured = Copy(configuration);
        if (captured == Configuration) return;
        Configuration = captured;
        _generation = checked(_generation + 1);
    }

    public void DiscardChanges()
    {
        Configuration = Copy(_saved);
        _generation = checked(_generation + 1);
        _savedGeneration = _generation;
    }

    public Submission CaptureSubmission() => new(this, Identity, ExpectedRevision, _generation, Copy(Configuration));

    /// <summary>Accepts an observed canonical save only for this same submission. New edits survive.</summary>
    public void Acknowledge(Submission submitted, AssistantDefinitionSnapshot observed)
    {
        ArgumentNullException.ThrowIfNull(submitted);
        ArgumentNullException.ThrowIfNull(observed);
        if (!ReferenceEquals(submitted.Owner, this) || submitted.Identity != Identity ||
            submitted.ExpectedRevision != ExpectedRevision || observed.Kind != ConfiguredIdentityKind.Assistant ||
            (Identity is not null && observed.Identity != Identity) || observed.Revision <= ExpectedRevision)
            throw new InvalidOperationException("The saved definition does not acknowledge this original form submission.");

        Identity = observed.Identity;
        ExpectedRevision = observed.Revision;
        _saved = Copy(observed.Configuration);
        if (_generation == submitted.Generation)
        {
            Configuration = Copy(_saved);
            _savedGeneration = _generation;
        }
        // A successful create binds newer edits to the SAME newly saved identity.
        // Their original draft stays visible and dirty against its new saved revision.
    }

    public sealed class Submission
    {
        internal Submission(AssistantConfigurationDraft owner, AssistantIdentity? identity, long revision,
            long generation, AssistantConfiguration configuration)
        { Owner = owner; Identity = identity; ExpectedRevision = revision; Generation = generation; Configuration = configuration; }
        internal AssistantConfigurationDraft Owner { get; }
        internal long Generation { get; }
        public AssistantIdentity? Identity { get; }
        public long ExpectedRevision { get; }
        public AssistantConfiguration Configuration { get; }
    }

    private static AssistantConfiguration Copy(AssistantConfiguration value) => value with
    {
        ToolIds = Array.AsReadOnly(value.ToolIds.ToArray()),
        ConnectedAppIds = Array.AsReadOnly(value.ConnectedAppIds.ToArray()),
        KnowledgeResourceIds = Array.AsReadOnly(value.KnowledgeResourceIds.ToArray()),
        ProjectReferences = Array.AsReadOnly(value.ProjectReferences.ToArray()),
        Modalities = Array.AsReadOnly(value.Modalities.ToArray()),
        Proactive = value.Proactive with
        {
            EventKinds = value.Proactive.EventKinds is { } events ? Array.AsReadOnly(events.ToArray()) : null,
            NotificationChannels = value.Proactive.NotificationChannels is { } channels ? Array.AsReadOnly(channels.ToArray()) : null,
            AutomationIds = value.Proactive.AutomationIds is { } schedules ? Array.AsReadOnly(schedules.ToArray()) : null
        }
    };
}
