# Retire an observed agent operation

`AgentTaskRuntimeService` owns the original `RunAsync` or `RetryAsync` producer. A page can observe progress and release its presentation without taking ownership of the task’s result. Keep the actual task, callback and cancellation source linked throughout shutdown.

When a subscriber fails or a view retires, preserve the original completion and diagnostics. A progress notification is not authority to retry an operation or publish to a different actor. Verify that a late callback cannot bring back retired UI or duplicate a completed effect.

## Observer and producer outcomes

`IAgentRunOriginalObservationSource` lends a scene an observation lease. `WaitOriginalObservationAsync` returns the original observation task. A `ProducerTerminal` outcome contains the actual Agent row, including failed or suspended status. `ObservationDetached` has no row and ends only the view’s observation.

Request withdrawal with `RequestOriginalObservationRetirement`, then join `DetachAndDrainOriginalObservationAsync` before detaching the renderer and its subscriptions. This preserves the producer and avoids a callback waiting on itself. The separate explicit Cancel action still belongs to the business operation.

[Task runtime contracts](../../src/Haven.Application) · [Durable recovery](state-and-persistence.md) · [Original observation implementation](../../src/Haven.Application/Agents/AgentTaskRuntimeService.OriginalObservation.cs)
