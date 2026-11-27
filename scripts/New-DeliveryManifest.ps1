[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath,[string]$RepositoryRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'DeliveryManifest.psm1') -Force
if(-not [IO.Path]::IsPathRooted($OutputPath)){throw 'Absolute manifest output required.'}
if(Test-Path -LiteralPath $OutputPath){throw 'Manifest exists; preserve it and use Test-DeliveryManifest.ps1.'}
$files=@(Get-MoyaiDeliveryInputs -RepositoryRoot $RepositoryRoot)
$version=$null;$props=Join-Path $RepositoryRoot 'Directory.Build.props'
if(Test-Path -LiteralPath $props){[xml]$xml=Get-Content -LiteralPath $props -Raw;$version=[string]$xml.Project.PropertyGroup.Version}
$manifest=[ordered]@{schemaVersion=1;version=$version;createdAtUtc=[DateTimeOffset]::UtcNow.ToString('o');gitStateComplete=$false;gitStateNote='Input inventory is not a Git tracking or staging inventory; require complete Provider status before commit.';files=$files}
$parent=Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$stream=[IO.File]::Open($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false));try{$writer.Write(($manifest | ConvertTo-Json -Depth 6));$writer.Flush()}finally{$writer.Dispose()}}finally{$stream.Dispose()}
[pscustomobject]@{files=$files.Count;sha256=(Get-FileHash -LiteralPath $OutputPath).Hash;gitStateComplete=$false}
