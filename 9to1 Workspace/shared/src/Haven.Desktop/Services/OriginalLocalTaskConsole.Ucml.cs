using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Dulche.Context;

namespace Haven.Desktop.Services;

public static partial class OriginalLocalTaskConsole
{
    private sealed partial class Host
    {
        private SqliteDatabase? _ucmlDatabase;
        private CanonicalSqliteOriginalStoreOwner? _ucmlStore;
        private CanonicalSqliteOriginalStoreEvidenceProvider? _ucmlEvidence;
        private OriginalAssistantPersonalDenHost? _ucmlDen;
        private HomeCanonicalAssistantMemoryWriteResourceResolver? _ucmlWriteResolver;
        private HomeCanonicalAssistantMemoryWriteActionPolicy? _ucmlWritePolicy;
        private HomeCanonicalAssistantMemoryWriteSource? _ucmlWrites;
        private AssistantOriginalMemorySource? _ucmlMemory;
        private HomeOriginalLocalStoreImportSession? _ucmlImports;
        private HomeLocalStoreSetupSession? _ucmlSetup;
        private OriginalAssistantConversationProcessOwner? _ucmlConversationProcess;
        private DenAssistantCanonicalBridge? _ucmlBridge;
        private AssistantMemoryManagementController? _ucmlManagement;
        private AssistantConversationBinding? _ucmlBinding;
        private IReadOnlyList<AssistantModelChoice> _ucmlModels = [];
        private UcmlBatch? _ucmlBatch;
        private Task? _ucmlBatchTask;
        private Task? _ucmlChatTask;
        private readonly List<AssistantOriginalConversationObservation> _ucmlConversations = [];
        // Same explicit high-risk store-import policy as Home's native AI composition.
        // This registers the action; only the actual Home ownership review can grant it.
        private sealed class LocalStoreImportPolicy : IHomeActionPolicySource
        {
            public HomePermissionActionPolicy? TryGet(string app, string action) =>
                app == "9to1.home.local-profile" && action == "home.profile.importStore"
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null;
        }
        private sealed record UcmlBatchItem(string FileName, string Sha256, UcmlDocument Document,
            AssistantMemoryWritePreview Preview);
        private sealed class UcmlBatch(Guid id, AssistantConversationBinding binding, IReadOnlyList<UcmlBatchItem> items)
        {
            internal Guid Id => id;
            internal AssistantConversationBinding Binding => binding;
            internal IReadOnlyList<UcmlBatchItem> Items => items;
            internal readonly List<object> Outcomes = [];
            internal Task? OriginalCommit;
        }

        private void PrepareOriginalUcmlOwners()
        {
            _ucmlDatabase = new(_paths);
            _ucmlWritePolicy = new();
            _ucmlWriteResolver = new(() => _ucmlWrites
                ?? throw new InvalidOperationException("Run assistant-setup before requesting memory writes."));
        }

        private IHomeLocalStoreEvidenceProvider[] CaptureOriginalUcmlStores(HomeLocalDomainIdentityComponents identity)
        {
            _ucmlStore = new(_ucmlDatabase!, _paths, identity.Profiles);
            _ucmlEvidence = new(_ucmlStore, AssistantOriginalMemorySource.ResourceKind);
            _ucmlDen = new(_paths, identity.Profiles);
            return [_ucmlEvidence, _ucmlDen];
        }

        private void ConfigureOriginalUcmlServices(IServiceCollection services)
        {
            var rows = services.Where(row => row.ServiceType == typeof(SqliteDatabase)).ToArray();
            if (rows.Length != 1 || rows[0].ImplementationType != typeof(SqliteDatabase) ||
                rows[0].Lifetime != ServiceLifetime.Singleton)
                throw new InvalidOperationException("The actual maintained, uninstantiated SQLite registration is required.");
            services.Remove(rows[0]);
            services.AddSingleton(_ucmlDatabase!);
            services.AddSingleton(_ucmlStore!);
        }

