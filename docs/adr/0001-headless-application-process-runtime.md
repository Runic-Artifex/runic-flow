# ADR 0001: Headless application-process runtime

- Status: Implemented
- Date: 2026-08-09

## Context

Flow originated as a framework-neutral extraction of an MVVM application shell.
Although it did not reference a UI framework, its public model still activated,
retained, presented, and disposed ViewModels for navigation, dialogs, operations,
and workflows.

Runic Toolkit has replaced its MVVM bridge with the schema-first Application
Bridge. The bridge exposes named domain commands, receipts, snapshots, and events
and owns transport sessions, revisions, sequences, cancellation, reconnect, and
backend operation lifetime. Svelte owns rune projection and component lifecycle;
SvelteKit owns URLs, browser history, routing, and page state.

The Setup reference vertical nevertheless repeats application-process mechanics:
serialized state mutation, transition validation, operation exclusion, progress,
cancellation, completion, and recovery snapshots.

## Decision

Flow is a headless .NET runtime for application processes and coordinated backend
operations.

1. Process definitions consume application-defined state and commands and return
   explicit accept, reject, complete, or cancel decisions.
2. Process sessions serialize handlers, commit immutable snapshots, provide an
   optional process-local stale-version guard, reject recursive dispatch, and do
   not own presentation or transport.
3. Checkpoint bytes remain opaque and require an explicit consumer codec.
4. Operations own concurrency policy, logical slots, timeout, progress,
   cancellation, monitoring, and typed outcomes.
5. `RunicFlow.ApplicationBridge` reuses the bridge operation identifier while
   preserving Application Bridge ownership of the external lifecycle.
6. Named application contracts remain authoritative. Flow does not define a
   generic navigation, dialog, action, or workflow wire protocol.
7. Svelte and SvelteKit require no Flow runtime. They consume validated domain
   state through the existing Application Bridge projection.

## Removed surface

The prerelease navigation, dialog, presenter, ViewModel activation, presentation
lease, CommunityToolkit, desktop-close, and generator APIs are removed without a
compatibility layer.

## Release gate

Flow may enter a first public release only after its core, Application Bridge
integration, isolated package consumer, NativeAOT test, and a Setup-style process
vertical pass. If the headless runtime does not materially reduce application
handler policy and ownership code, the product should be retired rather than
expanded back into a presentation architecture.
