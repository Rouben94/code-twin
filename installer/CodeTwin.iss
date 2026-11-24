; CodeTwin per-user setup. Build with installer\Build-Setup.ps1.

#ifndef Version
  #define Version "0.0.0"
#endif

[Setup]
AppId={{E4AC59C0-D9C3-43B7-8214-90B1E60DFE98}
AppName=CodeTwin
AppVersion={#Version}
VersionInfoVersion={#Version}
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
UninstallDisplayIcon={app}\codetwin.ico
SetupIconFile=..\assets\icons\codetwin.ico
WizardStyle=modern
InfoBeforeFile=Privacy.txt
; Restart Manager broke the processes setup starts afterwards (0xC0000142); setup ends code-twin-mcp itself after asking.
CloseApplications=no
WizardSmallImageFile=..\assets\icons\codetwin-64.png,..\assets\icons\codetwin-128.png
Compression=lzma2
SolidCompression=yes

[InstallDelete]
; Removes plugin files an older version shipped and this one does not.
Type: filesandordirs; Name: "{app}\plugins"

[Files]
Source: "..\src\CodeTwin.Vsix\bin\Release\net48\CodeTwin.Vsix.vsix"; DestDir: "{app}"
Source: "..\tools\Install-TcXaeShell.ps1"; DestDir: "{app}"
Source: "..\LICENSE"; DestDir: "{app}"
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"
Source: "..\assets\icons\codetwin.ico"; DestDir: "{app}"
Source: "marketplace.json"; DestDir: "{app}\.github\plugin"
Source: "..\plugins\twincat\*"; Excludes: "\bin\*"; DestDir: "{app}\plugins\twincat"; Flags: recursesubdirs
Source: "..\artifacts\mcp\code-twin-mcp.exe"; DestDir: "{app}\plugins\twincat\bin"

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c copilot plugin marketplace remove code-twin --force"; Flags: runhidden; RunOnceId: "Copilot"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-TcXaeShell.ps1"" -Uninstall"; Flags: runhidden; RunOnceId: "TcXaeShell"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// WMI instead of tasklist: no console processes are started while processes are being ended.
function QueryProcesses(const Condition: String): Variant;
var
  Locator, Service: Variant;
begin
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Service := Locator.ConnectServer('.', 'root\CIMV2');
  Result := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE ' + Condition);
end;

function ProcessRunning(const ImageName: String): Boolean;
begin
  Result := QueryProcesses('Name = "' + ImageName + '"').Count > 0;
end;

function McpServerCondition: String;
begin
  Result := ExpandConstant('{app}\plugins\twincat\bin\code-twin-mcp.exe');
  StringChangeEx(Result, '\', '\\', True);
  Result := 'ExecutablePath = "' + Result + '"';
end;

function WaitForShellClosed: Boolean;
begin
  Result := True;
  while ProcessRunning('TcXaeShell.exe') do
    if SuppressibleMsgBox('Close TwinCAT XAE Shell, then click Retry.', mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

// code-twin-mcp.exe is locked while a Copilot session (Copilot CLI or the Copilot app, which keeps running in the tray) uses the plugin.
function EndMcpServers: Boolean;
var
  Processes: Variant;
  I, Attempt: Integer;
begin
  Processes := QueryProcesses(McpServerCondition);
  Result := Processes.Count = 0;
  if Result then
    Exit;

  if SuppressibleMsgBox('CodeTwin is running in Copilot sessions (Copilot CLI or the Copilot app). End CodeTwin in these sessions?' + #13#10#13#10 +
    'They lose the TwinCAT tools until you restart them. Let running TwinCAT agents finish first.', mbConfirmation, MB_YESNO, IDNO) = IDNO then
    Exit;

  for I := 0 to Processes.Count - 1 do
    Processes.ItemIndex(I).Terminate(0);

  for Attempt := 1 to 40 do
  begin
    Result := QueryProcesses(McpServerCondition).Count = 0;
    if Result then
      Exit;
    Sleep(250);
  end;
  SuppressibleMsgBox('CodeTwin could not be ended in all Copilot sessions. Close them, then run setup again.', mbError, MB_OK, IDOK);
end;

function WaitForClients: Boolean;
begin
  Result := WaitForShellClosed and EndMcpServers;
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
  if WaitForClients then
    Result := ''
  else
    Result := 'TwinCAT XAE Shell or a Copilot session using CodeTwin is still running.';
end;

function InitializeUninstall: Boolean;
begin
  Result := WaitForClients;
end;

function PowerShellQuote(Value: String): String;
begin
  StringChangeEx(Value, '''', '''''', True);
  Result := '''' + Value + '''';
end;

procedure InstallExtension;
var
  ResultCode: Integer;
  OutputFile: String;
  Output: AnsiString;
begin
  OutputFile := ExpandConstant('{tmp}\Install-TcXaeShell.log');
  if Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "try { & ' +
    PowerShellQuote(ExpandConstant('{app}\Install-TcXaeShell.ps1')) + ' -Vsix ' + PowerShellQuote(ExpandConstant('{app}\CodeTwin.Vsix.vsix')) +
    ' } catch { $_ | Out-String | Set-Content -LiteralPath ' + PowerShellQuote(OutputFile) + '; exit 1 }"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    // A shell started during the installation read the extension state before the script flagged it as changed.
    if ProcessRunning('TcXaeShell.exe') then
      SuppressibleMsgBox('TwinCAT XAE Shell was started during the installation and has not loaded CodeTwin. ' +
        'Close it completely and start it again.', mbInformation, MB_OK, IDOK);
    Exit;
  end;

  if not LoadStringFromFile(OutputFile, Output) then
    Output := SysErrorMessage(ResultCode);
  Log('Installing the extension failed (' + IntToStr(ResultCode) + '): ' + String(Output));
  SuppressibleMsgBox('Installing the extension into TwinCAT XAE Shell failed:' + #13#10#13#10 + Trim(String(Output)) + #13#10#13#10 +
    'Close the shell and run setup again.', mbError, MB_OK, IDOK);
end;

function Copilot(const Arguments: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/c copilot ' + Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Not started: ResultCode is a Windows error code; started: the exit code.
  Log(Format('copilot %s: started=%d, code=%d', [Arguments, Ord(Result), ResultCode]));
  Result := Result and (ResultCode = 0);
end;

// Copilot CLI and the Copilot app share the plugin registry; the plugin is loaded live from {app}.
procedure RegisterCopilot;
begin
  if not Copilot('--version') then
  begin
    SuppressibleMsgBox('Copilot CLI was not found. The twincat plugin and the chat on GitHub Copilot need it: install Copilot CLI, then run setup again. ' +
      'To run the chat on Claude instead, install Claude Code and set Tools > Options > CodeTwin > Chat > AI provider to Claude.', mbInformation, MB_OK, IDOK);
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
