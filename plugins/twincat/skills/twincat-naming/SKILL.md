---
name: twincat-naming
description: Naming conventions for TwinCAT 3 PLC code - prefixes for POUs, DUTs, GVLs, variables and instances, and common abbreviations. Use when naming or reviewing names in Structured Text.
---

# TwinCAT naming

**The project's existing convention wins.** Before naming anything, look at a few existing POUs and GVLs (`plc_list_elements`, `plc_read_element`) and follow what they do, even if it differs from the defaults below. Only apply these defaults in a new project or where the project has no visible convention.

The defaults follow Beckhoff's PLC programming conventions in InfoSys ("Naming conventions", TwinCAT 3 PLC). Source pages:

- POUs and DUTs: https://infosys.beckhoff.com/content/1033/tc3_plc_intro/12073945483.html
- Variables and instances: https://infosys.beckhoff.com/content/1033/tc3_plc_intro/12073947403.html
- General rules and abbreviations: https://infosys.beckhoff.com/content/1033/tc3_plc_intro/12049871755.html

## Types and POUs

Upper-case prefix, underscore, then a PascalCase name.

| Object | Prefix | Example |
| --- | --- | --- |
| Function block | `FB_` | `FB_ConveyorControl` |
| Function | `F_` | `F_MmToInch` |
| Program | none | `MAIN`, `ModuleControl` |
| Method, action | none, verb first | `Reset`, `MoveToPark` |
| Property | variable prefix of its type | `bReady`, `fActVelocity` |
| Interface | `I_` | `I_Drive` |
| Struct | `ST_` | `ST_RecipeStep` |
| Enum | `E_` | `E_MachineState` |
| Union | `U_` | `U_Register` |
| Alias | `T_` | `T_RecipeName` |
| Global variable list | `GVL` or `GVL_` | `GVL_Io` |
| Parameter list | `Param` or `Param_` | `Param_Conveyor` |

A property backed by a member variable uses that name with a leading underscore: property `bReady`, field `_bReady`.

## Variables and instances

Lower-case type prefix, then PascalCase. Prefix by the outermost type only.

| Type | Prefix | Example |
| --- | --- | --- |
| `BOOL`, `BIT` | `b` | `bEnable` |
| integer and bit-string types (`INT`, `UDINT`, `WORD`, `LWORD`, ...) | `n` | `nErrorId`, `nStatusWord` |
| `REAL`, `LREAL` | `f` | `fSetPosition` |
| `STRING` / `WSTRING` | `s` / `ws` | `sAmsNetId`, `wsDisplayText` |
| `TIME`, `LTIME` | `t` | `tTimeout` |
| `TIME_OF_DAY` / `DATE` / `DATE_AND_TIME` | `td` / `d` / `dt` | `dtLastService` |
| `ARRAY` | `a` | `aRecipeSteps` |
| `POINTER TO` / `PVOID` | `p` | `pBuffer` |
| `POINTER TO POINTER TO` | `pp` | `ppNode` |
| `REFERENCE TO` | `ref` (method inputs: prefix of the referenced type) | `refAxis` |
| Function block instance | `fb` | `fbStartDelay : TON;` |
| Struct instance | `st` | `stActRecipe` |
| Enum instance | `e` | `eState` |
| Interface instance | `ip` | `ipDrive` |
| Union instance | `u` | `uRegister` |
| Alias instance | prefix of the underlying type | `sRecipe : T_RecipeName;` |
| `HRESULT` | `hr` | `hrResult` |
| Constants | `c` | `cMaxSteps : UINT := 16;` |

- Standard IEC blocks get only the `fb` prefix: `fbStartDelay : TON;`, `fbStartEdge : R_TRIG;`.
- Global variables use the same prefixes; don't add a scope prefix such as `g`. Access them qualified (`GVL_Io.bEmergencyStop`) and mark the GVL with `{attribute 'qualified_only'}`.
- Mapped I/O may add an upper-case `I` / `O` before the type prefix (`IbSensorFront AT %I* : BOOL;`). Decide once per project.

## General rules

- English names, ASCII letters and digits only, no double underscores.
- Name by meaning, not by type or position: `fbFillTimeout`, not `fbTon2`.
- Booleans read as a statement: `bDoorClosed`, `bAxisReady`.
- Enums: add `{attribute 'qualified_only'}` and `{attribute 'strict'}` and use the values qualified (`E_MachineState.Running`). Values in PascalCase.
- Keep abbreviations consistent with Beckhoff's list, e.g. `Act` (actual), `Ack`, `Cmd`, `Cnt`, `Diag`, `Hdl`, `Idx`, `Init`, `Msg`, `Pos`, `Prev`, `Tmp`, `Velo`. Write out `Error`, `Warning`, `Alarm`, `Timeout`, `Buffer`, `Execute`.
- Test suites (TcUnit): `<FB under test>_Test`, tests named as behavior (`ReturnsZeroWhenDisabled`).
