# Agent presentation observations

`AgentTaskRuntimeService` owns the actual existing `RunAsync` and `RetryAsync`
producers. A scene can borrow an `IAgentRunOriginalObservationSource` lease to
observe one of those producers. The lease exposes no producer Task, actor,
permission, completion or recovery capability. Existing producer API signatures
and explicit cancellation tokens stay unchanged.

The issuer publishes its lease, admission, actual producer driver and actual
observation wait before starting the existing producer callback path. Both raw
returned producer Tasks and their complete fault payloads remain service-owned.
The UI separately owns its `RunChanged` subscription and dispatcher work.

`WaitOriginalObservationAsync` returns the same actual observation Task. A
`ProducerTerminal` outcome contains the actual returned Agent row; its status
may be suspended or failed and is not evidence that the canonical Task completed.
An `ObservationDetached` outcome has no Agent row and records presentation
withdrawal only. It does not cancel, dispose, retry or join the business producer.

`RequestOriginalObservationRetirement` is request-only. The same actual
`DetachAndDrainOriginalObservationAsync` driver releases and independently joins
the original observation wait. Consumers must join this driver separately before
detaching renderer/subscription resources. The driver does not wait on the durable
producer, so a synchronous `RunChanged` callback can request observation retirement
without joining itself. Existing explicit `Cancel` remains the task-cancel action.

Issuance is the exact private service marker, original object and self-lease
reference. It remains valid after observation-slot retirement. Copied result
metadata, Agent history bindings and foreign service instances cannot issue it.

There are at most 128 retained observation admission records. Healthy records can
release that admission slot only after their actual producer and observation
originals are terminal and successful and custody is attached to the actual owning
canonical invocation. Detach drivers, if admitted, must also finish successfully.
Failed, canceled, unknown or unbound producer custody remains retained and refuses
new admission at the bound. No global archive of healthy bindings is created.

This boundary provides presentation withdrawal. Actual process retirement needs
a separate canonical checkpoint/suspension owner port. It does not implement
historical recovery, authentication renewal, accepted-checkpoint replay or durable
restart. Windows/Android/browser runtime acceptance remains required.
