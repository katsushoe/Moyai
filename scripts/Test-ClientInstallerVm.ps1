[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$MsiPath, [string]$PreviousMsiPath = '')

# Run only in a disposable, clean Windows VM from an administrator PowerShell.
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator PowerShell is required.' }
if (Get-Service Moyai -ErrorAction SilentlyContinue) { throw 'Existing Moyai service found; use a clean disposable VM.' }
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msi, 0)
$view = $database.OpenView("SELECT Value FROM Property WHERE Property = 'ProductCode'")
[void]$view.Execute()
$record = $view.Fetch()
$productCode = $record.StringData(1)
[void]$view.Close()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
$run = Join-Path ([IO.Path]::GetTempPath()) ('Moyai-ClientMsi-' + [Guid]::NewGuid().ToString('N'))
$profilePath = Join-Path $run 'test user'
[void][IO.Directory]::CreateDirectory((Join-Path $profilePath '.codex'))
$codexPath = Join-Path $profilePath '.codex\config.toml'
$claudePath = Join-Path $profilePath '.claude.json'
[IO.File]::WriteAllText($codexPath, "# keep`nmodel = 'example'`n")
[IO.File]::WriteAllText($claudePath, '{"theme":"dark"}')
$checks = [Collections.Generic.List[string]]::new()
function Check([string]$Name, [bool]$Passed) {
    if (-not $Passed) { throw "Failed: $Name" }
    $checks.Add($Name)
}
function Invoke-Msi([string]$Action, [string]$Target, [string]$Name, [string[]]$Properties = @(), [int]$Expected = 0) {
    $arguments = @($Action, ('"' + $Target + '"'), '/qn', '/norestart', 'REBOOT=ReallySuppress', '/L*v', ('"' + (Join-Path $run ($Name + '.log')) + '"')) + $Properties
    $process = Start-Process msiexec.exe -WindowStyle Hidden -ArgumentList $arguments -Wait -PassThru
    Check $Name ($process.ExitCode -eq $Expected)
}
$properties = @('MOYAI_CODEX=1','MOYAI_CLAUDE=1',('MOYAI_CLIENT_PROFILE="' + $profilePath + '"'))
try {
    if ($PreviousMsiPath) {
        $previousMsi = (Resolve-Path -LiteralPath $PreviousMsiPath).Path
        Invoke-Msi '/i' $previousMsi 'install-previous' $properties
    }
    Invoke-Msi '/i' $msi 'install' $properties
    Check 'Codex registered' ([IO.File]::ReadAllText($codexPath).Contains('[mcp_servers.moyai]'))
    $json = [IO.File]::ReadAllText($claudePath) | ConvertFrom-Json
    Check 'Claude registered' ($json.mcpServers.moyai.type -eq 'http')
    Check 'Other Claude settings retained' ($json.theme -eq 'dark')
    Check 'Service running' ((Get-Service Moyai).Status -eq 'Running')
    Invoke-Msi '/fa' $msi 'repair'
    Invoke-Msi '/x' $productCode 'uninstall'
    Check 'Codex entry removed' (-not [IO.File]::ReadAllText($codexPath).Contains('[mcp_servers.moyai]'))
    Check 'Other Codex settings retained' ([IO.File]::ReadAllText($codexPath).Contains('example'))
    $json = [IO.File]::ReadAllText($claudePath) | ConvertFrom-Json
    Check 'Claude entry removed' ($null -eq $json.mcpServers.moyai)
    Check 'Transaction journals cleared' (@(Get-ChildItem (Join-Path $profilePath '.moyai') -Filter '*-pending.json').Count -eq 0)
    Check 'Service removed' ($null -eq (Get-Service Moyai -ErrorAction SilentlyContinue))

    # The first client succeeds, the malformed second client forces MSI rollback.
    $beforeCodex = [IO.File]::ReadAllBytes($codexPath)
    [IO.File]::WriteAllText($claudePath, '{invalid')
    Invoke-Msi '/i' $msi 'failed-install' $properties 1603
    Check 'MSI restored original Codex bytes' ([Convert]::ToBase64String($beforeCodex) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes($codexPath)))
    Check 'Invalid Claude input preserved' ([IO.File]::ReadAllText($claudePath) -eq '{invalid')
    Check 'Failed install removed service' ($null -eq (Get-Service Moyai -ErrorAction SilentlyContinue))
    @{passed=$true;checks=$checks;msiHash=(Get-FileHash $msi).Hash} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'result.json') -Encoding UTF8
    Write-Output "PASS: $($checks.Count) checks. Results: $run"
} catch {
    @{passed=$false;checks=$checks;error=$_.Exception.Message} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'result.json') -Encoding UTF8
    throw
}
