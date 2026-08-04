# Wave B kernel semantics

Wave B implements frontend-neutral state machines for navigation, dialogs,
operations, and workflows. The runtime targets `net10.0`, does not capture a
`SynchronizationContext`, and does not reference Hosting or a frontend framework.
Presenters receive logical contracts and immutable snapshots; they do not own or
mutate engine state.

## Shared ownership and ordering

Every activated content item has one `FlowSessionId` and one owned scope. A content
session may additionally own an independently created ViewModel, a presentation
lease, and child sessions. Teardown is exact once even when callers race or retry:

1. Cancel the session lifetime token.
2. Tear down child sessions in reverse creation order.
3. Close and asynchronously dispose the presentation lease.
4. Dispose the ViewModel only when the activation explicitly transferred separate
   ownership to Flow. Prefer `IAsyncDisposable` to `IDisposable` and never invoke
   both on the same object.
5. Dispose the scope, again preferring asynchronous disposal.

Cleanup continues after individual failures. The ordered failures are reported by
`FlowCleanupException`; committed state is never resurrected. Caller cancellation
may select an outcome, but cleanup uses the session lifetime or a non-cancelled
cleanup token so an already-cancelled caller cannot abandon owned resources.

Lifecycle callbacks before presentation commit may abort and roll back a new
session. Post-commit callback failures are notifications: authoritative state stays
committed and the failures are reported in observable order.

Presenter and lease methods must return their `ValueTask` promptly. Timeout logic
can bound asynchronous waiting only after the method returns; it cannot preempt an
adapter that blocks synchronously inside `PresentAsync`, `ShowAsync`, `CloseAsync`,
or another presenter entry point before producing the `ValueTask`.

## Registration and validation kernel

`FlowRegistryBuilder` is single-threaded composition state. It accepts explicit,
closed `FlowRegistration` values, adapter mapping declarations, and named validators.
Registration identities partition logical keys by feature kind, use ordinal
case-sensitive comparison, and retain optional stable project-relative locations.
Closed factories receive a BCL `FlowActivationScope` around the session-local
`IServiceProvider`; the kernel never scans assemblies or selects constructors.

Duplicate registrations and adapter mappings fail immediately and preserve the
first and later locations. `Freeze()` permanently disables mutation, sorts
registrations and mappings by ordinal semantic identity, runs validators by ordinal
validator name, sorts diagnostics deterministically, and returns an immutable
`FlowRegistrySnapshot`. Freeze remains final even when validation reports errors;
repeated calls return the same snapshot and diagnostic values. Adapter mappings are
declarations for startup validation, not frontend implementations.

## Navigation regions

Each registered region owns an independently serialized logical stack. Registration
is explicit and closed over the ViewModel and parameter types. The registry becomes
immutable after `NavigationRegistryBuilder.Build()`; duplicate routes, duplicate
closed ViewModel signatures, invalid required-content regions, and missing start
routes fail before interaction.

The transition order is:

1. Reject shutdown, observe cancellation, and obtain the region admission policy.
   `Queue` is FIFO; `RejectWhileBusy` returns `Busy` without starting activation.
2. Resolve the closed route and validate its parameter without creating content.
3. Ask the current `INavigationGuard`, if any. A denial returns `Rejected`; a guard
   exception leaves the snapshot unchanged.
4. Invoke the closed content factory, run typed initialization, and call
   `ActivatingAsync`.
   A failure tears down only the target.
5. Ask `INavigationRegionPresenter` to atomically present the target and return its
   lease. A presenter failure is pre-commit and must retain or restore old content.
6. Transfer the lease to the target entry, mutate the stack without further
   fallible work, increment the region version,
   and publish one immutable snapshot. This is the commit point.
7. Run old deactivation notifications and target activation notifications.
8. Close and dispose leases exactly once, dispose removed scopes in reverse stack
   order according to retention, then admit the next request.

