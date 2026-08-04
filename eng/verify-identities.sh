#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

retired_content_pattern='WebUI[T]oolkit\.MVVM\.(Flow|Navigation|Dialogs|Operations|Workflows)|webui[t]oolkit\.mvvm\.flow|WUT[F]LOW'
if git grep -n -E "$retired_content_pattern" -- .; then
  echo "Retired Toolkit-owned Flow identities remain in tracked content." >&2
  exit 1
fi

retired_paths="$(find . \( -path './.git' -o -name bin -o -name obj -o -name .packages \) \
  -prune -o -type f -print | sed 's#^\./##' | \
  grep -E 'WebUI[T]oolkit\.MVVM\.Flow|webui[t]oolkit\.mvvm\.flow' || true)"
if [[ -n "$retired_paths" ]]; then
  echo "Retired Toolkit-owned Flow identities remain in tracked paths:" >&2
  echo "$retired_paths" >&2
  exit 1
fi

for project in RunicFlow RunicFlow.Generators RunicFlow.CommunityToolkit; do
  project_file="src/$project/$project.csproj"
  grep -Fq "<AssemblyName>$project</AssemblyName>" "$project_file"
  grep -Fq "<RootNamespace>$project</RootNamespace>" "$project_file"
  grep -Fq "<PackageId>$project</PackageId>" "$project_file"
done

protocol_matches="$(grep -R -l -F --exclude-dir=bin --exclude-dir=obj \
  'runic.flow.communitytoolkit/1' src tests | wc -l)"
if [[ "$protocol_matches" -lt 3 ]]; then
  echo "Runic Flow CommunityToolkit protocol identity is not consistently represented." >&2
  exit 1
fi

echo "Runic Flow identity boundary verified."
