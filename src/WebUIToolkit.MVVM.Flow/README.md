# WebUIToolkit.MVVM.Flow

The frontend-neutral Flow foundation for .NET 10 provides validated logical
keys, typed outcomes, content descriptors and presentation leases, lifecycle
contracts, exact-once completion, deterministic clock helpers, and the
content-session ownership kernel.

Public feature APIs remain in the flat namespaces `WebUIToolkit.MVVM.Navigation`,
`WebUIToolkit.MVVM.Dialogs`, `WebUIToolkit.MVVM.Operations`,
`WebUIToolkit.MVVM.Workflows`, and `WebUIToolkit.MVVM.Flow`.

`ObservableNavigationPresenter` publishes immutable logical-region outlets and
`ObservableDialogPresenter` publishes the active typed-dialog stack for cwhtml
or TypeScript presentation. `NavigationDesktopCloseGuard` maps the existing
current-page guards onto `IDesktopApplicationLifetime`; migrated applications
therefore do not need a second navigation, dialog, or close-guard abstraction.

Publication remains subject to the repository's pending license decision.
