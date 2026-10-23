---
name: twincat-data-types
description: TwinCAT 3 data types and memory - elementary types and sizes, STRING/WSTRING, TIME/LTIME, arrays, structs, enums, unions, aliases, pointers and references, alignment (pack_mode), stack vs. instance memory, persistent data. Use when declaring types or reasoning about memory.
---

# Data types and memory in TwinCAT 3

## Elementary types

| Type | Size | Notes |
| --- | --- | --- |
| `BOOL` | 1 byte | `BIT` (1 bit) only inside structs and FBs. |
| `SINT` / `USINT` / `BYTE` | 1 byte | |
| `INT` / `UINT` / `WORD` | 2 bytes | |
| `DINT` / `UDINT` / `DWORD` | 4 bytes | |
| `LINT` / `ULINT` / `LWORD` | 8 bytes | |
| `XINT` / `UXINT` / `XWORD`, `PVOID` | pointer size | 4 bytes on x86, 8 on x64. Use for addresses and sizes, never `DWORD`. |
| `REAL` / `LREAL` | 4 / 8 bytes | Use `LREAL` for positions, velocities and anything accumulated. |
| `TIME` | 4 bytes | ms resolution, wraps after ~49.7 days. |
| `LTIME` | 8 bytes | ns resolution. Use for long durations and fine timing. |
| `DATE`, `TIME_OF_DAY`, `DATE_AND_TIME` | 4 bytes | Seconds/ms based; `LDATE`, `LTOD`, `LDT` are 8-byte ns variants. |
| `STRING(n)` | n + 1 bytes | Default `n` = 80. Single-byte characters, null-terminated. |
| `WSTRING(n)` | 2 * (n + 1) bytes | UTF-16. |

Rules:

- Choose the smallest type that fits the **range with margin**, not the smallest that fits today's value. Counters that may run for months: `UDINT` or `ULINT`.
- Signed/unsigned mixing triggers implicit conversions; keep a calculation in one type and convert explicitly.
- Compare floating point with a tolerance: `ABS(a - b) < EPSILON`, never `=`.
- Standard string functions (`CONCAT`, `LEFT`, `MID`, `FIND`, ...) work on `STRING` up to 255 characters. For longer text see `Tc2_Utilities` in InfoSys. Building strings every cycle costs CPU; do it on change.

## User-defined types (DUTs)

```st
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_AxisState : (Idle := 0, Homing, Moving, Error := 99) UDINT;   // optional base type, default INT
END_TYPE

TYPE ST_RecipeStep :
STRUCT
    targetPos : LREAL;
    dwell     : TIME := T#500MS;
    enabled   : BOOL;
END_STRUCT
END_TYPE

TYPE ST_Extended EXTENDS ST_RecipeStep :
STRUCT
    retries : UINT;
END_STRUCT
END_TYPE

TYPE U_Register :
UNION
    raw   : WORD;
    bytes : ARRAY[0..1] OF BYTE;
END_UNION
END_TYPE

TYPE T_RecipeName : STRING(40); END_TYPE
```

Create DUTs with `plc_create_element` (`kind`: `struct`, `enum`), then write the full declaration with `plc_write_element`.

## Arrays

- Start arrays at index 0 by default: `ARRAY[0..MAX_STEPS - 1] OF ST_Step`, looped with `FOR idx := 0 TO MAX_STEPS - 1`. Use another lower bound only when the index carries meaning (for example `ARRAY[1..4] OF ST_Station` for station numbers 1 to 4) or the existing code does. Multi-dimensional: `ARRAY[0..2, 0..3] OF LREAL`. Use named constants for bounds and loop limits.
- Variable-length arrays `ARRAY[*] OF T` are allowed only in `VAR_IN_OUT` of functions, methods and FBs; get limits with `LOWER_BOUND(values, 1)` / `UPPER_BOUND(values, 1)`.
- Out-of-range writes silently corrupt neighbouring memory. During development add the implicit check function `CheckBounds` (Add Object > POUs for implicit checks) and remove or keep it consciously.

## Pointers and references

- Prefer `REFERENCE TO T` (non-null alias, `REF=`, `__ISVALIDREF`) over `POINTER TO T`.
- Pointers: typed (`POINTER TO ST_X`), assigned with `ADR()`, dereferenced with `^`, checked against `0`. No `DWORD` pointers.
- Avoid pointer arithmetic; if needed, stay inside one array.
- `MEMCPY`, `MEMSET`, `MEMCMP` (Tc2_System) need the exact `SIZEOF` of the destination. Prefer plain assignment (`recipeA := recipeB;` for same types).

## Memory model

- All PLC variables are allocated statically at compile time: program and FB instance variables live for the whole runtime; GVLs too.
- Function and method `VAR` live on the **task stack** and are re-initialized each call. Large arrays or strings there can overflow the stack (default task stack is limited); move them into FB members or `VAR_INST`.
- `__NEW` uses router memory; avoid at runtime, free with `__DELETE`, check for `0`.
- `VAR PERSISTENT` survives a TwinCAT restart (written on orderly shutdown); `VAR RETAIN` survives power loss only with retain hardware (NOVRAM / UPS). Changing a persistent variable's type or name loses its value; plan migrations.

## Alignment and layout

- On x64, TwinCAT aligns members to their natural size up to 8 bytes; structs get padding. `SIZEOF` tells the real size.
- For data exchanged as raw bytes (ADS, fieldbus, files, other controllers), fix the layout with `{attribute 'pack_mode' := '1'}` (or `'2'`, `'4'`, `'8'`) on the struct, or order members largest first and add explicit reserve bytes. Document the expected `SIZEOF`.
- Changing a struct used by ADS clients, OPC UA or the HMI breaks those clients; check consumers before reordering.

## Look up

Use `infosys-search` for "Data types", "pack_mode", "Alignment", "Persistent data", "CheckBounds", "Stack size".
