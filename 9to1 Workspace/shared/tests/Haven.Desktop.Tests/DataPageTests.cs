using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Data;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class DataPageTests
{
    [AvaloniaFact]
    public void Data_scene_uses_haven_grid_explorer_visual_sql_and_accessible_inputs()
    {
        using var scene = new DataHavenScene();
        var workbook = DataWorkbook.Create("Coursework data");
        workbook.Sheets[0].Name = "People";
        workbook.Sheets[0].SetCell(0, 0, "Ada");
        workbook.Sheets[0].SetCell(0, 1, "42", kind: DataCellKind.Number);
        workbook.Queries[0].Visual.Source = "People";
        workbook.Queries[0].Sql = "SELECT A, B FROM \"People\";";
        workbook.Schema.Tables.Add(new DataSchemaTable
        {
            Name = "ExternalResults",
            Columns = [new DataSchemaColumn { Name = "Score", DataType = "NUMBER" }]
        });

        scene.SetWorkbook(workbook, 0, 1, 0, 0, 0, 0, 0, 0, null);

        Assert.Equal("Coursework data", scene.WorkbookTitleInput.Text);
        Assert.Equal("People", scene.SheetNameInput.Text);
        Assert.Equal("Ada", scene.CellValueInput.Text);
        Assert.Equal(HavenAccessibleRole.Input, scene.WorkbookTitleInput.Accessibility.Role);
        Assert.Equal(HavenAccessibleRole.Input, scene.SqlInput.Accessibility.Role);
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name?.StartsWith("Data.Cell.A1", StringComparison.Ordinal) == true);
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name?.StartsWith("Data.Explorer.Sheet.", StringComparison.Ordinal) == true);
        Assert.Contains("Sheets are exposed as SQL tables", scene.ResultsText.Content, StringComparison.Ordinal);
        Assert.True(scene.RunQueryButton.GetValue(HavenProperties.Enabled));

        scene.SetQuerySafety("DELETE FROM \"People\"");
        Assert.False(scene.RunQueryButton.GetValue(HavenProperties.Enabled));
        Assert.Contains("Destructive", scene.SqlSafetyText.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element is Video or Web);
        scene.Root.ValidateUniqueNames();
    }

    [AvaloniaFact]
    public async Task Data_page_edits_cells_builds_and_runs_visual_query_saves_and_exports_state()
    {
        var workbook = DataWorkbook.Create("Initial workbook");
        workbook.Sheets[0].SetCell(0, 0, "Ada");
        var repository = new FakeDataRepository(workbook);
        var formats = new FakeDataFormats();
        var queries = new FakeDataQueries(new DataQueryResult(["A"], [["Ada"]], false, "Focused test"));
        using var page = new DataPage(new HavenEventBus(), repository, formats, queries);
        await page.InitializeAsync();
        var window = new Window { Width = 1800, Height = 2400, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            var router = new HavenInputRouter(page.SceneRoot);
            Assert.Same(page.SceneRoot, page.SceneHost.Root);
            Assert.Single(page.SceneHost.Children);

            page.Route.WorkbookTitleInput.Text = "Results workbook";
            page.Route.SheetNameInput.Text = "People";
            page.Route.CellValueInput.Text = "Grace";
            page.Route.QueryNameInput.Text = "Selected people";
            page.Route.VisualSourceInput.Text = "People";
            page.Route.VisualColumnsInput.Text = "A";
            page.Route.VisualFilterInput.Text = "A IS NOT NULL";
            page.Route.VisualLimitInput.Text = "20";
            Assert.True(page.IsDirty);
            Assert.Equal("Grace", page.Workbook!.Sheets[0].GetCell(0, 0)?.Value);

            window.UpdateLayout();
            Assert.True(page.Route.Editor.MaxScrollY > 0);
            page.Route.Editor.ScrollY = page.Route.Editor.MaxScrollY;
            window.UpdateLayout();
            Click(router, page.Route.BuildSqlButton);
            Assert.Equal("SELECT A FROM \"People\" WHERE A IS NOT NULL LIMIT 20;", page.Workbook.Queries[0].Sql);
            window.UpdateLayout();
            Click(router, page.Route.RunQueryButton);
            await WaitUntilAsync(() => queries.Calls == 1);
            Assert.Contains("Ada", page.Route.ResultsText.Content, StringComparison.Ordinal);
            Assert.Equal(page.Workbook.Queries[0].Sql, queries.LastSql);

            Assert.True(await page.SaveAsync("Focused test"));
            Assert.False(page.IsDirty);
            Assert.Equal(1, repository.SaveCalls);
            Assert.Equal("Results workbook", repository.LastSaved?.Title);
            Assert.Equal("People", repository.LastSaved?.Sheets[0].Name);

            var destination = Path.Combine(Path.GetTempPath(), "data-focused.xlsx");
            Assert.True(await page.ExportToPathAsync(destination));
            Assert.Equal(destination, formats.LastExportPath);
            Assert.Same(page.Workbook, formats.LastExportedWorkbook);
        }
        finally { window.Content = null; window.Close(); }
    }

    [Fact]
    public async Task Data_page_opens_an_app_owned_workbook_by_its_stable_id()
    {
        var current = DataWorkbook.Create("Current workbook");
        current.UpdatedAt = DateTimeOffset.UtcNow;
        var donor = DataWorkbook.Create("Forms responses");
        donor.Id = DataWorkbookAppLinks.FormsResponses;
        donor.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var repository = new FakeDataRepository(current, donor);
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries());

        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(current.Id, page.Workbook?.Id);

        var opened = await page.OpenWorkbookAsync(DataWorkbookAppLinks.FormsResponses, TestContext.Current.CancellationToken);

        Assert.True(opened);
        Assert.Equal(DataWorkbookAppLinks.FormsResponses, page.Workbook?.Id);
        Assert.Equal("Forms responses", page.Workbook?.Title);
    }

    [AvaloniaFact]
    public async Task Data_page_recalculates_formulas_renders_results_and_updates_dependents_from_cell_edits()
    {
        var workbook = DataWorkbook.Create("Formula page");
        workbook.Sheets[0].SetCell(0, 0, "10", kind: DataCellKind.Number);
        workbook.Sheets[0].SetCell(0, 1, "0", "=A1*2", DataCellKind.Formula);
        var repository = new FakeDataRepository(workbook);
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        var window = new Window { Width = 1800, Height = 2400, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.Equal("20", page.Workbook!.Sheets[0].GetCell(0, 1)!.Value);
            Assert.Equal(1, page.FormulaReport.FormulaCells);
            var spreadsheet = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>());
            spreadsheet.SelectCell(0, 1); window.UpdateLayout();
            Assert.Equal("=A1*2", page.Route.CellFormulaInput.Text); Assert.Equal("20", page.Route.CellValueInput.Text); Assert.False(page.Route.CellValueInput.GetValue(HavenProperties.Enabled)); Assert.Contains("Calculated locally", page.Route.FormulaStatusText.Content, StringComparison.Ordinal);
            spreadsheet = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); spreadsheet.SelectCell(0, 0); window.UpdateLayout(); page.Route.CellValueInput.Text = "7";
            Assert.Equal("14", page.Workbook.Sheets[0].GetCell(0, 1)!.Value); Assert.True(page.IsDirty); Assert.True(await page.SaveAsync("Formula interaction test")); Assert.Equal("14", repository.LastSaved!.Sheets[0].GetCell(0, 1)!.Value);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Spreadsheet_selection_updates_cell_chrome_without_rebuilding_or_normalizing_the_workbook()
    {
        var workbook = DataWorkbook.Create("Selection hot path");
        using var page = new DataPage(new HavenEventBus(), new FakeDataRepository(workbook), new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 1000, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            var sheet = page.Workbook!.Sheets[0];
            var late = new DataCell { Row = 50, Column = 0, Value = "late" };
            var early = new DataCell { Row = 3, Column = 0, Value = "early" };
            sheet.Cells.Clear(); sheet.Cells.Add(late); sheet.Cells.Add(early);
            var spreadsheet = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>());

            spreadsheet.SelectCell(3, 0);

            Assert.Same(spreadsheet, Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()));
            Assert.Same(late, sheet.Cells[0]); Assert.Same(early, sheet.Cells[1]);
            Assert.Equal("early", page.Route.CellValueInput.Text);
            Assert.Equal("Selected cell · A4", page.Route.SelectedCellText.Content);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Data_page_preserves_formula_references_on_sheet_rename_and_surfaces_ref_after_delete()
    {
        var workbook = DataWorkbook.Create("Structural formulas"); workbook.Sheets[0].Name = "Rates 2026"; workbook.Sheets[0].SetCell(0, 0, "2", kind: DataCellKind.Number);
        var summary = DataSheet.Create(1, "Summary"); summary.SetCell(0, 0, "0", "='Rates 2026'!A1*3", DataCellKind.Formula); workbook.Sheets.Add(summary);
        workbook.NamedRanges.Add(new DataNamedRange { Name = "Rate", RefersTo = "='Rates 2026'!$A$1" }); workbook.Queries[0].Visual.Source = "Rates 2026"; workbook.Queries[0].Sql = workbook.Queries[0].Visual.BuildSql();
        using var page = new DataPage(new HavenEventBus(), new FakeDataRepository(workbook), new FakeDataFormats(), new FakeDataQueries()); await page.InitializeAsync();
        var window = new Window { Width = 1800, Height = 2400, Content = page };
        try
        {
            window.Show(); window.UpdateLayout(); var router = new HavenInputRouter(page.SceneRoot);
            Assert.Equal("6", summary.GetCell(0, 0)!.Value); page.Route.SheetNameInput.Text = "Tax Rates";
            Assert.Equal("='Tax Rates'!A1*3", summary.GetCell(0, 0)!.Formula); Assert.Equal("'Tax Rates'!$A$1", workbook.NamedRanges[0].RefersTo); Assert.Equal("6", summary.GetCell(0, 0)!.Value); Assert.Equal("Tax Rates", workbook.Queries[0].Visual.Source); Assert.Contains("Tax Rates", workbook.Queries[0].Sql, StringComparison.Ordinal);
            window.UpdateLayout(); Click(router, page.Route.DeleteSheetButton);
            Assert.Single(workbook.Sheets); Assert.Equal("Summary", workbook.Sheets[0].Name); Assert.Equal("#REF!", workbook.Sheets[0].GetCell(0, 0)!.Value); Assert.Contains(page.FormulaReport.Issues, issue => issue.Code == DataFormulaErrorCode.Reference);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Data_page_shows_landing_state_without_creating_an_unrequested_workbook()
    {
        var repository = new FakeDataRepository();
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        Assert.Null(page.Workbook);
        Assert.False(page.IsDirty);
        Assert.Equal(0, repository.SaveCalls);
    }

    [AvaloniaFact]
    public async Task Data_page_keeps_dirty_state_when_save_fails_and_refuses_destructive_preview()
    {
        var workbook = DataWorkbook.Create("Failure test");
        var repository = new FakeDataRepository(workbook) { FailSaves = true };
        var queries = new FakeDataQueries();
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), queries);
        await page.InitializeAsync();
        page.Route.WorkbookTitleInput.Text = "Unsaved edit";
        page.Route.SqlInput.Text = "DELETE FROM \"Sheet 1\"";

        var saved = await page.SaveAsync("Expected failure");

        Assert.False(saved);
        Assert.True(page.IsDirty);
        Assert.Equal("Unsaved edit", page.Workbook?.Title);
        Assert.Contains("Couldn’t save", page.Route.StatusText.Content, StringComparison.Ordinal);
        Assert.False(page.Route.RunQueryButton.GetValue(HavenProperties.Enabled));
        Assert.Equal(0, queries.Calls);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Spreadsheet_table_sort_filter_and_keyboard_undo_redo_round_trip_cells_and_metadata(bool promoteLegacy)
    {
        var workbook = DataWorkbook.Create("Spreadsheet commands");
        var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Name"); sheet.SetCell(0, 1, "Score");
        sheet.SetCell(1, 0, "beta"); sheet.SetCell(1, 1, "10", kind: DataCellKind.Number);
        sheet.SetCell(2, 0, "alpha"); sheet.SetCell(2, 1, "30", kind: DataCellKind.Number);
        sheet.SetCell(3, 0, "gamma"); sheet.SetCell(3, 1, "20", kind: DataCellKind.Number);
        var legacyID = Guid.NewGuid();
        if (promoteLegacy) workbook.Tables.Add(new DataTableDefinition { Id = legacyID, SheetId = sheet.Id, Name = "Existing", HasHeaders = true,
            Range = new() { EndRow = 3, EndColumn = 1 } });
        using var page = new DataPage(new HavenEventBus(), new FakeDataRepository(workbook), new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        var window = new Window { Width = 3200, Height = 1400, Content = page };
        try
        {
            window.Show(); window.UpdateLayout(); var router = new HavenInputRouter(page.SceneRoot);
            var surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); surface.SelectRange(0, 0, 3, 1);
            Click(router, Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == "Data.Grid.Table.Create")));
            var definition = Assert.Single(page.Workbook!.Tables); Assert.Equal(0, definition.Range.StartRow); Assert.Equal(3, definition.Range.EndRow); Assert.True(definition.HasHeaders);

            Assert.Equal(1, definition.RecordIdentityVersion);
            if (promoteLegacy) Assert.Equal(legacyID, definition.Id);
            var tableID = definition.Id; var alphaID = definition.Records[1].RecordID; var nameID = definition.Fields[0].FieldID;
            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); surface.SelectCell(1, 1); window.UpdateLayout();
            Click(router, Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == "Data.Grid.Sort.Ascending")));
            Assert.Equal("beta", sheet.GetCell(1, 0)?.Value); Assert.Equal("10", sheet.GetCell(1, 1)?.Value);
            Assert.Equal("gamma", sheet.GetCell(2, 0)?.Value); Assert.Equal("20", sheet.GetCell(2, 1)?.Value);
            Assert.Equal("alpha", sheet.GetCell(3, 0)?.Value); Assert.Equal("30", sheet.GetCell(3, 1)?.Value);

            var filter = Assert.IsType<Input>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == "Data.Grid.Filter.Value")); filter.Text = "20"; window.UpdateLayout();
            Click(router, Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == "Data.Grid.Filter.Apply")));
            definition = Assert.Single(page.Workbook.Tables);
            var appliedFilter = Assert.Single(definition.Filters); Assert.Equal(DataFilterOperator.Contains, appliedFilter.Operator); Assert.Equal(1, appliedFilter.Column); Assert.Equal("20", appliedFilter.Value);
            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.Equal(2, surface.FilteredOutRowCount);
            Assert.Equal("beta", sheet.GetCell(1, 0)?.Value); Assert.Equal("gamma", sheet.GetCell(2, 0)?.Value); Assert.Equal("alpha", sheet.GetCell(3, 0)?.Value);

            Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Z, HavenKeyModifiers.Control)));
            definition = Assert.Single(page.Workbook.Tables); Assert.Empty(definition.Filters);
            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.Equal(0, surface.FilteredOutRowCount); Assert.Equal("gamma", sheet.GetCell(2, 0)?.Value);

            Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Z, HavenKeyModifiers.Control)));
            Assert.Equal("beta", sheet.GetCell(1, 0)?.Value); Assert.Equal("alpha", sheet.GetCell(2, 0)?.Value); Assert.Equal("gamma", sheet.GetCell(3, 0)?.Value);
            Assert.Equal(tableID, Assert.Single(page.Workbook.Tables).Id);
            Assert.Equal(2, Assert.Single(page.Workbook.Tables[0].Records, record => record.RecordID == alphaID).SheetRow);
            Assert.Equal("alpha", DataTableIdentity.ReadCell(page.Workbook, tableID, alphaID, nameID)!.Value);

            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Y, HavenKeyModifiers.Control)));
            Assert.Equal("beta", sheet.GetCell(1, 0)?.Value); Assert.Equal("gamma", sheet.GetCell(2, 0)?.Value); Assert.Equal("alpha", sheet.GetCell(3, 0)?.Value);
            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Y, HavenKeyModifiers.Control)));
            definition = Assert.Single(page.Workbook.Tables);
            Assert.Equal(tableID, definition.Id);
            Assert.Equal(3, Assert.Single(definition.Records, record => record.RecordID == alphaID).SheetRow);
            Assert.Equal("alpha", DataTableIdentity.ReadCell(page.Workbook, tableID, alphaID, nameID)!.Value);
            var redoneFilter = Assert.Single(definition.Filters); Assert.Equal(DataFilterOperator.Contains, redoneFilter.Operator); Assert.Equal(1, redoneFilter.Column); Assert.Equal("20", redoneFilter.Value);
            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.Equal(2, surface.FilteredOutRowCount);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(DataFieldType.Text, "initial", "42", DataCellKind.Text)]
    [InlineData(DataFieldType.Duration, "00:00:00", "01:02:03", DataCellKind.Text)]
    [InlineData(DataFieldType.Boolean, "false", "true", DataCellKind.Boolean)]
    public async Task Authored_field_cell_edit_preserves_declared_kind_instead_of_grid_inference(
        DataFieldType type, string initial, string edited, DataCellKind expectedKind)
    {
        var workbook = DataWorkbook.Create("Typed cell"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Value"); sheet.SetCell(1, 0, initial, kind: expectedKind);
        var table = new DataTableDefinition { SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "Value", type)], []).Workbook!;
        using var page = new DataPage(new HavenEventBus(), new FakeDataRepository(workbook), new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        var surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>());
        surface.SelectCell(1, 0); page.Route.CellValueInput.Text = edited;
        var cell = DataTableIdentity.ReadCell(page.Workbook!, table.Id, table.Records[0].RecordID, table.Fields[0].FieldID)!;
        Assert.Equal(edited, cell.Value); Assert.Equal(expectedKind, cell.Kind); Assert.True(page.IsDirty);
        Assert.Empty(DataRelationalSchema.Inspect(page.Workbook!));
    }

    [AvaloniaFact]
    public async Task Table_design_retains_invalid_and_changed_review_drafts_and_never_uses_legacy_save()
    {
        var workbook = DataWorkbook.Create("Schema design"); workbook.Version = 1; workbook.RevisionId = Guid.NewGuid();
        var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
        var table = new DataTableDefinition { SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        var repository = new FakeDataRepository(workbook); var owner = new SchemaDesignerFixture(workbook);
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries(), schemaDesigner: owner);
        await page.InitializeAsync();
        Input FieldName() => Assert.IsType<Input>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == $"Data.Schema.Name.{table.Fields[0].FieldID:N}"));
        HavenButton Button(string name) => Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == name));
        var field = FieldName(); field.Text = "";
        PressSchemaButton(Button("Data.Schema.Review"));
        Assert.Equal(0, owner.Reviews); Assert.Equal("", FieldName().Text); Assert.Equal(0, repository.SaveCalls);
        field.Text = "Score";
        PressSchemaButton(Button("Data.Schema.Review")); Assert.Equal(1, owner.Reviews);
        Assert.Equal(table.Fields[0].FieldID, Assert.Single(owner.Review!.Intent.Schema.Fields).FieldID);
        PressSchemaButton(Button("Data.Schema.Apply")); Assert.Equal(1, owner.Commits);
        Assert.Null(page.Workbook!.Tables[0].RelationalSchema); Assert.Equal("Score", FieldName().Text);
        field.Text = "Changed after review";
        PressSchemaButton(Button("Data.Schema.Apply")); Assert.Equal(1, owner.Commits); Assert.Equal(0, repository.SaveCalls);
        field.Text = "Score"; owner.Approved = true;
        PressSchemaButton(Button("Data.Schema.Apply")); Assert.Equal(2, owner.Commits);
        Assert.Equal("Score", page.Workbook!.Tables[0].RelationalSchema!.Fields[0].Name);
        Assert.Equal("1", DataTableIdentity.ReadCell(page.Workbook, table.Id, table.Records[0].RecordID, table.Fields[0].FieldID)!.Value);
        Assert.Equal(0, repository.SaveCalls); Assert.False(page.IsDirty);
        var stale = Button("Data.Schema.Review"); page.Dispose(); PressSchemaButton(stale);
        Assert.Equal(1, owner.Reviews);
    }

    [AvaloniaFact]
    public async Task Record_creation_controls_apply_authored_defaults_and_approved_formula_cache_without_legacy_save()
    {
        var (workbook, table) = RecordCreationWorkbook();
        var repository = new FakeDataRepository(workbook); var creator = new RecordCreatorFixture(workbook);
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries(), recordCreator: creator);
        await page.InitializeAsync();
        Assert.False(page.IsDirty);
        HavenButton Button(string name) => Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == name));
        var supply = Assert.IsType<Toggle>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == $"Data.RecordCreate.Supply.{table.Fields[0].FieldID:N}"));
        Assert.False(supply.IsChecked);
        PressSchemaButton(Button("Data.RecordCreate.Review")); Assert.Equal(1, creator.Reviews);
        var reviewed = creator.Review!; Assert.Empty(reviewed.Intent.Values);
        Assert.True(Assert.Single(reviewed.Intent.Arguments.GetProperty("actualValues").EnumerateArray()).GetProperty("defaultApplied").GetBoolean());
        PressSchemaButton(Button("Data.RecordCreate.Apply")); Assert.Equal(1, creator.Commits);
        Assert.Empty(page.Workbook!.Tables[0].Records); Assert.Equal(0, repository.SaveCalls);
        creator.Approved = true;
        PressSchemaButton(Button("Data.RecordCreate.Apply")); Assert.Equal(2, creator.Commits);
        var record = Assert.Single(page.Workbook!.Tables[0].Records);
        Assert.Equal(reviewed.Intent.RecordID, record.RecordID);
        Assert.Equal("7", DataTableIdentity.ReadCell(page.Workbook, table.Id, record.RecordID, table.Fields[0].FieldID)!.Value);
        Assert.Equal("14", page.Workbook.Sheets[0].GetCell(0, 2)!.Value);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(reviewed.Intent.Calculation), System.Text.Json.JsonSerializer.Serialize(page.FormulaReport));
        Assert.Equal(0, repository.SaveCalls); Assert.False(page.IsDirty);
    }

    [AvaloniaFact]
    public async Task Record_creation_controls_retain_invalid_and_changed_drafts_and_discard_explicitly()
    {
        var (workbook, table) = RecordCreationWorkbook();
        var repository = new FakeDataRepository(workbook); var creator = new RecordCreatorFixture(workbook);
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries(), recordCreator: creator);
        await page.InitializeAsync();
        Assert.False(page.IsDirty);
        HavenButton Button(string name) => Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == name));
        Input Value() => Assert.IsType<Input>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == $"Data.RecordCreate.Value.{table.Fields[0].FieldID:N}"));
        Toggle Supply() => Assert.IsType<Toggle>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == $"Data.RecordCreate.Supply.{table.Fields[0].FieldID:N}"));
        Value().Text = "invalid integer"; Assert.True(Supply().IsChecked);
        PressSchemaButton(Button("Data.RecordCreate.Review")); Assert.Equal(0, creator.Reviews); Assert.Equal("invalid integer", Value().Text);
        Assert.Contains("Amount", Assert.IsType<Haven.UI.Components.Text>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == "Data.RecordCreate.Status")).Content);
        Value().Text = "9"; PressSchemaButton(Button("Data.RecordCreate.Review")); Assert.Equal(1, creator.Reviews);
        var recordID = creator.Review!.Intent.RecordID; Value().Text = "10";
        PressSchemaButton(Button("Data.RecordCreate.Apply")); Assert.Equal(0, creator.Commits); Assert.Equal("10", Value().Text);
        PressSchemaButton(Button("Data.RecordCreate.Review")); Assert.Equal(recordID, creator.Review!.Intent.RecordID);
        Assert.Equal(10, creator.Review.Intent.Values[table.Fields[0].FieldID].Value.GetInt32());
        PressSchemaButton(Button("Data.RecordCreate.Reload")); Assert.Equal("", Value().Text); Assert.False(Supply().IsChecked);
        Assert.Equal(0, repository.SaveCalls); Assert.Empty(page.Workbook!.Tables[0].Records);
        var stale = Button("Data.RecordCreate.Review"); page.Dispose(); PressSchemaButton(stale); Assert.Equal(2, creator.Reviews);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_record_review_cannot_attach_after_workbook_revision_change_or_disposal(bool dispose)
    {
        var (workbook, table) = RecordCreationWorkbook();
        var creator = new RecordCreatorFixture(workbook)
        { HeldReview = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var page = new DataPage(new HavenEventBus(), new FakeDataRepository(workbook), new FakeDataFormats(), new FakeDataQueries(), recordCreator: creator);
        await page.InitializeAsync();
        Assert.False(page.IsDirty);
        HavenButton Button(string name) => Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == name));
        var apply = Button("Data.RecordCreate.Apply");
        PressSchemaButton(Button("Data.RecordCreate.Review")); Assert.Equal(1, creator.Reviews);
        var reviewed = creator.Review!;
        if (dispose) page.Dispose(); else { page.Workbook!.Version++; page.Workbook.RevisionId = Guid.NewGuid(); }
        creator.HeldReview.SetResult(reviewed);
        await WaitUntilAsync(() => creator.ReviewReturned);
        if (!dispose)
            await WaitUntilAsync(() => page.SceneRoot.DescendantsAndSelf().Single(item => item.Name == "Data.RecordCreate.Design").GetValue(HavenProperties.Enabled));
        await Task.Yield();
        creator.Approved = true; PressSchemaButton(apply);
        Assert.Equal(0, creator.Commits); Assert.Empty(page.Workbook!.Tables[0].Records);
    }

    private static (DataWorkbook Workbook, DataTableDefinition Table) RecordCreationWorkbook()
    {
        var workbook = DataWorkbook.Create("Atomic record"); var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "Amount");
        var table = new DataTableDefinition { Name = "Amounts", SheetId = sheet.Id, Range = new() { EndRow = 0 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        table.RelationalSchema = new(1, 1, [new(table.Fields[0].FieldID, "Amount", DataFieldType.Integer, Nullable: false, DefaultValue: "7")], []);
        sheet.SetCell(0, 2, "old cache", "=A2*2", DataCellKind.Formula);
        // Persisted owner fixtures begin with the actual formula cache, as a saved workbook does.
        // Leaving old cache deliberately makes actual DataPage load dirty and correctly blocks Review.
        _ = new DataFormulaEngine().Recalculate(workbook);
        workbook.Version = 1; workbook.RevisionId = Guid.NewGuid(); return (workbook, table);
    }

    // Only the native owner port boundary is simulated here. Actual Home and
    // physical storage admission is covered by the owning Infra fixtures.
    private sealed class RecordCreatorFixture(DataWorkbook initial) : IDataRecordCreator
    {
        public int Reviews { get; private set; }
        public int Commits { get; private set; }
        public bool Approved { get; set; }
        public DataRecordCreateReview? Review { get; private set; }
        public TaskCompletionSource<DataRecordCreateReview>? HeldReview { get; set; }
        public bool ReviewReturned { get; private set; }
        private sealed record DisplayToken(RecordCreatorFixture Issuer) : IDataRecordDisplaySelection;
        public Task<DataRecordDisplaySnapshot> LoadForDisplayAsync(Guid workbookID, CancellationToken token = default)
            => Task.FromResult(new DataRecordDisplaySnapshot(initial, new DisplayToken(this)));
        public async Task<DataRecordCreateReview> ReviewAsync(IDataRecordDisplaySelection selection, Guid workbookID,
            Guid tableID, Guid recordID, int version, Guid revision, IReadOnlyDictionary<Guid, DataScalarRecordValue> values, CancellationToken token = default)
        {
            Assert.Same(this, Assert.IsType<DisplayToken>(selection).Issuer);
            return (await ReviewAsync(workbookID, tableID, recordID, version, revision, values, token)) with { DisplaySelection = selection };
        }
        public async Task<DataRecordCreateReview> ReviewAsync(Guid workbookID, Guid tableID, Guid recordID, int version,
            Guid revision, IReadOnlyDictionary<Guid, DataScalarRecordValue> values, CancellationToken token = default)
        {
            Reviews++; Review = new("native-record-review", DataRecordCreateIntent.Capture(Guid.NewGuid(), initial, tableID, recordID, values));
            try { return HeldReview is null ? Review! : await HeldReview.Task.WaitAsync(token); }
            finally { ReviewReturned = true; }
        }
        public Task<DataRecordCreatorCommit> CommitAsync(DataRecordCreateReview review, CancellationToken token = default)
        {
            Commits++;
            if (!Approved) return Task.FromResult(new DataRecordCreatorCommit(false, "ApprovalRequired", null, false));
            var projected = DataRecordCreationProjection.Prepare(initial, review.Intent.TableID, review.Intent.RecordID,
                review.Intent.Version, review.Intent.RevisionID, review.Intent.Values, review.Intent.CalculationAt);
            Assert.True(projected.Success); var saved = projected.Workbook!; saved.Version++; saved.RevisionId = Guid.NewGuid();
            return Task.FromResult(new DataRecordCreatorCommit(true, "DataRecordCreated", saved, true, review.Intent.Calculation));
        }
    }

    [AvaloniaFact]
    public async Task Composite_unique_key_controls_retain_canonical_identity_and_require_exact_review_before_apply()
    {
        var workbook = DataWorkbook.Create("Composite candidate key"); workbook.Version = 1; workbook.RevisionId = Guid.NewGuid();
        var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "Region"); sheet.SetCell(0, 1, "Code");
        sheet.SetCell(1, 0, "North"); sheet.SetCell(1, 1, "1"); sheet.SetCell(2, 0, "North"); sheet.SetCell(2, 1, "2");
        var table = new DataTableDefinition { SheetId = sheet.Id, Range = new() { EndRow = 2, EndColumn = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        var owner = new SchemaDesignerFixture(workbook); var repository = new FakeDataRepository(workbook);
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries(), schemaDesigner: owner);
        await page.InitializeAsync();
        HavenButton Button(string name) => Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == name));
        PressSchemaButton(Button("Data.Schema.Unique.Add"));
        // Region alone has duplicates: local validation must retain this draft and make no owning review.
        PressSchemaButton(Button("Data.Schema.Review")); Assert.Equal(0, owner.Reviews);
        var keyName = Assert.Single(page.SceneRoot.DescendantsAndSelf().OfType<Input>(), input => input.Name?.StartsWith("Data.Schema.Unique.Name.", StringComparison.Ordinal) == true);
        var keyID = Guid.ParseExact(keyName.Name!["Data.Schema.Unique.Name.".Length..], "N"); keyName.Text = "Region and code";
        var secondID = table.Fields.OrderBy(field => field.SheetColumn).Last().FieldID;
        var member = Assert.IsType<Toggle>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == $"Data.Schema.Unique.Member.{keyID:N}.{secondID:N}"));
        member.IsChecked = true;
        PressSchemaButton(Button($"Data.Schema.Unique.Earlier.{keyID:N}.{secondID:N}"));
        PressSchemaButton(Button("Data.Schema.Review")); Assert.Equal(1, owner.Reviews);
        var reviewed = Assert.Single(owner.Review!.Intent.Schema.Keys); Assert.Equal(keyID, reviewed.KeyID);
        Assert.Equal(secondID, reviewed.FieldIDs[0]); Assert.Equal("Region and code", reviewed.Name);
        PressSchemaButton(Button("Data.Schema.Apply")); Assert.Equal(1, owner.Commits); Assert.Null(page.Workbook!.Tables[0].RelationalSchema);
        owner.Approved = true; PressSchemaButton(Button("Data.Schema.Apply"));
        var committed = Assert.Single(page.Workbook!.Tables[0].RelationalSchema!.Keys);
        Assert.Equal(keyID, committed.KeyID); Assert.Equal(reviewed.FieldIDs, committed.FieldIDs);
        Assert.Equal(table.Id, page.Workbook.Tables[0].Id); Assert.Equal(table.Fields.Select(field => field.FieldID), page.Workbook.Tables[0].Fields.Select(field => field.FieldID));
        Assert.Equal(table.Records.Select(record => record.RecordID), page.Workbook.Tables[0].Records.Select(record => record.RecordID));
        Assert.Equal(0, repository.SaveCalls);
    }

    private static void PressSchemaButton(HavenButton button)
    {
        Assert.True(button.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.True(button.KeyUp(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
    }

    // Native adapter fixture only; actual Home/SQL/File admission is exercised in Infrastructure tests.
    private sealed class SchemaDesignerFixture(DataWorkbook initial) : IDataTableSchemaDesigner
    {
        public int Reviews { get; private set; }
        public int Commits { get; private set; }
        public bool Approved { get; set; }
        public DataTableSchemaReview? Review { get; private set; }
        public Task<DataTableSchemaReview> ReviewAsync(Guid workbookID, Guid tableID, int expectedVersion, Guid expectedRevision,
            long? expectedSchemaRevision, IReadOnlyList<DataFieldDefinition> fields, IReadOnlyList<DataKeyDefinition> keys,
            CancellationToken cancellationToken = default)
        {
            Reviews++; Review = new("native-fixture-review", DataTableSchemaUpdateIntent.Capture(Guid.NewGuid(), initial,
                tableID, expectedSchemaRevision, fields, keys)); return Task.FromResult(Review);
        }
        public Task<DataTableSchemaDesignerCommit> CommitAsync(DataTableSchemaReview review, CancellationToken cancellationToken = default)
        {
            Commits++;
            if (!Approved) return Task.FromResult(new DataTableSchemaDesignerCommit(false, "ApprovalRequired", null, false));
            var candidate = DataTableDesign.SetSchema(initial, review.Intent.TableID, initial.Version, initial.RevisionId,
                review.Intent.ExpectedSchemaRevision, review.Intent.Schema.Fields, review.Intent.Schema.Keys).Workbook!;
            candidate.Version++; candidate.RevisionId = Guid.NewGuid();
            return Task.FromResult(new DataTableSchemaDesignerCommit(true, "DataTableSchemaUpdated", candidate, true));
        }
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        var hit = router.HitTest(point);
        Assert.True(ReferenceEquals(element, hit), $"Expected pointer hit {element.Name}, but hit {hit?.Name ?? "<none>"}. Target bounds: {element.Bounds}. Parent {element.Parent?.Name} bounds: {element.Parent?.Bounds}. Hit bounds: {hit?.Bounds}.");
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The focused Data page action did not complete.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeDataRepository(params DataWorkbook[] workbooks) : IDataWorkbookRepository
    {
        private readonly List<DataWorkbook> _workbooks = [.. workbooks];
        public int SaveCalls { get; private set; }
        public DataWorkbook? LastSaved { get; private set; }
        public bool FailSaves { get; set; }

        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DataWorkbookSummary> result = _workbooks.OrderByDescending(workbook => workbook.UpdatedAt)
                .Select(workbook => new DataWorkbookSummary(workbook.Id, workbook.Title, workbook.UpdatedAt, workbook.Version, workbook.Sheets.Count, workbook.Queries.Count, workbook.Recovery.RecoveredFromBackup)).ToArray();
            return Task.FromResult(result);
        }

        public Task<DataWorkbook?> LoadAsync(Guid workbookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_workbooks.FirstOrDefault(workbook => workbook.Id == workbookId));
        }

        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSaves) throw new IOException("Synthetic save failure");
            SaveCalls++; workbook.Normalize(); workbook.Version++; LastSaved = workbook;
            var index = _workbooks.FindIndex(item => item.Id == workbook.Id);
            if (index < 0) _workbooks.Add(workbook); else _workbooks[index] = workbook;
            var root = Path.Combine(Path.GetTempPath(), "data-fake");
            return Task.FromResult(new DataSaveResult(workbook.Id, workbook.Version, DateTimeOffset.UtcNow, Path.Combine(root, "current.json"), Path.Combine(root, "previous.json")));
        }

        public Task DeleteAsync(Guid workbookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); _workbooks.RemoveAll(workbook => workbook.Id == workbookId); return Task.CompletedTask;
        }
    }

    private sealed class FakeDataFormats : IDataWorkbookFormatService
    {
        public IReadOnlyList<string> ImportExtensions { get; } = [".xlsx"];
        public IReadOnlyList<string> ExportExtensions { get; } = [".xlsx"];
        public string? LastExportPath { get; private set; }
        public DataWorkbook? LastExportedWorkbook { get; private set; }
        public DataWorkbook ImportedWorkbook { get; set; } = DataWorkbook.Create("Imported workbook");
        public Task<DataWorkbook> ImportAsync(string sourcePath, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(ImportedWorkbook); }
        public Task<string> ExportAsync(DataWorkbook workbook, string destinationPath, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); LastExportedWorkbook = workbook; LastExportPath = destinationPath; return Task.FromResult(destinationPath); }
    }

    private sealed class FakeDataQueries(DataQueryResult? result = null) : IDataWorkbookQueryService
    {
        private readonly DataQueryResult _result = result ?? new DataQueryResult([], [], false, "Fake preview");
        public int Calls { get; private set; }
        public string? LastSql { get; private set; }
        public Task<DataQueryResult> ExecuteReadOnlyAsync(DataWorkbook workbook, string sql, int maxRows, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; LastSql = sql; return Task.FromResult(_result);
        }
    }
}
