# Runic Flow

Runic Flow is a headless .NET runtime for deterministic application processes and
coordinated backend operations. It provides typed command decisions, serialized
state commits, process-local versions, opaque checkpoints, concurrency slots,
timeouts, progress, cancellation, and terminal outcomes without owning UI state.

## Projects

| Project | Purpose |
| --- | --- |
| `RunicFlow` | Framework-neutral process and operation kernel |
| `RunicFlow.ApplicationBridge` | Flow-owned operation integration for Runic Toolkit Application Bridge |

The core has no dependency on Runic Toolkit or a frontend framework. The
Application Bridge integration depends on both products and preserves the bridge
as the sole owner of transport sessions, wire revisions, sequences, reconnect,
schema validation, and externally visible operation identity.

Flow deliberately does not provide navigation services, dialogs, presenters,
ViewModel activation, component lifecycle, routing, or a generic wire protocol.
Applications expose named domain commands, receipts, snapshots, and events.

## Development

Enter the Nix development shell and run:

```bash
nix develop
./eng/verify.sh
```

Verification performs identity checks, warning-free Release builds, core and
Application Bridge contract tests, an isolated package consumer, and a NativeAOT
publish and execution smoke test.

## Release status

The former MVVM-oriented prerelease surface was intentionally retired before a
first public release. A public release requires the headless Setup-style vertical
and package gates to pass; no compatibility layer is provided for prerelease APIs.

## License

Runic Flow is licensed under the [MIT License](LICENSE).
