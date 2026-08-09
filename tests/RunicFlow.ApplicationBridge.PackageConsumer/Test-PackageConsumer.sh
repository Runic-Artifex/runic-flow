#!/usr/bin/env bash
set -euo pipefail

repository_root="$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)"
package_version="${1:?Package version is required.}"
package_feed="${2:?Package feed is required.}"
runtime_identifier="${3:-linux-x64}"
fixture_root="${repository_root}/tests/RunicFlow.ApplicationBridge.PackageConsumer"
consumer="${repository_root}/tests/RunicFlow.AotSmoke/RunicFlow.AotSmoke.csproj"
temporary_root="$(mktemp -d)"
packages="${temporary_root}/packages"
publish="${temporary_root}/publish"
nuget_config="${temporary_root}/NuGet.config"
mkdir -p "${packages}" "${publish}"
trap 'rm -rf -- "${temporary_root}"' EXIT

sed "s|__LOCAL_FEED__|${package_feed}|g" \
  "${fixture_root}/NuGet.config.template" > "${nuget_config}"

dotnet restore "${consumer}" --nologo --configfile "${nuget_config}" \
  --runtime "${runtime_identifier}" --no-cache -m:1 --disable-parallel \
  -p:RunicFlowPackageVersion="${package_version}" \
  -p:RestorePackagesPath="${packages}" \
  -p:NuGetAudit=false -p:PublishAot=true -p:PublishTrimmed=true

dotnet publish "${consumer}" --configuration Release --nologo \
  --runtime "${runtime_identifier}" --self-contained true --no-restore \
  --output "${publish}" \
  -p:RunicFlowPackageVersion="${package_version}" \
  -p:RestorePackagesPath="${packages}" \
  -p:PublishAot=true -p:PublishTrimmed=true -p:TrimMode=full \
  -p:IlcTreatWarningsAsErrors=true

native_executable="${publish}/RunicFlow.AotSmoke"
[[ -x "${native_executable}" ]] || {
  echo "NativeAOT package consumer was not produced: ${native_executable}" >&2
  exit 1
}
"${native_executable}"

echo "Runic Flow/Application Bridge isolated NativeAOT package consumer passed."
