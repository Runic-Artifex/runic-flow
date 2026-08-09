# RunicFlow

`RunicFlow` supplies NativeAOT-compatible, presentation-free mechanics for .NET
applications:

- typed process definitions and serialized command dispatch;
- immutable authoritative snapshots and process-local stale-version guards;
- explicit accept, reject, complete, and cancel decisions;
- bounded, consumer-coded checkpoints without serializer discovery;
- coordinated operations with slots, timeout, progress, cancellation, monitoring,
  and typed outcomes.

The package does not contain navigation, dialogs, ViewModels, presenters,
presentation leases, frontend state, transport, or framework lifecycle adapters.
Applications keep their named domain contract and project process state into the
Application Bridge or another delivery mechanism.
