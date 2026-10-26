; CodeTwin per-user setup. Build with installer\Build-Setup.ps1.

#ifndef Version
  #define Version "0.0.0"
#endif

[Setup]
AppId={{E4AC59C0-D9C3-43B7-8214-90B1E60DFE98}
AppName=CodeTwin
AppVersion={#Version}
AppPublisher=CodeTwin
AppPublisherURL=https://github.com/Rouben94/code-twin
DefaultDirName={localappdata}\Programs\CodeTwin
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=CodeTwin-Setup-{#Version}
UninstallDisplayName=CodeTwin
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[InstallDelete]
; Removes plugin files an older version shipped and this one does not.
Type: filesandordirs; Name: "{app}\plugins"

[Files]
Source: "..\src\CodeTwin.Vsix\bin\Release\net48\CodeTwin.Vsix.vsix"; DestDir: "{app}"
Source: "..\tools\Install-TcXaeShell.ps1"; DestDir: "{app}"
Source: "..\LICENSE"; DestDir: "{app}"
Source: "marketplace.json"; DestDir: "{app}\.github\plugin"
Source: "..\plugins\twincat\*"; Excludes: "\bin\*"; DestDir: "{app}\plugins\twincat"; Flags: recursesubdirs
Source: "..\artifacts\mcp\code-twin-mcp.exe"; DestDir: "{app}\plugins\twincat\bin"

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c copilot plugin marketplace remove code-twin --force"; Flags: runhidden; RunOnceId: "Copilot"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-TcXaeShell.ps1"" -Uninstall"; Flags: runhidden; RunOnceId: "TcXaeShell"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function ShellRunning: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/c tasklist /FI "IMAGENAME eq TcXaeShell.exe" /NH | find /I "TcXaeShell.exe"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function WaitForShellClosed: Boolean;
begin
  Result := True;
  while ShellRunning do
    if SuppressibleMsgBox('Close TwinCAT XAE Shell, then click Retry.', mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

function InitializeSetup: Boolean;
var
  Runtime: TFindRec;
  ErrorCode: Integer;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\10.*'), Runtime);
  if Result then
    FindClose(Runtime)
  else if SuppressibleMsgBox('CodeTwin needs the .NET 10 Runtime (x64). Open the download page?', mbError, MB_YESNO, IDNO) = IDYES then
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if WaitForShellClosed then
    Result := ''
  else
    Result := 'TwinCAT XAE Shell is still running.';
end;

function InitializeUninstall: Boolean;
begin
  Result := WaitForShellClosed;
end;

procedure InstallExtension;
var
  ResultCode: Integer;
begin
  if not Exec('powershell.exe', ExpandConstant('-NoProfile -ExecutionPolicy Bypass -File "{app}\Install-TcXaeShell.ps1" -Vsix "{app}\CodeTwin.Vsix.vsix"'),
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    SuppressibleMsgBox('Installing the extension into TwinCAT XAE Shell failed. Close the shell and run setup again.', mbError, MB_OK, IDOK);
end;

function Copilot(const Arguments: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/c copilot ' + Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Copilot CLI and the Copilot app share the plugin registry; the plugin is loaded live from {app}.
procedure RegisterCopilot;
begin
  if not Copilot('--version') then
  begin
    SuppressibleMsgBox('Copilot CLI was not found. The chat and the twincat plugin need it: install Copilot CLI, then run setup again.', mbInformation, MB_OK, IDOK);
    Exit;
  end;
  Copilot('plugin marketplace remove code-twin --force');
  if not (Copilot(ExpandConstant('plugin marketplace add "{app}"')) and Copilot('plugin install twincat@code-twin')) then
    SuppressibleMsgBox('Registering the twincat plugin with Copilot CLI failed. Run setup again, or run:' + #13#10 +
      ExpandConstant('copilot plugin marketplace add "{app}"') + #13#10 + 'copilot plugin install twincat@code-twin', mbError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    InstallExtension;
    RegisterCopilot;
  end;
end;
