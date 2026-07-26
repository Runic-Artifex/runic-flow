# Public API and namespaces

The package identity is `WebUIToolkit.MVVM.Flow`. Public feature namespaces are
intentionally flat and are preserved if features later move to separate packages.
No public API exposes Hosting, DOM, HTML, component, dispatcher,
container-specific, or third-party MVVM types.

## `WebUIToolkit.MVVM.Flow`

Shared immutable values and ownership contracts:

- Logical identities: `RouteKey`, `ViewContract`, `RegionKey`, `DialogKey`,
  `PresenterKey`, `OperationKey`, `WorkflowKey`, `StepKey`, `ActionKey`, and
  `IconKey`. Values are non-empty, trimmed, at most 128 characters, limited to ASCII
  letters, digits, `.`, `-`, `_`, and `/`, and compared ordinal/case-sensitively.
- Session and presentation: `FlowSessionId`, `FlowContentDescriptor`, and
  `IFlowPresentationLease`.
- Lifecycle: `IFlowInitializable<TParameter>`, `IFlowActivation`,
  `FlowActivationContext`, and `FlowDeactivationContext`.
- Actions: `FlowAction`, `ActionRole`, `SemanticTone`, and `ActionPlacement`.
- Fault metadata: `FlowException`, `FlowFeature`, `FlowLifecycleStage`,
  `FlowRegistrationException`, `FlowValidationException`,
  `FlowLifecycleException`, `FlowCleanupException`, `FlowReentrancyException`, and
  `FlowPresenterException`.
- Stable diagnostic identities: `FlowDiagnosticIds`.
- Registration and validation: `FlowRegistrationKind`,
  `FlowRegistrationIdentity`, `FlowRegistrationLocation`, `FlowRegistration`,
  `FlowActivationScope`, `FlowRegistrationFactory<TViewModel>`,
  `FlowRegistryBuilder`, `FlowRegistrySnapshot`, `FlowRegistryValidationContext`,
  `FlowRegistryValidator`, `FlowRegistrationDiagnostic`,
  `FlowRegistrationDiagnosticSeverity`, and `FlowRegistrationDiagnosticSink`.
- Adapter mapping declarations: `FlowAdapterMappingKind`,
  `FlowAdapterMappingIdentity`, and `FlowAdapterMapping`.

## `WebUIToolkit.MVVM.Navigation`

- State and policy: `NavigationMode`, `NavigationRetention`,
  `NavigationConcurrency`, `NavigationResultKind`, `NavigationOptions`,
  `NavigationGuardResult`, and `NavigationGuardContext`.
- Immutable observations: `NavigationRouteDescriptor`,
  `NavigationEntrySnapshot`, `NavigationSnapshot`, `NavigationResult`, and
  `NavigationSnapshotChangedEventArgs`.
- Runtime boundaries: `INavigationService`, `NavigationService`, `INavigationGuard`,
  `INavigationRegionPresenter`, and `NavigationPresentationContext`.
- Closed registration: `NavigationRouteContent`, `NavigationRegionRegistration`,
  `NavigationRegistryBuilder`, and `NavigationRegistry`.

`INavigationService` starts configured root regions; navigates by closed ViewModel
signature or explicit route; performs Back and Clear with optional stale-session
checks; exposes the latest snapshot; and performs deterministic shutdown.

## `WebUIToolkit.MVVM.Dialogs`

- Typed outcome: `DialogOutcome<T>` and `DialogOutcomeKind`.
- Registration: `DialogRegistration<TViewModel,TRequest,TResult>`,
  `DialogRegistryBuilder`, `DialogRegistry`,
  `DialogContentFactory<TViewModel,TRequest,TResult>`, and
  `DialogContent<TViewModel>`.
- Presentation and completion: `DialogPresentation<TResult>`, `IDialogPresenter`,
  `DialogPresenterRegistry`, `IDialogController<TResult>`, `IDialogService`,
  `DialogService`, `IDialogShutdown`, and `IDialogChildOwner`.
- Close policy: `DialogCloseReason`, `DialogCloseGuardContext<TResult>`, and
  `IDialogCloseGuard<TResult>`.
- Failure with an accepted result: `DialogTeardownException<TResult>`.

The controller returns `true` only when its completion request wins and is accepted.
Outcome kind, not result nullability, defines Completed, Cancelled, or Dismissed.
`DialogService` accepts an optional `TimeProvider` and optional teardown timeout in
its constructor. `TeardownTimeout` exposes the validated overall bound; a null
constructor value selects the 30-second default rather than disabling the bound.

