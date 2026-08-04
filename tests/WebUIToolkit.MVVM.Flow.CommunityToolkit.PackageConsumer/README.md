# Flow CommunityToolkit packed-package consumer

This executable proves the public schema-v1 Flow projection against actual
CommunityToolkit-generated `Title` and asynchronous `SubmitCommand` members.

`Test-PackageConsumer.ps1` packs Flow and its CommunityToolkit integration at the
same local version, seeds an isolated package feed with the cached exact
CommunityToolkit.Mvvm 8.4.2 package, and clears every other source. The consumer
does not declare CommunityToolkit directly. The script validates the packed
dependency metadata, restores the packages into an empty isolated cache without
a project-reference fallback, and executes property validation plus async
running/cancellation behavior.
