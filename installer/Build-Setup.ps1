<#
.SYNOPSIS
    Builds the CodeTwin setup (artifacts\CodeTwin-Setup-<version>.exe).

.DESCRIPTION
    Builds the VSIX (with the chat sidecar), publishes the code-twin-mcp bridge and compiles
    installer\CodeTwin.iss with Inno Setup 6 (https://jrsoftware.org/isinfo.php).

.PARAMETER Version
    Version to stamp on all binaries and the setup. Defaults to the version in Directory.Build.props.
#>
[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
}

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it, for example with: winget install JRSoftware.InnoSetup' }

# --no-incremental: the VSIX manifest is not regenerated when only the version changes.
dotnet build (Join-Path $root 'src\CodeTwin.Vsix') -c Release "-p:Version=$Version" --no-incremental
if ($LASTEXITCODE) { throw 'Building the VSIX failed.' }

dotnet publish (Join-Path $root 'src\CodeTwin.Mcp') -c Release "-p:Version=$Version" -o (Join-Path $artifacts 'mcp')
if ($LASTEXITCODE) { throw 'Publishing code-twin-mcp failed.' }

& $iscc /Q "/DVersion=$Version" "/O$artifacts" (Join-Path $PSScriptRoot 'CodeTwin.iss')
if ($LASTEXITCODE) { throw 'Compiling the setup failed.' }
Write-Host "Built $artifacts\CodeTwin-Setup-$Version.exe"
