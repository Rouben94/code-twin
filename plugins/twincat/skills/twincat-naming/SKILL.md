---
name: twincat-naming
description: Naming conventions for TwinCAT 3 PLC code - prefixes for POUs, DUTs and GVLs, prefix-free variable and instance names, casing and common abbreviations. Use when naming or reviewing names in Structured Text.
---

# TwinCAT naming

**The project's existing convention wins.** Before naming anything, look at a few existing POUs and GVLs (`plc_list_elements`, `plc_read_element`) and follow what they do, even if it differs from the defaults below. Only apply these defaults in a new project or where the project has no visible convention. Don't mix styles inside one POU.

Types and POUs follow Beckhoff's naming conventions in InfoSys. Variables deliberately do **not** use Beckhoff's type prefixes (`b`, `n`, `f`, `fb`, `st`, ...): the IDE shows the type on hover, and prefix-free names read cleaner.

- POUs and DUTs: https://infosys.beckhoff.com/content/1033/tc3_plc_intro/12073945483.html
- General rules and abbreviations: https://infosys.beckhoff.com/content/1033/tc3_plc_intro/12049871755.html

## Types and POUs

Upper-case prefix, underscore, then a PascalCase name.

| Object | Prefix | Example |
| --- | --- | --- |
| Function block | `FB_` | `FB_ConveyorControl` |
| Function | `F_` | `F_MmToInch` (avoid functions; see `twincat-oop`) |
| Program | none | `MAIN`, `ModuleControl` |
| Method, action | none, PascalCase, verb first | `Reset`, `MoveToPark` |
| Property | none, PascalCase | `Ready`, `ActVelocity` |
| Interface | `I_` | `I_Drive` |
| Struct | `ST_` | `ST_RecipeStep` |
| Enum | `E_` | `E_MachineState` |
| Union | `U_` | `U_Register` |
| Alias | `T_` | `T_RecipeName` |
| Global variable list | `GVL` or `GVL_` | `GVL_Io` |
| Parameter list | `Param` or `Param_` | `Param_Conveyor` |

## Variables and instances

No type prefix. camelCase for every variable, including inputs, outputs, constants, globals and instances of FBs, structs, enums and interfaces.

```st
VAR_INPUT
    enable        : BOOL;
    setPosition   : LREAL;
    timeout       : TIME := T#2S;
END_VAR
VAR
    startDelay    : TON;
    actRecipe     : ST_Recipe;
    state         : E_MachineState;
    drive         : I_Drive;
    recipeSteps   : ARRAY[0..MAX_STEPS - 1] OF ST_RecipeStep;
END_VAR
VAR CONSTANT
    MAX_STEPS     : UINT := 16;
END_VAR
```

- Constants are the one exception: UPPER_SNAKE_CASE (`MAX_STEPS`), so they stand out from variables.
- ST is **case-insensitive**. A name must not equal a keyword, a type or a POU name in scope (`time`, `string`, `ton`, `axis_ref`). Pick a meaningful name instead (`fillTimeout`, `axis`).
- Because of case-insensitivity, a property and its backing field need different names: property `Ready`, field `_ready`.
- Pointers and references may keep a short marker when it helps the reader avoid misuse: `bufferPtr`, `axisRef`.
- Global variables get no scope prefix such as `g`. Access them qualified (`GVL_Io.emergencyStop`) and mark the GVL with `{attribute 'qualified_only'}`.

## General rules

- English names, ASCII letters and digits only, no double underscores.
- Name by meaning, not by type or position: `fillTimeout`, not `ton2`.
- Booleans read as a statement: `doorClosed`, `axisReady`, `isBusy`.
- Enums: add `{attribute 'qualified_only'}` and `{attribute 'strict'}` and use the values qualified (`E_MachineState.Running`). Values in PascalCase.
- Keep abbreviations consistent with Beckhoff's list, e.g. `Act` (actual), `Ack`, `Cmd`, `Cnt`, `Diag`, `Hdl`, `Idx`, `Init`, `Msg`, `Pos`, `Prev`, `Tmp`, `Velo`. Write out `Error`, `Warning`, `Alarm`, `Timeout`, `Buffer`, `Execute`.
- Test suites (TcUnit): `<FB under test>_Test`, tests named as behavior (`ReturnsZeroWhenDisabled`).
