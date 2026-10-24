using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Apps.Data;

/// <summary>
/// Bounded workbook context suitable for the shared contextual-AI router. It contains only the
/// visible Data viewport and a file name; worker handles and absolute paths never enter prompts.
/// </summary>
public sealed record DataWorkbookAiContext(
    string WorkbookName,
    bool ReadOnly,
    string ActiveSheet,
    IReadOnlyList<string> Sheets,
    DataWorkbookSelectionContext Selection,
    DataRangeSnapshot VisibleRange,
    DataWorkbookQueryContext? Query,
    IReadOnlyList<DataWorkbookActionCapability> AvailableActions);

public sealed record DataWorkbookSelectionContext(
    int Row,
    int Column,
    string Address,
    string DisplayedValue,
    string FormulaBarText);

public sealed record DataWorkbookQueryContext(
    bool IsOpen,
    string DatabaseName,
    string Sql,
    IReadOnlyList<string> PublishedTables,
    IReadOnlyList<string> ResultColumns,
    int ResultRowCount,
    bool ResultTruncated);

public sealed record DataWorkbookActionCapability(
    string ActionId,
    string Title,
    bool MutatesWorkbook,
    string RequiredCapability)
{
    public DataWorkbookActionEffect? EffectOverride { get; init; }
    public DataWorkbookActionEffect Effect => EffectOverride ?? (MutatesWorkbook
        ? DataWorkbookActionEffect.WorkbookMutation
        : DataWorkbookActionEffect.ReadOnly);
    public bool RequiresPerActionApproval => Effect is DataWorkbookActionEffect.WorkbookMutation or DataWorkbookActionEffect.DatabaseMutation;
}

public enum DataWorkbookActionEffect
{
    ReadOnly,
    WorkbookMutation,
    DatabaseMutation,
    ConnectionLifecycle,
}

/// <summary>
/// Typed actions accepted from the shared AI/action router. These are domain intents, not raw UI
/// input. The CUI controller requires an injected authorizer before executing any such request.
/// </summary>
public abstract record DataWorkbookAction
{
    public abstract string ActionId { get; }
    public abstract bool MutatesWorkbook { get; }
    public abstract string RequiredCapability { get; }
    public virtual DataWorkbookActionEffect Effect => MutatesWorkbook
        ? DataWorkbookActionEffect.WorkbookMutation
        : DataWorkbookActionEffect.ReadOnly;
    public bool RequiresPerActionApproval => Effect is DataWorkbookActionEffect.WorkbookMutation or DataWorkbookActionEffect.DatabaseMutation;
}

public sealed record DataSelectCellAction(int Row, int Column) : DataWorkbookAction
{
    public const string Id = "data.workbook.select-cell.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "workbook.read";
}

public sealed record DataSelectSheetAction(int SheetIndex) : DataWorkbookAction
{
    public const string Id = "data.workbook.select-sheet.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "workbook.read";
}

public sealed record DataOpenQueryPanelAction(string DatabasePath) : DataWorkbookAction
{
    public const string Id = "data.workbook.open-query-panel.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "database.query.read";
    public override DataWorkbookActionEffect Effect => DataWorkbookActionEffect.ConnectionLifecycle;
}

public sealed record DataCloseQueryPanelAction(
    string? DatabaseTarget = null,
    long? QuerySessionGeneration = null) : DataWorkbookAction
{
    public const string Id = "data.workbook.close-query-panel.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "database.query.read";
    public override DataWorkbookActionEffect Effect => DataWorkbookActionEffect.ConnectionLifecycle;
}

public sealed record DataPublishVisibleRangeAction(
    string TableName,
    string? SourceWorkbookId = null,
    string? SourceSheet = null,
    DataRangeRequest? SourceRange = null,
    string? DatabaseTarget = null,
    long? QuerySessionGeneration = null,
    string? SourceContentFingerprint = null) : DataWorkbookAction
{
    public const string Id = "data.workbook.publish-visible.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "database.publish";
    public override DataWorkbookActionEffect Effect => DataWorkbookActionEffect.DatabaseMutation;
}

public sealed record DataEditCellAction(int Row, int Column, string Value) : DataWorkbookAction
{
    public const string Id = "data.workbook.edit-cell.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => true;
    public override string RequiredCapability => "workbook.write";
}