        private async Task SetupOriginalUcmlAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
        {
            if (_ucmlBridge is not null)
            {
                Write(await Acquire(original, () => _ucmlSetup!.InspectAsync(token)).ConfigureAwait(false));
                return;
            }
            var actor = await Acquire(original, () => Home.Profiles.GetCurrentAsync(token).AsTask()).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The current OS-local Home profile is unavailable.");
            // Explicit setup uses only the chosen canonical data location. Existing Den
            // files take the existing-owner path; corrupt stores are never reset.
            await Acquire(original, () => _ucmlDen!.OpenOrCreateMissingOwnedOriginalAsync(actor,
                Home.LocalStoreOwnership, token)).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
                foreach (var path in new[] { _paths.DatabasePath, _paths.DatabasePath + "-wal", _paths.DatabasePath + "-shm" })
                    if (File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Scope(original, () =>
            {
                var factory = _ucmlDen!.CreateOriginalFactory(Home.Ownership);
                var conversations = Provider.GetRequiredService<IConversationRepository>();
                var tasks = Provider.GetRequiredService<TaskExecutionCoordinator>();
                _ucmlMemory = new(factory, conversations, tasks, Provider.GetRequiredService<KnowledgeLibraryService>(),
                    _ucmlStore!, Home.Profiles, Home.Ownership);
                _ucmlMemory.BindOriginalUcmlSecretStore(Provider.GetRequiredService<IProviderSecretStore>());
                _ucmlMemory.BindOriginalUcmlAccessAudit(new(Home.StateStore, Home.Profiles, Home.Permissions, _ucmlMemory));
                _ucmlMemory.BindOriginalUcmlContextApproval(new(Home.StateStore, Home.Profiles, Home.Permissions, _ucmlMemory));
                _ucmlImports = new(AssistantOriginalMemorySource.ResourceKind, _ucmlMemory,
                    Home.Profiles, Home.LocalStoreOwnership, _ucmlEvidence!, Home.Permissions, Home.Ownership);
                _ucmlMemory.BindOriginalMemoryImportSession(_ucmlImports, _ucmlEvidence!);
                _ucmlWrites = new(Home.StateStore, Home.Profiles, Home.Resources, Home.Broker, Home.Permissions, _ucmlMemory);
                _ucmlMemory.BindOriginalHomeWriteSource(_ucmlWrites);
                var chat = Provider.GetRequiredService<ChatSessionService>();
                chat.BindOriginalPersistentMemorySource(_ucmlMemory);
                _ucmlConversationProcess = new();
                var routing = Provider.GetRequiredService<ProviderRoutingModelClient>();
                var models = new AssistantOriginalModelSelectionOwner(Provider.GetRequiredService<IModelProviderRegistry>(),
                    Provider.GetRequiredService<IPrivacyPreferenceStore>(), Provider.GetRequiredService<ModelPermissionEvaluator>(),
                    Home.Profiles, routing.ToCompatibilityDescriptor);
                _ucmlBridge = new(factory, conversations, Provider.GetRequiredService<IConversationProductionRepository>(),
                    chat, tasks, _ucmlConversationProcess.OriginalHost, models, null, null, null, _ucmlMemory);
                _ucmlManagement = new(_ucmlMemory, _ucmlBridge);
                _ucmlSetup = new(AssistantOriginalMemorySource.ResourceKind, _ucmlStore!, Home.Profiles,
                    Home.LocalStoreOwnership, _ucmlEvidence!);
            });
            Write(await Acquire(original, () => _ucmlSetup!.InspectAsync(token)).ConfigureAwait(false));
        }

        private DenAssistantCanonicalBridge UcmlBridge => _ucmlBridge
            ?? throw new InvalidOperationException("Run assistant-setup first.");
        private AssistantConversationBinding UcmlBinding => _ucmlBinding
            ?? throw new InvalidOperationException("Create or open an Assistant conversation first.");

        private async Task ExecuteOriginalUcmlCommandAsync(DesktopOriginalWorkLifetime.Original original,
            string command, string argument, CancellationToken token)
        {
            switch (command)
            {
                case "assistant-setup": await SetupOriginalUcmlAsync(original, token).ConfigureAwait(false); return;
                case "assistant-store-bind-empty":
                    _ = UcmlBridge;
                    await Acquire(original, () => _ucmlSetup!.BindEmptyAsync(token)).ConfigureAwait(false);
                    Write(await Acquire(original, () => _ucmlSetup!.InspectAsync(token)).ConfigureAwait(false)); return;
                case "assistant-store-request":
                    _ = UcmlBridge;
                    Write(await Acquire(original, () => _ucmlSetup!.RequestImportAsync(token)).ConfigureAwait(false)); return;
                case "assistant-store-complete":
                    _ = UcmlBridge;
                    await Acquire(original, () => _ucmlSetup!.CompleteImportAsync(token)).ConfigureAwait(false);
                    Write(await Acquire(original, () => _ucmlSetup!.InspectAsync(token)).ConfigureAwait(false)); return;
                case "assistants":
                    Write(await Acquire(original, () => UcmlBridge.ListAsync(token)).ConfigureAwait(false)); return;
                case "assistant-create":
                {
                    DemandNoUcmlBatch();
                    ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                    var definition = await Acquire(original, () => UcmlBridge.CreateAsync(ConfiguredIdentityKind.Assistant,
                        new() { Name = argument, Memory = new(true), Model = new(AllowCloud: false, AllowFallback: false) },
                        Guid.NewGuid(), token)).ConfigureAwait(false);
                    _ucmlBinding = await Acquire(original, () => UcmlBridge.CreateConversationAsync(definition.Identity,
                        definition.Revision, Guid.NewGuid(), "Local personal context", Guid.NewGuid(), token)).ConfigureAwait(false);
                    Write(new { definition, conversation = _ucmlBinding.Conversation.Id }); return;
                }
                case "assistant-open":
                {
                    DemandNoUcmlBatch();
                    var args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (args.Length is < 1 or > 2) throw new ArgumentException("Use an observed definition ID and optional conversation ID.");
                    var list = await Acquire(original, () => UcmlBridge.ListAsync(token)).ConfigureAwait(false);
                    var definition = list.Definitions.Single(row => row.Identity.DefinitionId == args[0]);
                    _ucmlBinding = args.Length == 2
                        ? await Acquire(original, () => UcmlBridge.OpenConversationAsync(definition.Identity, Guid.Parse(args[1]), token)).ConfigureAwait(false)
                        : await Acquire(original, () => UcmlBridge.CreateConversationAsync(definition.Identity, definition.Revision,
                            Guid.NewGuid(), "Local personal context", Guid.NewGuid(), token)).ConfigureAwait(false);
                    _ucmlModels = []; _ucmlBatch = null;
                    Write(new { definition = _ucmlBinding.Definition, conversation = _ucmlBinding.Conversation.Id }); return;
                }
                case "assistant-categories": case "assistant-ask-categories": case "assistant-expiry":
                {
                    DemandNoUcmlBatch();
                    var binding = UcmlBinding;
                    var policy = binding.Definition.Configuration.Memory;
                    policy = command switch
                    {
                        "assistant-categories" => policy with { UcmlCategories = ParseUcmlCategories(argument) },
                        "assistant-ask-categories" => policy with { UcmlAskCategories = ParseUcmlCategories(argument) },
                        _ => policy with { UcmlAccessExpiresAt = argument == "none" ? null : DateTimeOffset.Parse(argument, System.Globalization.CultureInfo.InvariantCulture) }
                    };
                    policy.ValidateUcmlPolicy();
                    var changed = await Acquire(original, () => UcmlBridge.UpdateAsync(binding.Definition.Identity,
                        binding.Definition.Revision, binding.Definition.Configuration with { Memory = policy }, Guid.NewGuid(), token)).ConfigureAwait(false);
                    _ucmlBinding = await Acquire(original, () => UcmlBridge.OpenConversationAsync(changed.Identity, binding.Conversation.Id, token)).ConfigureAwait(false);
                    _ucmlBatch = null; _ucmlModels = [];
                    Write(new { app = "9to1.assistants", recipient = "local-model", purpose = "personalization", policy = changed.Configuration.Memory }); return;
                }
                case "assistant-models":
                    _ucmlModels = await Acquire(original, () => UcmlBridge.ListAvailableModelsAsync(UcmlBinding, token)).ConfigureAwait(false);
                    Write(_ucmlModels); return;
                case "assistant-chat":
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                    if (_ucmlChatTask is { IsCompleted: false }) throw new InvalidOperationException("Wait for the current Assistant turn or quit the console.");
                    var binding = UcmlBinding;
                    var selected = _model ?? throw new InvalidOperationException("Select an observed local model with models/model first.");
                    if (!selected.IsLocal) throw new UnauthorizedAccessException("UCML conversation routing is local only.");
                    _ucmlChatTask = _work.RunAsync(owner => RunOriginalUcmlChatAsync(owner, binding, selected, argument, token));
                    Write(new { assistantChatStarted = true, conversation = binding.Conversation.Id,
                        note = "Home review commands remain available; assistant-chat-status reports completion." }); return;
                }
                case "assistant-chat-status":
                    Write(new { assistantChatState = _ucmlChatTask?.Status.ToString(),
                        failure = _ucmlChatTask?.Exception?.GetBaseException().Message }); return;
                case "ucml-review": await ReviewOriginalUcmlBatchAsync(original, argument, token).ConfigureAwait(false); return;
                case "ucml-commit":
                {
                    DemandNoUcmlBatch();
                    var batch = _ucmlBatch ?? throw new InvalidOperationException("Review the selected files first.");
                    if (batch.Id != Guid.Parse(argument) || !ReferenceEquals(batch.Binding, UcmlBinding))
                        throw new UnauthorizedAccessException("Use the exact displayed batch and unchanged Assistant selection.");
                    _ucmlBatchTask = _work.RunAsync(owner => CommitOriginalUcmlBatchAsync(owner, batch, token),
                        actual => batch.OriginalCommit = actual);
                    Write(new { batch = batch.Id, started = true, note = "Use home-requests/display/accept or decline for exact per-record reviews; ucml-status reports outcomes." }); return;
                }
                case "ucml-status":
                    lock (_gate) Write(new { batch = _ucmlBatch?.Id, state = _ucmlBatchTask?.Status.ToString(), outcomes = _ucmlBatch?.Outcomes.ToArray() }); return;
                case "ucml-list":
                    foreach (var page in await ReadAllUcmlPagesAsync(original, token).ConfigureAwait(false))
                        Write(page.Records.Select(row => new { row.Id, row.Title, row.Status, row.Confidence }));
                    return;
                case "ucml-export":
                {
                    var split = argument.IndexOf(' ');
                    if (split < 1) throw new ArgumentException("Use a record ID and new absolute output path.");
                    var id = Guid.Parse(argument[..split]); var path = argument[(split + 1)..];
                    DemandUcmlLocalPath(path, requireExisting: false);
                    var pages = await ReadAllUcmlPagesAsync(original, token).ConfigureAwait(false);
                    var view = pages.Single(page => page.Records.Any(record => record.Id == id));
                    var encoded = await Acquire(original, () => _ucmlManagement!.ExportUcmlAsync(view,
                        view.Records.Single(record => record.Id == id), token)).ConfigureAwait(false);
                    await WriteOriginalUcmlExportAsync(path, encoded, token).ConfigureAwait(false);
                    Write(new { exported = id, path }); return;
                }
            }
        }

