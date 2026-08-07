# RunicFlow

The frontend-neutral Flow foundation for .NET 10 provides validated logical
keys, typed outcomes, content descriptors and presentation leases, lifecycle
contracts, exact-once completion, deterministic clock helpers, and the
content-session ownership kernel.

Public feature APIs remain in the flat namespaces `RunicFlow.Navigation`,
`RunicFlow.Dialogs`, `RunicFlow.Operations`,
`RunicFlow.Workflows`, and `RunicFlow`.

`ObservableNavigationPresenter` publishes immutable logical-region outlets and
`ObservableDialogPresenter` publishes the active typed-dialog stack for .NET or
TypeScript presentation. Toolkit desktop lifetime mapping is owned by the
separate `RunicFlow.RunicToolkit` integration boundary, so this core package has
no UI-framework or Toolkit assembly dependency.

The project is licensed under the repository's MIT License. Package publication
still requires independent identity and release-readiness review.
