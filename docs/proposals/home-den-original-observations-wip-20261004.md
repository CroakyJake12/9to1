# Canonical Den operation observations

This isolated source checkpoint preserves the original Home Den/import signatures, exact exception classes and messages, manual decisions, binding writes and audit behavior. Its new observation ports require the exact canonical returned task from the same producer invocation.

OpenAsync, CompleteImportAsync and RetryImportAuditAsync now create private per-invocation scopes and map their actual original core tasks once. Direct pre-effect refusals retain the exact original exception in that invocation. Replaying an earlier observed exception from a later actor, evidence or permission callback cannot mark the later task. Actor/store/body/cleanup failures and post-removal execution failures remain blocking.

Audit recovery requires the exact original failed task and pending exception, an actual successful canonical Retry task/result, and the same private completion/request/binding/actor. Its immutable historical observation is created only after RecordExecutionAsync succeeded and the completion gate was released. It grants no current permission, ownership, import retry or redispatch; caller-created tasks or binding results cannot produce it. Unknown binding and audit outcomes remain unobserved.

Consumers must retain these canonical tasks separately from their own wrapper tasks and independently await their own cleanup. Successful historical observation does not waive a consumer/body/cleanup failure.

BindAsync is unchanged in this checkpoint. A current integrator must compose these method-level changes with the independently owned BindAsync failure-classification correction rather than replace a newer entire ownership file. Existing Studio fixtures/assertions are untouched. Same-exception replay and exact-task recovery controls require separate source review and execution.

Status: unselected, uncompiled source WIP. No native consumer, workflow, installation policy or permission grant is changed.
