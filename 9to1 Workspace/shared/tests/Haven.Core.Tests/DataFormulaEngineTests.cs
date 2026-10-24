using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataFormulaEngineTests
{
    [Fact]
    public void Recalculation_resolves_operator_precedence_ranges_and_incremental_dependencies()
    {
        var workbook = DataWorkbook.Create("Formula workbook"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "10", kind: DataCellKind.Number); sheet.SetCell(1, 0, "5", kind: DataCellKind.Number);
        sheet.SetCell(0, 1, string.Empty, "=A1+A2*2"); sheet.SetCell(1, 1, string.Empty, "=SUM(A1:A2)");
        sheet.SetCell(2, 1, string.Empty, "=IF(B1>15,\"yes\",\"no\")"); sheet.SetCell(0, 2, string.Empty, "=AVERAGE(A1:A2)");
        sheet.SetCell(1, 2, string.Empty, "=-2^2"); sheet.SetCell(2, 2, string.Empty, "=2^-2"); sheet.SetCell(3, 2, string.Empty, "=2^3^2");
        sheet.SetCell(4, 2, string.Empty, "=-2"); sheet.SetCell(5, 2, string.Empty, "=+2"); sheet.SetCell(6, 2, string.Empty, "=2*-3"); sheet.SetCell(7, 2, string.Empty, "=-(2+3)"); sheet.SetCell(8, 2, string.Empty, "=2*(-3)");
        var engine = new DataFormulaEngine(new FixedTimeProvider());

        var initial = engine.Recalculate(workbook);

        Assert.Equal(12, initial.FormulaCells); Assert.Empty(initial.Issues);
        Assert.Equal("20", sheet.GetCell(0, 1)!.Value); Assert.Equal("15", sheet.GetCell(1, 1)!.Value); Assert.Equal("yes", sheet.GetCell(2, 1)!.Value); Assert.Equal("7.5", sheet.GetCell(0, 2)!.Value);
        Assert.Equal("4", sheet.GetCell(1, 2)!.Value); Assert.Equal("0.25", sheet.GetCell(2, 2)!.Value); Assert.Equal("64", sheet.GetCell(3, 2)!.Value);
        Assert.Equal("-2", sheet.GetCell(4, 2)!.Value); Assert.Equal("2", sheet.GetCell(5, 2)!.Value); Assert.Equal("-6", sheet.GetCell(6, 2)!.Value); Assert.Equal("-5", sheet.GetCell(7, 2)!.Value); Assert.Equal("-6", sheet.GetCell(8, 2)!.Value);
        var graph = engine.BuildDependencyGraph(workbook); var b1 = graph.Dependencies.Single(pair => pair.Key.A1 == "B1").Value; Assert.Contains(b1, address => address.A1 == "A1"); Assert.Contains(b1, address => address.A1 == "A2");

        sheet.SetCell(0, 0, "20", kind: DataCellKind.Number);
        var incremental = engine.Recalculate(workbook, [DataFormulaEngine.Address(sheet, 0, 0)]);

        Assert.True(incremental.EvaluatedCells >= 4); Assert.Equal("30", sheet.GetCell(0, 1)!.Value); Assert.Equal("25", sheet.GetCell(1, 1)!.Value); Assert.Equal("12.5", sheet.GetCell(0, 2)!.Value); Assert.Equal("yes", sheet.GetCell(2, 1)!.Value);
    }

    [Fact]
    public void Sheet_references_and_persistent_named_ranges_calculate()
    {
        var workbook = DataWorkbook.Create(); workbook.Sheets[0].Name = "Summary"; var rates = DataSheet.Create(1, "Rates 2026"); workbook.Sheets.Add(rates); rates.SetCell(0, 0, "0.2", kind: DataCellKind.Number);
        workbook.NamedRanges.Add(new DataNamedRange { Name = "TaxRate", RefersTo = "='Rates 2026'!$A$1" });
        workbook.Sheets[0].SetCell(0, 0, string.Empty, "=100*TaxRate"); workbook.Sheets[0].SetCell(0, 1, string.Empty, "='Rates 2026'!A1+1");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Empty(report.Issues); Assert.Equal("20", workbook.Sheets[0].GetCell(0, 0)!.Value); Assert.Equal("1.2", workbook.Sheets[0].GetCell(0, 1)!.Value); Assert.Equal(DataWorkbook.CurrentSchemaVersion, workbook.SchemaVersion);
    }

    [Fact]
    public void Circular_references_and_formula_errors_are_visible_and_non_throwing()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, string.Empty, "=B1+1"); sheet.SetCell(0, 1, string.Empty, "=A1+1");
        sheet.SetCell(1, 0, string.Empty, "=1/0"); sheet.SetCell(1, 1, string.Empty, "=DOES_NOT_EXIST(1)"); sheet.SetCell(1, 2, string.Empty, "=SUM("); sheet.SetCell(2, 0, string.Empty, "=#N/A"); sheet.SetCell(2, 1, string.Empty, "=IFNA(#N/A,\"available\")"); sheet.SetCell(3, 0, string.Empty, "=NA()"); sheet.SetCell(3, 1, string.Empty, "=IFNA(NA(),\"available\")"); sheet.SetCell(4, 0, string.Empty, "=ISNA(#N/A)"); sheet.SetCell(4, 1, string.Empty, "=ISERROR(1/0)"); sheet.SetCell(4, 2, string.Empty, "=ISERR(#N/A)");
        sheet.SetCell(4, 3, string.Empty, "=ISBLANK(A2)");
        sheet.SetCell(1, 3, string.Empty, "=ROWS(A2:A2)");
        sheet.SetCell(5, 0, string.Empty, "=ISNUMBER(#N/A)"); sheet.SetCell(5, 1, string.Empty, "=ISTEXT(#N/A)"); sheet.SetCell(5, 2, string.Empty, "=IFERROR(1/0,\"caught\")"); sheet.SetCell(5, 3, string.Empty, "=IFERROR(#N/A,\"caught\")"); sheet.SetCell(6, 0, string.Empty, "=IFERROR(5,1/0)"); sheet.SetCell(6, 1, string.Empty, "=ISNONTEXT(#N/A)"); sheet.SetCell(6, 2, string.Empty, "=ISLOGICAL(TRUE)"); sheet.SetCell(6, 3, string.Empty, "=ISLOGICAL(1)"); sheet.SetCell(6, 4, string.Empty, "=IF(TRUE,5,1/0)"); sheet.SetCell(7, 0, string.Empty, "=IF(FALSE,1/0,5)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("#CYCLE!", sheet.GetCell(0, 0)!.Value); Assert.Equal("#CYCLE!", sheet.GetCell(0, 1)!.Value);
        Assert.Equal("#DIV/0!", sheet.GetCell(1, 0)!.Value); Assert.Equal("#NAME?", sheet.GetCell(1, 1)!.Value); Assert.Equal("#ERROR!", sheet.GetCell(1, 2)!.Value); Assert.Equal("1", sheet.GetCell(1, 3)!.Value); Assert.Equal("#N/A", sheet.GetCell(2, 0)!.Value); Assert.Equal("available", sheet.GetCell(2, 1)!.Value); Assert.Equal("#N/A", sheet.GetCell(3, 0)!.Value); Assert.Equal("available", sheet.GetCell(3, 1)!.Value); Assert.Equal("TRUE", sheet.GetCell(4, 0)!.Value); Assert.Equal("TRUE", sheet.GetCell(4, 1)!.Value); Assert.Equal("FALSE", sheet.GetCell(4, 2)!.Value); Assert.Equal("FALSE", sheet.GetCell(4, 3)!.Value); Assert.Equal("FALSE", sheet.GetCell(5, 0)!.Value); Assert.Equal("FALSE", sheet.GetCell(5, 1)!.Value); Assert.Equal("caught", sheet.GetCell(5, 2)!.Value); Assert.Equal("caught", sheet.GetCell(5, 3)!.Value); Assert.Equal("5", sheet.GetCell(6, 0)!.Value); Assert.Equal("TRUE", sheet.GetCell(6, 1)!.Value); Assert.Equal("TRUE", sheet.GetCell(6, 2)!.Value); Assert.Equal("FALSE", sheet.GetCell(6, 3)!.Value); Assert.Equal("5", sheet.GetCell(6, 4)!.Value); Assert.Equal("5", sheet.GetCell(7, 0)!.Value);
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Cycle); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Name); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Parse); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.NotAvailable && issue.CellAddress == "A3"); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.NotAvailable && issue.CellAddress == "A4");
    }

    [Fact]
    public void Core_text_date_lookup_and_statistical_functions_use_ranges()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Alice"); sheet.SetCell(1, 0, "Bob"); sheet.SetCell(0, 1, "10", kind: DataCellKind.Number); sheet.SetCell(1, 1, "20", kind: DataCellKind.Number);
        sheet.SetCell(0, 2, string.Empty, "=XLOOKUP(\"Bob\",A1:A2,B1:B2)"); sheet.SetCell(1, 2, string.Empty, "=INDEX(B1:B2,2)"); sheet.SetCell(2, 2, string.Empty, "=MATCH(\"Alice\",A1:A2,0)");
        sheet.SetCell(3, 2, string.Empty, "=CONCAT(UPPER(A1),\"-\",ROUND(1.234,2))"); sheet.SetCell(4, 2, string.Empty, "=DATE(2026,8,17)"); sheet.SetCell(5, 2, string.Empty, "=MEDIAN(B1:B2)");
        var engine = new DataFormulaEngine(new FixedTimeProvider());

        var report = engine.Recalculate(workbook);

        Assert.Empty(report.Issues); Assert.Equal("20", sheet.GetCell(0, 2)!.Value); Assert.Equal("20", sheet.GetCell(1, 2)!.Value); Assert.Equal("1", sheet.GetCell(2, 2)!.Value); Assert.Equal("ALICE-1.23", sheet.GetCell(3, 2)!.Value); Assert.Equal("2026-08-17", sheet.GetCell(4, 2)!.Value); Assert.Equal("15", sheet.GetCell(5, 2)!.Value);
    }

    [Fact]
    public void Aggregates_distinguish_referenced_text_from_numbers_and_formula_empty_text()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "123"); sheet.SetCell(1, 0, "30", kind: DataCellKind.Number); sheet.SetCell(2, 0, string.Empty, "=\"\"");
        sheet.SetCell(0, 1, string.Empty, "=COUNT(A1:A2)"); sheet.SetCell(1, 1, string.Empty, "=SUM(A1:A2)"); sheet.SetCell(2, 1, string.Empty, "=COUNT(A1)"); sheet.SetCell(3, 1, string.Empty, "=SUM(A1)"); sheet.SetCell(4, 1, string.Empty, "=COUNTA(A3:A4)"); sheet.SetCell(5, 1, string.Empty, "=AVERAGE(A1:A2)"); sheet.SetCell(6, 1, string.Empty, "=MIN(A1:A2)"); sheet.SetCell(7, 1, string.Empty, "=MAX(A1:A2)"); sheet.SetCell(8, 1, string.Empty, "=COUNTBLANK(A3:A4)"); sheet.SetCell(9, 1, string.Empty, "=ISBLANK(A3)"); sheet.SetCell(10, 1, string.Empty, "=ISBLANK(A4)"); sheet.SetCell(11, 1, string.Empty, "=ISNUMBER(A1)"); sheet.SetCell(12, 1, string.Empty, "=ISTEXT(A1)"); sheet.SetCell(13, 1, string.Empty, "=ISNUMBER(A2)"); sheet.SetCell(14, 1, string.Empty, "=PRODUCT(A1:A2)"); sheet.SetCell(15, 1, string.Empty, "=PRODUCT(A1)"); sheet.SetCell(16, 1, string.Empty, "=SUMSQ(A1:A2)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Empty(report.Issues); Assert.Equal("1", sheet.GetCell(0, 1)!.Value); Assert.Equal("30", sheet.GetCell(1, 1)!.Value); Assert.Equal("0", sheet.GetCell(2, 1)!.Value); Assert.Equal("0", sheet.GetCell(3, 1)!.Value); Assert.Equal("1", sheet.GetCell(4, 1)!.Value); Assert.Equal("30", sheet.GetCell(5, 1)!.Value); Assert.Equal("30", sheet.GetCell(6, 1)!.Value); Assert.Equal("30", sheet.GetCell(7, 1)!.Value); Assert.Equal("2", sheet.GetCell(8, 1)!.Value); Assert.Equal("FALSE", sheet.GetCell(9, 1)!.Value); Assert.Equal("TRUE", sheet.GetCell(10, 1)!.Value); Assert.Equal("FALSE", sheet.GetCell(11, 1)!.Value); Assert.Equal("TRUE", sheet.GetCell(12, 1)!.Value); Assert.Equal("TRUE", sheet.GetCell(13, 1)!.Value); Assert.Equal("30", sheet.GetCell(14, 1)!.Value); Assert.Equal("0", sheet.GetCell(15, 1)!.Value); Assert.Equal("900", sheet.GetCell(16, 1)!.Value);
    }

    [Fact]
    public void Counta_counts_error_cells_without_returning_the_error()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, string.Empty, "=1/0"); sheet.SetCell(0, 1, string.Empty, "=COUNTA(A1:A2)"); sheet.SetCell(1, 1, string.Empty, "=COUNTBLANK(A1:A2)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("1", sheet.GetCell(0, 1)!.Value); Assert.Equal("1", sheet.GetCell(1, 1)!.Value); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero && issue.CellAddress == "A1");
    }

    [Fact]
    public void Count_ignores_errors_text_and_empty_cells_in_referenced_ranges()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, string.Empty, "=1/0"); sheet.SetCell(1, 0, "5"); sheet.SetCell(2, 0, "30", kind: DataCellKind.Number); sheet.SetCell(0, 1, string.Empty, "=COUNT(A1:A4)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("1", sheet.GetCell(0, 1)!.Value); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero && issue.CellAddress == "A1");
        Assert.DoesNotContain(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero && issue.CellAddress == "B1");
    }

    [Fact]
    public void Aggregates_without_numeric_range_values_return_their_typed_errors()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "text"); sheet.SetCell(0, 1, string.Empty, "=MEDIAN(A1:A1)"); sheet.SetCell(1, 1, string.Empty, "=AVERAGE(A1:A1)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("#VALUE!", sheet.GetCell(0, 1)!.Value); Assert.Equal("#DIV/0!", sheet.GetCell(1, 1)!.Value); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Value && issue.CellAddress == "B1"); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero && issue.CellAddress == "B2");
    }

    [Fact]
    public void Average_and_median_report_num_when_large_results_overflow()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "1E308", kind: DataCellKind.Number); sheet.SetCell(1, 0, "1E308", kind: DataCellKind.Number); sheet.SetCell(0, 1, string.Empty, "=MEDIAN(A1:A2)"); sheet.SetCell(1, 1, string.Empty, "=AVERAGE(A1:A2)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("#NUM!", sheet.GetCell(0, 1)!.Value); Assert.Equal("#NUM!", sheet.GetCell(1, 1)!.Value); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Number && issue.CellAddress == "B1"); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Number && issue.CellAddress == "B2");
    }

    [Fact]
    public void Lookup_functions_validate_modes_and_preserve_range_semantics()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Alice"); sheet.SetCell(1, 0, "Bob"); sheet.SetCell(0, 1, "10", kind: DataCellKind.Number); sheet.SetCell(1, 1, "20", kind: DataCellKind.Number);
        sheet.SetCell(0, 3, "10", kind: DataCellKind.Number); sheet.SetCell(1, 3, "20", kind: DataCellKind.Number); sheet.SetCell(0, 4, "100", kind: DataCellKind.Number); sheet.SetCell(1, 4, "200", kind: DataCellKind.Number);
        sheet.SetCell(2, 3, "15"); sheet.SetCell(2, 4, "text"); sheet.SetCell(3, 3, "15", kind: DataCellKind.Number); sheet.SetCell(3, 4, "150", kind: DataCellKind.Number);
        string[] formulas = ["=INDEX(B1:B2,2,\"not a column\")", "=INDEX(A1:B2,2,2)", "=MATCH(\"Alice\",A1:A2,0)", "=MATCH(15,D1:D2)", "=MATCH(15,D2:D1,-1)", "=MATCH(15,D1:D2,2)", "=MATCH(15,A1:B2,0)", "=VLOOKUP(15,D1:E2,2,TRUE)", "=VLOOKUP(15,D1:E2,2,FALSE)", "=VLOOKUP(20,D1:E2,2,FALSE)", "=VLOOKUP(15,D1:E2,2)", "=XLOOKUP(\"Bob\",A1:A2,B1:B2,1/0)", "=XLOOKUP(\"Missing\",A1:A2,B1:B2,1/0)", "=XLOOKUP(\"Missing\",A1:A2,B1:B2)", "=MATCH(5,D1:D2)", "=XLOOKUP(10,A1:B2,A1:B2)", "=VLOOKUP(10,D1:E2,2,\"not boolean\")", "=HLOOKUP(15,D1:E2,2)", "=HLOOKUP(100,D1:E2,2,FALSE)", "=HLOOKUP(15,D1:E2,3)", "=HLOOKUP(10,D1:E2,2,\"not boolean\")", "=LOOKUP(15,D1:D2,E1:E2)", "=LOOKUP(15,D1:D2)", "=LOOKUP(5,D1:D2,E1:E2)", "=LOOKUP(15,D1:D2,E1:E1)", "=MATCH(15,D1:D2,1.9)", "=MATCH(15,D3:D4,0)", "=MATCH(\"15\",D3:D4,0)", "=VLOOKUP(15,D3:E4,2,FALSE)", "=XLOOKUP(15,D3:D4,E3:E4)", "=LOOKUP(15,D3:D4,E3:E4)"];
        foreach (var (formula, index) in formulas.Select((formula, index) => (formula, index))) sheet.SetCell(index, 2, string.Empty, formula);

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Value && issue.CellAddress == "C1");
        Assert.Equal("#VALUE!", sheet.GetCell(0, 2)!.Value); Assert.Equal("20", sheet.GetCell(1, 2)!.Value); Assert.Equal("1", sheet.GetCell(2, 2)!.Value); Assert.Equal("1", sheet.GetCell(3, 2)!.Value); Assert.Equal("1", sheet.GetCell(4, 2)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(5, 2)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(6, 2)!.Value); Assert.Equal("100", sheet.GetCell(7, 2)!.Value); Assert.Equal("#N/A", sheet.GetCell(8, 2)!.Value); Assert.Equal("200", sheet.GetCell(9, 2)!.Value); Assert.Equal("100", sheet.GetCell(10, 2)!.Value); Assert.Equal("20", sheet.GetCell(11, 2)!.Value); Assert.Equal("#DIV/0!", sheet.GetCell(12, 2)!.Value); Assert.Equal("#N/A", sheet.GetCell(13, 2)!.Value); Assert.Equal("#N/A", sheet.GetCell(14, 2)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(15, 2)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(16, 2)!.Value); Assert.Equal("20", sheet.GetCell(17, 2)!.Value); Assert.Equal("200", sheet.GetCell(18, 2)!.Value); Assert.Equal("#REF!", sheet.GetCell(19, 2)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(20, 2)!.Value); Assert.Equal("100", sheet.GetCell(21, 2)!.Value); Assert.Equal("10", sheet.GetCell(22, 2)!.Value); Assert.Equal("#N/A", sheet.GetCell(23, 2)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(24, 2)!.Value); Assert.Equal("1", sheet.GetCell(25, 2)!.Value); Assert.Equal("2", sheet.GetCell(26, 2)!.Value); Assert.Equal("1", sheet.GetCell(27, 2)!.Value); Assert.Equal("150", sheet.GetCell(28, 2)!.Value); Assert.Equal("150", sheet.GetCell(29, 2)!.Value); Assert.Equal("150", sheet.GetCell(30, 2)!.Value);
    }

    [Fact]
    public void Text_functions_reject_invalid_optional_arguments_and_keep_valid_boundaries()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        string[] formulas = ["=MID(\"abc\",0,1)", "=MID(\"abc\",1,-1)", "=MID(\"abcdef\",2,3)", "=MID(\"abc\",\"NaN\",1)", "=LEFT(\"abc\",\"bad\")", "=ROUNDUP(1.2,\"bad\")", "=LEFT(\"abcdef\",2)", "=RIGHT(\"abcdef\",2)", "=ROUNDUP(1.21,1)", "=ROUNDDOWN(1.29,1)", "=SUBSTITUTE(\"abab\",\"a\",\"x\",2)", "=SUBSTITUTE(\"banana\",\"a\",\"o\")", "=SUBSTITUTE(\"abc\",\"z\",\"x\",1)", "=SUBSTITUTE(\"abc\",\"a\",\"x\",0)", "=REPLACE(\"abcdef\",2,3,\"Z\")", "=REPLACE(\"abc\",0,1,\"x\")", "=REPLACE(\"abc\",8,1,\"x\")", "=REPT(\"ha\",3)", "=REPT(\"x\",0)", "=REPT(\"x\",-1)", "=REPT(\"ha\",2.9)", "=REPT(\"x\",32768)", "=ROUNDUP(-1.21,1)", "=ROUNDDOWN(-1.29,1)", "=LEFT(\"🙂a\",1)", "=RIGHT(\"a🙂\",1)", "=MID(\"🙂a\",1,1)", "=REPLACE(\"🙂a\",1,1,\"x\")", "=LEN(\"🙂a\")"];
        foreach (var (formula, index) in formulas.Select((formula, index) => (formula, index))) sheet.SetCell(index, 0, string.Empty, formula);

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Value && issue.CellAddress == "A1");
        Assert.Equal("#VALUE!", sheet.GetCell(0, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(1, 0)!.Value); Assert.Equal("bcd", sheet.GetCell(2, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(3, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(4, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(5, 0)!.Value); Assert.Equal("ab", sheet.GetCell(6, 0)!.Value); Assert.Equal("ef", sheet.GetCell(7, 0)!.Value); Assert.Equal("1.3", sheet.GetCell(8, 0)!.Value); Assert.Equal("1.2", sheet.GetCell(9, 0)!.Value); Assert.Equal("abxb", sheet.GetCell(10, 0)!.Value); Assert.Equal("bonono", sheet.GetCell(11, 0)!.Value); Assert.Equal("abc", sheet.GetCell(12, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(13, 0)!.Value); Assert.Equal("aZef", sheet.GetCell(14, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(15, 0)!.Value); Assert.Equal("abcx", sheet.GetCell(16, 0)!.Value); Assert.Equal("hahaha", sheet.GetCell(17, 0)!.Value); Assert.Equal(string.Empty, sheet.GetCell(18, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(19, 0)!.Value); Assert.Equal("haha", sheet.GetCell(20, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(21, 0)!.Value); Assert.Equal("-1.3", sheet.GetCell(22, 0)!.Value); Assert.Equal("-1.2", sheet.GetCell(23, 0)!.Value); Assert.Equal("🙂", sheet.GetCell(24, 0)!.Value); Assert.Equal("🙂", sheet.GetCell(25, 0)!.Value); Assert.Equal("🙂", sheet.GetCell(26, 0)!.Value); Assert.Equal("xa", sheet.GetCell(27, 0)!.Value); Assert.Equal("2", sheet.GetCell(28, 0)!.Value);
    }

    [Fact]
    public void Date_normalizes_month_day_and_two_digit_years()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, string.Empty, "=DATE(2026,13,1)"); sheet.SetCell(1, 0, string.Empty, "=DATE(2026,1,0)"); sheet.SetCell(2, 0, string.Empty, "=DATE(26,1,1)"); sheet.SetCell(3, 0, string.Empty, "=DATE(2026,0,1)"); sheet.SetCell(4, 0, string.Empty, "=DATE(49,1,1)"); sheet.SetCell(5, 0, string.Empty, "=DATE(50,1,1)"); sheet.SetCell(6, 0, string.Empty, "=DATE(100,1,1)"); sheet.SetCell(7, 0, string.Empty, "=DATE(0,1,1)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Empty(report.Issues); Assert.Equal("2027-01-01", sheet.GetCell(0, 0)!.Value); Assert.Equal("2025-12-31", sheet.GetCell(1, 0)!.Value); Assert.Equal("2026-01-01", sheet.GetCell(2, 0)!.Value); Assert.Equal("2025-12-01", sheet.GetCell(3, 0)!.Value); Assert.Equal("2049-01-01", sheet.GetCell(4, 0)!.Value); Assert.Equal("1950-01-01", sheet.GetCell(5, 0)!.Value); Assert.Equal("0100-01-01", sheet.GetCell(6, 0)!.Value); Assert.Equal("2000-01-01", sheet.GetCell(7, 0)!.Value);
    }

    [Fact]
    public void Date_with_an_invalid_calendar_value_returns_value_error()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, string.Empty, "=DATE(-1,1,1)"); sheet.SetCell(1, 0, string.Empty, "=DATE(9999,13,1)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("#VALUE!", sheet.GetCell(0, 0)!.Value); Assert.Equal("#VALUE!", sheet.GetCell(1, 0)!.Value);
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Value && issue.CellAddress == "A1");
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Value && issue.CellAddress == "A2");
    }

    [Fact]
    public void Reversed_rectangular_range_endpoints_cover_the_same_cells()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 3, "10", kind: DataCellKind.Number); sheet.SetCell(1, 3, "20", kind: DataCellKind.Number); sheet.SetCell(0, 4, "100", kind: DataCellKind.Number); sheet.SetCell(1, 4, "200", kind: DataCellKind.Number);
        sheet.SetCell(0, 0, string.Empty, "=SUM(E2:D1)"); sheet.SetCell(0, 1, string.Empty, "=ROWS(E2:D1)"); sheet.SetCell(0, 2, string.Empty, "=COLUMNS(E2:D1)");

        var engine = new DataFormulaEngine(new FixedTimeProvider()); var report = engine.Recalculate(workbook);

        Assert.Empty(report.Issues); Assert.Equal("330", sheet.GetCell(0, 0)!.Value); Assert.Equal("2", sheet.GetCell(0, 1)!.Value); Assert.Equal("2", sheet.GetCell(0, 2)!.Value);
        sheet.SetCell(0, 3, "11", kind: DataCellKind.Number);
        var incremental = engine.Recalculate(workbook, [DataFormulaEngine.Address(sheet, 0, 3)]);
        Assert.True(incremental.EvaluatedCells >= 1); Assert.Equal("331", sheet.GetCell(0, 0)!.Value);
    }

    [Fact]
    public void Index_and_Xlookup_only_propagate_selected_cell_errors()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Alice"); sheet.SetCell(1, 0, "Bob"); sheet.SetCell(0, 1, "10", kind: DataCellKind.Number); sheet.SetCell(1, 1, string.Empty, "=1/0");
        sheet.SetCell(0, 3, "10", kind: DataCellKind.Number); sheet.SetCell(0, 4, "100", kind: DataCellKind.Number); sheet.SetCell(1, 3, "20", kind: DataCellKind.Number); sheet.SetCell(1, 4, string.Empty, "=1/0");
        sheet.SetCell(2, 0, string.Empty, "=#N/A"); sheet.SetCell(3, 0, "Found"); sheet.SetCell(2, 1, "30", kind: DataCellKind.Number); sheet.SetCell(3, 1, "40", kind: DataCellKind.Number);
        sheet.SetCell(0, 2, string.Empty, "=INDEX(B1:B2,1)"); sheet.SetCell(1, 2, string.Empty, "=XLOOKUP(\"Alice\",A1:A2,B1:B2)");
        sheet.SetCell(2, 2, string.Empty, "=INDEX(B1:B2,2)"); sheet.SetCell(3, 2, string.Empty, "=XLOOKUP(\"Bob\",A1:A2,B1:B2)"); sheet.SetCell(4, 2, string.Empty, "=VLOOKUP(10,D1:E2,2,FALSE)"); sheet.SetCell(5, 2, string.Empty, "=VLOOKUP(20,D1:E2,2,FALSE)"); sheet.SetCell(6, 2, string.Empty, "=XLOOKUP(\"Found\",A3:A4,B3:B4,99)");
        sheet.SetCell(7, 0, string.Empty, "=XLOOKUP(\"Missing\",B1:B1,E1:E1)"); sheet.SetCell(7, 3, "1", kind: DataCellKind.Number); sheet.SetCell(7, 4, "found"); sheet.SetCell(7, 2, string.Empty, "=XLOOKUP(A8,D8:D8,E8:E8,\"fallback\")"); sheet.SetCell(8, 2, string.Empty, "=HLOOKUP(10,D1:E2,2,FALSE)"); sheet.SetCell(9, 2, string.Empty, "=LOOKUP(10,D1:D2,E1:E2)"); sheet.SetCell(10, 2, string.Empty, "=LOOKUP(20,D1:D2,E1:E2)"); sheet.SetCell(11, 2, string.Empty, "=HLOOKUP(100,D1:E2,2,FALSE)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("10", sheet.GetCell(0, 2)!.Value); Assert.Equal("10", sheet.GetCell(1, 2)!.Value); Assert.Equal("#DIV/0!", sheet.GetCell(2, 2)!.Value); Assert.Equal("#DIV/0!", sheet.GetCell(3, 2)!.Value); Assert.Equal("100", sheet.GetCell(4, 2)!.Value); Assert.Equal("#DIV/0!", sheet.GetCell(5, 2)!.Value); Assert.Equal("#N/A", sheet.GetCell(6, 2)!.Value); Assert.Equal("#N/A", sheet.GetCell(7, 2)!.Value); Assert.Equal("20", sheet.GetCell(8, 2)!.Value); Assert.Equal("100", sheet.GetCell(9, 2)!.Value); Assert.Equal("#DIV/0!", sheet.GetCell(10, 2)!.Value); Assert.Equal("#DIV/0!", sheet.GetCell(11, 2)!.Value);
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero && issue.CellAddress == "C3");
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.NotAvailable && issue.CellAddress == "C7");
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.NotAvailable && issue.CellAddress == "C8");
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero && issue.CellAddress == "C10");
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero && issue.CellAddress == "C11");
    }

    [Fact]
    public void Lookup_and_index_functions_accept_single_cell_references_as_vectors()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0]; sheet.SetCell(0, 3, "10", kind: DataCellKind.Number);
        sheet.SetCell(0, 0, string.Empty, "=INDEX(D1,1)"); sheet.SetCell(1, 0, string.Empty, "=MATCH(10,D1,0)");
        sheet.SetCell(2, 0, string.Empty, "=LOOKUP(10,D1)"); sheet.SetCell(3, 0, string.Empty, "=XLOOKUP(10,D1,D1)");
        sheet.SetCell(4, 0, string.Empty, "=VLOOKUP(10,D1,1,FALSE)"); sheet.SetCell(5, 0, string.Empty, "=HLOOKUP(10,D1,1,FALSE)"); sheet.SetCell(6, 0, string.Empty, "=LOOKUP(10,D1,D1)");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Empty(report.Issues); Assert.Equal("10", sheet.GetCell(0, 0)!.Value); Assert.Equal("1", sheet.GetCell(1, 0)!.Value); Assert.Equal("10", sheet.GetCell(2, 0)!.Value); Assert.Equal("10", sheet.GetCell(3, 0)!.Value); Assert.Equal("10", sheet.GetCell(4, 0)!.Value); Assert.Equal("10", sheet.GetCell(5, 0)!.Value); Assert.Equal("10", sheet.GetCell(6, 0)!.Value);
    }

    [Fact]
    public void Copy_and_fill_respect_relative_absolute_and_mixed_A1_references()
    {
        Assert.Equal("=B2+$A2+B$1+$A$1", DataFormulaReferenceUtility.TranslateFormula("=A1+$A1+A$1+$A$1", 1, 1));
        Assert.Equal("=#REF!+$A$1", DataFormulaReferenceUtility.TranslateFormula("=A1+$A$1", 0, -1));
        Assert.Equal("=\"A1 is text\"&B2", DataFormulaReferenceUtility.TranslateFormula("=\"A1 is text\"&A1", 1, 1));
        Assert.Equal("=LOG10(B2)+B2", DataFormulaReferenceUtility.TranslateFormula("=LOG10(A1)+A1", 1, 1));
        Assert.Equal("=LOG10 (B2)+B2", DataFormulaReferenceUtility.TranslateFormula("=LOG10 (A1)+A1", 1, 1));

        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "50", kind: DataCellKind.Number); sheet.SetCell(1, 1, string.Empty, "=A1*2"); sheet.SetCell(1, 3, string.Empty, "=LOG10(A1)");
        var engine = new DataFormulaEngine(new FixedTimeProvider()); Assert.True(engine.CopyFormula(workbook, sheet.Id, 1, 1, 2, 2));
        Assert.Equal("=B2*2", sheet.GetCell(2, 2)!.Formula); Assert.Equal("200", sheet.GetCell(2, 2)!.Value);
        Assert.True(engine.CopyFormula(workbook, sheet.Id, 1, 3, 2, 4)); Assert.Equal("=LOG10(B2)", sheet.GetCell(2, 4)!.Formula); Assert.Equal("2", sheet.GetCell(2, 4)!.Value);
        sheet.SetCell(1, 1, "4", kind: DataCellKind.Number); _ = engine.Recalculate(workbook, [DataFormulaEngine.Address(sheet, 1, 1)]); Assert.Equal("8", sheet.GetCell(2, 2)!.Value);

        var edgeWorkbook = DataWorkbook.Create(); var edgeSheet = edgeWorkbook.Sheets[0]; edgeSheet.SetCell(0, 1, string.Empty, "=A1");
        Assert.True(engine.CopyFormula(edgeWorkbook, edgeSheet.Id, 0, 1, 0, 0)); Assert.Equal("=#REF!", edgeSheet.GetCell(0, 0)!.Formula); Assert.Equal("#REF!", edgeSheet.GetCell(0, 0)!.Value); Assert.Equal("#REF!", edgeSheet.GetCell(0, 1)!.Value);
    }

    [Fact]
    public void Sheet_reference_rename_rewrites_quoted_and_plain_references_without_touching_strings()
    {
        Assert.Equal("='Tax ''26'!A1+'Tax ''26'!$B$2+\"Rates 2026!A1\"", DataFormulaReferenceUtility.RenameSheetReferences("='Rates 2026'!A1+Rates 2026!$B$2+\"Rates 2026!A1\"", "Rates 2026", "Tax '26"));
        Assert.Equal("='Renamed'!A1", DataFormulaReferenceUtility.RenameSheetReferences("=Sheet1!A1", "Sheet1", "Renamed"));
    }

    [Fact]
    public void Imported_unsupported_formula_preserves_cached_result_and_reports_fallback()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 3, "42", "=UNSUPPORTED_EXCEL_FN(1)", DataCellKind.Formula);
        var cell = sheet.GetCell(0, 3)!; cell.Metadata["xlsxCachedValue"] = "42";

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("42", cell.Value); Assert.Equal("xlsx", cell.Metadata["formulaCachedFallback"]);
        Assert.Contains("Unknown function", cell.Metadata["formulaError"], StringComparison.Ordinal);
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Name && issue.CellAddress == "D1");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 17, 12, 30, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