## `WebUIToolkit.MVVM.Operations`

- Execution: `OperationRequest`, `OperationId`, `OperationContext`,
  `IOperationRunner`, `OperationRunner`, and `OperationConcurrency`.
- Progress and monitoring: `OperationProgress`, `OperationSegment`,
  `OperationState`, `OperationCancellationReason`, `OperationSnapshot`,
  `IOperationMonitor`, and `OperationRunnerOptions`.
- Presentation: `IOperationPresenter` and `IOperationPresenterRegistry`.
- Results/faults: `OperationOutcome<T>`, `OperationOutcomeKind`, and
  `OperationBusyException`.

`RunAsync` preserves success values, cancellation, and original failures.
`TryRunAsync` returns an ordinary typed outcome. A monitor observes logical,
exception-free state and can request cooperative cancellation by `OperationId`.

## `WebUIToolkit.MVVM.Workflows`

- Graphs: `WorkflowDefinition<TContext,TResult>`,
  `WorkflowDefinitionBuilder<TContext,TResult>`,
  `WorkflowStepDefinition<TContext>`, `WorkflowEdge<TContext>`,
  `WorkflowStepActivation`, and `WorkflowStepRetention`.
- Validation and hooks: `WorkflowValidationResult`, `WorkflowValidationIssue`,
  `WorkflowValidationSeverity`, `IWorkflowStepValidator<TContext>`,
  `IWorkflowStepCommit<TContext>`, and `IWorkflowCancelGuard<TContext>`.
- Presentation and transition state: `IWorkflowPresenter`,
  `WorkflowPresentationContext`, `WorkflowPresentationReason`, `WorkflowSnapshot`,
  `WorkflowTransition<TResult>`, `WorkflowTransitionKind`,
  `WorkflowActionResult`, and `WorkflowActionResultKind`.
- Runtime: `WorkflowSession<TContext,TResult>`, `WorkflowActionKeys`, and
  `IWorkflowActionHandler<TContext>`. A session exposes Start, Next, GoTo, Back,
  action dispatch, Finish, guarded/bypassed Cancel, Abandon, snapshots, and terminal
  outcome. Its compatibility constructor uses system time and a 30-second teardown
  bound; an overload accepts explicit `TimeProvider` and `TimeSpan` values.
- Results/faults: `WorkflowOutcome<T>`, `WorkflowOutcomeKind`, and
  `WorkflowGraphException`.
- Checkpoints: `WorkflowCheckpointEnvelope`, `WorkflowCheckpointLimits`,
  `IWorkflowCheckpointCodec<TContext>`, `IWorkflowCheckpointStore`,
  `IWorkflowCheckpointMigration`, `WorkflowCheckpointRestoreValidator`,
  `WorkflowCheckpointRejection`, and `WorkflowCheckpointException`.

Workflow context and result types remain closed and typed. Checkpoint payload bytes
are opaque to the kernel and require an explicit consumer codec; reflection-based
serializer discovery is not a supported path.

## `WebUIToolkit.MVVM.Flow.Generators`

The separate `WebUIToolkit.MVVM.Flow.Generators` package is a `net10.0`,
Roslyn-independent contract kernel. Its public surface is:

- Input model: `FlowGeneratorInput`, `FlowGeneratorDeclaration`,
  `FlowGeneratorDeclarationKind`, `FlowGeneratorProperty`, and
  `FlowSourceLocation`.
- Output model: `FlowGenerationResult` and `FlowGeneratedSource`.
- Diagnostics: `FlowGeneratorDiagnostic`, `FlowGeneratorDiagnosticDescriptor`,
  `FlowGeneratorDiagnosticSeverity`, and `FlowGeneratorDiagnosticCatalog`.
- Kernel seam and text primitives: `IFlowGeneratorKernel.Generate` and
  `FlowDeterministicEmission`.

The Roslyn incremental-generator entry point is a future adapter because centrally
approved Roslyn package versions are not yet available.

## Compatibility commitments

Wave A contracts for keys, typed outcomes, session ownership, exact-once teardown,
cancellation, and `TimeProvider` behavior are frozen. Changing key equality,
lifecycle commit boundaries, outcome meaning, disposal ownership, exception
categories, default concurrency/retention, or checkpoint envelope meaning is a
breaking change. New overloads and optional bounded metadata may be additive.