public sealed record DataSetFormulaAction(int Row, int Column, string Formula) : DataWorkbookAction
{
    public const string Id = "data.workbook.set-formula.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => true;
    public override string RequiredCapability => "workbook.write";
}

public sealed record DataSortVisibleRangeAction(int KeyColumn, bool Ascending) : DataWorkbookAction
{
    public const string Id = "data.workbook.sort-visible.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => true;
    public override string RequiredCapability => "workbook.write";
}

public sealed record DataFilterVisibleRangeAction(int KeyColumn, string Value) : DataWorkbookAction
{
    public const string Id = "data.workbook.filter-visible.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => true;
    public override string RequiredCapability => "workbook.write";
}

public sealed record DataClearVisibleFilterAction : DataWorkbookAction
{
    public const string Id = "data.workbook.clear-filter.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => true;
    public override string RequiredCapability => "workbook.write";
}

public sealed record DataRunReadOnlyQueryAction(
    string Sql,
    int MaxRows = 200,
    string? DatabaseTarget = null,
    long? QuerySessionGeneration = null) : DataWorkbookAction
{
    public const string Id = "data.workbook.run-read-only-query.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "database.query.read";
}

public sealed record DataCancelReadOnlyQueryAction(
    string? DatabaseTarget = null,
    long? QuerySessionGeneration = null,
    Guid? ExecutionId = null) : DataWorkbookAction
{
    public const string Id = "data.workbook.cancel-read-only-query.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "database.query.read";
    public override DataWorkbookActionEffect Effect => DataWorkbookActionEffect.ConnectionLifecycle;
}

public sealed record DataSaveWorkbookAsAction(string DestinationPath) : DataWorkbookAction
{
    public const string Id = "data.workbook.save-as.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => true;
    public override string RequiredCapability => "files.write";
}

public sealed record DataWorkbookActionRequest(
    string InvocationId,
    string? OriginatingTaskId,
    string? OriginatingAgentId,
    DataWorkbookAction Action)
{
    /// <summary>Opaque exact-request binding for a one-action approval; includes caller/task IDs and payload.</summary>
    public string ActionFingerprint()
    {
        var payload = JsonSerializer.Serialize(new
        {
            InvocationId,
            OriginatingTaskId,
            OriginatingAgentId,
            ActionId = Action.ActionId,
            ActionPayload = JsonSerializer.Serialize(Action, Action.GetType()),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}

/// <summary>
/// Mutating workbook/database actions execute only when the host returns the exact request
/// fingerprint. Allowed alone is deliberately insufficient for consequential mutations.
/// </summary>
public sealed record DataWorkbookActionAuthorization(bool Allowed, string Reason, string? ApprovedActionFingerprint = null)
{
    public static DataWorkbookActionAuthorization Allow(string reason = "Approved by the host action router.") => new(true, reason);
    public static DataWorkbookActionAuthorization AllowFor(
        DataWorkbookActionRequest request,
        string reason = "Approved by the host action router for this exact action payload.")
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(true, reason, request.ActionFingerprint());
    }
    public static DataWorkbookActionAuthorization Deny(string reason) => new(false, reason);
}

/// <summary>
/// Adapter point for the host's central permission/approval engine. Data deliberately provides no
/// allow-all default; AI action execution is unavailable until the host supplies this contract.
/// </summary>
public interface IDataWorkbookActionAuthorizer
{
    ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(
        DataWorkbookActionRequest request,
        CancellationToken cancellationToken = default);
}

public enum DataWorkbookActionOutcome
{
    Succeeded,
    Denied,
    Failed,
    Cancelled,
}

public sealed record DataWorkbookActionResult(
    string InvocationId,
    string ActionId,
    DataWorkbookActionOutcome Outcome,
    string Message,
    DataWorkbookAiContext Context,
    string? ErrorCode = null)
{
    // Kept for the originating task's private diagnostics; never serialized into UI/action payloads.
    [JsonIgnore]
    public Exception? DiagnosticException { get; init; }

    [JsonIgnore]
    public Exception? ErrorStatePublicationException { get; init; }
}
