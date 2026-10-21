[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$MsiPath
)

$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$client = Join-Path $directory 'moyaictl.exe'
$alias = Join-Path $directory 'moyaimcp.exe'
$hash = (Get-FileHash -LiteralPath $client -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $alias -Algorithm SHA256).Hash -ne $hash) { throw 'CLI alias differs from its shared apphost.' }

function Invoke-LocalFlag([string]$Executable, [string]$Flag) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('--config', (Join-Path $directory ('missing-' + [Guid]::NewGuid().ToString('N') + '.json')), $Flag)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { throw 'CLI local flag timed out.' }
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or $errorOutput.Length -ne 0) { throw "Local $Flag failed with exit $($process.ExitCode): $errorOutput" }
        return $output
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}

foreach ($flag in @('--help', '--version')) {
    $original = Invoke-LocalFlag $client $flag
    $aliased = Invoke-LocalFlag $alias $flag
    if ($original -cne $aliased) { throw "CLI outputs differ for $flag." }
    if ([string]::IsNullOrWhiteSpace($aliased)) { throw "Empty CLI output for $flag." }
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
$view = $null
try {
    $database = $installer.OpenDatabase($msi, 0)
    $view = $database.OpenView('SELECT FileName FROM File')
    [void]$view.Execute()
    $names = while ($null -ne ($record = $view.Fetch())) {
        try { ($record.StringData(1) -split '\|')[-1] }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
    }
    foreach ($required in @('moyaictl.exe', 'moyaimcp.exe', 'moyaictl.dll', 'Moyai.Mcp.exe')) {
        if (@($names | Where-Object { $_ -ceq $required }).Count -ne 1) { throw "Missing or duplicated MSI file: $required" }
    }
}
finally {
    if ($null -ne $view) { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
    if ($null -ne $database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

[pscustomobject]@{ ok = $true; aliasSha256 = $hash; localFlags = 4; msiFilesVerified = 4; liveInstallationPerformed = $false } | ConvertTo-Json
