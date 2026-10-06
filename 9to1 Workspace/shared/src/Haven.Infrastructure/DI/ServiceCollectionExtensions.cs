/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ServiceCollectionExtensions.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ServiceCollectionExtensions. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Reflection;
using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure;

/// <summary>
/// Represents service collection extensions and keeps its related state and behavior together.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Performs the add haven infrastructure step owned by this component.
    /// </summary>
    public static IServiceCollection AddHavenInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IBoardsWorkspaceService, BoardsWorkspaceService>();
        services.AddSingleton<PrivacyPreferenceStore>();
        services.AddSingleton<IPrivacyPreferenceStore>(provider => provider.GetRequiredService<PrivacyPreferenceStore>());
        services.AddSingleton<ProductionDiagnostics>();
        services.AddSingleton<IProductionDiagnostics>(provider => provider.GetRequiredService<ProductionDiagnostics>());
        services.AddSingleton<INotesDocumentValidator, NotesDocumentValidator>();
        services.AddSingleton<INotesDocumentMigrator, NotesDocumentMigrator>();
        services.AddSingleton<NotesRepository>();
        services.AddSingleton<VerifiedNotesRepository>();
        services.AddSingleton<MigratingNotesRepository>();
        services.AddSingleton<INotesRepository>(provider => provider.GetRequiredService<MigratingNotesRepository>());
        services.AddSingleton<NotesImportExportService>();
        services.AddSingleton<MigratingNotesImportExportService>();
        services.AddSingleton<INotesImportExportService>(provider => provider.GetRequiredService<MigratingNotesImportExportService>());
        services.AddSingleton<IPresentRepository, PresentRepository>();
        services.AddSingleton<IPresentExportService, PresentPptxExportService>();
        services.AddSingleton<IPresentImportService, PresentPptxImportService>();
        services.AddSingleton<IDocumentShapeGallery, DocumentShapeGalleryRepository>();
        services.AddSingleton<IDataWorkbookRepository, DataWorkbookRepository>();
        services.AddSingleton<IDataWorkbookFormatService, DataXlsxFormatService>();
        services.AddSingleton<IDataWorkbookQueryService, DataWorkbookQueryService>();
        services.AddSingleton<NotesAttachmentStore>();
        services.AddSingleton<SecureNotesAttachmentStore>();
        services.AddSingleton<INotesAttachmentStore>(provider => provider.GetRequiredService<SecureNotesAttachmentStore>());
        services.AddSingleton<INotesMediaAssetService, NotesMediaAssetService>();
        services.AddSingleton<IImagineProjectRepository, ImagineProjectRepository>();
        services.AddSingleton<IImagineSemanticService, ImagineSemanticService>();
        services.AddSingleton<IImagineAssistantService, ImagineAssistantService>();
        services.AddSingleton<DatabaseMaintenanceService>();
        services.AddSingleton<IDatabaseMaintenance>(provider => provider.GetRequiredService<DatabaseMaintenanceService>());
        services.AddSingleton<DatabaseRestoreService>();
        services.AddSingleton<SecureDatabaseRestoreService>();
        services.AddSingleton<IDatabaseRestoreService>(provider => provider.GetRequiredService<SecureDatabaseRestoreService>());
        services.AddSingleton<StartupRecoveryCoordinator>();
        services.AddSingleton<CleanResetStartupRecoveryCoordinator>();
        services.AddSingleton<IStartupRecoveryCoordinator>(provider => provider.GetRequiredService<CleanResetStartupRecoveryCoordinator>());
        services.AddSingleton<IRecoverySafetyProbe, RecoverySafetyProbe>();
        services.AddSingleton<IDiagnosticsBundleService, DiagnosticsBundleService>();
        services.AddSingleton<SqliteDatabase>();
        services.AddSingleton<IAppDatabase, ConversationProductionDatabase>();
        services.AddSingleton<ISqliteConnectionFactory>(provider => provider.GetRequiredService<SqliteDatabase>());
        services.AddSingleton<IExecutionEventRepository, ExecutionEventRepository>();
        services.AddSingleton<IActionFeedbackRepository, ActionFeedbackRepository>();
        services.AddSingleton<IRemediationRepository, RemediationRepository>();
        services.AddSingleton<IExternalAgentTaskRepository, ExternalAgentTaskRepository>();
        services.AddSingleton<IHavenNotificationRepository, HavenNotificationRepository>();
        services.AddSingleton<IWorkspaceSessionRepository, WorkspaceSessionRepository>();
        services.AddSingleton<IExtensionRepository, ExtensionRepository>();
        services.AddSingleton<ExecutionEventHub>();
        services.AddSingleton<IExecutionEventSink>(provider => provider.GetRequiredService<ExecutionEventHub>());
        services.AddSingleton<ITaskExecutionRepository, TaskExecutionRepository>();
        services.AddSingleton<ExecutionTraceService>();
        services.AddSingleton<AutonomousRecoveryService>();
        services.AddSingleton<RemediationContinuationRegistry>();
        services.AddSingleton<ExternalAgentTaskService>();
        services.AddSingleton<HavenNotificationService>();
        services.AddSingleton<ConversationSafetyService>();
        services.AddSingleton<IConversationSafetyService>(provider => provider.GetRequiredService<ConversationSafetyService>());
        services.AddSingleton<ITextEmbeddingService, LocalHashEmbeddingService>();
        services.AddSingleton<RetrievalIndexService>();
        services.AddSingleton<IRetrievalIndexService>(provider => provider.GetRequiredService<RetrievalIndexService>());
        services.AddSingleton<IRetrievalSearchService>(provider => provider.GetRequiredService<RetrievalIndexService>());
        services.AddSingleton<ProviderUsageCaptureBuffer>();
        services.AddSingleton<IProviderPricingService, ProviderPricingService>();
        services.AddSingleton<IModelUsageRepository, ModelUsageRepository>();
        services.AddSingleton<ConversationRepository>();
        services.AddSingleton<UsageTrackingConversationRepository>();
        services.AddSingleton<IConversationRepository>(provider => provider.GetRequiredService<UsageTrackingConversationRepository>());
        services.AddSingleton<ConversationProductionRepository>();
        services.AddSingleton<SafeConversationProductionRepository>();
        services.AddSingleton<IConversationProductionRepository>(provider => provider.GetRequiredService<SafeConversationProductionRepository>());
        services.AddSingleton<IConversationVersioningService, ConversationVersioningService>();
        services.AddSingleton<IConversationExportService, ConversationExportService>();
        services.AddSingleton<ILocalConversationShareService, LocalConversationShareService>();
        services.AddSingleton<ILocalMediaToolLocator, LocalMediaToolLocator>();
        services.AddSingleton<MessageAttachmentService>();
        services.AddSingleton<SafeMessageAttachmentService>();
        services.AddSingleton<IMessageAttachmentService>(provider => provider.GetRequiredService<SafeMessageAttachmentService>());
        services.AddSingleton<IContainerRepository, ContainerRepository>();
        services.AddSingleton<IContainerResourceRepository, ContainerResourceRepository>();
        services.AddSingleton<ICatalogRepository, CatalogRepository>();
        services.AddSingleton<IAgentRunRepository, AgentRunRepository>();
        services.AddSingleton<ICapabilityRepository, CapabilityRepository>();
        services.AddSingleton<IExternalConnectionRepository, ExternalConnectionRepository>();
        services.AddSingleton<McpConnectionClient>();
        services.AddSingleton<IMcpConnectionClient>(provider => provider.GetRequiredService<McpConnectionClient>());
        services.AddSingleton<ExternalConnectionRegistryService>();
        services.AddSingleton<McpToolRuntime>();
        services.AddSingleton<CapabilityRegistryService>();
        services.AddSingleton<IGenUiTemplateRepository, GenUiTemplateRepository>();
        services.AddSingleton<IGenUiAppRepository, GenUiAppRepository>();
        services.AddSingleton<GenUiAppSessionService>();
        services.AddSingleton<GenUiInstanceStore>();
        services.AddSingleton<GenUiLiveActivityTracker>();
        services.AddSingleton<GenUiLocalActionRegistry>();
        services.AddSingleton<IGenUiEventHandler>(provider => provider.GetRequiredService<GenUiLocalActionRegistry>());
        services.AddSingleton<GenUiAppEventHandler>();
        services.AddSingleton<IGenUiEventHandler>(provider => provider.GetRequiredService<GenUiAppEventHandler>());
        services.AddSingleton<CalculatorTemplateRuntime>();
        services.AddSingleton<StructuredFormTemplateRuntime>();
        services.AddSingleton<ChoicePromptTemplateRuntime>();
        services.AddSingleton<ChecklistTemplateRuntime>();
        services.AddSingleton<DataGridTemplateRuntime>();
        services.AddSingleton<CardDeckTemplateRuntime>();
        services.AddSingleton<GraphTemplateRuntime>();
        services.AddSingleton<TaskListTemplateRuntime>();
        services.AddSingleton<DashboardTemplateRuntime>();
        services.AddSingleton<AssessmentTemplateRuntime>();
        services.AddSingleton<WorkflowTemplateRuntime>();
        services.AddSingleton<CustomTemplateRuntime>();
        services.AddSingleton<BoundedGenUiEventAuditSink>();
        services.AddSingleton<IGenUiEventAuditSink>(provider => provider.GetRequiredService<BoundedGenUiEventAuditSink>());
        services.AddSingleton<GenerativeUiEventRouter>();
        services.AddSingleton<IWorkspaceStateRepository, WorkspaceStateRepository>();
        services.AddSingleton<IProjectIntelligenceService, ProjectIntelligenceService>();
        services.AddSingleton<IAutomationRepository, AutomationRepository>();
        services.AddSingleton<ITrainingRepository, TrainingRepository>();
        services.AddSingleton<IDashboardRepository, DashboardRepository>();
        services.AddSingleton<IDashboardLayoutRepository, DashboardLayoutRepository>();
        services.AddSingleton<IDashboardTileProviderRegistry, DashboardTileProviderRegistry>();
        services.AddSingleton<ICallRepository, CallRepository>();
        services.AddSingleton<ISpeechInputService, WindowsSpeechInputService>();
        services.AddSingleton<ISpeechOutputService, SystemSpeechOutputService>();
        services.AddSingleton<IScreenShareService, UnsupportedScreenShareService>();
        services.AddHttpClient<ISpeechModelManager, WhisperModelManager>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<CallOptimizedOllamaClient>();
        services.AddSingleton<CallCoordinator>();
        services.AddSingleton<ResponsiveCallCoordinator>();
        services.AddSingleton<VoiceProfileCatalog>();
        services.AddSingleton<KnowledgeLibraryService>();
        services.AddSingleton<IKnowledgeLibrary>(provider => provider.GetRequiredService<KnowledgeLibraryService>());
        services.AddSingleton<IMemoryQuerySource>(provider => provider.GetRequiredService<KnowledgeLibraryService>());
        services.AddSingleton<IApiBank, ApiBankService>();
        services.AddSingleton<IKnowledgeMaintenanceService, KnowledgeMaintenanceService>();
        services.AddSingleton<BackgroundLearningScheduler>();
        services.AddSingleton<IBackgroundLearningScheduler>(provider => provider.GetRequiredService<BackgroundLearningScheduler>());
        services.AddSingleton<PermissionDecisionEngine>();
        services.AddSingleton<IPermissionDecisionEngine>(provider => provider.GetRequiredService<PermissionDecisionEngine>());
        services.AddSingleton<IPermissionOriginalEffectFence>(provider => provider.GetRequiredService<PermissionDecisionEngine>());
        services.AddSingleton<ICallCoordinator>(provider => provider.GetRequiredService<ResponsiveCallCoordinator>());
        services.AddSingleton<ILegacyStateMigrator, LegacyStateMigrator>();
        services.AddSingleton<IWorkspaceToolService, WorkspaceToolService>();
        services.AddSingleton<ITerminalSessionFactory, PowerShellTerminalSessionFactory>();
        services.AddSingleton<IWorkspaceTransactionService, WorkspaceTransactionService>();
        services.AddSingleton<ILanguageServerConfigurationStore, LanguageServerConfigurationStore>();
        services.AddSingleton<ILanguageServerClientFactory, LanguageServerClientFactory>();
        services.AddSingleton<ProductionCodeIntelligenceService>();
        services.AddSingleton<ICodeIntelligenceService, SafeModeCodeIntelligenceService>();
        services.AddSingleton<IAdvancedCodeIntelligenceService, AdvancedCodeIntelligenceService>();
        services.AddSingleton<IComputerUseSessionController, ComputerUseSessionController>();
        services.AddSingleton<IComputerToolService, WindowsComputerToolService>();
        services.AddSingleton<Haven.Application.Automations.IDeviceActionProvider, Haven.Application.Automations.WindowsComputerDeviceActionProvider>();
        services.AddSingleton<Haven.Application.Automations.DeviceActionRouter>();
        services.AddSingleton<Haven.Application.Automations.DeviceAutomationNodeExecutor>();
        services.AddSingleton<Haven.Application.Automations.BuiltInAutomationActionNodeExecutor>();
        services.AddSingleton<Haven.Application.Automations.IAutomationGraphAiEditor, Haven.Application.Automations.AutomationGraphAiEditor>();
        services.AddHttpClient<OllamaClient>(client =>
        {
            var endpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST")?.Trim();
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = "http://127.0.0.1:11434/";
            if (!endpoint.EndsWith("/", StringComparison.Ordinal)) endpoint += "/";
            client.BaseAddress = new Uri(endpoint, UriKind.Absolute);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<IOllamaClient>(provider => provider.GetRequiredService<OllamaClient>());
        services.AddSingleton<ILocalOllamaClient, LocalOllamaClientAdapter>();

        services.AddHttpClient("Haven.ModelProvider.openai");
        services.AddHttpClient("Haven.ModelProvider.openrouter");
        services.AddHttpClient("Haven.ModelProvider.openai-compatible");
        services.AddHttpClient("Haven.ModelProvider.anthropic");
        services.AddHttpClient("Haven.ModelProvider.gemini");
        services.AddSingleton<IProviderConfigurationStore, ProviderConfigurationStore>();
        services.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
        services.AddSingleton<IModelFallbackOrderStore, VersionedModelFallbackOrderStore>();
        services.AddSingleton<IModelPersonalisationStore, VersionedModelPersonalisationStore>();
        services.AddSingleton<IModelPermissionStore, VersionedModelPermissionStore>();
        services.AddSingleton<ModelPersonalityService>();
        services.AddSingleton<ModelPermissionEvaluator>();
        services.AddSingleton<IDefaultProviderStore, VersionedDefaultProviderStore>();
        services.AddSingleton<CheckpointService>();
        services.AddSingleton<ICheckpointExecutionObservationSource>(provider => provider.GetRequiredService<CheckpointService>());
        services.AddSingleton<ICheckpointRepository, SqliteCheckpointRepository>();
        services.AddSingleton<ICheckpointRestorer, WorkspaceCheckpointRestorer>();
        services.AddSingleton<IProjectInstructionSource, ProjectInstructionFileSource>();
        services.AddSingleton<IImagineGenerationService, OpenAiImagineGenerationService>();
        services.AddSingleton<ImagineGenerationCommand>();
        services.AddSingleton<RemediationCoordinator>();
        // Task identity is scoped to the local execution host, separate from Home/resource grants.
        services.AddSingleton<HostLocalTaskActorSource>();
        services.AddSingleton<WorkspaceTaskRunReceiptAuthority>();
        services.AddSingleton<ITaskRunActionReceiptAuthority>(provider => provider.GetRequiredService<WorkspaceTaskRunReceiptAuthority>());
        services.AddSingleton<TaskRunCentralCloudUsePermissionSource>(provider => new TaskRunCentralCloudUsePermissionSource(
            provider.GetRequiredService<HostLocalTaskActorSource>(),
            provider.GetRequiredService<PermissionDecisionEngine>()));
        services.AddSingleton<ITaskRunCloudUsePermissionSource>(provider => provider.GetRequiredService<TaskRunCentralCloudUsePermissionSource>());
        services.AddSingleton<TaskRunConfiguredCloudAdmissionSource>(provider => new TaskRunConfiguredCloudAdmissionSource(
            provider.GetRequiredService<HostLocalTaskActorSource>(),
            provider.GetRequiredService<IProviderConfigurationStore>(),
            provider.GetRequiredService<IProviderSecretStore>(),
            provider.GetRequiredService<IPrivacyPreferenceStore>(),
            provider.GetRequiredService<IConversationRepository>(),
            (task, run, attempt, token) => provider.GetRequiredService<TaskExecutionCoordinator>()
                .TryGetIssuedAttemptAsync(task, run, attempt, token),
            knowledge: provider.GetService<IKnowledgeLibrary>(),
            cloudUsePermission: provider.GetRequiredService<ITaskRunCloudUsePermissionSource>()));
        services.AddSingleton<ITaskRunCloudAdmissionSource>(provider => provider.GetRequiredService<TaskRunConfiguredCloudAdmissionSource>());
        services.AddSingleton<ITaskRunProviderContextCapture>(provider => provider.GetRequiredService<TaskRunConfiguredCloudAdmissionSource>());
        services.AddSingleton<ITaskRunProviderContextAuthority>(provider => provider.GetRequiredService<TaskRunConfiguredCloudAdmissionSource>());
        services.AddSingleton<TaskRunPermissionAuthority>(provider => new TaskRunPermissionAuthority(
            provider.GetRequiredService<HostLocalTaskActorSource>(),
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IProviderConfigurationStore>(),
            provider.GetRequiredService<IPrivacyPreferenceStore>(),
            provider.GetRequiredService<ModelPermissionEvaluator>(),
            cloud: provider.GetRequiredService<ITaskRunCloudAdmissionSource>(),
            receipts: provider.GetRequiredService<ITaskRunActionReceiptAuthority>(),
            routes: provider.GetService<ITaskRunRouteObservationSource>()));
        services.AddSingleton<ITaskRunAdmissionAuthority>(provider => provider.GetRequiredService<TaskRunPermissionAuthority>());
        services.AddSingleton<ITaskRunCommandAuthority>(provider => provider.GetRequiredService<TaskRunPermissionAuthority>());
        services.AddSingleton<ITaskRunSelectedRouteCapture>(provider => provider.GetRequiredService<TaskRunPermissionAuthority>());
        services.AddSingleton<ITaskRunOriginalIssuedRouteConfigurationSource>(provider => provider.GetRequiredService<TaskRunPermissionAuthority>());
        services.AddSingleton<ITaskRunOriginalInferenceAdmissionSource>(provider => provider.GetRequiredService<TaskRunPermissionAuthority>());
        services.AddSingleton<ITaskRunOriginalResponseAdmissionSource>(provider => provider.GetRequiredService<TaskRunPermissionAuthority>());
        services.AddSingleton<TaskRunCloudPermissionRemediationOwner>(provider => new TaskRunCloudPermissionRemediationOwner(
            provider.GetRequiredService<TaskRunCentralCloudUsePermissionSource>(),
            provider.GetRequiredService<TaskRunPermissionAuthority>(),
            () => provider.GetRequiredService<TaskExecutionCoordinator>(),
            provider.GetRequiredService<RemediationCoordinator>(),
            provider.GetRequiredService<IRemediationRepository>(),
            provider.GetRequiredService<RemediationContinuationRegistry>(),
            provider.GetRequiredService<IExecutionEventSink>()));
        services.AddSingleton<TaskRunOriginalFrameOwner>(provider => new TaskRunOriginalFrameOwner(
            (task, run, attempt, token) => provider.GetRequiredService<TaskExecutionCoordinator>()
                .TryGetIssuedAttemptAsync(task, run, attempt, token)));
        services.AddSingleton<ITaskRunOriginalFrameOwner>(provider => provider.GetRequiredService<TaskRunOriginalFrameOwner>());
        services.AddSingleton<ITaskRunOriginalFrameProcessJoinGuard>(provider => provider.GetRequiredService<TaskRunOriginalFrameOwner>());
        services.AddSingleton<ITaskRunOriginalAttemptRegistrationSource>(provider => provider.GetRequiredService<TaskRunOriginalFrameOwner>());
        services.AddSingleton<ITaskRunRuntimeSettlement>(provider => provider.GetRequiredService<TaskRunOriginalFrameOwner>());
        services.AddSingleton<ITaskRunProviderFailureSettlement>(provider => provider.GetRequiredService<TaskRunOriginalFrameOwner>());
        services.AddSingleton<ITaskRunOriginalAttemptRetirement>(provider => provider.GetRequiredService<TaskRunOriginalFrameOwner>());
        services.AddSingleton<ITaskRunOriginalFailedAttemptSettlementSource>(provider => provider.GetRequiredService<TaskRunOriginalFrameOwner>());
        // Lazy descriptors only. A genuine installed-package source is independently required;
        // the global observer is unbound and no Taskless model initialization is authorized.
        services.TryAddSingleton<StrataNativeArtifactSource>(provider => new StrataNativeArtifactSource(
            provider.GetRequiredService<TaskExecutionCoordinator>(),
            provider.GetRequiredService<TaskRunPermissionAuthority>(),
            provider.GetService<IStrataVerifiedInstallationSource>()));
        services.TryAddSingleton<Dulche.Runtime.IOriginalStrataModelSource>(provider => provider.GetRequiredService<StrataNativeArtifactSource>());
        services.TryAddSingleton<Dulche.Runtime.IOriginalStrataWorkerSource>(provider => provider.GetRequiredService<StrataNativeArtifactSource>());
        services.TryAddSingleton<StrataRuntimeObservationSource>(provider => new StrataRuntimeObservationSource(
            provider.GetRequiredService<StrataNativeArtifactSource>()));
        services.TryAddSingleton<Dulche.Runtime.IInferenceRuntimeObservationSource>(provider => provider.GetRequiredService<StrataRuntimeObservationSource>());
        services.TryAddSingleton<IStrataOriginalRequestObservationSource>(provider => provider.GetRequiredService<StrataRuntimeObservationSource>());
        services.TryAddSingleton<ManagedDulcheInferenceComposition>(provider => new ManagedDulcheInferenceComposition(
            provider.GetRequiredService<Dulche.Runtime.IInferenceRuntimeObservationSource>(),
            provider.GetRequiredService<Dulche.Runtime.IOriginalStrataModelSource>(),
            provider.GetRequiredService<Dulche.Runtime.IOriginalStrataWorkerSource>()));
        services.TryAddSingleton<IManagedDulcheInferenceCompositionSource>(provider => provider.GetRequiredService<ManagedDulcheInferenceComposition>());
        services.AddSingleton<ManagedDulcheRuntimeService>(provider => new ManagedDulcheRuntimeService(
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IProviderConfigurationStore>(),
            provider.GetRequiredService<TaskExecutionCoordinator>(),
            provider.GetRequiredService<ITaskRunOriginalFrameOwner>(),
            tools: provider.GetService<Dulche.Runtime.IOriginalDulcheProviderToolSource>(),
            contextSource: provider.GetService<Dulche.Runtime.IOriginalDulcheProviderContextSource>(),
            contextAuthority: provider.GetRequiredService<ITaskRunProviderContextAuthority>(),
            toolCoordinator: provider.GetService<Dulche.Runtime.IDulcheToolCoordinator>(),
            inferenceComposition: provider.GetService<IManagedDulcheInferenceCompositionSource>()));
        services.AddSingleton<IManagedDulcheOriginalModelRequestConsumer>(provider => provider.GetRequiredService<ManagedDulcheRuntimeService>());
        services.AddSingleton<WorkspaceTaskRunEffectAuthority>();
        services.AddSingleton<IWorkspaceToolFinalFenceAuthority>(provider => provider.GetRequiredService<WorkspaceTaskRunEffectAuthority>());
        services.AddSingleton<WorkspaceTaskRunToolActionOwner>(provider => new WorkspaceTaskRunToolActionOwner(
            () => provider.GetRequiredService<TaskExecutionCoordinator>(),
            provider.GetRequiredService<ITaskRunOriginalFrameOwner>(),
            provider.GetRequiredService<IWorkspaceToolService>(),
            provider.GetRequiredService<CapabilityRegistryService>(),
            provider.GetRequiredService<WorkspaceTaskRunEffectAuthority>(),
            OperatingSystem.IsWindows() ? CapabilityPlatform.Windows :
                OperatingSystem.IsAndroid() ? CapabilityPlatform.Android :
                OperatingSystem.IsLinux() ? CapabilityPlatform.Linux : CapabilityPlatform.None,
            provider.GetRequiredService<WorkspaceTaskRunReceiptAuthority>()));
        services.AddSingleton<ITaskRunToolActionOwner>(provider => provider.GetRequiredService<WorkspaceTaskRunToolActionOwner>());
        services.AddSingleton<TaskExecutionCoordinator>(provider => new TaskExecutionCoordinator(
            provider.GetRequiredService<ITaskExecutionRepository>(),
            provider.GetRequiredService<IExecutionEventSink>(),
            provider.GetService<TimeProvider>(),
            provider.GetRequiredService<ITaskRunAdmissionAuthority>(),
            provider.GetRequiredService<ITaskRunRuntimeSettlement>(),
            provider.GetRequiredService<ICheckpointRepository>(),
            provider.GetRequiredService<ITaskRunToolActionOwner>(),
            provider.GetRequiredService<ICheckpointExecutionObservationSource>(),
            unstartedPermissionSource: provider.GetRequiredService<TaskRunCloudPermissionRemediationOwner>()));
        services.AddSingleton<ITaskRunOriginalActionAdmissionSource>(provider => provider.GetRequiredService<TaskExecutionCoordinator>());
        services.AddSingleton<ITaskRunOriginalInferenceAttemptSource>(provider => provider.GetRequiredService<TaskExecutionCoordinator>());
        services.AddSingleton<ITaskRunProcessRetirementParticipant>(provider => provider.GetRequiredService<TaskExecutionCoordinator>());
        services.AddSingleton<TaskRunCanonicalProcessRetirementOwner>(provider => new TaskRunCanonicalProcessRetirementOwner(
            provider.GetRequiredService<TaskExecutionCoordinator>(),
            provider.GetRequiredService<AgentTaskRuntimeService>(),
            provider.GetRequiredService<TaskRunPermissionAuthority>(),
            provider.GetRequiredService<TaskRunOriginalFrameOwner>()));
        services.AddSingleton<IProjectPreviewProvider, WebProjectPreviewProvider>();
        services.AddSingleton<IModelProvider>(provider => new OllamaModelProvider(
            provider.GetRequiredService<ILocalOllamaClient>(),
            provider.GetRequiredService<IProviderConfigurationStore>()));
        services.AddSingleton<IModelProvider, OpenAiModelProvider>();
        services.AddSingleton<IModelProvider, OpenRouterModelProvider>();
        services.AddSingleton<IModelProvider, CustomOpenAiCompatibleModelProvider>();
        services.AddSingleton<IModelProvider, AnthropicModelProvider>();
        services.AddSingleton<IModelProvider, GeminiModelProvider>();
        services.AddHavenObservedLocalLlamaCpp(LocalLlamaCppRegistration.ReadExplicitEnvironment());
        services.AddSingleton<IModelProviderRegistry, ModelProviderRegistry>();
        services.AddSingleton<IModelRouter, ModelRouter>();
        services.AddSingleton<ProviderRoutingModelClient>(provider => new ProviderRoutingModelClient(
            provider.GetRequiredService<ILocalOllamaClient>(),
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IPrivacyPreferenceStore>()));
        services.AddSingleton<ResilientProviderRoutingModelClient>(provider => new ResilientProviderRoutingModelClient(
            provider.GetRequiredService<ProviderRoutingModelClient>(),
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IProviderConfigurationStore>(),
            provider.GetRequiredService<IPrivacyPreferenceStore>(),
            fallbackOrder: provider.GetService<IModelFallbackOrderStore>(),
            executionEvents: provider.GetService<IExecutionEventSink>(),
            taskCoordinator: provider.GetService<TaskExecutionCoordinator>(),
            routeCapture: provider.GetService<ITaskRunSelectedRouteCapture>(),
            originalFrames: provider.GetService<ITaskRunOriginalFrameOwner>(),
            modelPermissions: provider.GetService<ModelPermissionEvaluator>(),
            taskContextAuthority: provider.GetService<ITaskRunProviderContextAuthority>(),
            catalogueEligibility: provider.GetService<IProviderCatalogueEligibility>(),
            originalModelRequests: provider.GetService<IManagedDulcheOriginalModelRequestConsumer>()));
        services.AddSingleton<IProviderModelClient>(provider => provider.GetRequiredService<ResilientProviderRoutingModelClient>());
        services.AddSingleton<ITaskRunOriginalRequestFailureSource>(provider => provider.GetRequiredService<ResilientProviderRoutingModelClient>());
        services.AddSingleton<ITaskRunOriginalToolCheckpointSelectionSource>(provider => provider.GetRequiredService<ResilientProviderRoutingModelClient>());
        services.AddSingleton<ITaskRunOriginalToolResponseDispatchWitnessSource>(provider => provider.GetRequiredService<ResilientProviderRoutingModelClient>());
        services.AddSingleton<INotesAiService>(provider => new NotesAiService(
            provider.GetRequiredService<IProviderModelClient>(),
            provider.GetRequiredService<IProductionDiagnostics>()));

        services.AddSingleton<IModeRegistry, ModeRegistry>();
        services.AddSingleton<IModeUsageRepository, ModeUsageRepository>();
        services.AddSingleton<IPinRepository, PinRepository>();
        services.AddSingleton<ISurfaceRouter, SurfaceRouter>();
        services.AddSingleton<IModeIntentRouter, IntentRouter>();
        services.AddSingleton<IActivityLogRepository, ActivityLogRepository>();
        services.AddSingleton<IConversationMoveRepository, ConversationMoveRepository>();
        services.AddSingleton<ICompanionDockService, CompanionDockService>();
        services.AddSingleton<IBrowserTabHostManager, BrowserTabHostManager>();
        services.AddSingleton<IPlatformShellService, PlatformShellService>();
        services.AddSingleton<IAppDiagnostics, AppDiagnostics>();
        services.AddSingleton<IAppCommandRegistry, AppCommandRegistry>();
        services.AddSingleton<SurfaceOrchestrationService>();
        services.AddSingleton<ModeSeedService>();
        services.AddSingleton<ModeManifestValidator>();
        services.AddSingleton<ModeCreationService>();
        services.AddSingleton<FilesystemActionService>();
        services.AddSingleton<Haven.Application.Automations.BuiltInAutomationActionNodeExecutor>();
        services.AddSingleton<SettingsEncryptionService>();
        services.AddSingleton<SettingsExportService>();
        services.AddSingleton<KeybindingService>();
        services.AddSingleton<AccessibilityService>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<IConversationPlacementService, ConversationPlacementService>();
        services.AddSingleton<IModePackageValidator, ModePackageValidator>();
        services.AddSingleton<ExtensionManifestValidator>();
        services.AddSingleton<IExtensionSourceTransport, GitExtensionSourceTransport>();
        services.AddSingleton<INativePluginProcessFactory, NativePluginProcessFactory>();
        services.AddSingleton<NativePluginRuntime>();
        services.AddSingleton<ExtensionManager>();
        services.AddSingleton<PluginToolRuntime>();
        services.AddSingleton<IVersionedSettingsStore, VersionedAtomicSettingsStore>();
        services.AddSingleton<SpaceRegistry>(provider => new SpaceRegistry(provider.GetRequiredService<IVersionedSettingsStore>()));
        services.AddSingleton<IUpdatePreferenceStore, VersionedUpdatePreferenceStore>();
        services.AddSingleton(new Func<InstallationInfo>(WindowsInstallationDetector.DetectInstallationSource));
        services.AddSingleton(new Func<string>(CurrentExecutableVersion));
        // Documented override point: channel URLs stay on the placeholder template until product
        // configuration supplies real release-feed endpoints; the Settings Updates section reports
        // the not-yet-configured state honestly while the placeholder remains in place.
        services.AddSingleton<DirectUpdateOptions>(provider => new DirectUpdateOptions
        {
            StableUrl = DirectUpdateOptions.PlaceholderChannelTemplate,
            PreviewUrl = DirectUpdateOptions.PlaceholderChannelTemplate,
            DevelopmentUrl = DirectUpdateOptions.PlaceholderChannelTemplate,
            DataDirectory = provider.GetRequiredService<IAppPaths>().DataDirectory,
        });
        services.AddHttpClient(HavenDirectUpdateProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Haven-Updates/1.0");
        });
        services.AddSingleton<HavenDirectUpdateProvider>(provider => new HavenDirectUpdateProvider(
            provider.GetRequiredService<DirectUpdateOptions>(),
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HavenDirectUpdateProvider.HttpClientName)));
        services.AddSingleton<StoreUpdateProvider>();
        services.AddSingleton<IReadOnlyDictionary<InstallationSource, IUpdateProvider>>(provider => new Dictionary<InstallationSource, IUpdateProvider>
        {
            [InstallationSource.MicrosoftStore] = provider.GetRequiredService<StoreUpdateProvider>(),
            [InstallationSource.DirectInstall] = provider.GetRequiredService<HavenDirectUpdateProvider>(),
        });
        services.AddSingleton<UpdateOrchestrator>();
        services.AddSingleton<IUpdateService>(provider => provider.GetRequiredService<UpdateOrchestrator>());
        services.AddHttpClient(OpenStreetMapService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(OpenStreetMapService.UserAgent);
        });
        services.AddSingleton<IMapsSavedPlaceStore, MapsSavedPlaceStore>();
        services.AddSingleton<IFormsSubmissionStore, DataWorkbookFormsSubmissionStore>();
        services.AddSingleton<OsmRasterTileSource>();
        services.AddSingleton<ITileSource>(provider => provider.GetRequiredService<OsmRasterTileSource>());
        services.AddSingleton<OsrmRoutingService>();
        services.AddSingleton<IMapService, OpenStreetMapService>();
        services.AddSingleton<Haven.Application.Play.PlaySessionService>();
        services.AddSingleton<IApplicationLifecycle, ApplicationLifecycleService>();
        services.AddSingleton<ISingleInstanceService, SingleInstanceService>();
        return services;
    }

    /// <summary>
    /// Reads the entry assembly's informational version so update comparisons run against the real product version; returns "0.0.0" when no attribute exists.
    /// </summary>
    private static string CurrentExecutableVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version!;
    }
}
