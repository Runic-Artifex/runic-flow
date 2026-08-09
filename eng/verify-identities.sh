#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

retired_paths="$(find . \( -path './.git' -o -name bin -o -name obj -o -name .packages \) \
  -prune -o -type f -print | sed 's#^\./##' | \
  grep -E 'RunicFlow\.(CommunityToolkit|Generators|RunicToolkit)|WebUI[T]oolkit\.MVVM\.Flow' || true)"
if [[ -n "$retired_paths" ]]; then
  echo "Retired Flow product paths remain:" >&2
  echo "$retired_paths" >&2
  exit 1
fi

core_project="src/RunicFlow/RunicFlow.csproj"
grep -Fq '<AssemblyName>RunicFlow</AssemblyName>' "$core_project"
grep -Fq '<RootNamespace>RunicFlow</RootNamespace>' "$core_project"
grep -Fq '<PackageId>RunicFlow</PackageId>' "$core_project"

integration_project="integrations/RunicFlow.ApplicationBridge/RunicFlow.ApplicationBridge.csproj"
grep -Fq '<AssemblyName>RunicFlow.ApplicationBridge</AssemblyName>' "$integration_project"
grep -Fq '<RootNamespace>RunicFlow.ApplicationBridge</RootNamespace>' "$integration_project"
grep -Fq '<PackageId>RunicFlow.ApplicationBridge</PackageId>' "$integration_project"

if git grep -n -E 'namespace RunicFlow\.(Navigation|Dialogs|Workflows|Presentation|Registration)' -- '*.cs'; then
  echo "Retired presentation-oriented namespaces remain in C# source." >&2
  exit 1
fi

echo "Runic Flow headless identity boundary verified."
