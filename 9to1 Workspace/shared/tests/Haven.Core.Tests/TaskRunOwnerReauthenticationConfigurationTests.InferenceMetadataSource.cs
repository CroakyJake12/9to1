using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Local_inference_held_configuration_then_restored_metadata_refuses_authority_self_join()
    {
        var inner = new PolicyProvider("inference-metadata", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        inner.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([inner.Model]);
        var provider = new InferenceLocalMetadataProvider(inner);
        var row = new ProviderConfiguration(inner.Id, inner.Kind, inner.DisplayName, "http://127.0.0.1:11434",
            true, true, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var configurations = new ResponseConfigurations(row);
        TaskRunPermissionAuthority? authority = null; TaskRunAttemptAdmission? admission = null;
        Task<TaskRunAttemptAdmission>? mint = null; var prior = ExecutionContext.Capture()!;
        var raw = new TaskCompletionSource<ProviderConfiguration?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originals = new List<Task>(); var refused = 0;
        Task? actual = null; Exception? primary = null;
        try
        {
            authority = new TaskRunPermissionAuthority(new Actors(), new Registry(provider), configurations, new Privacy(), new(new PolicyStore()));
            mint = InferenceAdmission(authority, inner.Model); admission = await mint;
            configurations.Read = () => raw.Task;
            var source = Assert.IsAssignableFrom<ITaskRunOriginalInferenceLeaseSource>(admission.Lease);
            actual = source.RevalidateOriginalInferenceWithinSourceAsync(admission, callback => callback(), value =>
            { originals.Add(value); if (ReferenceEquals(value, raw.Task)) enrolled.TrySetResult(); }, default);
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(actual.IsCompleted);
            Assert.Contains(originals, value => ReferenceEquals(value, raw.Task));
            provider.ReadIsLocal = () => ExecutionContext.Run(prior, _ =>
            {
                Assert.Throws<InvalidOperationException>((Action)(() => { _ = authority.CloseAndDrainOwnerReauthenticationAsync(); }));
                refused++;
            }, null);
            raw.TrySetResult(row); await actual;
            Assert.Equal(1, refused); Assert.True(actual.IsCompletedSuccessfully);
            Assert.All(originals, value => Assert.True(value.IsCompletedSuccessfully));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            raw.TrySetResult(row);
            var failures = new List<Exception>();
            if (actual is not null)
            {
                try { await actual; }
                catch (Exception error) { Add(actual, error); }
            }
            if (authority is not null)
                try { await CloseGenuineLeaseSourceFixtureAsync(authority, admission, mint, primary); }
                catch (Exception error) { Add(null, error); }
            if (failures.Count != 0)
            {
                if (primary is not null && !failures.Any(value => ReferenceEquals(value, primary))) failures.Insert(0, primary);
                throw new AggregateException("Actual metadata mint/body and independently joined original cleanup failed.", failures);
            }
            void Add(Task? task, Exception error)
            {
                Capture(error);
                if (task?.Exception is { } group)
                { Capture(group); foreach (var cause in group.InnerExceptions) Capture(cause); }
            }
            void Capture(Exception error)
            { if (!failures.Any(value => ReferenceEquals(value, error))) failures.Add(error); }
        }
    }
    private sealed class InferenceLocalMetadataProvider(IModelProvider inner) : IModelProvider
    {
        internal Action? ReadIsLocal;
        public string Id => inner.Id; public string DisplayName => inner.DisplayName;
        public bool IsLocal { get { ReadIsLocal?.Invoke(); return inner.IsLocal; } }
        public ModelProviderKind Kind => inner.Kind; public bool CanManageModels => inner.CanManageModels;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => inner.GetModelsAsync(token);
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => inner.CheckHealthAsync(token);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => inner.StreamChatAsync(request, token);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => inner.CompleteAsync(request, token);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => inner.ChatWithToolsAsync(request, token);
    }
}
