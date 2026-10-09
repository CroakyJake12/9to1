using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Migration;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Local review only. The exact owner-issued preview remains the command input.</summary>
public sealed class LegacyAgentMigrationDraft
{
    private long _generation;
    private long _acknowledgedGeneration;
    private Guid? _operationId;

    public LegacyAgentMigrationDraft(LegacyAgentMigrationPreview original)
    {
        Preview = original ?? throw new ArgumentNullException(nameof(original));
        Configuration = Copy(original.SuggestedConfiguration);
        // A fresh source has NO kind. Only an already-recorded explicit choice is displayed.
        Kind = original.PendingClassification?.ConfirmedKind;
        _operationId = original.PendingClassification?.OperationId;
    }

    public LegacyAgentMigrationPreview Preview { get; }
    public AssistantConfiguration Configuration { get; private set; }
    public ConfiguredIdentityKind? Kind { get; private set; }
    public bool IsContinuation => Preview.PendingClassification is not null;
    public bool HasUnconfirmedChanges => _generation != _acknowledgedGeneration;
    public bool CanConfirm => Preview.CanClassify && Kind is not null && Configuration.Name.Trim().Length != 0;

    public void ChooseKind(ConfiguredIdentityKind explicitChoice)
    {
        if (!Enum.IsDefined(explicitChoice)) throw new InvalidOperationException("Choose Assistant or Specialist.");
        if (IsContinuation && Kind != explicitChoice)
            throw new InvalidOperationException("Continue the recorded migration choice or return to its recovery review.");
        if (Kind == explicitChoice) return;
        Kind = explicitChoice; Changed();
    }

    public bool TrySetText(string field, string text)
    {
        if (IsContinuation || !Preview.CanClassify) return false;
        var changed = field switch
        {
            "MigrationName" => Configuration with { Name = text },
            "MigrationDescription" => Configuration with { Description = text },
            "MigrationPurpose" => Configuration with { Purpose = text },
            "MigrationRole" => Configuration with { Role = text },
            "MigrationInstructions" => Configuration with { Instructions = text },
            _ => null
        };
        if (changed is null) return false;
        if (changed != Configuration) { Configuration = Copy(changed); Changed(); }
        return true;
    }

    private void Changed() { _generation = checked(_generation + 1); if (!IsContinuation) _operationId = null; }

    public Submission CaptureSubmission()
    {
        if (!CanConfirm) throw new InvalidOperationException("Review this source and explicitly choose its configured identity type.");
        _operationId ??= Guid.NewGuid();
        if (_operationId == Guid.Empty) throw new InvalidOperationException("The recorded migration operation is unavailable.");
        return new(this, Preview, _generation, Kind!.Value, Copy(Configuration), _operationId.Value);
    }

    public void Acknowledge(Submission original, LegacyAgentMigrationResult actualReceipt)
    {
        if (!ReferenceEquals(original.Owner, this) || !ReferenceEquals(original.Preview, Preview) ||
            actualReceipt.LegacyAgentId != Preview.LegacyAgentId || actualReceipt.SourceRevision != Preview.SourceRevision ||
            actualReceipt.OperationId != original.OperationId || actualReceipt.Definition.Kind != original.Kind ||
            !actualReceipt.LegacySourceRetained)
            throw new InvalidOperationException("The migration receipt does not acknowledge this exact review.");
        if (_generation == original.Generation) _acknowledgedGeneration = _generation;
    }

    public sealed class Submission
    {
        internal Submission(LegacyAgentMigrationDraft owner, LegacyAgentMigrationPreview preview, long generation,
            ConfiguredIdentityKind kind, AssistantConfiguration configuration, Guid operationId)
        { Owner = owner; Preview = preview; Generation = generation; Kind = kind; Configuration = configuration; OperationId = operationId; }
        internal LegacyAgentMigrationDraft Owner { get; }
        internal long Generation { get; }
        public LegacyAgentMigrationPreview Preview { get; }
        public ConfiguredIdentityKind Kind { get; }
        public AssistantConfiguration Configuration { get; }
        public Guid OperationId { get; }
    }

    private static AssistantConfiguration Copy(AssistantConfiguration value) => value with
    {
        ToolIds = Array.AsReadOnly(value.ToolIds.ToArray()), ConnectedAppIds = Array.AsReadOnly(value.ConnectedAppIds.ToArray()),
        KnowledgeResourceIds = Array.AsReadOnly(value.KnowledgeResourceIds.ToArray()),
        ProjectReferences = Array.AsReadOnly(value.ProjectReferences.ToArray()), Modalities = Array.AsReadOnly(value.Modalities.ToArray()),
        Proactive = value.Proactive with
        {
            EventKinds = value.Proactive.EventKinds is { } events ? Array.AsReadOnly(events.ToArray()) : null,
            NotificationChannels = value.Proactive.NotificationChannels is { } channels ? Array.AsReadOnly(channels.ToArray()) : null,
            AutomationIds = value.Proactive.AutomationIds is { } automations ? Array.AsReadOnly(automations.ToArray()) : null
        }
    };
}