        // Static/current-selection refusals happen before admission to the retained
        // effect owner. A mistyped batch ID is a normal no-effect outcome, not an
        // unknown storage/producer failure that should poison process retirement.
        private void ValidateOriginalUcmlCommandAdmission(string command, string argument)
        {
            if (command == "ucml-export")
            {
                var split = argument.IndexOf(' ');
                if (split < 1 || !Guid.TryParse(argument[..split], out _))
                    throw new ArgumentException("Use a record ID and new absolute output path.");
                var path = argument[(split + 1)..]; DemandUcmlLocalPath(path, requireExisting: false);
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Choose a new local UCML export path; existing files are never overwritten.");
                return;
            }
            if (command != "ucml-commit") return;
            DemandNoUcmlBatch();
            var batch = _ucmlBatch ?? throw new InvalidOperationException("Review the selected files first.");
            if (batch.OriginalCommit is not null) throw new InvalidOperationException("This exact batch was already submitted. Observe ucml-status; do not replay it.");
            if (!Guid.TryParse(argument, out var id) || batch.Id != id || !ReferenceEquals(batch.Binding, _ucmlBinding))
                throw new UnauthorizedAccessException("Use the exact displayed batch and unchanged Assistant selection.");
        }

        private static async Task WriteOriginalUcmlExportAsync(string path, string encoded, CancellationToken token)
        {
            DemandUcmlLocalPath(path, requireExisting: false);
            var destination = Path.GetFullPath(path);
            var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".ucml-export-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                    Options = FileOptions.Asynchronous };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var file = new FileStream(temporary, options))
                {
                    await file.WriteAsync(Encoding.UTF8.GetBytes(encoded), token).ConfigureAwait(false);
                    await file.FlushAsync(token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                DemandUcmlLocalPath(path, requireExisting: false);
                File.Move(temporary, destination, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private void DemandNoUcmlBatch()
        {
            if (_ucmlBatchTask is { IsCompleted: false })
                throw new InvalidOperationException("Finish or decline the current import before changing its selection or policy.");
        }

        private static string[] ParseUcmlCategories(string argument) => argument == "none" ? [] :
            argument.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        private async Task RunOriginalUcmlChatAsync(DesktopOriginalWorkLifetime.Original original,
            AssistantConversationBinding binding, ProviderModelDescriptor selected, string prompt, CancellationToken token)
        {
            var choices = await Acquire(original, () => UcmlBridge.ListAvailableModelsAsync(binding, token)).ConfigureAwait(false);
            var model = choices.Single(choice => choice.ProviderId == selected.ProviderId &&
                (choice.Model.Name == selected.Model.Name || choice.Model.Name == selected.Key));
            var observation = await Acquire(original, () => UcmlBridge.SendConversationOriginalAsync(binding,
                new(prompt, model.Model, ProviderId: model.ProviderId), token)).ConfigureAwait(false);
            lock (_gate) _ucmlConversations.Add(observation);
            await foreach (var item in observation.ObserveOriginalEventsAsync(token).ConfigureAwait(false)) Write(item);
            Write(await Acquire(original, () => observation.WaitAsync(token)).ConfigureAwait(false));
            await Acquire(original, observation.CloseAndDrainAsync).ConfigureAwait(false);
        }

        private async Task<List<AssistantMemoryView>> ReadAllUcmlPagesAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
        {
            var binding = UcmlBinding; var pages = new List<AssistantMemoryView>();
            AssistantMemoryPageContinuation? next = null;
            do
            {
                var page = await Acquire(original, () => _ucmlManagement!.ReadPageAsync(binding, 64, sameContinuation: next, token: token)).ConfigureAwait(false);
                if (!page.IsAvailable) throw new UnauthorizedAccessException(page.Reason);
                pages.Add(page); next = page.NextContinuation;
                if (pages.Count >= 16 && next is not null) throw new InvalidOperationException("This local import review is limited to 1,024 existing records.");
            } while (next is not null);
            return pages;
        }

        private async Task ReviewOriginalUcmlBatchAsync(DesktopOriginalWorkLifetime.Original original, string path, CancellationToken token)
        {
            DemandNoUcmlBatch(); DemandUcmlLocalPath(path, requireExisting: true);
            var names = Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.ucml").Take(1025).ToArray() : [path];
            if (names.Length is < 1 or > 1024) throw new ArgumentException("Choose between 1 and 1,024 individual UCML files.");
            Array.Sort(names, StringComparer.Ordinal);
            var pages = await ReadAllUcmlPagesAsync(original, token).ConfigureAwait(false);
            var existing = pages.SelectMany(page => page.Records.Select(record => (page, record))).ToDictionary(pair => pair.record.Id);
            var items = new List<UcmlBatchItem>(); var ids = new HashSet<Guid>(); long bytes = 0;
            foreach (var name in names)
            {
                DemandUcmlLocalPath(name, requireExisting: true);
                var info = new FileInfo(name);
                if (info.Length > UcmlCodec.MaximumDocumentBytes)
                    throw new ArgumentException("The bounded import review accepts up to 512 KiB per document and 16 MiB per batch.");
                await using var stream = new FileStream(name, FileMode.Open, FileAccess.Read, FileShare.Read,
                    4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[UcmlCodec.MaximumDocumentBytes + 1];
                var size = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false,
                    cancellationToken: token).ConfigureAwait(false);
                if (size > UcmlCodec.MaximumDocumentBytes) throw new ArgumentException("The selected document grew beyond its size limit.");
                bytes += size;
                if (bytes > 16 * 1024 * 1024) throw new ArgumentException("The selected files exceed the 16 MiB batch limit.");
                var raw = buffer.AsSpan(0, size).ToArray();
                var encoded = new UTF8Encoding(false, true).GetString(raw); var document = UcmlCodec.Parse(encoded);
                if (!ids.Add(document.Metadata.Id)) throw new ArgumentException("The batch contains duplicate document IDs.");
                if (existing.TryGetValue(document.Metadata.Id, out var prior))
                {
                    var current = await Acquire(original, () => _ucmlManagement!.ExportUcmlAsync(prior.page, prior.record, token)).ConfigureAwait(false);
                    if (UcmlCodec.Serialize(UcmlCodec.Parse(current)) != UcmlCodec.Serialize(document))
                        throw new InvalidOperationException("An existing document ID has different content. Use the canonical correction workflow.");
                    Write(new { document.Metadata.Id, alreadyImported = true }); continue;
                }
                var preview = await Acquire(original, () => _ucmlManagement!.PrepareUcmlImportAsync(pages[0], encoded, Guid.NewGuid(), token)).ConfigureAwait(false);
                items.Add(new(Path.GetFileName(name), Convert.ToHexString(SHA256.HashData(raw)), document, preview));
            }
            _ucmlBatch = new(Guid.NewGuid(), UcmlBinding, items.AsReadOnly());
            Write(new { batch = _ucmlBatch.Id, count = items.Count, categories = items.Select(row => row.Document.Metadata.Category).Distinct().ToArray(),
                records = items.Select(row => new { row.FileName, row.Sha256, row.Preview.OperationId, row.Document.Metadata, markdown = row.Document.Markdown }),
                note = "Frozen local import review; each exact write still needs Home approval. Files cannot change this frozen batch. This does not enable model disclosure." });
        }

        private async Task CommitOriginalUcmlBatchAsync(DesktopOriginalWorkLifetime.Original original, UcmlBatch batch, CancellationToken token)
        {
            foreach (var item in batch.Items)
            {
                var result = await Acquire(original, () => _ucmlManagement!.CommitAsync(item.Preview, token)).ConfigureAwait(false);
                lock (_gate) batch.Outcomes.Add(new { id = item.Document.Metadata.Id, result.Saved, result.Reason });
                Write(new { batch = batch.Id, id = item.Document.Metadata.Id, result.Saved, result.Reason });
            }
        }

        private static void DemandUcmlLocalPath(string path, bool requireExisting)
        {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose an explicit absolute local path.");
            var full = Path.GetFullPath(path);
            for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            {
                if (!File.Exists(current) && !Directory.Exists(current)) continue;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Linked UCML source/destination paths are not accepted.");
                if (Path.GetPathRoot(current) == current) break;
            }
            if (requireExisting && !File.Exists(full) && !Directory.Exists(full)) throw new FileNotFoundException("The selected local source is unavailable.", full);
        }

        private void RequestOriginalUcmlRetirement(List<Exception> failures)
        {
            try { _ucmlConversationProcess?.RequestRetirement(); } catch (Exception cause) { Capture(failures, null, cause); }
            try { _ucmlManagement?.RequestRetirement(); } catch (Exception cause) { Capture(failures, null, cause); }
        }

        private async Task CloseOriginalUcmlBorrowersAsync(List<Exception> failures)
        {
            async Task Join(Func<Task> acquire)
            {
                var closes = new List<Task>(); AcquireClose(acquire, closes, failures);
                await JoinAll(closes, failures).ConfigureAwait(false); Throw(failures);
            }
            if (_ucmlManagement is not null) await Join(_ucmlManagement.CloseAndDrainAsync).ConfigureAwait(false);
            if (_ucmlBridge is not null) await Join(_ucmlBridge.CloseAndDrainAsync).ConfigureAwait(false);
            if (_ucmlConversationProcess is not null) await Join(_ucmlConversationProcess.CloseAndDrainAsync).ConfigureAwait(false);
            if (_ucmlMemory is not null) await Join(_ucmlMemory.CloseAndDrainAsync).ConfigureAwait(false);
            if (_ucmlWrites is not null) await Join(_ucmlWrites.CloseAndDrainOriginalAsync).ConfigureAwait(false);
            if (_ucmlImports is not null) await Join(_ucmlImports.CloseAndDrainAsync).ConfigureAwait(false);
            if (_ucmlStore is not null) await Join(_ucmlStore.CloseAndDrainAsync).ConfigureAwait(false);
            if (_ucmlDen is not null) await Join(() => _ucmlDen.DisposeAsync().AsTask()).ConfigureAwait(false);
        }
    }
}
