[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$productVersion = [Version]::Parse($Version)
$build = [Math]::Max(0, $productVersion.Build)
$revision = [Math]::Max(0, $productVersion.Revision)
$encodedBuild = [long]$build * 100 + $revision
if ($productVersion.Major -gt 255 -or $productVersion.Minor -gt 255 -or $revision -gt 99 -or $encodedBuild -gt 65535) {
    throw 'MSI version requires major/minor <= 255, revision <= 99 and build*100+revision <= 65535. Increase a higher product-version component instead of truncating.'
}
'{0}.{1}.{2}' -f $productVersion.Major, $productVersion.Minor, $encodedBuild
