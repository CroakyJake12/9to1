# Home, voice sessions and planning

Home hosts the Dashboard and global controls. A voice session and a planning action have their own service and authorization lifetimes. Moving between pages should preserve the active session and current document or calendar context.

For a planning proposal, inspect the affected item, intended calendar and actor before applying it. Cancellation or rejection should preserve the draft. For a call, check actual input/output availability and distinguish a local device failure from a disconnected model or remote service.

The existing local Call and calendar interfaces below contain the implementation details for those capabilities. Exercise their owning routes as well as the Home shortcut.

## Fully local Haven Call

The call boundary is defined in `src/Haven.Application/CallAbstractions.cs`. `CallCoordinator` owns the one-active-call invariant and deterministic states:

```text
Idle -> Listening -> Transcribing -> Thinking -> Speaking
                         |              |          |
                         +--------- Paused / Error
```

The normal pipeline is:

1. `WindowsSpeechInputService` captures 16 kHz mono audio with NAudio.
2. WebRTC VAD detects speech turns; push-to-talk can delimit a turn manually.
3. Whisper.net transcribes locally with the selected multilingual model.
4. The existing Ollama client streams the assistant response.
5. `CallCoordinator` splits complete sentences and sends them to System Speech synthesis.
6. New detected user speech interrupts generation/speech (barge-in) and starts another turn.

The package versions live in `Haven.Infrastructure.csproj`: NAudio 2.3.0, Whisper.net 1.9.1 (normal and no-AVX runtimes), WebRtcVadSharp 1.3.2 and System.Speech 10.0.0. The main implementation files are `CallCoordinator.cs`, `SentenceChunker.cs`, `WindowsSpeechInputService.cs`, `WhisperModelManager.cs`, `CallFallbackServices.cs`, `CallRepository.cs` and Desktop's `WindowsGraphicsCaptureService.cs`. The desktop capture service owns the Windows system picker and most-recent-frame preview.

First use downloads Tiny, Base or Small whisper.cpp model data into `SpeechModels`; Base is the recommended default. Downloads are cancellable and use a `.download` partial file that is atomically renamed on completion.

Screen sharing keeps only the most recent downsampled JPEG frame in memory and clears it on stop. The coordinator attaches it only to the current Ollama request at the end of a user turn and only for a vision-capable model. Raw PCM/WAV exists only for the current utterance and is discarded after local inference. Source closure, End Call, fatal error, application shutdown and coordinator disposal must stop capture, microphone and speech output.

Persistence is deliberately narrow:

- transcript messages are ordinary conversation messages;
- the conversation uses `ConversationKind.Call` (numeric value 7);
- `call_sessions` stores device/model/voice, status, timestamps and whether sharing was used;
- raw audio and frame bytes are never written to the database, token store, logs or call metadata.

If you add another media service, keep its raw buffers behind the service interface and add a cleanup assertion to `CallCoordinatorTests`.

## Google and Microsoft calendars

`ICalendarSyncProvider` and `ICalendarSyncProviderRegistry` abstract provider behavior. Configuration is public-client configurationÔÇöthere are no client secrets.

Set the appropriate environment variable before launching Haven:

```powershell
$env:HAVEN_GOOGLE_CALENDAR_CLIENT_ID = "your-installed-app-client-id"
$env:HAVEN_MICROSOFT_CALENDAR_CLIENT_ID = "your-desktop-public-client-id"
```

Missing values produce a visible Not Configured state instead of a crash. Sign-in uses the system browser, Authorization Code + PKCE, a five-minute callback timeout and provider scopes declared in `CalendarSyncProviders.cs`.

Register these exact public-client redirect URIs with the providers:

```text
Google:    http://127.0.0.1:53682/oauth/google/
Microsoft: http://localhost:53683/oauth/microsoft/
```

Google requests `openid`, `email` and Calendar access. Microsoft requests `openid`, `offline_access`, `User.Read` and `Calendars.ReadWrite`. The concrete transports are `GoogleCalendarProviderTransport.cs` and `MicrosoftCalendarProviderTransport.cs`; their shared PKCE/token/loopback work is in `OAuthCalendarTransportBase.cs`. `AddHavenPlannerInfrastructure` registers the repository/sync store, DPAPI token store, a 45-second named `HavenCalendarSync` client, both transports, provider wrappers and the registry.

`WindowsCalendarTokenStore` encrypts each token envelope for the current Windows user using DPAPI and atomically stores it under `CalendarTokens`. SQLite contains only account metadata, provider IDs/ETags, cursors/delta links, outbox operations and conflict snapshots.

Synchronization code must retain these rules:

- Google incremental sync uses sync tokens and resets after HTTP 410; Microsoft uses calendar-view delta links and resets after Gone/Bad Request;
- expired cursors trigger a bounded full-window refresh;
- offline writes enter `calendar_outbox` and retry with backoff;
- concurrent local/remote edits create `calendar_conflicts` for Keep Haven, Keep Provider or Duplicate;
- provider readers and meetings with attendees/another organizer remain read-only in this pass;
- new events default to the local Haven calendar unless the user explicitly chooses a writable provider calendar;
- provider deletions soft-delete the local linked event instead of erasing unrelated local records.

Never put a client secret in source, settings, SQLite or an environment-variable example.

[Home surface](../../../Home/UI/Home.cui) · [Call view model](../../src/Haven.Desktop/ViewModels/CallPageViewModel.cs) · [Planner proposals](../../src/Haven.Application/Planner/PlannerProposalService.cs)
