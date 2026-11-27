[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$WixCommand = "",
    [string]$ArtifactsDirectory = "",
    [switch]$SkipNuGetAudit
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Build.props") -Raw
    $Version = [string]$buildProperties.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "Directory.Build.props must specify Version." }
}
$artifactsRoot = if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) { Join-Path $repositoryRoot "artifacts" } else { [IO.Path]::GetFullPath($ArtifactsDirectory) }
$allowedArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
if ($artifactsRoot -ne $allowedArtifactsRoot -and -not $artifactsRoot.StartsWith($allowedArtifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactsDirectory must be within this repository's artifacts directory."
}
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
$deliveryManifest = Join-Path $artifactsRoot 'delivery-inputs.json'
if (Test-Path -LiteralPath $deliveryManifest) {
    & (Join-Path $PSScriptRoot 'Test-DeliveryManifest.ps1') -ManifestPath $deliveryManifest -RepositoryRoot $repositoryRoot | Out-Null
} else {
    & (Join-Path $PSScriptRoot 'New-DeliveryManifest.ps1') -OutputPath $deliveryManifest -RepositoryRoot $repositoryRoot | Out-Null
}
$publishDirectory = Join-Path $artifactsRoot "publish\win-x64"
$installerDirectory = Join-Path $artifactsRoot "installer"
$clientSetupDirectory = Join-Path $artifactsRoot "client-setup"
$ownerActionsDirectory = Join-Path $artifactsRoot 'installer-actions'
$wixSource = Join-Path $repositoryRoot "installer\Moyai.wxs"
$msiPath = Join-Path $installerDirectory "Moyai-$Version-x64.msi"
$parsedVersion = [Version]::Parse($Version)
$buildVersion = [Math]::Max(0, $parsedVersion.Build)
$revisionVersion = [Math]::Max(0, $parsedVersion.Revision)
$assemblyVersion = "$($parsedVersion.Major).$($parsedVersion.Minor).$buildVersion.$revisionVersion"
$msiVersion = & (Join-Path $PSScriptRoot 'Get-MsiProductVersion.ps1') -Version $Version

if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublishDirectory = (Resolve-Path -LiteralPath $publishDirectory).Path
    if ($resolvedPublishDirectory -ne [IO.Path]::GetFullPath($publishDirectory)) { throw "Unexpected publish directory resolution." }
    $taskDirectory = Get-Item -LiteralPath $resolvedPublishDirectory
    while ($taskDirectory.FullName -ne $repositoryRoot) {
        if (($taskDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing to clear artifacts through a reparse point." }
        $taskDirectory = $taskDirectory.Parent
        if ($null -eq $taskDirectory) { throw "Publish directory is outside the repository." }
    }
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

$publishProperties = @(
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:FileVersion=$assemblyVersion",
    "-p:PublishSingleFile=false",
    "--disable-build-servers",
    "-m:1",
    "-p:UseSharedCompilation=false",
    "-p:NuGetAudit=$((-not $SkipNuGetAudit).ToString().ToLowerInvariant())"
)

& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Cli\Moyai.Cli.csproj") @publishProperties --output $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "Moyai.Cli publish failed." }

& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Mcp\Moyai.Mcp.csproj") @publishProperties --output (Join-Path $publishDirectory 'host')
if ($LASTEXITCODE -ne 0) { throw "Moyai.Mcp publish failed." }

& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Tray\Moyai.Tray.csproj") @publishProperties --output (Join-Path $publishDirectory 'tray')
if ($LASTEXITCODE -ne 0) { throw "Moyai.Tray publish failed." }

# Embedded self-contained helper remains executable during uninstall commit/rollback.
& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Cli\Moyai.Cli.csproj") `
    --configuration Release --runtime win-x64 --self-contained true --output $clientSetupDirectory `
    "-p:Version=$Version" "-p:AssemblyVersion=$assemblyVersion" "-p:FileVersion=$assemblyVersion" `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --disable-build-servers -m:1 -p:UseSharedCompilation=false "-p:NuGetAudit=$((-not $SkipNuGetAudit).ToString().ToLowerInvariant())"
if ($LASTEXITCODE -ne 0) { throw "Client registration helper publish failed." }
$clientSetupExecutable = Join-Path $clientSetupDirectory "moyaictl.exe"

& dotnet build (Join-Path $repositoryRoot 'src/Moyai.InstallerActions/Moyai.InstallerActions.csproj') `
    --configuration Release --output $ownerActionsDirectory --disable-build-servers -m:1 -p:Platform=x64 `
    "-p:Version=$Version" "-p:AssemblyVersion=$assemblyVersion" "-p:FileVersion=$assemblyVersion" `
    "-p:NuGetAudit=$((-not $SkipNuGetAudit).ToString().ToLowerInvariant())"
if ($LASTEXITCODE -ne 0) { throw 'Configuration owner custom action build failed.' }
$ownerActions = Join-Path $ownerActionsDirectory 'Moyai.InstallerActions.CA.dll'
if (-not (Test-Path -LiteralPath $ownerActions)) { throw 'Configuration owner native custom action package missing.' }
$ownerActionBytes = [IO.File]::ReadAllBytes($ownerActions)
$ownerPeOffset = [BitConverter]::ToInt32($ownerActionBytes, 0x3c)
if ([BitConverter]::ToUInt16($ownerActionBytes, $ownerPeOffset + 4) -ne 0x8664) {
    throw 'Configuration owner native custom action must target x64.'
}

foreach ($executable in @((Join-Path $publishDirectory 'moyaictl.exe'), (Join-Path $publishDirectory 'moyaimcp.exe'), $clientSetupExecutable)) {
    & $executable --version
    if ($LASTEXITCODE -ne 0) { throw "Published CLI cannot start: $executable" }
}

if ([string]::IsNullOrWhiteSpace($WixCommand)) {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "WiX Toolset restore failed." }

    & dotnet tool run wix -- extension add WixToolset.Util.wixext/5.0.2
    if ($LASTEXITCODE -ne 0) { throw 'Pinned WiX utility extension restore failed.' }

    & dotnet tool run wix -- build $wixSource -v `
        -arch x64 `
        -d "MoyaiVersion=$msiVersion" `
        -d "PublishDirectory=$publishDirectory" `
        -d "ClientSetupExecutable=$clientSetupExecutable" `
        -d "ConfigOwnerActions=$ownerActions" `
        -ext WixToolset.Util.wixext `
        -o $msiPath
}
else {
    & $WixCommand extension add WixToolset.Util.wixext/5.0.2
    if ($LASTEXITCODE -ne 0) { throw 'Pinned WiX utility extension restore failed.' }
    & $WixCommand build $wixSource -v `
        -arch x64 `
        -d "MoyaiVersion=$msiVersion" `
        -d "PublishDirectory=$publishDirectory" `
        -d "ClientSetupExecutable=$clientSetupExecutable" `
        -d "ConfigOwnerActions=$ownerActions" `
        -ext WixToolset.Util.wixext `
        -o $msiPath
}
if ($LASTEXITCODE -ne 0) { throw "MSI build failed with exit code $LASTEXITCODE." }

$hash = Get-FileHash -LiteralPath $msiPath -Algorithm SHA256
$inputVerification = & (Join-Path $PSScriptRoot 'Test-DeliveryManifest.ps1') -ManifestPath $deliveryManifest -RepositoryRoot $repositoryRoot
[ordered]@{version=$Version;inputManifestSha256=$inputVerification.sha256;inputFiles=$inputVerification.files;msiSha256=$hash.Hash;gitStateComplete=$false;installed=$false;published=$false} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsRoot 'package-source-link.json') -Encoding utf8
Write-Output $msiPath
Write-Output "SHA256: $($hash.Hash)"
