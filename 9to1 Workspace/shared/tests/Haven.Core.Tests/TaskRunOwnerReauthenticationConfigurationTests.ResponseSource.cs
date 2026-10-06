using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

// Actual existing authority/lease plus synthetic controlled model source; no model/use grant added.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Response_source_local_raw_catalogue_fault_conserves_direct_OCE_sibling_and_raw_identity()
    {
        var provider = new PolicyProvider("response", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); var admission = await InferenceAdmission(authority, provider.Model);
        var one = new OperationCanceledException("response actual raw fault"); var two = new IOException("response direct sibling");
        var raw = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([one, two]); provider.Read = () => raw.Task; var originals = new List<Task>(); Exception? primary = null;
        try
        {
            var actual = authority.ValidateOriginalResponseAdmissionAsync(admission, callback => callback(), originals.Add, default);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled); Assert.True(raw.Task.IsFaulted);
            Assert.Contains(originals, value => ReferenceEquals(value, raw.Task));
            Assert.Contains(failure.Flatten().InnerExceptions, value => ReferenceEquals(value, one));
            Assert.Contains(failure.Flatten().InnerExceptions, value => ReferenceEquals(value, two));
        }
        catch (Exception error) { primary = error; throw; }
        finally { await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, [one, two]); }
    }
    [Fact]
    public async Task Response_source_restored_provider_callback_cannot_join_its_real_authority_driver()
    {
        var provider = new PolicyProvider("response", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); var admission = await InferenceAdmission(authority, provider.Model);
        var prior = ExecutionContext.Capture()!; var refused = 0; var originals = new List<Task>(); Exception? primary = null;
        provider.Read = () =>
        {
            ExecutionContext.Run(prior, _ => { Assert.Throws<InvalidOperationException>((Action)(() =>
                { _ = authority.CloseAndDrainOwnerReauthenticationAsync(); })); refused++; }, null);
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        };
        try
        {
            await authority.ValidateOriginalResponseAdmissionAsync(admission, callback => callback(), originals.Add, default);
            Assert.Equal(1, refused); Assert.NotEmpty(originals); Assert.All(originals, actual => Assert.True(actual.IsCompletedSuccessfully));
            authority.DemandOriginalInferenceAdmission(admission); // unchanged separate native-local gate remains mandatory
        }
        catch (Exception error) { primary = error; throw; }
        finally { await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, []); }
    }
    [Fact]
    public async Task Response_held_configuration_then_restored_provider_metadata_cannot_join_authority()
    {
        var inner = new PolicyProvider("response-metadata", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        inner.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([inner.Model]);
        var provider = new ResponseMetadataProvider(inner);
        var row = new ProviderConfiguration(inner.Id, inner.Kind, inner.DisplayName, "http://127.0.0.1:11434",
            true, true, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var configurations = new ResponseConfigurations(row);
        var authority = new TaskRunPermissionAuthority(new Actors(), new Registry(provider), configurations, new Privacy(), new(new PolicyStore()));
        var admission = await InferenceAdmission(authority, inner.Model); var prior = ExecutionContext.Capture()!;
        var raw = new TaskCompletionSource<ProviderConfiguration?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        configurations.Read = () => { reached.TrySetResult(); return raw.Task; };
        var originals = new List<Task>(); var refused = 0; Task? actual = null; Exception? primary = null;
        try
        {
            actual = authority.ValidateOriginalResponseAdmissionAsync(admission, callback => callback(), value =>
            { originals.Add(value); if (ReferenceEquals(value, raw.Task)) enrolled.TrySetResult(); }, default);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(actual.IsCompleted);
            Assert.Contains(originals, value => ReferenceEquals(value, raw.Task));
            provider.ReadKind = () => ExecutionContext.Run(prior, _ =>
            {
                Assert.Throws<InvalidOperationException>((Action)(() => { _ = authority.CloseAndDrainOwnerReauthenticationAsync(); }));
                refused++;
            }, null);
            raw.TrySetResult(row); await actual;
            Assert.Equal(1, refused); Assert.True(actual.IsCompletedSuccessfully);
            Assert.All(originals, value => Assert.True(value.IsCompletedSuccessfully));
            authority.DemandOriginalInferenceAdmission(admission);
        }
        catch (Exception error) { primary = error; throw; }
        finally { raw.TrySetResult(row); provider.ReadKind = null; await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, []); }
    }
    [Fact]
    public async Task Response_swallowed_actual_synchronous_catalogue_fault_is_not_a_successful_read()
    {
        var provider = new PolicyProvider("response-swallowed", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); var admission = await InferenceAdmission(authority, provider.Model);
        var one = new IOException("actual synchronous response catalogue cause"); var reads = 0; var swallowed = 0;
        provider.Read = () => { reads++; throw one; }; var originals = new List<Task>(); Exception? primary = null;
        try
        {
            var actual = authority.ValidateOriginalResponseAdmissionAsync(admission,
                callback => { try { callback(); } catch (IOException error) { Assert.Same(one, error); swallowed++; } }, originals.Add, default);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.Equal(1, reads); Assert.Equal(1, swallowed); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(failure.Flatten().InnerExceptions, cause => ReferenceEquals(cause, one));
            Assert.All(failure.Flatten().InnerExceptions, cause => Assert.Same(one, cause));
            Assert.NotEmpty(originals); Assert.All(originals, value => Assert.True(value.IsCompletedSuccessfully));
        }
        catch (Exception error) { primary = error; throw; }
        finally { await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, [one]); }
    }
    private sealed class ResponseConfigurations(ProviderConfiguration row) : IProviderConfigurationStore
    {
        internal Func<Task<ProviderConfiguration?>>? Read;
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return id == row.Id ? Read?.Invoke() ?? Task.FromResult<ProviderConfiguration?>(row) : Task.FromResult<ProviderConfiguration?>(null); }
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([row]);
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ResponseMetadataProvider(IModelProvider inner) : IModelProvider
    {
        internal Action? ReadKind;
        public string Id => inner.Id; public string DisplayName => inner.DisplayName; public bool IsLocal => inner.IsLocal;
        public bool CanManageModels => inner.CanManageModels;
        public ModelProviderKind Kind { get { ReadKind?.Invoke(); return inner.Kind; } }
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => inner.GetModelsAsync(token);
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => inner.CheckHealthAsync(token);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => inner.StreamChatAsync(request, token);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => inner.CompleteAsync(request, token);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => inner.ChatWithToolsAsync(request, token);
    }
}
