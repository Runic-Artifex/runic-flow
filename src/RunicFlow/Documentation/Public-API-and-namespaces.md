# Public API and namespaces

The package identity is `RunicFlow`. Its public surface is intentionally limited
to headless process and operation mechanics.

## `RunicFlow`

- `ProcessKey` identifies an application process definition.
- `OperationKey` identifies an operation kind.
- `OperationStage` identifies logical progress without presentation text.

Keys are bounded, ordinal, case-sensitive values containing ASCII letters,
digits, `.`, `-`, `_`, or `/`.

## `RunicFlow.Processes`

- Definitions: `ProcessDefinition<TState,TCommand,TResult>` and
  `ProcessCommandHandler<TState,TCommand,TResult>`.
- Runtime: `ProcessSession<TState,TCommand,TResult>`, `ProcessId`,
  `ProcessCommandContext<TState>`, and immutable process snapshots.
- Decisions and observations: explicit accept, reject, complete, cancel, stale,
  and terminal results. Rejected and stale commands never advance process state.
- Checkpoints: `ProcessCheckpoint`, `ProcessCheckpointLimits`,
  `IProcessCheckpointCodec<TState>`, `IProcessCheckpointStore`, and
  `ProcessCheckpointing`.

Process versions describe process commits only. They are not transport revisions
and do not replace Application Bridge authority.

## `RunicFlow.Operations`

- Execution: `OperationRequest`, `OperationId`, `OperationContext`,
  `IOperationRunner`, and `OperationRunner`.
- Policy: allow, reject, queue, or cancel-previous concurrency; logical slots;
  timeout; and cooperative cancellation.
- Observation: immutable `OperationSnapshot` values through `IOperationMonitor`.
- Results: typed `OperationOutcome<T>` preserving success, cancellation, or the
  original failure.

Operation progress contains only a normalized fraction and optional logical
stage. User-facing text and localization remain application presentation data.

## Removed prerelease surface

Navigation regions, dialogs, presenters, ViewModel factories, presentation
leases, CommunityToolkit projections, and speculative generator contracts were
retired before public release. No compatibility aliases are provided.