`Push`, `Replace`, and `Reset` create a target. `Back` revisits actual stack history;
`Clear` first clears the presenter outlet and is rejected for a required-content
region. `RetainInBackStack` keeps a live session. `RecreateOnBack` retains only the
closed route and in-memory parameter, then creates a new session when revisited.
Session-aware `BackAsync` and `ClearAsync` return `Stale` when an adapter event no
longer targets current content.

Shutdown rejects new work, cancels queued requests, bypasses user leave guards, and
tears down regions and entries in reverse ownership order. Direct same-region
re-entrancy is rejected with `FlowReentrancyException`.

## Typed dialogs

A dialog registration closes over ViewModel, request, and result types and selects a
presenter by `PresenterKey`. `Completed`, `Cancelled`, and `Dismissed` are typed
ordinary outcomes; initialization, lifecycle, presenter, and cleanup failures remain
exceptions. A nullable result is distinguished from cancellation by outcome kind.

Open and completion follow this order:

1. Create an owned dialog content session (a child when a parent session exists),
   resolve its scoped controller, initialize the ViewModel, and run pre-activation.
2. Open through the selected presenter, attach the returned lease, mark the session
   open, and run post-commit activation.
3. Await a request from the controller, caller cancellation, presenter dismissal, or
   shutdown. An interlocked claim ensures one contender enters completion.
4. Consult the close guard for ordinary complete/cancel/dismiss requests. A denial
   releases the claim so a later request may retry. Shutdown atomically preempts a
   pending ordinary guard decision, bypasses the guard, rejects new opens, and drains
   admitted pre-open work.
5. Disable further completion, close child dialogs in reverse order, call
   `DeactivatingAsync`, close the lease, call `DeactivatedAsync`, dispose the lease,
   then dispose a separately owned ViewModel and its scope.
6. Complete `ShowAsync` only after successful teardown. Losing completion requests
   return `false` and have no side effects.

Parent teardown owns nested-dialog teardown. A close failure after an accepted
outcome is a lifecycle/cleanup fault; it does not convert the outcome to Dismissed.
`DialogService.TeardownTimeout` bounds the entire ordered teardown with one deadline
driven by the configured `TimeProvider`; the default is 30 seconds. Once that
deadline elapses,
later cleanup stages are skipped and `DialogTeardownException<TResult>` preserves
the already accepted typed outcome alongside the ordered failures.

## Operations and monitoring

`OperationRunner` validates bounded request metadata before work, creates an
`OperationId`, and timestamps snapshots through its configured `TimeProvider`.
Observable state advances through:

`Queued -> Starting -> Running -> Succeeded|Cancelled|Faulted -> Finished`.

Terminal cancellation records the first winning deterministic
`OperationCancellationReason`: Caller, Requested, Timeout, or Replaced. A delayed
`OperationCanceledException` cannot overwrite that reason.

`OperationRequest.Timeout` is cooperative. Expiry requests cancellation through the
delegate token, but the runner still awaits the delegate to preserve slot
serialization and resource ownership. A delegate that ignores cancellation delays
the terminal snapshot and caller completion; it also delays a `CancelPrevious`
replacement, which cannot start until prior work and cleanup finish.

When a presenter is selected, presentation succeeds before the work delegate is
invoked. Work executes exactly once. Progress is accepted only while Running;
fractions must be finite and in the inclusive range 0 through 1. Segment collections
are defensively copied. Monitor snapshots contain no exception payload.

Slot policies for the same non-empty ordinal slot are:

- `Allow`: start concurrently.
- `Reject`: throw `OperationBusyException` before presentation or user work.
- `Queue`: wait FIFO; cancellation removes a waiter without presentation.
- `CancelPrevious`: reserve Replaced as the current work's cancellation reason,
  signal cancellation outside the slot lock, wait for complete cleanup and Finished
  publication, and then admit the replacement.

The runner closes and disposes presentation, publishes Finished, and then releases
the slot to the next waiter. A work failure is primary and preserves its original
stack. Cleanup faults are attached at
`Exception.Data["RunicFlow.CleanupException"]`; cleanup alone faults the
call with `FlowCleanupException`. `TryRunAsync` projects success, runner-controlled
cancellation, and failure to `OperationOutcome<T>`.

