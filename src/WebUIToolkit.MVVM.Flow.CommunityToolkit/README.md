# WebUIToolkit.MVVM.Flow.CommunityToolkit

This package is the Wave C CommunityToolkit integration for Flow. It consumes the
accepted CommunityToolkit.Mvvm 8.4.2 generated-member handoff as a closed schema-v1
projection:

| Producer proof | Flow projection | Member |
| --- | --- | --- |
| `communitytoolkit.generated-member.title.v1` | `flow.projection.communitytoolkit.title.v1` | `101` / generated `Title` |
| `communitytoolkit.generated-member.submit-command.v1` | `flow.projection.communitytoolkit.submit-command.v1` | `102` / generated `SubmitCommand` |

`CommunityToolkitFlowProjection<TViewModel>` accepts direct delegates to those two
generated members. It performs no reflection, assembly scanning, `dynamic`, or
string-based member resolution. The adapter preserves Flow session authority,
projects bounded validation and command state, forwards cancellation to
`IAsyncRelayCommand`, and owns its producer subscriptions until exact-once async
disposal.

The framework-neutral Flow package does not reference CommunityToolkit. This
integration package alone carries the exact CommunityToolkit.Mvvm `[8.4.2]`
dependency and preserves its generated-member build assets for package consumers.
