[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ExecutablePath)

$ErrorActionPreference = 'Stop'
$cli = (Resolve-Path -LiteralPath $ExecutablePath).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Moyai-ClientCli-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$checks = 0
# Library tests cover registration, ownership and rollback in temporary profiles.
# The CLI itself must reject any profile other than the signed-in user's profile.
foreach ($client in @('codex', 'claude')) {
    foreach ($command in @('configure', 'unconfigure', 'client-transaction')) {
        $arguments = @($command, $client, '--profile', $testRoot)
        if ($command -eq 'client-transaction') { $arguments += @('--phase', 'rollback') }
        $output = & $cli @arguments 2>&1
        if ($LASTEXITCODE -ne 1) { throw 'A foreign profile was not rejected.' }
        $response = ($output -join "`n") | ConvertFrom-Json
        if ($response.ok -or $response.error.message -notmatch "current user's profile") { throw 'Unexpected profile rejection.' }
        $checks++
    }
}
if (@(Get-ChildItem -LiteralPath $testRoot -Force).Count -ne 0) { throw 'Profile rejection wrote files.' }
$checks++
Write-Output "PASS: $checks isolated client CLI profile-boundary checks. Temporary evidence: $testRoot"
