[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsiPath,
    [string]$ExpectedVersion = '2.0.0.0'
)

$ErrorActionPreference = 'Stop'
$resolvedMsi = (Resolve-Path -LiteralPath $MsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($resolvedMsi, 0)

function Read-MsiRows([string]$Query) {
    $view = $database.OpenView($Query)
    try {
        [void]$view.Execute()
        while ($null -ne ($record = $view.Fetch())) {
            try {
                $fieldCount = $record.GetType().InvokeMember('FieldCount', [Reflection.BindingFlags]::GetProperty, $null, $record, $null)
                $values = for ($index = 1; $index -le $fieldCount; $index++) { $record.StringData($index) }
                ,@($values)
            }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    }
    finally {
        [void]$view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Assert-Package([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

try {
    $version = @(Read-MsiRows "SELECT Value FROM Property WHERE Property = 'ProductVersion'")[0][0]
    $expectedMsiVersion = & (Join-Path $PSScriptRoot 'Get-MsiProductVersion.ps1') -Version $ExpectedVersion
    Assert-Package ($version -eq $expectedMsiVersion) 'Unexpected ProductVersion.'
    $tables = @(Read-MsiRows 'SELECT Name FROM _Tables' | ForEach-Object { $_[0] })
    foreach ($table in @('ServiceInstall', 'ServiceControl')) {
        if ($tables -contains $table) {
            Assert-Package (@(Read-MsiRows "SELECT * FROM $table").Count -eq 0) "$table must not register a Windows Service."
        }
    }
    $actions = @(Read-MsiRows 'SELECT Action,Type,Source,Target FROM CustomAction')
    foreach ($actionName in @('InitializeUserProfile', 'RemoveUserAutostart', 'RollbackInitializeUserProfile', 'CommitInitializeUserProfile')) {
        $rows = @($actions | Where-Object { $_[0] -eq $actionName })
        Assert-Package ($rows.Count -eq 1) "Missing profile action $actionName."
        $actionType = [int]$rows[0][1]
        Assert-Package (($actionType -band 0x400) -ne 0) "$actionName must be deferred."
        Assert-Package (($actionType -band 0x800) -eq 0) "$actionName must impersonate the user."
        Assert-Package ($rows[0][2] -eq 'ClientSetup') "$actionName must use the embedded helper."
    }
    $registry = @(Read-MsiRows 'SELECT Name FROM Registry' | ForEach-Object { $_[0] })
    Assert-Package ($registry -notcontains 'McpUrl') 'Do not register a machine-wide endpoint.'
    if ([Version]$ExpectedVersion -ge [Version]'2.0.0.2') {
        Assert-Package ($registry -contains 'ConfigOwnerSid') 'Persist the configured Windows owner.'
        Assert-Package ($registry -contains 'InstallDirectory') 'Persist the selected installation directory.'
        $permissions = @(Read-MsiRows 'SELECT LockObject,SDDLText FROM MsiLockPermissionsEx')
        $configPermission = @($permissions | Where-Object { $_[0] -eq 'ConfigFolder' })
        Assert-Package ($configPermission.Count -eq 1) 'Require one canonical configuration folder permission.'
        foreach ($permission in $permissions) {
            Assert-Package ($permission[1] -notmatch '[\[\]]') 'Native MSI SDDL does not support public/private property references.'
        }
        Assert-Package ($configPermission[0][1] -eq 'D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)') 'Configuration must start private to SYSTEM and administrators.'
        $ownerPermissions = @(Read-MsiRows 'SELECT `User`,`Permission`,`Table`,`SecureObject` FROM Wix4SecureObject')
        $ownerPermission = @($ownerPermissions | Where-Object { $_[0] -eq '[MOYAI_CONFIG_OWNER_ACCOUNT]' -and $_[2] -eq 'CreateFolder' -and $_[3] -eq 'ConfigFolder' })
        Assert-Package ($ownerPermission.Count -eq 1 -and [int]$ownerPermission[0][1] -eq 0x1301BF) 'Grant only configured owner modification access through the formatted utility action.'
        $resolver = @($actions | Where-Object { $_[0] -eq 'ResolveConfigOwner' })
        Assert-Package ($resolver.Count -eq 1 -and $resolver[0][2] -eq 'ConfigOwnerActions' -and $resolver[0][3] -eq 'ResolveConfigurationOwner' -and [int]$resolver[0][1] -eq 1) 'Require immediate read-only owner resolution.'
        $sequences = @{}
        foreach ($entry in @(Read-MsiRows 'SELECT Action,Sequence FROM InstallExecuteSequence')) { $sequences[$entry[0]] = [int]$entry[1] }
        Assert-Package ($sequences.ResolveConfigOwner -lt $sequences.InstallInitialize) 'Resolve owner before any old product removal.'
        Assert-Package ($sequences.RemoveExistingProducts -gt $sequences.InstallInitialize -and $sequences.RemoveExistingProducts -lt $sequences.InstallFiles) 'Old product removal must participate in rollback.'
        Assert-Package (@($actions | Where-Object { $_[2] -eq 'MOYAI_CONFIG_OWNER_SID' -and $_[3] -eq '[UserSID]' }).Count -eq 1) 'Initialize the configured owner from the installing user.'
    }
    $fileRows = @(Read-MsiRows 'SELECT FileName,Component_ FROM File')
    $files = @($fileRows | ForEach-Object { $_[0] })
    foreach ($file in @('Moyai.Tray.exe', 'Moyai.Mcp.exe', 'moyaictl.exe', 'moyaimcp.exe')) {
        Assert-Package (@($files | Where-Object { ($_ -split '\|')[-1] -eq $file }).Count -eq 1) "Missing package executable $file."
    }
    $components = @(Read-MsiRows 'SELECT Component,Directory_ FROM Component')
    $directories = @(Read-MsiRows 'SELECT Directory,DefaultDir FROM Directory')
    foreach ($app in @(@('Moyai.Mcp.exe', 'host'), @('Moyai.Tray.exe', 'tray'))) {
        $appFile = @($fileRows | Where-Object { ($_[0] -split '\|')[-1] -eq $app[0] })[0]
        $component = @($components | Where-Object { $_[0] -eq $appFile[1] })[0]
        $directory = @($directories | Where-Object { $_[0] -eq $component[1] })[0]
        Assert-Package (($directory[1] -split '\|')[-1] -eq $app[1]) "Isolate dependency directory for $($app[0])."
    }
    [ordered]@{ ok = $true; version = $version; sha256 = (Get-FileHash -LiteralPath $resolvedMsi -Algorithm SHA256).Hash; serviceRegistration = 'absent'; profileActions = 'user-context embedded helper'; executables = 4 } | ConvertTo-Json
}
finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
