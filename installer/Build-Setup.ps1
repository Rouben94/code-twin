<#
.SYNOPSIS
    Builds the CodeTwin setup (artifacts\CodeTwin-Setup-<version>.exe).

.DESCRIPTION
    Builds the VSIX (with the chat sidecar), publishes the code-twin-mcp bridge and compiles
    installer\CodeTwin.iss with Inno Setup 6 (https://jrsoftware.org/isinfo.php).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it, for example with: winget install JRSoftware.InnoSetup' }

dotnet build (Join-Path $root 'src\CodeTwin.Vsix') -c Release
if ($LASTEXITCODE) { throw 'Building the VSIX failed.' }

dotnet publish (Join-Path $root 'src\CodeTwin.Mcp') -c Release -o (Join-Path $artifacts 'mcp')
if ($LASTEXITCODE) { throw 'Publishing code-twin-mcp failed.' }

& $iscc /Q "/DVersion=$version" "/O$artifacts" (Join-Path $PSScriptRoot 'CodeTwin.iss')
if ($LASTEXITCODE) { throw 'Compiling the setup failed.' }
Write-Host "Built $artifacts\CodeTwin-Setup-$version.exe"
