---
name: twincat-st-syntax
description: IEC 61131-3 Structured Text as implemented in TwinCAT 3 - declarations, statements, operators, literals, calls, pragmas and the cyclic execution model. Use when writing or reviewing ST code.
---

# Structured Text in TwinCAT 3

Look up anything not covered here in InfoSys ("TwinCAT 3 PLC > Reference Programming") via the `infosys-search` skill.

## Execution model (most bugs come from forgetting this)

- PLC code runs **cyclically** in a task. Each call must finish quickly. Never wait in a loop for an input, a timer or a flag: it causes cycle overruns and the task watchdog. Use a state machine (`CASE`) that advances across cycles.
- Function block **instances keep their state** between cycles. Function and method `VAR` blocks are **re-initialized on every call** (use `VAR_INST` in a method or a member variable for state).
- Timers (`TON`, `TOF`, `TP`) and edge triggers (`R_TRIG`, `F_TRIG`) must be called **every cycle**, not only in one branch, or they freeze. Call them once, unconditionally, and read `.Q` where needed.
- Outputs of a function block update only when the instance is called.

## Declarations

```st
FUNCTION_BLOCK FB_Valve EXTENDS FB_Base IMPLEMENTS I_Valve
VAR_INPUT
    bOpen       : BOOL;
    tTimeout    : TIME := T#2S;
END_VAR
VAR_OUTPUT
    bError      : BOOL;
END_VAR
VAR_IN_OUT
    stConfig    : ST_ValveConfig;    // passed by reference, must be connected
END_VAR
VAR
    fbTimeout   : TON;
END_VAR
VAR CONSTANT
    cMaxRetries : UINT := 3;
END_VAR
```

Sections: `VAR_INPUT`, `VAR_OUTPUT`, `VAR_IN_OUT`, `VAR_IN_OUT CONSTANT`, `VAR`, `VAR_TEMP`, `VAR_INST` (methods), `VAR_STAT`, `VAR CONSTANT`, `VAR_GLOBAL`, `VAR PERSISTENT`, `VAR RETAIN`. Initial values with `:=`. Mapped I/O: `bSensor AT %I* : BOOL;` / `bLamp AT %Q* : BOOL;`.

Functions declare their return type in the header (`FUNCTION F_Scale : LREAL`) and return by assigning to the function name (`F_Scale := fIn * fFactor;`) or with `RETURN`. Methods and property getters work the same way.

## Statements

```st
IF bStart AND NOT bError THEN
    eState := E_State.Running;
ELSIF bStop THEN
    eState := E_State.Stopping;
ELSE
    ;
END_IF

CASE eState OF
    E_State.Idle:
        ...
    E_State.Running, E_State.Paused:
        ...
ELSE
    eState := E_State.Error;
END_CASE

FOR nIdx := 1 TO cMaxSteps BY 1 DO
    IF aSteps[nIdx].bDone THEN
        CONTINUE;
    END_IF
    ...
END_FOR

WHILE nCount < 10 DO nCount := nCount + 1; END_WHILE
REPEAT nCount := nCount - 1; UNTIL nCount = 0 END_REPEAT
```

- Statements end with `;`. `EXIT` leaves a loop, `CONTINUE` skips to the next iteration, `RETURN` leaves the POU.
- `CASE` selectors: single values, lists `1, 2`, ranges `3..5`. Always add an `ELSE` branch for state machines.
- Avoid `JMP` / labels.

## Operators

| Operator | Meaning |
| --- | --- |
| `:=` | assignment |
| `=>` | output assignment in a call (`fbTimer(IN := b, Q => bDone);`) |
| `REF=` | assign a reference (`refAxis REF= stAxis;`) |
| `S=` / `R=` | set / reset a BOOL |
| `=`, `<>`, `<`, `<=`, `>`, `>=` | comparison |
| `+ - * /`, `MOD` | arithmetic (integer `/` truncates) |
| `AND`, `OR`, `XOR`, `NOT` | logical / bitwise |
| `AND_THEN`, `OR_ELSE` | short-circuit evaluation (guard pointer access) |
| `^` | dereference a pointer |
| `ADR()`, `SIZEOF()` | address, size in bytes |

Power is `EXPT(base, exp)`. Bit access: `nWord.3`. Integer overflow wraps silently; integer division by zero raises an exception and stops the PLC, so guard divisors.

## Literals

`16#FF`, `2#1010_0101`, `8#17`, `1_000_000`, `1.5E3`, typed `INT#5`, `LREAL#0.1`; `T#1S500MS`, `LTIME#5US`, `TOD#08:30:00`, `D#2026-01-31`, `DT#2026-01-31-08:30:00`; `'text'` (STRING), `"text"` (WSTRING). Escapes: `$'` quote, `$$` dollar, `$N` newline, `$R` carriage return, `$T` tab, `$0D` hex byte.

## Calls

- Functions: `nSum := F_Add(nA := 1, nB := 2);` (named) or `F_Add(1, 2)` (positional, in declaration order). Prefer named arguments.
- Function blocks: call the instance, then read outputs: `fbDelay(IN := bStart, PT := T#2S); IF fbDelay.Q THEN ...`.
- Methods and properties: `fbValve.Open();`, `fbValve.bIsOpen`. Enum values qualified: `E_State.Running`.
- `THIS^` is the current instance, `SUPER^` the base implementation (`SUPER^.Reset();`).

## Type conversion

Convert explicitly: `TO_LREAL(nCounts)`, `INT_TO_REAL(n)`, `LREAL_TO_DINT(f)`. Float-to-integer conversions round to nearest; use `TRUNC` to truncate. Narrowing conversions can overflow silently, so range-check first. Implicit narrowing produces compiler warnings; don't ignore them.

## Pragmas and attributes

Placed on the line before the declaration they affect:

- `{attribute 'qualified_only'}` on GVLs and enums, `{attribute 'strict'}` on enums.
- `{attribute 'hide'}` hides a variable from IntelliSense and online view.
- `{attribute 'call_after_init'}` / `FB_init`, `FB_reinit`, `FB_exit` methods for initialization logic.
- `{attribute 'TcLinkTo' := '...'}` for I/O links declared in code.
- Conditional compilation: `{IF defined (MY_DEFINE)} ... {END_IF}`.

## Comments

`// line` and `(* block *)`. Comment intent, not mechanics.
