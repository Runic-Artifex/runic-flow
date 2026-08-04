#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

solution="RunicFlow.slnx"
configuration="Release"

case "$(uname -s):$(uname -m)" in
  Linux:x86_64) runtime_identifier="linux-x64" ;;
  Linux:aarch64) runtime_identifier="linux-arm64" ;;
  *)
    echo "NativeAOT verification does not yet define a runtime for $(uname -s):$(uname -m)." >&2
    exit 1
    ;;
esac

verification_tmp="$(mktemp -d /tmp/runic-flow.XXXXXXXXXX)"
cleanup() {
  case "$verification_tmp" in
    /tmp/runic-flow.*) rm -rf -- "$verification_tmp" ;;
    *) echo "Refusing to remove unexpected verification path: $verification_tmp" >&2 ;;
  esac
}
trap cleanup EXIT

./eng/verify-identities.sh

dotnet restore "$solution"
dotnet build "$solution" --configuration "$configuration" --no-restore

dotnet run \
  --project tests/RunicFlow.Tests/RunicFlow.Tests.csproj \
  --configuration "$configuration" \
  --no-build

dotnet run \
  --project tests/RunicFlow.CommunityToolkit.Tests/RunicFlow.CommunityToolkit.Tests.csproj \
  --configuration "$configuration" \
  --no-build

pwsh -NoProfile \
  -File tests/RunicFlow.CommunityToolkit.PackageConsumer/Test-PackageConsumer.ps1 \
  -Configuration "$configuration" \
  -SkipNativeAot

dotnet restore \
  tests/RunicFlow.AotSmoke/RunicFlow.AotSmoke.csproj \
  --runtime "$runtime_identifier" \
  -p:PublishAot=true \
  -p:PublishTrimmed=true

dotnet publish \
  tests/RunicFlow.AotSmoke/RunicFlow.AotSmoke.csproj \
  --configuration "$configuration" \
  --runtime "$runtime_identifier" \
  --no-restore \
  -p:PublishAot=true \
  -p:PublishTrimmed=true \
  --output "$verification_tmp/aot"

"$verification_tmp/aot/RunicFlow.AotSmoke"
