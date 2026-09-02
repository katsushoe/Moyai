[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [Parameter(Mandatory = $true)][string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
$resolvedMsi = (Resolve-Path -LiteralPath $MsiPath).Path
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($resolvedMsi, 0)

function Read-MsiRows([string]$Query) {
    $view = $database.OpenView($Query)
    try {
        [void]$view.Execute()
        while ($null -ne ($record = $view.Fetch())) {
            try {
                $values = @()
                $fieldCount = $record.GetType().InvokeMember('FieldCount', [Reflection.BindingFlags]::GetProperty, $null, $record, $null)
                for ($index = 1; $index -le $fieldCount; $index++) { $values += $record.StringData($index) }
                ,$values
            }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    }
    finally {
        [void]$view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

try {
    $versionRows = @(Read-MsiRows "SELECT Value FROM Property WHERE Property = 'ProductVersion'")
    $packageVersion = $versionRows[0][0]
    $executableVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedExecutable).FileVersion
    if ($executableVersion -ne ($packageVersion + '.0')) { throw 'MSI and executable versions do not match.' }
    $service = @(Read-MsiRows 'SELECT * FROM ServiceInstall')
    if ($service.Count -ne 1) { throw 'Expected exactly one service.' }
    $row = $service[0]
    if ($row[1] -ne 'Moyai' -or $row[3] -ne '16' -or $row[4] -ne '2' -or $row[8] -ne 'NT AUTHORITY\LocalService') { throw 'Unexpected service type, startup or account.' }
    if ($row[5] -ne '32769') { throw 'Service startup is not vital.' }
    if ($row[10] -ne '--MOYAI_DB_PATH "[DataFolder]moyai.db" --MOYAI_MCP_URL "[MOYAI_MCP_URL]"') { throw 'Unexpected service configuration arguments.' }
    $control = @(Read-MsiRows 'SELECT * FROM ServiceControl')
    if ($control.Count -ne 1 -or $control[0][2] -ne '163' -or $control[0][4] -ne '1') { throw 'Incorrect service start/stop/delete control.' }
    $files = @(Read-MsiRows 'SELECT * FROM File')
    if (@($files | Where-Object { $_[2] -match '(^|\|)Moyai\.Mcp\.exe$' }).Count -ne 1) { throw 'MCP executable missing or duplicated.' }
    $permissions = @(Read-MsiRows 'SELECT * FROM MsiLockPermissionsEx')
    if (@($permissions | Where-Object { $_[3] -like '*;;;LS)*' }).Count -ne 2) { throw 'Expected LocalService access on data and logs.' }
    Write-Output 'PASS: MSI service, account, startup, stop/delete, executable and permissions tables.'
}
finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

# Only a temporary database and an ephemeral loopback port are used. No service is installed.
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = $probe.LocalEndpoint.Port
$probe.Stop()
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Moyai-ServicePackage-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $testDirectory)
$testDatabase = Join-Path $testDirectory 'moyai.db'
$startInfo = [Diagnostics.ProcessStartInfo]::new($resolvedExecutable)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WorkingDirectory = $testDirectory
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.EnvironmentVariables['MOYAI_DB_PATH'] = Join-Path $testDirectory 'must-not-create.db'
$startInfo.EnvironmentVariables['MOYAI_MCP_URL'] = 'invalid-environment-value'
$startInfo.Arguments = '--MOYAI_DB_PATH "' + $testDatabase + '" --MOYAI_MCP_URL "http://127.0.0.1:' + $port + '"'
$process = [Diagnostics.Process]::Start($startInfo)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
try {
    $body = '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_version","arguments":{}}}'
    $response = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($process.HasExited) { throw "MCP exited: $($stderr.GetAwaiter().GetResult())" }
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port/mcp" -Method Post -ContentType 'application/json' -Headers @{ Accept = 'application/json, text/event-stream' } -Body $body -TimeoutSec 2
            break
        }
        catch [Net.WebException] { if ($attempt -eq 39) { throw }; Start-Sleep -Milliseconds 250 }
    }
    $json = ($response.Content -split "`n" | Where-Object { $_.StartsWith('data: ') } | Select-Object -First 1)
    if ($json) { $json = $json.Substring(6) } else { $json = $response.Content }
    $result = $json | ConvertFrom-Json
    if ($result.error -or $result.result.isError -or -not $result.result.content) { throw 'get_version did not return a successful MCP result.' }
    if (($result.result.content | ConvertTo-Json -Depth 10) -notmatch [Regex]::Escape($executableVersion)) { throw 'MCP get_version does not match the executable version.' }
    if (-not (Test-Path -LiteralPath $testDatabase) -or (Test-Path -LiteralPath (Join-Path $testDirectory 'must-not-create.db'))) { throw 'DB argument precedence failed.' }
    Write-Output 'PASS: Published MCP HTTP response, loopback startup, command-line precedence and isolated DB creation.'
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    Write-Verbose $stdout.GetAwaiter().GetResult()
    Write-Verbose $stderr.GetAwaiter().GetResult()
    $process.Dispose()
    Write-Output "Temporary diagnostic database retained at $testDatabase"
}
