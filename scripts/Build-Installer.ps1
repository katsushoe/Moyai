[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$WixCommand = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$publishDirectory = Join-Path $artifactsRoot "publish\win-x64"
$installerDirectory = Join-Path $artifactsRoot "installer"
$wixSource = Join-Path $repositoryRoot "installer\Moyai.wxs"
$msiPath = Join-Path $installerDirectory "Moyai-$Version-x64.msi"

if (Test-Path -LiteralPath $publishDirectory) {
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
    "-p:PublishSingleFile=false",
    "--disable-build-servers"
)

& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Cli\Moyai.Cli.csproj") @publishProperties
if ($LASTEXITCODE -ne 0) { throw "Moyai.Cli publish failed." }

& dotnet publish (Join-Path $repositoryRoot "src\Moyai.Mcp\Moyai.Mcp.csproj") @publishProperties
if ($LASTEXITCODE -ne 0) { throw "Moyai.Mcp publish failed." }

if ([string]::IsNullOrWhiteSpace($WixCommand)) {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "WiX Toolset restore failed." }

    & dotnet tool run wix -- build $wixSource `
        -arch x64 `
        -d "MoyaiVersion=$Version" `
        -d "PublishDirectory=$publishDirectory" `
        -o $msiPath
}
else {
    & $WixCommand build $wixSource `
        -arch x64 `
        -d "MoyaiVersion=$Version" `
        -d "PublishDirectory=$publishDirectory" `
        -o $msiPath
}
if ($LASTEXITCODE -ne 0) { throw "MSI build failed." }

$hash = Get-FileHash -LiteralPath $msiPath -Algorithm SHA256
Write-Output $msiPath
Write-Output "SHA256: $($hash.Hash)"
