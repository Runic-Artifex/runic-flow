# Dependencies, verification, and Wave C boundary

## Dependency manifest

`WebUIToolkit.MVVM.Flow` is a `net10.0` BCL-first shipping library. Its runtime
project has no third-party or Hosting package reference. The centrally supplied
`Microsoft.NET.ILLink.Tasks` package is a build/analyzer dependency used by shipping
trim and Native-AOT analysis; it is not a runtime feature dependency.

The allowed dependency direction is:

```text
frontend and MVVM-framework adapters
                |
                v
WebUIToolkit.MVVM.Flow -> BCL and approved minimal MVVM abstractions only
                |
                v
              BCL
```

Flow never references `WebUIToolkit.Hosting`. Microsoft.Extensions DI, logging, and
options integration remains an outward adapter until approved central package
versions are available. Closed delegates and disposable scope objects keep the
kernel testable without a container. The Wave B project currently requires no MVVM
or Microsoft.Extensions package at runtime.

`WebUIToolkit.MVVM.Flow.Generators` targets `netstandard2.0`. Its only locked package
graph is the target framework's `NETStandard.Library` and transitive platform
metadata. It intentionally has no Microsoft.CodeAnalysis dependency in Wave B;
Roslyn binding is a future adapter over the frozen diagnostic and emission models.

Project-local `packages.lock.json` files are owned artifacts. Committed locks must
be portable and RID-free. A Native-AOT restore may resolve RID-specific runtime
packs only into the ignored `obj/aot.packages.lock.json`; that temporary file must
not replace or alter a committed lock.

## Owned verification

Run the BCL kernel and executable contract suite with locked restores:

```powershell
dotnet restore --locked-mode src/WebUIToolkit.MVVM.Flow/WebUIToolkit.MVVM.Flow.csproj
dotnet restore --locked-mode src/WebUIToolkit.MVVM.Flow.Generators/WebUIToolkit.MVVM.Flow.Generators.csproj
dotnet restore --locked-mode tests/WebUIToolkit.MVVM.Flow.Tests/WebUIToolkit.MVVM.Flow.Tests.csproj
dotnet build -c Release --no-restore src/WebUIToolkit.MVVM.Flow/WebUIToolkit.MVVM.Flow.csproj
dotnet build -c Release --no-restore src/WebUIToolkit.MVVM.Flow.Generators/WebUIToolkit.MVVM.Flow.Generators.csproj
dotnet run -c Release --no-restore --project tests/WebUIToolkit.MVVM.Flow.Tests/WebUIToolkit.MVVM.Flow.Tests.csproj
```

Build a local package before validating a consumer:

```powershell
dotnet pack -c Release --no-restore -p:PackageVersion=0.0.0-local -o artifacts/packages src/WebUIToolkit.MVVM.Flow/WebUIToolkit.MVVM.Flow.csproj
```

`FlowPackageVersion` switches the executable consumer from its development
`ProjectReference` to the packed artifact. Its package-mode lock is temporary:

```powershell
dotnet restore -p:FlowPackageVersion=0.0.0-local -p:RestoreAdditionalProjectSources=artifacts/packages -p:NuGetLockFilePath=obj/package.packages.lock.json -p:RestoreLockedMode=false tests/WebUIToolkit.MVVM.Flow.PackageConsumer/WebUIToolkit.MVVM.Flow.PackageConsumer.csproj
dotnet run -c Release --no-restore -p:FlowPackageVersion=0.0.0-local --project tests/WebUIToolkit.MVVM.Flow.PackageConsumer/WebUIToolkit.MVVM.Flow.PackageConsumer.csproj
```

The consumer must run and exit zero without falling back to a source project or an
undeclared package feed.

For Native AOT, keep the committed lock portable and create a temporary RID lock:

```powershell
dotnet restore --locked-mode tests/WebUIToolkit.MVVM.Flow.AotSmoke/WebUIToolkit.MVVM.Flow.AotSmoke.csproj
dotnet pack -c Release -p:PackageVersion=0.0.0-local -o artifacts/packages src/WebUIToolkit.MVVM.Flow/WebUIToolkit.MVVM.Flow.csproj
dotnet restore -r win-x64 -p:FlowPackageVersion=0.0.0-local -p:RestoreAdditionalProjectSources=artifacts/packages -p:PublishAot=true -p:PublishTrimmed=true -p:NuGetLockFilePath=obj/aot.packages.lock.json -p:RestoreLockedMode=false tests/WebUIToolkit.MVVM.Flow.AotSmoke/WebUIToolkit.MVVM.Flow.AotSmoke.csproj
dotnet publish -c Release -r win-x64 --no-restore -p:FlowPackageVersion=0.0.0-local -p:PublishAot=true -p:PublishTrimmed=true -p:NuGetLockFilePath=obj/aot.packages.lock.json tests/WebUIToolkit.MVVM.Flow.AotSmoke/WebUIToolkit.MVVM.Flow.AotSmoke.csproj
```

Run the produced executable and require exit code zero and no owned trim/AOT
warnings. Repeat with a separate temporary RID lock for every additional supported
RID. Repository namespace and architecture gates remain mandatory:

```powershell
pwsh eng/verify-namespaces.ps1
pwsh eng/verify-architecture.ps1
```

## Required behavioral evidence

Executable tests assert order, not only final state. The Wave B matrix includes:

- navigation commit/rollback, guards, retention, stale sessions, queue/reject,
  presenter failures, lifecycle failure aggregation, shutdown, and reverse cleanup;
- nullable typed dialog results, close-guard deny/retry, racing completion sources,
  nested ownership, caller cancellation, presenter open/close failures, manual-clock
  overall teardown deadlines, accepted-outcome preservation, and shutdown;
- every operation slot policy, progress validation, timeout through a manual clock,
  cooperative delegates that do and do not observe cancellation, delayed
  CancelPrevious admission, presenter admission, monitor retention, cleanup
  precedence, and concurrent cancel;
- workflow branching, exclusions, validation stay, commit faults, actual-history Back,
  redirect loops, typed finish retry without repeated commit, active-mutation
  cancellation during disposal, teardown safety-abandon and late-fault observation,
  retention, checkpoint
  round-trip/rejection/migration, restore-before-activation validation, and shutdown;
- deterministic generator output/diagnostics, packed public consumer execution, and
  Native-AOT publish-and-run.

## Deferred Wave C edges

Wave C owns integration without changing Wave A or Wave B semantics:

- Microsoft.Extensions dependency-injection builders, scope factories, options
  validation, logging/event-ID projection, and graceful host-shutdown coordination;
- adapters to the minimal frozen `WebUIToolkit.MVVM` observable/dispatcher
  abstractions once their package handoff is approved;
- CommunityToolkit.MVVM and ReactiveUI command/state projections in their dedicated
  packages;
- frontend presenters, contract-to-component/template mapping, browser history,
  focus/accessibility behavior, disconnect handling, and stale-event transport;
- source-generated JSON or consumer codecs for deep links and checkpoint payloads;
- application policies for checkpoint storage, migration orchestration, encryption,
  authentication, expiration, and recovery;
- shared adapter conformance fixtures, client-component and HTMX reference adapters,
  vertical hosting composition, and multi-RID release evidence.

Adapters depend on Flow; Flow does not depend on adapters or Hosting. Wave C may add
convenience registration and projection APIs, but it must preserve frozen key
comparison, lifecycle ordering and commit points, typed outcomes, exact-once
completion/teardown, checkpoint envelope meaning, and diagnostic identities.
Presenter adapters must return asynchronous method `ValueTask` values promptly;
configured Flow timeouts cannot interrupt code that blocks synchronously before the
return.
