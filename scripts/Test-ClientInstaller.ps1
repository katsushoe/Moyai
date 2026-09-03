[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$MsiPath)

$ErrorActionPreference = 'Stop'
$resolvedMsi = (Resolve-Path -LiteralPath $MsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($resolvedMsi, 0)
function Read-Rows([string]$Query) {
    $view = $database.OpenView($Query)
    try {
        [void]$view.Execute()
        while ($null -ne ($record = $view.Fetch())) {
            $count = $record.GetType().InvokeMember('FieldCount', [Reflection.BindingFlags]::GetProperty, $null, $record, $null)
            $values = @()
            for ($i=1; $i -le $count; $i++) { $values += $record.StringData($i) }
            ,$values
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
    } finally { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
}
try {
    $rows = @(Read-Rows 'SELECT Action, Type, Source, Target FROM CustomAction')
    $sequences = @{}
    foreach ($row in @(Read-Rows 'SELECT Action, Sequence FROM InstallExecuteSequence')) { $sequences[$row[0]] = [int]$row[1] }
    foreach ($client in @('Codex','Claude')) {
        foreach ($operation in @('Configure','Unconfigure')) {
            $id = $operation + $client
            $row = @($rows | Where-Object { $_[0] -eq $id })[0]
            if ($row[2] -ne 'ClientSetup' -or (([int]$row[1] -band 2048) -ne 0) -or (([int]$row[1] -band 1024) -eq 0)) { throw "Invalid embedded/impersonated action: $id" }
            if ($row[3] -notlike '*--profile*MOYAI_CLIENT_PROFILE*--transaction-id*') { throw "Missing profile/transaction: $id" }
            if ($sequences['Rollback'+$id] -ge $sequences[$id] -or $sequences['Commit'+$id] -le $sequences[$id]) { throw "Invalid rollback ordering: $id" }
            if ($operation -eq 'Configure' -and $sequences[$id] -le $sequences['StartServices']) { throw 'Registration precedes service start.' }
            if ($operation -eq 'Unconfigure' -and $sequences[$id] -ge $sequences['RemoveFiles']) { throw 'Removal follows file deletion.' }
        }
    }
    $dialog = @(Read-Rows "SELECT Dialog FROM Dialog WHERE Dialog = 'McpClientsDialog'")
    if ($dialog.Count -ne 1) { throw 'Client selection dialog missing.' }
    Write-Output 'PASS: embedded client actions, impersonation, rollback, ordering and dialog tables.'
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
