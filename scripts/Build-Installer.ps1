[CmdletBinding()]
param(
    [string]$Version = "1.2.1",
    [string]$WixCommand = "",
    [string]$ArtifactsDirectory = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) { Join-Path $repositoryRoot "artifacts" } else { [IO.Path]::GetFullPath($ArtifactsDirectory) }
$allowedArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
if ($artifactsRoot -ne $allowedArtifactsRoot -and -not $artifactsRoot.StartsWith($allowedArtifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactsDirectory must be within this repository's artifacts directory."
}
$publishDirectory = Join-Path $artifactsRoot "publish\win-x64"
$installerDirectory = Join-Path $artifactsRoot "installer"
$clientSetupDirectory = Join-Path $artifactsRoot "client-setup"
$wixSource = Join-Path $repositoryRoot "installer\Moyai.wxs"
$msiPath = Join-Path $installerDirectory "Moyai-$Version-x64.msi"
$parsedVersion = [Version]::Parse($Version)
$buildVersion = [Math]::Max(0, $parsedVersion.Build)
$revisionVersion = [Math]::Max(0, $parsedVersion.Revision)
$assemblyVersion = "$($parsedVersion.Major).$($parsedVersion.Minor).$buildVersion.$revisionVersion"

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
    "--output", $publishDirectory,
    "-p:Version=$Version",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:FileVersion=$assemblyVersion",
    "-p:PublishSingleFile=false",
    "--disable-build-servers"
)

& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Cli\Moyai.Cli.csproj") @publishProperties
if ($LASTEXITCODE -ne 0) { throw "Moyai.Cli publish failed." }

& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Mcp\Moyai.Mcp.csproj") @publishProperties
if ($LASTEXITCODE -ne 0) { throw "Moyai.Mcp publish failed." }

# Embedded self-contained helper remains executable during uninstall commit/rollback.
& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Cli\Moyai.Cli.csproj") `
    --configuration Release --runtime win-x64 --self-contained true --output $clientSetupDirectory `
    "-p:Version=$Version" "-p:AssemblyVersion=$assemblyVersion" "-p:FileVersion=$assemblyVersion" `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw "Client registration helper publish failed." }
$clientSetupExecutable = Join-Path $clientSetupDirectory "moyaictl.exe"

if ([string]::IsNullOrWhiteSpace($WixCommand)) {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "WiX Toolset restore failed." }

    & dotnet tool run wix -- build $wixSource `
        -arch x64 `
        -d "MoyaiVersion=$Version" `
        -d "PublishDirectory=$publishDirectory" `
        -d "ClientSetupExecutable=$clientSetupExecutable" `
        -o $msiPath
}
else {
    & $WixCommand build $wixSource `
        -arch x64 `
        -d "MoyaiVersion=$Version" `
        -d "PublishDirectory=$publishDirectory" `
        -d "ClientSetupExecutable=$clientSetupExecutable" `
        -o $msiPath
}
if ($LASTEXITCODE -ne 0) { throw "MSI build failed." }

$hash = Get-FileHash -LiteralPath $msiPath -Algorithm SHA256
Write-Output $msiPath
Write-Output "SHA256: $($hash.Hash)"
