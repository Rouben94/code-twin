<#
.SYNOPSIS
    Installs (or uninstalls) the CodeTwin VSIX into TwinCAT XAE Shell 17.0 for the current user.

.DESCRIPTION
    TcXaeShell is a VS isolated shell, not a Visual Studio setup instance. VSIXInstaller cannot
    install into the hive the shell actually runs with (17.0_IsoShell), so this script deploys the
    extension the way the shell's Extension Manager discovers it: it extracts the VSIX into the
    per-user Extensions folder, enables it in the ExtensionManager registry key and flags the
    extension set as changed so the shell rebuilds its pkgdef cache. No admin rights needed.
    Close TcXaeShell before running. See docs/spike.md for the details.
#>
[CmdletBinding()]
param(
    [string]$Vsix,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
if (-not $Vsix) { $Vsix = Join-Path $PSScriptRoot '..\src\CodeTwin.Vsix\bin\Release\net48\CodeTwin.Vsix.vsix' }

$hive = '17.0_IsoShell'
$extensionManager = "HKCU:\Software\Beckhoff\TcXaeShell\$hive\ExtensionManager"
$target = Join-Path $env:LOCALAPPDATA "Beckhoff\TcXaeShell\$hive\Extensions\CodeTwin"

if (Get-Process TcXaeShell -ErrorAction SilentlyContinue) {
    throw 'Close TcXaeShell first.'
}

# Renaming fails while any file in the folder is in use, so a locked extension stays intact.
$previous = "$target.previous"
if (Test-Path $previous) { Remove-Item $previous -Recurse -Force }
if (Test-Path $target) { Rename-Item -LiteralPath $target -NewName (Split-Path $previous -Leaf) }

$enabled = "$extensionManager\EnabledExtensions"
foreach ($subKey in 'EnabledExtensions', 'ExtensionTypes', 'ExtensionAutoUpdateEnrollment') {
    $key = "$extensionManager\$subKey"
    if (-not (Test-Path $key)) { continue }
    foreach ($name in (Get-Item $key).GetValueNames() | Where-Object { $_.StartsWith('CodeTwin.Vsix.', [StringComparison]::OrdinalIgnoreCase) }) {
        Remove-ItemProperty $key -Name $name
    }
}

if (-not $Uninstall) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $Vsix).Path, $target)
    foreach ($setupPart in 'manifest.json', 'catalog.json', '[Content_Types].xml') {
        Remove-Item -LiteralPath (Join-Path $target $setupPart) -ErrorAction SilentlyContinue
    }

    $identity = ([xml](Get-Content (Join-Path $target 'extension.vsixmanifest') -Raw)).PackageManifest.Metadata.Identity
    if (-not (Test-Path $enabled)) { New-Item $enabled -Force | Out-Null }
    Set-ItemProperty $enabled -Name "$($identity.Id),$($identity.Version)" -Value "$target\"
}

if (Test-Path $previous) { Remove-Item $previous -Recurse -Force }
Set-ItemProperty $extensionManager -Name ExtensionsChanged -Type QWord -Value ([DateTime]::UtcNow.ToFileTimeUtc())
Write-Host ($(if ($Uninstall) { 'Uninstalled from' } else { 'Installed to' }) + " $target")
