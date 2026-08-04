# Diagnostics and generator contracts

Flow owns the reserved diagnostic range `RFLOW0001` through `RFLOW0999`.
Wave B freezes the first ten identities, their meanings, and their source-span
policy. The older `FLOW###` names in the standalone design are draft aliases only;
implementations and documentation use `RFLOW####`.

| ID | Severity | Stable trigger |
|---|---|---|
| `RFLOW0001` | Error | Duplicate logical key in one registry. |
| `RFLOW0002` | Error | Invalid or empty logical key or presentation contract. |
| `RFLOW0003` | Error | Missing or ambiguous start route or workflow step. |
| `RFLOW0004` | Error | Dialog request/result types cannot be closed or a controller type conflicts. |
| `RFLOW0005` | Error | Workflow edge references a missing step or the graph has no finish path. |
| `RFLOW0006` | Error | More than one default action or more than one cancel action is visible. |
| `RFLOW0007` | Warning | Generated registration cannot prove that a ViewModel is registered. |
| `RFLOW0008` | Error | ViewModel is open-generic, abstract, inaccessible, or nested private. |
| `RFLOW0009` | Warning | Recreate-on-back or checkpointed data has no registered codec. |
| `RFLOW0010` | Error | Two generated identifiers collide after normalization. |

Diagnostics use the smallest source span that names the offending declaration or
argument. Duplicate diagnostics point at the later declaration and include the
logical identity; they do not embed absolute machine paths. Severity, argument
order, and source-span selection are compatibility commitments.

The runtime registration kernel projects validation through
`FlowRegistrationDiagnostic`. `FlowRegistryBuilder.TryAdd` returns a structured
`RFLOW0001` duplicate with primary and related locations; `Add` raises a
`FlowRegistrationException`. Named validators receive an immutable
`FlowRegistryValidationContext` and `FlowRegistrationDiagnosticSink`. Validator
execution and returned diagnostics are ordinally sorted, so registration order does
not change output. Freeze with any Error diagnostic raises `FlowValidationException`;
the diagnostic-returning overload preserves the complete deterministic set.
Runtime registration diagnostics accept only the reserved `RFLOW0001` through
`RFLOW0999` range; `RFLOW0000`, values at or above `RFLOW1000`, and malformed
identities are rejected.

## Generator boundary

Explicit fluent registration is always supported and is the semantic authority.
Wave B ships a Roslyn-independent `net10.0` generator contract kernel. A
future Roslyn front end may only emit deterministic, fully qualified, closed-generic
calls to the same public registration APIs. It must not introduce a second runtime
model.

The contract kernel models declarations, source locations, properties, diagnostic
descriptors/instances, and generated sources without referencing Microsoft.CodeAnalysis.
The future adapter's inputs are attributes and syntax from the current compilation.
It does not inspect output assemblies, scan referenced assemblies, perform runtime
type-name lookup, select constructors through reflection, or emit dynamic code.
Complex workflow definitions remain consumer-authored registrations rather than an
attribute graph language.

`IFlowGeneratorKernel.Generate(FlowGeneratorInput)` is the frozen adapter seam.
Inputs sort declarations and properties by ordinal semantic identity. Source paths
normalize directory separators and are expected to be logical rather than absolute.
Generated text converts CRLF and CR to LF and ensures a final LF. Invalid or non-ASCII
UTF-16 identifier units encode as invariant `_XXXX`; hint names derive from the
fully qualified module name.

Generated output requirements:

- stable hint names and normalized identifiers;
- fully qualified framework and Flow type names;
- ordinal ordering independent of syntax-tree enumeration, current culture,
  machine paths, and line endings;
- closed ViewModel, request, parameter, result, and module types;
- no timestamps, random values, absolute paths, environment values, or assembly
  scanning hooks;
- incremental invalidation limited to affected declarations;
- equivalent diagnostics and runtime semantics to fluent registration.

Generator tests cover every reserved diagnostic, aliases, malformed syntax,
inaccessible/nested/generic types, identifier collisions, deterministic repeated
runs, incremental caching, and absence of reflection/discovery output.

## Runtime diagnostics

The BCL kernel owns `ActivitySource` and `Meter` instrumentation named
`RunicFlow`. Reserved activities are `flow.navigate`, `flow.dialog`,
`flow.operation`, `flow.workflow`, and `flow.present`. Counters record bounded
transition, outcome, and fault totals; histograms record duration and queue wait.

Diagnostic metadata may include feature, bounded logical key, session or operation
identity, lifecycle stage, outcome, and duration. It must not include route
parameters, dialog results, workflow context/checkpoint payloads, arbitrary progress
messages, or other user content by default. Microsoft.Extensions logging and event-ID
projection are Wave C adapters; the kernel does not take a logging dependency.
