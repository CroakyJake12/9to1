using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    // Additive optional fixture. Existing WRITE8 controls retain their original no-Den
    // setup. This uses actual app-owned Den creation/new-empty Home binding; no mock
    // permissions, imported SQLite shortcut, dummy bridge or model dispatch is supplied.
    private static async Task RunWithOriginalDen(Func<Rig, Task> body)
    {
        var rig = new Rig(includeOriginalDen: true); var errors = new List<Exception>();
        try { await rig.InitializeAsync(); await body(rig); } catch (Exception cause) { errors.Add(cause); }
        await rig.JoinOriginalsAsync(errors);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual Den/compatible Tasks fixture failed; retained at " + rig.Root, errors);
    }
    private sealed partial class Rig
    {
        internal OriginalAssistantPersonalDenHost? DenHost;
        internal HomePersonalDenFactory? DenFactory;
        internal HomePersonalDenSession? OriginalOpenedDen;
        internal AssistantOriginalConversationHost? OriginalOrdinaryBusiness;
        private readonly List<AssistantsWorkspaceController> _originalDenControllers = [];
        private readonly List<DenAssistantOriginalDevelopmentOwner> _originalDenDevelopmentOwners = [];
        private void ConfigureOriginalDenEvidence(HomeLocalProfileIdentity actualProfiles,
            List<IHomeLocalStoreEvidenceProvider> evidence, Dictionary<Type, object> services)
        {
            if (!includeOriginalDen) return;
            // Before composition publication; ctor only captures SAME path/profile refs.
            DenHost = new(_paths, actualProfiles); evidence.Add(DenHost);
            services.Add(typeof(OriginalAssistantPersonalDenHost), DenHost);
        }
        private async Task InitializeOriginalDenAsync()
        {
            if (!includeOriginalDen) return;
            if (DenHost is null) throw new InvalidOperationException("Actual registered Den evidence is missing.");
            await Keep(DenHost.CreateAndBindNewEmptyOriginalAsync(Actor, Home.LocalStoreOwnership, Token));
            DenFactory = DenHost.CreateOriginalFactory(Home.Ownership);
            OriginalOpenedDen = await Keep(DenFactory.OpenWithinOriginalSourceAsync(Scope, Retain, Token));
            if (!DenFactory.TryObserveOriginalDenOwnership(OriginalOpenedDen, out var receipt) || receipt is null)
                throw new InvalidOperationException("The SAME actual healthy Home Open did not issue its original Den receipt.");
            OriginalOrdinaryBusiness = new(Token);
        }
        internal AssistantsWorkspaceController CreateOriginalAssistantsController(DenAssistantOriginalDevelopmentOwner actualDevelopment)
        {
            if (DenFactory is null || OriginalOrdinaryBusiness is null)
                throw new InvalidOperationException("The genuine optional Den fixture must initialize before controller acquisition.");
            lock (_gate) if (!_originalDenDevelopmentOwners.Any(value => ReferenceEquals(value, actualDevelopment)))
                _originalDenDevelopmentOwners.Add(actualDevelopment); // Process owner captured even if controller construction fails.
            var actual = AssistantsWorkspaceFactory.Create(DenFactory, Provider.GetRequiredService<IConversationRepository>(),
                Provider.GetRequiredService<IConversationProductionRepository>(), Provider.GetRequiredService<ChatSessionService>(),
                Provider.GetRequiredService<TaskExecutionCoordinator>(), OriginalOrdinaryBusiness, actualDevelopment: actualDevelopment);
            lock (_gate) _originalDenControllers.Add(actual); // Capture before Initialize or borrowed callbacks.
            return actual;
        }
        private async Task JoinOriginalAssistantBorrowersAsync(List<Exception> errors)
        {
            AssistantsWorkspaceController[] controllers; lock (_gate) controllers = _originalDenControllers.ToArray();
            var closes = new List<Task>();
            foreach (var actual in controllers)
                try { closes.Add(Keep(actual.CloseAndDrainAsync())); } catch (Exception cause) { AddUnexpected(cause, errors); }
            foreach (var raw in closes)
                try { await raw; } catch (Exception cause) { AddUnexpected(raw.Exception ?? cause, errors); }
            if (OriginalOrdinaryBusiness is not null)
            {
                Task? raw = null;
                try { raw = Keep(OriginalOrdinaryBusiness.CloseAndDrainAsync()); await raw; }
                catch (Exception cause) { AddUnexpected(raw?.Exception ?? cause, errors); }
            }
            DenAssistantOriginalDevelopmentOwner[] development; lock (_gate) development = _originalDenDevelopmentOwners.ToArray();
            closes.Clear();
            foreach (var actual in development)
                try { closes.Add(Keep(actual.CloseAndDrainAsync())); } catch (Exception cause) { AddUnexpected(cause, errors); }
            foreach (var raw in closes)
                try { await raw; } catch (Exception cause) { AddUnexpected(raw.Exception ?? cause, errors); }
        }
        private async Task JoinOriginalAssistantDenAsync(List<Exception> errors)
        {
            if (DenHost is null) return;
            Task? raw = null;
            try { raw = Keep(DenHost.CloseAndDrainAsync()); await raw; }
            catch (Exception cause) { AddUnexpected(raw?.Exception ?? cause, errors); }
        }
    }
}
