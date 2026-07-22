# WebUIToolkit.MVVM.Flow.Generators

This package contains the Roslyn-independent contract layer for Flow source generation. It defines the immutable declaration, diagnostic, and generated-source vocabulary shared by generator front ends and deterministic test harnesses.

The current package intentionally has no `Microsoft.CodeAnalysis` dependency. Central Roslyn package versions have not been approved, so the incremental-generator entry point and conversion to Roslyn diagnostics remain a future adapter. The adapter must translate these contracts without changing the `WUTFLOW0001`–`WUTFLOW0010` identities, severities, location policies, ordering rules, or emitted text.

All public types use the `WebUIToolkit.MVVM.Flow.Generators` namespace. The package targets `netstandard2.0`, does not inspect output assemblies, and does not perform runtime reflection or registration.
