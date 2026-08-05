# Releasing Runic Flow

The `Public release` workflow builds, consumes, and validates the four NuGet
packages as one independently versioned family. Runic Flow owns its Runic
Toolkit integration package. Verify-only dispatches are safe on any branch;
publication is accepted only from `main`, after the exact `PUBLISH PUBLIC`
confirmation and the `public-release` environment's `main` deployment policy.
Add a required reviewer when the repository becomes public.

Before the first public release, complete the product documentation, make this
repository public, and create NuGet trusted-publisher policies for owner
`Runic-Artifex`, repository `runic-flow`, workflow `public-release.yml`, and
environment `public-release`. The environment variable `NUGET_USER` must name
the nuget.org account.
