# Releasing Runic Flow

The `Public release` workflow builds, consumes, and validates two NuGet packages
as one versioned family: `RunicFlow` and `RunicFlow.ApplicationBridge`.

Verify-only dispatches are safe on any branch. Publication is accepted only from
`main`, after the exact `PUBLISH PUBLIC` confirmation and the `public-release`
environment policy.

Before the first public release:

1. Publish a guarded GitHub Packages prerelease.
2. Migrate the package-only Setup reference host to that exact Flow version and
   confirm its existing Application Bridge contract and Svelte frontend remain
   unchanged.
3. Run `./eng/verify.sh`, the prerelease package consumer, and the public artifact
   validator from a clean clone.
4. Ensure NuGet trusted-publisher policies target owner `Runic-Artifex`, repository
   `runic-flow`, workflow `public-release.yml`, and environment `public-release`.

The environment variable `NUGET_USER` must name the nuget.org account. Do not
publish the retired MVVM-oriented package family or compatibility aliases.
