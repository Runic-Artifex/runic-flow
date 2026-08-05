# Runic Flow

Runic Flow provides framework-neutral application mechanics for .NET: typed
navigation, dialogs, coordinated operations, workflows, presentation contracts,
and deterministic generator vocabulary. Its core is designed for trimming and
NativeAOT and does not depend on a UI framework or Runic Toolkit.

This repository was extracted from Runic Toolkit with its product history intact.
It uses the independent `RunicFlow.*` package, assembly, namespace, diagnostic,
and protocol identities without compatibility aliases for the retired Toolkit
identities.

## Projects

| Project | Purpose |
| --- | --- |
| `RunicFlow` | UI-neutral navigation, dialogs, operations, workflows, and presentation contracts |
| `RunicFlow.Generators` | Roslyn-independent generator and diagnostic contracts |
| `RunicFlow.CommunityToolkit` | Flow-owned CommunityToolkit.Mvvm projection adapter |
| `integrations/RunicFlow.RunicToolkit` | Published Toolkit desktop integration owned by Flow |

The Toolkit adapter source is intentionally outside the standalone solution until
Runic Toolkit contracts are consumed as exact packages. This keeps dependency
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

## Prerelease packages

Pull requests that affect packaging produce validated, non-published artifacts
for `RunicFlow`, `RunicFlow.Generators`, and `RunicFlow.CommunityToolkit`. The
package gate checks the exact artifact set, MIT and repository metadata, the
declared dependency graph, an isolated package consumer, and NativeAOT.

Publishing to GitHub Packages is deliberately separate: a maintainer must run
the **Prerelease packages** workflow manually and explicitly enable its
`publish` input. To reproduce the package stage locally:

```bash
./eng/pack.sh 0.1.0-preview.local.1 /tmp/runic-flow-packages
```

## License

Runic Flow is licensed under the [MIT License](LICENSE).
