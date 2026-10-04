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
    string RequiredCapability);

/// <summary>
/// Typed actions accepted from the shared AI/action router. These are domain intents, not raw UI
/// input. The CUI controller requires an injected authorizer before executing any such request.
/// </summary>
public abstract record DataWorkbookAction
{
    public abstract string ActionId { get; }
    public abstract bool MutatesWorkbook { get; }
    public abstract string RequiredCapability { get; }
}

public sealed record DataSelectCellAction(int Row, int Column) : DataWorkbookAction
{
    public const string Id = "data.workbook.select-cell.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "workbook.read";
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

public sealed record DataRunReadOnlyQueryAction(string Sql, int MaxRows = 200) : DataWorkbookAction
{
    public const string Id = "data.workbook.run-read-only-query.v1";
    public override string ActionId => Id;
    public override bool MutatesWorkbook => false;
    public override string RequiredCapability => "database.query.read";
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
    DataWorkbookAction Action);

public sealed record DataWorkbookActionAuthorization(bool Allowed, string Reason)
{
    public static DataWorkbookActionAuthorization Allow(string reason = "Approved by the host action router.") => new(true, reason);
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
}

public sealed record DataWorkbookActionResult(
    string InvocationId,
    string ActionId,
    DataWorkbookActionOutcome Outcome,
    string Message,
    DataWorkbookAiContext Context);
