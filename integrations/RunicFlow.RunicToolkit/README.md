# Runic Flow integration for Runic Toolkit

This directory is the Flow-owned integration boundary for Runic Toolkit. The
framework-neutral Flow core must not reference Toolkit desktop, web, hosting, or
MVVM assemblies; this adapter may reference both products.

`NavigationDesktopCloseGuard` has moved here unchanged so applications retain the
bridge between Flow navigation guards and Toolkit desktop lifetime. The adapter
project will become buildable and packageable as `RunicFlow.RunicToolkit` once the
corresponding Runic Toolkit contracts are available as a package. Until then this
source is deliberately excluded from the standalone solution instead of restoring
a source-tree dependency on a sibling repository.

The deferred `G3EvidenceTests.cs` corpus in the CommunityToolkit test directory is
also Toolkit integration evidence: it exercises the Toolkit MVVM wire protocol,
not the independent CommunityToolkit projection adapter. It remains tracked but is
excluded from standalone compilation until this integration project owns it.
