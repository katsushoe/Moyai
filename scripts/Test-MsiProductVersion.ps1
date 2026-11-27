[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$convert = Join-Path $PSScriptRoot 'Get-MsiProductVersion.ps1'
$cases = @{
    '2.0.0.0' = '2.0.0'
    '2.0.0.1' = '2.0.1'
    '2.0.0.99' = '2.0.99'
    '2.0.1.0' = '2.0.100'
    '1.3.14.0' = '1.3.1400'
    '255.255.655.35' = '255.255.65535'
}
foreach ($version in $cases.Keys) {
    $actual = & $convert -Version $version
    if ($actual -ne $cases[$version]) { throw "Version mapping failed: $version" }
}
foreach ($invalid in @('2.0.0.100','2.0.655.36','256.0.0.0','2.256.0.0','not-a-version')) {
    $rejected = $false
    try { $null = & $convert -Version $invalid } catch { $rejected = $true }
    if (-not $rejected) { throw "Invalid MSI version accepted: $invalid" }
}
if ([Version](& $convert -Version '2.0.0.99') -ge [Version](& $convert -Version '2.0.1.0')) { throw 'Version mapping is not monotonic.' }
[ordered]@{ ok = $true; passed = 12; productVersion = '2.0.0.1'; msiVersion = (& $convert -Version '2.0.0.1') } | ConvertTo-Json
