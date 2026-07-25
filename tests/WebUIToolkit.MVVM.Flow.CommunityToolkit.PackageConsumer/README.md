# Flow CommunityToolkit packed-package consumer

This executable proves the public schema-v1 Flow projection against actual
CommunityToolkit-generated `Title` and asynchronous `SubmitCommand` members.

`Test-PackageConsumer.ps1` packs Flow and its CommunityToolkit integration at the
same local version, seeds an isolated package feed with the cached exact
CommunityToolkit.Mvvm 8.4.2 package, and clears every other source. The consumer
does not declare CommunityToolkit directly: its committed package-mode lock
proves the dependency is supplied by the packed integration. The script validates
that graph, normalizes newly packed local artifacts to deterministic bytes,
checks their SHA-512 content hashes against the committed lock, and directly
replays that lock in an empty package cache without a project-reference fallback.
It then executes property validation plus async running/cancellation behavior.

After an intentional package-graph change, regenerate the fixture lock with
`-UpdateLock`, review it, and rerun without that switch to prove locked replay.
