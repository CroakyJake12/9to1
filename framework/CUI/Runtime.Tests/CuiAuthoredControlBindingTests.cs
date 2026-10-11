using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiAuthoredControlBindingTests
{
    [Fact]
    public async Task Authored_binding_serves_one_component_without_replacing_a_reserved_builtin_type()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        var completed = await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var prepared = new ListBox();
            loader.BindAuthoredControl("dashboard-tiles", _ => prepared);
            var (root, diagnostics) = loader.LoadMarkup(
                "<Cui><Page><Input Type=\"List\" id=\"dashboard-tiles\" /><Input Type=\"List\" id=\"other-tiles\" /></Page></Cui>");
            if (diagnostics.Count != 0) return false;
            var controls = new List<Control>();
            Collect(root!, controls);
            var bound = Assert.Single(controls, control => ReferenceEquals(control, prepared));
            var other = Assert.Single(controls, control => control is ListBox && !ReferenceEquals(control, prepared));
            return loader.Inspect(bound) is not null && loader.Inspect(other) is not null;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public void Reserved_builtin_type_names_stay_closed_to_app_overrides()
    {
        var registry = new CuiControlRegistry();
        Assert.Throws<ArgumentException>(() => registry.RegisterControlType("ListBox", _ => new ListBox()));
        registry.BindAuthoredControl("dashboard-tiles", _ => new ListBox());
        Assert.Throws<ArgumentException>(() => registry.BindAuthoredControl("dashboard-tiles", _ => new ListBox()));
    }

    [Fact]
    public async Task Conditional_rebuild_reinvokes_the_authored_factory_and_records_each_control_once()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        var completed = await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var produced = new List<Control>();
            loader.BindAuthoredControl("dashboard-tiles", component =>
            {
                var list = new ListBox();
                produced.Add(list);
                return list;
            });
            var context = new CuiViewModel();
            context.Set("IsDashboard", true);
            loader.SetBindingContext(context);
            var (root, diagnostics) = loader.LoadMarkup(
                "<Cui><Page><If Condition=\"{Binding IsDashboard}\"><Input Type=\"List\" id=\"dashboard-tiles\" /></If></Page></Cui>");
            if (diagnostics.Count != 0) return false;
            context.Set("IsDashboard", false);
            loader.RefreshBindings();
            context.Set("IsDashboard", true);
            loader.RefreshBindings();
            return produced.Count >= 2 && produced.Distinct().Count() == produced.Count;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    private static void Collect(Control control, List<Control> controls)
    {
        controls.Add(control);
        if (control is Panel panel)
            foreach (var child in panel.Children) Collect(child, controls);
    }
}