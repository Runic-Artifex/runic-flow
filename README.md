# Runic Flow

Runic Flow provides framework-neutral application mechanics for .NET: typed
navigation, dialogs, coordinated operations, workflows, presentation contracts,
and deterministic generator vocabulary. Its core is designed for trimming and
NativeAOT and does not depend on a UI framework or Runic Toolkit.

This repository was extracted from Runic Toolkit with its product history intact.
The current package and namespace identities are retained temporarily so the
standalone dependency boundary can be proven before the clean-break
`RunicFlow.*` identity migration.

## Projects

| Project | Purpose |
| --- | --- |
| `WebUIToolkit.MVVM.Flow` | UI-neutral navigation, dialogs, operations, workflows, and presentation contracts |
| `WebUIToolkit.MVVM.Flow.Generators` | Roslyn-independent generator and diagnostic contracts |
| `WebUIToolkit.MVVM.Flow.CommunityToolkit` | Flow-owned CommunityToolkit.Mvvm projection adapter |
| `integrations/RunicFlow.RunicToolkit` | Staged Toolkit integration boundary owned by Flow |

The Toolkit adapter source is intentionally outside the standalone solution until
Runic Toolkit contracts can be consumed as packages. This keeps dependency
direction explicit: integrations depend on both products; neither core depends on
an integration.

## Development

Enter the Nix development shell and run the complete standalone verification:

```bash
nix develop
./eng/verify.sh
```

Verification performs a restore, a warning-free Release build, the Flow
contract suites, the CommunityToolkit adapter fixtures, and a NativeAOT publish
and execution smoke test.

## License

Runic Flow is licensed under the [MIT License](LICENSE).
