using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual disabled provider/whole-publication schedules. No model, peer, actor,
/// catalogue eligibility or Task permission is minted by these controlled callback ports.</summary>
public sealed class LlamaCppTaskCatalogueTests
{
    [Fact]
    public async Task ApplicationCataloguePortReturnsAndRetainsSameActualWholeWithoutModelReads()
    {
        var configurations = new DisabledConfigurations(); var provider = new LlamaCppModelProvider(new(), configurations);
        var retained = new List<Task>(); Task<IReadOnlyList<ProviderModelDescriptor>>? whole = null;
        Task? close = null; Exception? primary = null; var errors = new List<Exception>(); var callbacks = 0;
        try
        {
            whole = ((ITaskRunOriginalProviderCatalogueSource)provider).GetModelsWithinOriginalTaskSourceAsync(
                action => { callbacks++; action(); }, task => retained.Add(task), CancellationToken.None);
            Assert.Empty(await whole);
            Assert.Contains(retained, actual => ReferenceEquals(actual, whole));
            Assert.True(callbacks > 0);
            Assert.Equal(0, configurations.Reads);
            Assert.True(whole.IsCompletedSuccessfully);
            close = provider.CloseAndDrainAsync(); await close;
            Assert.True(close.IsCompletedSuccessfully);
        }
        catch (Exception cause) { primary = cause; }
        finally { await JoinWholeAndClose(); }
        Keep(primary);
        if (errors.Count != 0) throw new AggregateException("Actual catalogue control/cleanup failed.", errors);
        async Task JoinWholeAndClose()
        {
            if (whole is not null) await Join(whole, errors);
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception cause) { Keep(cause); }
            if (close is not null) await Join(close, errors);
        }
        void Keep(Exception? cause) { if (cause is not null && !errors.Any(e => ReferenceEquals(e, cause))) errors.Add(cause); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationCatalogueDeferredOrRepeatedPublicationRefusesBodyAndPreservesWhole(bool repeat)
    {
        var configurations = new DisabledConfigurations(); var provider = new LlamaCppModelProvider(new(), configurations);
        Action? delayed = null; var retained = new List<Task>(); Task? whole = null; Task? close = null;
        Exception? primary = null; var errors = new List<Exception>(); var expected = new List<Exception>();
        try
        {
            var failure = Assert.Throws<AggregateException>((Action)(() =>
            {
                _ = ((ITaskRunOriginalProviderCatalogueSource)provider).GetModelsWithinOriginalTaskSourceAsync(
                    action =>
                    {
                        if (!repeat) { delayed = action; return; }
                        action(); try { action(); } catch (InvalidOperationException) { }
                    }, task => retained.Add(task), CancellationToken.None);
            }));
            AddGraph(failure, expected);
            whole = ActualWhole(provider);
            var wholeFailure = await Capture(whole);
            Assert.True(whole.IsFaulted);
            Assert.NotNull(wholeFailure);
            var actualWholeGraph = new List<Exception>(); AddGraph(wholeFailure!, actualWholeGraph);
            Assert.Contains(actualWholeGraph, cause => ReferenceEquals(cause, failure));
            Assert.Equal(0, configurations.Reads);
            if (repeat) Assert.Contains(retained, actual => ReferenceEquals(actual, whole));
            else
            {
                Assert.NotNull(delayed);
                Assert.Throws<InvalidOperationException>((Action)(() => delayed!()));
                Assert.Empty(retained);
            }
            close = provider.CloseAndDrainAsync(); var closeFailure = await Capture(close);
            Assert.True(close.IsFaulted);
            Assert.NotNull(closeFailure);
            var actualCloseGraph = new List<Exception>(); AddGraph(closeFailure!, actualCloseGraph);
            Assert.Contains(actualCloseGraph, cause => ReferenceEquals(cause, failure));
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            try { whole ??= ActualWholeOrNull(provider); } catch (Exception cause) { errors.Add(cause); }
            if (whole is not null) await Join(whole, errors);
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (close is not null) await Join(close, errors);
        }
        // Expected graph is exact original reference custody; unrelated cleanup is a failure.
        var unexpected = errors.Where(cause => !(cause is AggregateException group && group.InnerExceptions.Count != 0)
            && !expected.Any(known => ReferenceEquals(known, cause))).ToList();
        if (primary is not null) unexpected.Insert(0, primary);
        if (unexpected.Count != 0) throw new AggregateException("Unexpected actual catalogue control/cleanup causes.", unexpected);
    }

    [Fact]
    public async Task ApplicationCatalogueForeignThreadCannotPublishOrRunFactory()
    {
        var configurations = new DisabledConfigurations(); var provider = new LlamaCppModelProvider(new(), configurations);
        Task? whole = null; Task? close = null; Exception? primary = null;
        var errors = new List<Exception>(); var expected = new List<Exception>(); var retains = 0;
        try
        {
            var failure = Assert.Throws<AggregateException>((Action)(() =>
            {
                _ = ((ITaskRunOriginalProviderCatalogueSource)provider).GetModelsWithinOriginalTaskSourceAsync(
                    action =>
                    {
                        Exception? foreign = null;
                        var thread = new Thread(() => { try { action(); } catch (Exception cause) { foreign = cause; } });
                        thread.Start(); thread.Join();
                        if (foreign is not null) ExceptionDispatchInfo.Capture(foreign).Throw();
                    }, _ => retains++, CancellationToken.None);
            }));
            AddGraph(failure, expected); whole = ActualWhole(provider);
            var observed = await Capture(whole); Assert.NotNull(observed);
            var actualWholeGraph = new List<Exception>(); AddGraph(observed!, actualWholeGraph);
            Assert.Contains(actualWholeGraph, cause => ReferenceEquals(cause, failure));
            Assert.True(whole.IsFaulted); Assert.Equal(0, retains); Assert.Equal(0, configurations.Reads);
            close = provider.CloseAndDrainAsync(); observed = await Capture(close);
            Assert.NotNull(observed);
            var actualCloseGraph = new List<Exception>(); AddGraph(observed!, actualCloseGraph);
            Assert.Contains(actualCloseGraph, cause => ReferenceEquals(cause, failure)); Assert.True(close.IsFaulted);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            try { whole ??= ActualWholeOrNull(provider); } catch (Exception cause) { errors.Add(cause); }
            if (whole is not null) await Join(whole, errors);
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (close is not null) await Join(close, errors);
        }
        var unexpected = errors.Where(cause => !(cause is AggregateException group && group.InnerExceptions.Count != 0)
            && !expected.Any(known => ReferenceEquals(known, cause))).ToList();
        if (primary is not null) unexpected.Insert(0, primary);
        if (unexpected.Count != 0) throw new AggregateException("Unexpected actual foreign-thread control/cleanup causes.", unexpected);
    }

    private static Task ActualWhole(LlamaCppModelProvider provider) => ActualWholeOrNull(provider)
        ?? throw new InvalidOperationException("The actual admitted provider Whole was not retained.");
    private static Task? ActualWholeOrNull(LlamaCppModelProvider provider)
    {
        var work = (IEnumerable)typeof(LlamaCppModelProvider).GetField("_work", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(provider)!;
        foreach (var actual in work) return (Task)actual.GetType().GetField("Whole", BindingFlags.Instance | BindingFlags.Public)!.GetValue(actual)!;
        return null;
    }
    private static async Task<Exception?> Capture(Task actual)
    { try { await actual; return null; } catch (Exception cause) { return actual.Exception ?? cause; } }
    private static async Task Join(Task actual, List<Exception> errors)
    { var failure = await Capture(actual); if (failure is not null) AddGraph(failure, errors); }
    private static void AddGraph(Exception cause, List<Exception> errors)
    {
        if (errors.Any(known => ReferenceEquals(known, cause))) return;
        errors.Add(cause);
        if (cause is AggregateException group) foreach (var child in group.InnerExceptions) AddGraph(child, errors);
        else if (cause.InnerException is { } inner) AddGraph(inner, errors);
    }
    private sealed class DisabledConfigurations : IProviderConfigurationStore
    {
        public int Reads;
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken token)
        { Reads++; throw new InvalidOperationException("No disabled catalogue may read configuration."); }
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => throw new NotSupportedException();
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken token) => throw new NotSupportedException();
    }
}
