namespace Haven.Core;

/// <summary>
/// Automation schedule kind values.
/// </summary>
public enum AutomationScheduleKind { Once, Hourly, Daily, Weekly, ConditionWatch }
/// <summary>
/// Automation run status values.
/// </summary>
public enum AutomationRunStatus { Pending, Running, Succeeded, Failed, Cancelled, SkippedDuplicate, Waiting, ApprovalRequired, Paused, Stopped, Blocked, RetryPending }

/// <summary>Only the owning admission path can make a migrated definition ready.</summary>
public enum AutomationOperationalState { NeedsAttention, Disabled, Ready, Archived }
public enum AutomationGraphPublicationPhase { Prepared, DraftPublished, ActivePublished, DefinitionCommitted, NeedsRecovery }

public enum AutomationTriggerKind { Manual, Schedule, AppEvent, ConditionWatch, ConnectorEvent, AutomationInvocation }

public enum AutomationDefinitionEntityKind { Automation = 0, ReusableTask = 1 }
