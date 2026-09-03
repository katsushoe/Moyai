[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ExecutablePath)

$ErrorActionPreference = 'Stop'
$cli = (Resolve-Path -LiteralPath $ExecutablePath).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Moyai-ClientCli-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$profilePath = Join-Path $testRoot 'test user'
[void][IO.Directory]::CreateDirectory($profilePath)
$configPath = Join-Path $testRoot 'moyai.json'
[IO.File]::WriteAllText($configPath, '{"databasePath":"unused.db","serverUrl":"http://127.0.0.1:43219"}')
$checks = 0
function Invoke-Registration([string[]]$Arguments) {
    $output = & $cli @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Client registration CLI failed.' }
    $value = $output | ConvertFrom-Json
    if (-not $value.ok) { throw 'Expected successful structured response.' }
    return $value
}
foreach ($client in @('codex', 'claude')) {
    $result = Invoke-Registration @('configure', $client, '--profile', $profilePath, '--config', $configPath, '--transaction', '--transaction-id', 'cli-test')
    if ($result.status -ne 'configured') { throw 'Expected configured state.' }; $checks++
    $path = if ($client -eq 'codex') { Join-Path $profilePath '.codex\config.toml' } else { Join-Path $profilePath '.claude.json' }
    if ([IO.File]::ReadAllText($path) -notmatch '43219/mcp') { throw 'Endpoint did not follow service configuration.' }; $checks++
    $null = Invoke-Registration @('client-transaction', $client, '--profile', $profilePath, '--phase', 'rollback', '--transaction-id', 'cli-test')
    if (Test-Path -LiteralPath $path) { throw 'Rollback did not remove newly created client configuration.' }; $checks++
    $null = Invoke-Registration @('configure', $client, '--profile', $profilePath, '--config', $configPath)
    $result = Invoke-Registration @('configure', $client, '--profile', $profilePath, '--config', $configPath)
    if ($result.status -ne 'unchanged') { throw 'Repeated configuration is not idempotent.' }; $checks++
    $result = Invoke-Registration @('unconfigure', $client, '--profile', $profilePath, '--config', (Join-Path $testRoot 'missing.json'))
    if ($result.status -ne 'unconfigured') { throw 'Unconfigure required a running service or configuration.' }; $checks++
}
if (Test-Path -LiteralPath (Join-Path $testRoot 'unused.db')) { throw 'Registration accessed the database.' }; $checks++
Write-Output "PASS: $checks client CLI checks. Temporary evidence: $testRoot"
