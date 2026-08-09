# RunicFlow.ApplicationBridge

This Flow-owned integration binds backend operations to the Runic Toolkit
Application Bridge without introducing another protocol runtime.

`StartFlowOperation(...)` lets Application Bridge retain external operation
ownership and cancellation while Flow supplies concurrency slots, timeouts,
monitoring, and deterministic outcomes. The bridge operation identifier is reused
as the Flow operation identifier. Applications continue to publish their own
schema-specific progress, completion, cancellation, and failure events.

The integration does not own sessions, revisions, sequences, reconnect, transport,
schema validation, command semantics, or frontend state.
