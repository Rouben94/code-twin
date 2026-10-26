# Phase 0 spike: VSIX in TwinCAT XAE Shell and Visual Studio 2026

Result: **go**. A single VSIX (`src/CodeTwin.Vsix`, net48, `AsyncPackage`) loads in both IDEs,
autoloads on solution open, shows the dockable "CodeTwin" tool window and lists the PLC tree via
the Automation Interface on the UI thread.

Tested on:

| IDE | Version | Install | Result |
| --- | --- | --- | --- |
| TwinCAT XAE Shell 64-bit | TcXaeShell 17.0 (VS 17.10 isolated shell), TwinCAT 3.1.4026.25 | `tools/Install-TcXaeShell.ps1` | loads, tree read |
| Visual Studio 2026 Professional | 18.9 with TwinCAT XAE integration | `VSIXInstaller.exe` | loads, tree read |

## Build

```powershell
dotnet build CodeTwin.slnx -c Release   # -> src\CodeTwin.Vsix\bin\Release\net48\CodeTwin.Vsix.vsix
```

- SDK-style project with `Microsoft.VSSDK.BuildTools`. `Microsoft.VsSDK.targets` must be imported
  **after** `Sdk.targets`, otherwise no `.vsix` is produced.
- `Microsoft.VisualStudio.SDK` 17.10 (the shell's VS version) with `ExcludeAssets="runtime"`: the
  IDE provides those assemblies. The same binary runs in VS 2026.
- `UseCodebase=true` is required. Without a `CodeBase` in the pkgdef the shell fails with
  `FileNotFoundException: CodeTwin.Vsix` because it does not probe per-user extension folders.
- Beckhoff interop: the few Automation Interface interfaces CodeTwin uses are declared in
  `TwinCatAutomation.cs` (IIDs and vtable order from `TCatSysManagerLib`), so the build needs
  no Beckhoff assembly. Nothing from Beckhoff is committed or shipped.

## Manifest

One target is enough for both IDEs:

```xml
<InstallationTarget Id="Microsoft.VisualStudio.IntegratedShell" Version="[17.0,19.0)">
  <ProductArchitecture>amd64</ProductArchitecture>
</InstallationTarget>
```

- The shell's Extension Manager accepts `IntegratedShell` (TcXaeShell is a VS isolated shell). A
  product-specific target `Id="TcXaeShell"` (the AppEnv app id, without `ProductArchitecture`)
  is only needed for the shell's `VSIXInstaller.exe`, which is not used (see below). It also
  forces `BypassVsixValidation` because of VSSDK1311.
- The `CoreEditor` prerequisite and `manifest.json`/`catalog.json` are required by the VS 2026
  installer and ignored by the shell deploy script.

## Install into TcXaeShell

```powershell
# close TcXaeShell first; no admin rights needed
.\tools\Install-TcXaeShell.ps1               # default: Release build output
.\tools\Install-TcXaeShell.ps1 -Uninstall
```

The script does what the shell's Extension Manager expects for a per-user extension:

1. extract the VSIX to `%LOCALAPPDATA%\Beckhoff\TcXaeShell\17.0_IsoShell\Extensions\CodeTwin\`,
2. add `"<Id>,<Version>" = "<folder>\"` under
   `HKCU\Software\Beckhoff\TcXaeShell\17.0_IsoShell\ExtensionManager\EnabledExtensions`,
3. set `ExtensionsChanged` (QWORD, FILETIME UTC) in the same `ExtensionManager` key so the next
   start rebuilds the pkgdef cache. Without it a new extension is discovered after the pkgdef
   merge and only loads on the second start.

Key facts about the shell:

- Runtime hive is **`17.0_IsoShell`**, not `17.0`: registry
  `HKCU\Software\Beckhoff\TcXaeShell\17.0_IsoShell[_Config]`, user data
  `%LOCALAPPDATA%\Beckhoff\TcXaeShell\17.0_IsoShell`.
- AppEnv registration: `HKLM\SOFTWARE\Microsoft\AppEnv\17.0\Apps\TcXaeShell_17.0`, install dir
  `C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE`.
- Machine-wide alternative (admin, untested): copy to `Common7\IDE\Extensions\<Name>\`, which is
  on the shell's `PkgDefSearchPath`, and touch `Common7\IDE\Extensions\extensions.configurationchanged`.
- Command line: `TcXaeShell.exe "<sln>" /log "<file>"`. `/log` takes the **next** argument as
  the log path, so passing the solution after `/log` overwrites the `.sln`.

### Why not VSIXInstaller

- The VS 2022/2026 `VSIXInstaller.exe` only targets setup-engine instances. TcXaeShell is not
  one (error 2003, no applicable SKU).
- The shell's own `Common7\IDE\VSIXInstaller.exe` (17.10) does not start: its `.config` points
  `codeBase` entries to missing files (`net472\Microsoft.VisualStudio.Threading.dll`, ...) and
  lacks MessagePack 2.5. With a patched config in an AppDomain host it runs, but:
  - it throws a `NullReferenceException` resolving `<Prerequisites>` (no setup catalog) and
    rejects packages with `manifest.json`/`catalog.json`, so the VSIX has to be repacked;
  - it installs into hive `17.0` (`%LOCALAPPDATA%\Beckhoff\TcXaeShell\17.0\Extensions`), which
    the shell never reads. `/rootSuffix:IsoShell` is ignored.

  Dead end. The direct deploy above is simpler and works.

## Install into Visual Studio 2026

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\VSIXInstaller.exe" /quiet /instanceIds:<id> CodeTwin.Vsix.vsix
& "...\VSIXInstaller.exe" /quiet /instanceIds:<id> /uninstall:CodeTwin.Vsix.6f0b8f5e-3c1a-4d5e-9a57-2f7c1d4b8e21
```

`<id>` from `vswhere -all -property instanceId`. Without `/instanceIds` the version range
`[17.0,19.0)` also matches VS 2022. Installs per user to
`%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<id>\Extensions\<random>\`.

## Runtime behaviour

- Autoload: `ProvideAutoLoad(SolutionExistsAndFullyLoaded, BackgroundLoad)`.
- **Do not show a tool window from `InitializeAsync`.** Frame creation waits for the package load
  to finish, which deadlocks the UI thread (confirmed with a stack dump). The first show runs in
  `OnAfterPackageLoadedAsync`, later ones on `OnAfterBackgroundSolutionLoadComplete`.
- `Project.Object` of a TwinCAT project is `ITcSysManager`. `LookupTreeItem("TIPC")` returns the
  PLC configuration and its children enumerate as `ITcSmTreeItem` (`foreach`), all on the UI thread.

| Solution | Nodes | Read time (TcXaeShell) | Read time (VS 2026) |
| --- | --- | --- | --- |
| InterviewProject | 22 | 44-55 ms | 56-86 ms |
| ByMotionStorage | 1266 | 109 ms | - |

A full tree walk is cheap; no background threading or caching is needed for Phase 1.

## Open points for Phase 1

- No View menu command yet; the window is (re)opened on solution load only.
- Toolbar button is not themed (dark theme).
- Some `ItemSubTypeName` values are raw enum names (`TREEITEMTYPE_PLCAPP`).