`IOperationMonitor` returns immutable point-in-time copies, publishes snapshots to
observers, and offers cooperative cancellation only while the invocation advertises
that capability. Cancellation reservation and signalling are split so user code is
never invoked under the runner lock. Observer notifications are queued in state
transition order, and observer exceptions cannot affect execution. Finished-entry
retention is bounded by `OperationRunnerOptions`.

## Workflow state machine and checkpoints

A workflow definition is a typed immutable graph with a positive consumer schema
version, unique steps, one start step, ordered conditional edges, retention policy,
and an exactly-once typed result factory. The builder rejects missing starts, missing
edge endpoints, duplicate or unreachable steps, and a missing result factory.

Forward movement is serialized per workflow session:

1. Validate the current step. Structured error issues publish a Stayed snapshot and
   do not run commit or leave hooks.
2. Confirm that the graph has an outgoing path. With no outgoing path, remain on the
   current step without committing.
3. Run the step commit and pre-deactivation hooks. Failure leaves the current
   presentation authoritative.
4. Evaluate ordered edge and inclusion predicates against the possibly mutated
   context, skip excluded targets, and detect per-transition redirect loops. Then
   create the target session, initialize with the shared typed context,
   run pre-activation, and present it. Failure disposes a new target and retains the
   current presentation as authoritative.
5. Commit current key and actual visited history in one immutable snapshot.
6. Run post-commit lifecycle notifications and dispose the prior session unless its
   retention policy keeps it in visited history. Snapshot-subscriber failures are
   post-commit lifecycle failures reported only after callbacks and cleanup finish.

Back follows visited history, not static graph order. Context is application-owned;
Flow does not clone it or undo business side effects when history moves backward.
Finish validates and commits, invokes the typed result factory once, closes, publishes
Completed, and disposes. Ordinary cancel consults `IWorkflowCancelGuard`; shutdown
bypasses it. Abandon never invokes the result factory.

Finish memoizes successful validation, commit, and pre-deactivation across a result
factory fault. A retry invokes the result factory again without repeating business
commit or pre-deactivation, and successful termination does not issue a second
pre-deactivation callback. Session disposal requests cancellation of an active
linked mutation outside the workflow mutex and then drains owned presentation and
step resources.

The default `WorkflowSession<TContext,TResult>` constructor uses
`TimeProvider.System` and a 30-second teardown timeout. Its overload accepts an
explicit `TimeProvider` and `TimeSpan` for deterministic tests and host policy.
Teardown timeouts use safety-abandon semantics: if lease Close times out, Flow does
not invoke lease Dispose or dispose the associated ViewModel/scope while the late
close may still use them. If separately owned ViewModel disposal times out, its
dependent scope is likewise not disposed. Late faults are observed, while
independent retained step trees continue draining.

Checkpoint envelopes contain only format version, workflow key, consumer schema
version, current step, visited step keys, and bounded opaque consumer payload. They
never contain ViewModels, scopes, services, delegates, commands, or
presenter state. Restore validates the envelope, key, version, graph membership,
history, payload size, and optional forward migration before consumer activation.
Rejection is explicit; Flow never silently starts a new workflow. Consumer codecs,
workflow-session restore integration, encryption, authentication, expiry, and
storage policy remain outside the kernel.

The Wave B envelope format is version 1, bounds visited history at 4,096 keys and the
opaque payload at 1 MiB, and defensively copies both. Restore accepts the current
format, rejects newer consumer schemas, and may apply one consumer-provided forward
migration from an older schema before graph membership is checked.

## Time and cancellation

All timeout, deadline, and timestamp behavior accepts `TimeProvider`; executable
tests advance a manual clock and do not rely on wall-clock sleeps. Operation
timeouts request cancellation and await cooperative delegate exit. Dialog teardown
uses one overall deadline and reports the accepted outcome with failures when the
bound prevents later stages. Bounded waits do not make synchronously blocking
presenter implementations preemptible.
