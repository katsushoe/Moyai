[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManifestPath,[string]$RepositoryRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'DeliveryManifest.psm1') -Force
$manifest=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$count=Test-MoyaiDeliveryInputs -RepositoryRoot $RepositoryRoot -Manifest $manifest
[pscustomobject]@{ok=$true;files=$count;sha256=(Get-FileHash -LiteralPath $ManifestPath).Hash;gitStateComplete=$false}
