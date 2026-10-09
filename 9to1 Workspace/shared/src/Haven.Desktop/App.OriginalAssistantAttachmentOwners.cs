#if !ANDROID
using Haven.Application;
using Haven.Application.Compatibility;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Attachments;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private AssistantOriginalAttachmentSource? _actualAssistantAttachments;
    private CanonicalAttachmentOriginalFileSource? _actualAssistantAttachmentContent;
    private FilesNativeBrowserService? _actualAssistantAttachmentFiles;
    private OdsAwareMessageAttachmentService? _actualAssistantAttachmentProcessing;
    private HomeCanonicalAssistantAttachmentReadSource? _actualAssistantAttachmentReads;
    private HomeCanonicalAssistantAttachmentImportSource? _actualAssistantAttachmentImports;
    private HomeAssistantAttachmentReadResourceResolver? _actualAssistantAttachmentReadResolver;
    private HomeAssistantAttachmentImportResourceResolver? _actualAssistantAttachmentImportResolver;
    private HomeAssistantAttachmentReadActionPolicySource? _actualAssistantAttachmentReadPolicy;
    private HomeAssistantAttachmentImportActionPolicySource? _actualAssistantAttachmentImportPolicy;
    private readonly List<Task> _actualAssistantAttachmentWithdrawals = [];
    private Task? _actualAssistantAttachmentDrain;

    private void PrepareOriginalAssistantAttachmentPolicies()
    {
        _actualAssistantAttachmentReadResolver = new(() => _actualAssistantAttachmentReads ?? throw new InvalidOperationException("The actual attachment READ source is not retained."));
        _actualAssistantAttachmentImportResolver = new(() => _actualAssistantAttachmentImports ?? throw new InvalidOperationException("The actual attachment import WRITE source is not retained."));
        _actualAssistantAttachmentReadPolicy = new(); _actualAssistantAttachmentImportPolicy = new();
        PrepareOriginalAssistantAttachmentEgressPolicies();
    }
    private bool HasOriginalAssistantAttachmentProvider(IServiceProvider actual) => _services is { } root &&
        (ReferenceEquals(actual, root) || ReferenceEquals(actual, root.GetRequiredService<IServiceProvider>()));
    private void ConfigureOriginalAssistantAttachmentOwnerRegistrations(IServiceCollection collection)
    {
        if (_actualWindowsHome is not { } home) return;
        Type[] owned = [typeof(AssistantOriginalAttachmentSource), typeof(IAssistantOriginalAttachmentOwner),
            typeof(IAssistantOriginalAttachmentCommandSource), typeof(OdsAwareMessageAttachmentService),
            typeof(CanonicalAttachmentOriginalFileSource), typeof(HomeCanonicalAssistantAttachmentReadSource), typeof(HomeCanonicalAssistantAttachmentImportSource),
            typeof(HomeCanonicalAssistantAttachmentEgressSource)];
        if (collection.Any(row => owned.Contains(row.ServiceType))) throw new InvalidOperationException("Retain the single actual attachment/source chain.");
        void DemandMaintained(Type type)
        {
            var row = collection.Where(value => value.ServiceType == type).ToArray();
            if (row.Length != 1 || row[0].Lifetime != ServiceLifetime.Singleton || row[0].ImplementationType != type)
                throw new InvalidOperationException("Use the maintained configured canonical attachment owner: " + type.Name);
        }
        DemandMaintained(typeof(MessageAttachmentService)); DemandMaintained(typeof(SafeMessageAttachmentService)); DemandMaintained(typeof(ConversationProductionRepository));
        // One explicit outer chain for original scoped extraction. Ordinary
        // IMessageAttachmentService remains the configured Safe service alias.
        collection.AddSingleton<OdsAwareMessageAttachmentService>(provider => new(provider.GetRequiredService<SafeMessageAttachmentService>(),
            provider.GetRequiredService<IAppPaths>(), provider.GetRequiredService<ISqliteConnectionFactory>(), provider.GetRequiredService<IRetrievalIndexService>()));
        collection.AddSingleton<AssistantOriginalAttachmentSource>(provider =>
        {
            AssistantOriginalAttachmentSource? acquired = null;
            _originalAppWork.RunSynchronous(original => acquired = AcquireOriginalAppSynchronous(original, () =>
            {
            original.DemandPublication();
            if (!HasOriginalAssistantAttachmentProvider(provider)) throw new UnauthorizedAccessException("Use the actual configured root provider.");
            _ = provider.RequireOriginalWindowsHomeComponents(home);
            if (_actualAssistantAttachments is not null || _actualAssistantAttachmentContent is not null)
                throw new InvalidOperationException("The actual attachment acquisition already exists; retain partial owners.");
            var factory = provider.GetRequiredService<HomePersonalDenFactory>();
            var conversations = provider.GetRequiredService<IConversationRepository>();
            var files = _actualAssistantAttachmentFiles = provider.GetRequiredService<FilesNativeBrowserService>();
            if (!files.IsBoundToOriginalComposition(provider.GetRequiredService<NativeFilesWorkspaceAuthority>(), home.Profiles,
                home.Resources, provider.GetRequiredService<ICompatibilityPackageContentSource>()))
                throw new UnauthorizedAccessException("Use the SAME maintained Files browser/Home source tuple.");
            var content = _actualAssistantAttachmentContent = new(files, home.Profiles);
            var processing = _actualAssistantAttachmentProcessing = provider.GetRequiredService<OdsAwareMessageAttachmentService>();
            if (!ReferenceEquals(provider.GetRequiredService<IMessageAttachmentService>(), provider.GetRequiredService<SafeMessageAttachmentService>()))
                throw new UnauthorizedAccessException("Retain the SAME configured Safe/Message parser alias.");
            var paths = provider.GetRequiredService<IAppPaths>();
            var parser = provider.GetRequiredService<MessageAttachmentService>(); var safe = provider.GetRequiredService<SafeMessageAttachmentService>();
            if (!parser.HasOriginalProcessingComposition(paths, provider.GetRequiredService<IConversationProductionRepository>(), provider.GetRequiredService<ILocalMediaToolLocator>()) ||
                !safe.HasOriginalProcessingComposition(parser, paths) || !processing.HasOriginalProcessingComposition(safe, paths,
                    provider.GetRequiredService<ISqliteConnectionFactory>(), provider.GetRequiredService<IRetrievalIndexService>()))
                throw new UnauthorizedAccessException("Retain the SAME configured Ods/Safe/Message dependency chain.");
            processing.BindOriginalContentSource(content);
            var source = _actualAssistantAttachments = new(factory, conversations, provider.GetRequiredService<ConversationProductionRepository>(),
                _actualAssistantSqliteStore!, home.Profiles, home.Ownership, files, content, processing);
            var reads = _actualAssistantAttachmentReads = new(home.StateStore, home.Profiles, home.Resources, home.Broker, home.Permissions, files);
            var imports = _actualAssistantAttachmentImports = new(home.StateStore, home.Profiles, home.Resources, home.Broker, home.Permissions, source);
            source.BindOriginalHomeSources(reads, imports);
            var chat = provider.GetRequiredService<ChatSessionService>();
            var tasks = provider.GetRequiredService<TaskExecutionCoordinator>();
            chat.BindOriginalAttachmentInputSource(source);
            source.BindOriginalInputOwners(chat, tasks);
            RetainOriginalAssistantAttachmentEgressOwners(provider, home, source);
            DemandOriginalAssistantAttachmentComposition(provider, home, factory, source); original.DemandPublication(); return source;
            }));
            return acquired ?? throw new InvalidOperationException("The actual original attachment source was not captured.");
        });
        collection.AddSingleton<IAssistantOriginalAttachmentOwner>(provider => provider.GetRequiredService<AssistantOriginalAttachmentSource>());
        collection.AddSingleton<IAssistantOriginalAttachmentCommandSource>(provider => provider.GetRequiredService<AssistantOriginalAttachmentSource>());
    }
    private void DemandOriginalAssistantAttachmentComposition(IServiceProvider provider, HomeNativeWindowsComposition home,
        HomePersonalDenFactory factory, AssistantOriginalAttachmentSource same)
    {
        if (!HasOriginalAssistantAttachmentProvider(provider) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(same, _actualAssistantAttachments) || same.OriginalClose is not null ||
            !same.HasOriginalComposition(factory, provider.GetRequiredService<IConversationRepository>()) ||
            _actualAssistantAttachmentFiles is not { } files || _actualAssistantAttachmentContent is not { } content ||
            _actualAssistantAttachmentProcessing is not { } processing || _actualAssistantAttachmentReads is not { } reads ||
            _actualAssistantAttachmentImports is not { } imports ||
            !ReferenceEquals(files, provider.GetRequiredService<FilesNativeBrowserService>()) ||
            !ReferenceEquals(processing, provider.GetRequiredService<OdsAwareMessageAttachmentService>()) ||
            !content.HasOriginalComposition(files, home.Profiles, reads) || !processing.HasOriginalContentSource(content) ||
            !same.HasOriginalHomeSources(reads, imports) ||
            !same.HasOriginalInputComposition(provider.GetRequiredService<ChatSessionService>(), provider.GetRequiredService<TaskExecutionCoordinator>()) ||
            !reads.HasOriginalComposition(home.StateStore, home.Profiles, files) ||
            !imports.HasOriginalComposition(home.StateStore, home.Profiles, same) ||
            !_actualAssistantAttachmentReadResolver!.IsBoundToOriginalOwner(reads) || !_actualAssistantAttachmentImportResolver!.IsBoundToOriginalOwner(imports))
            throw new UnauthorizedAccessException("Retain the SAME actual App/Files/parser/Home READ/import/source tuple.");
        DemandOriginalAssistantAttachmentEgressComposition(provider, home, same);
    }
    private void DemandOriginalAssistantAttachmentRetirementJoin()
    {
        _actualAssistantAttachments?.DemandExternalOriginalRetirementJoin(); _actualAssistantAttachmentContent?.DemandExternalOriginalJoin();
        _actualAssistantAttachmentReads?.DemandExternalOriginalJoin(); _actualAssistantAttachmentImports?.DemandExternalOriginalJoin();
        _actualAssistantAttachmentFiles?.DemandExternalOriginalAttachmentJoin();
        DemandOriginalAssistantAttachmentEgressRetirementJoin();
    }
    private void RequestOriginalAssistantAttachmentPendingWithdrawals(List<Exception> errors)
    {
        void Request(Action request, Func<Task?> receipt)
        {
            try { _originalAppWork.RunCloseCallback(request); } catch (Exception cause) { AddAppCause(errors, cause); }
            Task? actual = null;
            try { _originalAppWork.RunCloseCallback(() => actual = receipt()); } catch (Exception cause) { AddAppCause(errors, cause); }
            if (actual is not null) lock (_actualStartupAcquisitionGate)
                if (!_actualAssistantAttachmentWithdrawals.Any(own => ReferenceEquals(own, actual))) _actualAssistantAttachmentWithdrawals.Add(actual);
        }
        if (_actualAssistantAttachmentReads is { } reads) Request(reads.RequestOriginalPendingReviewWithdrawals, () => reads.OriginalPendingReviewWithdrawalTask);
        if (_actualAssistantAttachmentImports is { } imports) Request(imports.RequestOriginalPendingReviewWithdrawals, () => imports.OriginalPendingReviewWithdrawalTask);
        if (_actualAssistantAttachmentEgressHome is { } disclosure) Request(disclosure.RequestOriginalPendingReviewWithdrawals, () => disclosure.OriginalPendingReviewWithdrawalTask);
    }
    private Task JoinOriginalAssistantAttachmentsAfterBorrowersAsync()
    {
        DemandOriginalAssistantAttachmentRetirementJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_actualStartupAcquisitionGate)
        { if (_actualAssistantAttachmentDrain is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _actualAssistantAttachmentDrain = Drain(start.Task); } actual = _actualAssistantAttachmentDrain; }
        start?.SetResult(); return actual;
        async Task Drain(Task begin)
        {
            await begin; var errors = new List<Exception>(); RequestOriginalAssistantAttachmentPendingWithdrawals(errors);
            Task[] withdrawals; lock (_actualStartupAcquisitionGate) withdrawals = _actualAssistantAttachmentWithdrawals.ToArray();
            foreach (var raw in withdrawals) await JoinOriginalAppTaskAsync(raw, errors);
            ThrowAppCauses(errors);
            OriginalAssistantsAcquisition[] acquisitions; lock (_actualStartupAcquisitionGate) acquisitions = _actualAssistantAcquisitions.ToArray();
            if (acquisitions.Any(item => item.OriginalController.OriginalClose?.IsCompletedSuccessfully != true))
                throw new InvalidOperationException("Actual attachment borrowers remain unresolved; retain source/content/Home/store.");
            async Task Join(Func<Task> close)
            {
                Task? raw = null; try { _originalAppWork.RunCloseCallback(() => raw = close()); } catch (Exception cause) { AddAppCause(errors, cause); }
                if (raw is not null) await JoinOriginalAppTaskAsync(raw, errors); ThrowAppCauses(errors);
            }
            if (_actualAssistantAttachmentEgressCloud is { } cloud)
                await Join(() => _actualAssistantAttachmentFramesClose = cloud.CloseOriginalAttachmentFramesAndDrainAsync());
            if (_actualAssistantAttachments is { } source) await Join(source.CloseAndDrainAsync);
            if (_actualAssistantAttachmentContent is { } content) await Join(content.CloseAndDrainOriginalAsync);
            if (_actualAssistantAttachmentReads is { } reads) await Join(reads.CloseAndDrainOriginalAsync);
            if (_actualAssistantAttachmentImports is { } imports) await Join(imports.CloseAndDrainOriginalAsync);
            if (_actualAssistantAttachmentEgressHome is { } disclosure)
                await Join(() => _actualAssistantAttachmentEgressClose = disclosure.CloseAndDrainOriginalAsync());
            if (_actualAssistantAttachmentFiles is { } files) await Join(files.CloseOriginalAttachmentSelectionsAndDrainAsync);
        }
    }
}
#endif
