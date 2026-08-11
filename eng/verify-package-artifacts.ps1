param(
    [Parameter(Mandatory)][string]$PackageVersion,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$RepositoryCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryUrl = "https://github.com/Runic-Artifex/runic-flow"
$expectedPackages = [ordered]@{
    "RunicFlow" = @{}
    "RunicFlow.ApplicationBridge" = @{
        "RunicFlow" = $PackageVersion
        "RunicToolkit.ApplicationBridge" = "[0.1.0-preview.27.1]"
    }
}

function Read-Nuspec {
    param([Parameter(Mandatory)][string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName.EndsWith(".nuspec") })
        if ($entries.Count -ne 1) { throw "Expected one nuspec in '$Path'." }
        $reader = [System.IO.StreamReader]::new($entries[0].Open())
        try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Read-RequiredMetadataValue {
    param([xml]$Document, [string]$Name, [string]$PackagePath)
    $node = $Document.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='$Name']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Package '$PackagePath' is missing required '$Name' metadata."
    }
    return $node.InnerText
}

$resolvedDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$actualPackages = @(Get-ChildItem -LiteralPath $resolvedDirectory -File -Filter "*.nupkg")
if ($actualPackages.Count -ne $expectedPackages.Count) {
    throw "Expected $($expectedPackages.Count) packages, found $($actualPackages.Count)."
}

foreach ($packageId in $expectedPackages.Keys) {
    $packagePath = Join-Path $resolvedDirectory "$packageId.$PackageVersion.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Expected package was not produced: $packagePath"
    }
    $document = Read-Nuspec -Path $packagePath
    if ((Read-RequiredMetadataValue $document "id" $packagePath) -ne $packageId -or
        (Read-RequiredMetadataValue $document "version" $packagePath) -ne $PackageVersion -or
        (Read-RequiredMetadataValue $document "license" $packagePath) -ne "MIT") {
        throw "Package '$packagePath' has invalid identity, version, or license metadata."
    }
    $repository = $document.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='repository']")
    if ($null -eq $repository -or $repository.GetAttribute("type") -ne "git" -or
        $repository.GetAttribute("url") -ne $repositoryUrl -or
        $repository.GetAttribute("commit") -ne $RepositoryCommit) {
        throw "Package '$packagePath' does not contain the expected repository provenance."
    }
    $actualDependencies = @($document.SelectNodes("//*[local-name()='dependency']"))
    $expectedDependencies = $expectedPackages[$packageId]
    if ($actualDependencies.Count -ne $expectedDependencies.Count) {
        throw "Unexpected dependency count in '$packagePath'."
    }
    foreach ($dependency in $actualDependencies) {
        $id = $dependency.GetAttribute("id")
        $version = $dependency.GetAttribute("version")
        if (-not $expectedDependencies.ContainsKey($id) -or $expectedDependencies[$id] -ne $version) {
            throw "Unexpected dependency '$id' version '$version' in '$packagePath'."
        }
    }
}

Write-Host "Verified $($expectedPackages.Count) Runic Flow package artifacts for $PackageVersion."
