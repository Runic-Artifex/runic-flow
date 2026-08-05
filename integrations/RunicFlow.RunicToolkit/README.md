# Runic Flow integration for Runic Toolkit

This directory is the Flow-owned integration boundary for Runic Toolkit. The
framework-neutral Flow core must not reference Toolkit desktop, web, hosting, or
MVVM assemblies; this adapter may reference both products.

`NavigationDesktopCloseGuard` bridges Flow navigation guards to Runic Toolkit's
desktop lifetime. The `RunicFlow.RunicToolkit` package depends on the independent
`RunicFlow` core and the exact published `RunicToolkit.Desktop` contract package;
neither core depends on this adapter.

The adapter is covered in the standalone solution, package metadata verification,
and the managed plus NativeAOT package-consumer gate.
