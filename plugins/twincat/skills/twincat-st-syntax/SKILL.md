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
    open        : BOOL;
    timeout     : TIME := T#2S;
END_VAR
VAR_OUTPUT
    error       : BOOL;
END_VAR
VAR_IN_OUT
    config      : ST_ValveConfig;    // passed by reference, must be connected
END_VAR
VAR
    timeoutTimer : TON;
END_VAR
VAR CONSTANT
    MAX_RETRIES : UINT := 3;
END_VAR
```

Sections: `VAR_INPUT`, `VAR_OUTPUT`, `VAR_IN_OUT`, `VAR_IN_OUT CONSTANT`, `VAR`, `VAR_TEMP`, `VAR_INST` (methods), `VAR_STAT`, `VAR CONSTANT`, `VAR_GLOBAL`, `VAR PERSISTENT`, `VAR RETAIN`. Initial values with `:=`. Mapped I/O: `sensor AT %I* : BOOL;` / `lamp AT %Q* : BOOL;`. Variable names carry no type prefix; see `twincat-naming`.

Methods declare their return type in the header (`METHOD Scale : LREAL`) and return by assigning to the method name (`Scale := value * factor;`) or with `RETURN`. Property getters work the same way. Prefer methods on an FB over standalone functions (see `twincat-oop`).

## Statements

```st
IF start AND NOT error THEN
    state := E_State.Running;
ELSIF stop THEN
    state := E_State.Stopping;
ELSE
    ;
END_IF

CASE state OF
    E_State.Idle:
        ...
    E_State.Running, E_State.Paused:
        ...
ELSE
    state := E_State.Error;
END_CASE

FOR idx := 0 TO MAX_STEPS - 1 BY 1 DO
    IF steps[idx].done THEN
        CONTINUE;
    END_IF
    ...
END_FOR

WHILE count < 10 DO count := count + 1; END_WHILE
REPEAT count := count - 1; UNTIL count = 0 END_REPEAT
```

- Statements end with `;`. `EXIT` leaves a loop, `CONTINUE` skips to the next iteration, `RETURN` leaves the POU.
- `CASE` selectors: single values, lists `1, 2`, ranges `3..5`. Always add an `ELSE` branch for state machines.
- Avoid `JMP` / labels.

## Operators

| Operator | Meaning |
| --- | --- |
| `:=` | assignment |
| `=>` | output assignment in a call (`timer(IN := start, Q => done);`) |
| `REF=` | assign a reference (`axisRef REF= axis;`) |
| `S=` / `R=` | set / reset a BOOL |
| `=`, `<>`, `<`, `<=`, `>`, `>=` | comparison |
| `+ - * /`, `MOD` | arithmetic (integer `/` truncates) |
| `AND`, `OR`, `XOR`, `NOT` | logical / bitwise |
| `AND_THEN`, `OR_ELSE` | short-circuit evaluation (guard pointer access) |
| `^` | dereference a pointer |
| `ADR()`, `SIZEOF()` | address, size in bytes |

Power is `EXPT(base, exp)`. Bit access: `statusWord.3`. Integer overflow wraps silently; integer division by zero raises an exception and stops the PLC, so guard divisors.

## Literals

`16#FF`, `2#1010_0101`, `8#17`, `1_000_000`, `1.5E3`, typed `INT#5`, `LREAL#0.1`; `T#1S500MS`, `LTIME#5US`, `TOD#08:30:00`, `D#2026-01-31`, `DT#2026-01-31-08:30:00`; `'text'` (STRING), `"text"` (WSTRING). Escapes: `$'` quote, `$$` dollar, `$N` newline, `$R` carriage return, `$T` tab, `$0D` hex byte.

## Calls

- Function blocks: call the instance, then read outputs: `startDelay(IN := start, PT := T#2S); IF startDelay.Q THEN ...`.
- Methods and properties: `valve.Open();`, `sum := calculator.Add(a := 1, b := 2);`, `valve.IsOpen`. Prefer named arguments. Enum values qualified: `E_State.Running`.
- Functions (only in existing code or library calls): `sum := F_Add(a := 1, b := 2);`.
- `THIS^` is the current instance, `SUPER^` the base implementation (`SUPER^.Reset();`).

## Type conversion

Convert explicitly: `TO_LREAL(counts)`, `INT_TO_REAL(value)`, `LREAL_TO_DINT(position)`. Float-to-integer conversions round to nearest; use `TRUNC` to truncate. Narrowing conversions can overflow silently, so range-check first. Implicit narrowing produces compiler warnings; don't ignore them.

## Pragmas and attributes

Placed on the line before the declaration they affect:

- `{attribute 'qualified_only'}` on GVLs and enums, `{attribute 'strict'}` on enums.
- `{attribute 'hide'}` hides a variable from IntelliSense and online view.
- `{attribute 'call_after_init'}` / `FB_init`, `FB_reinit`, `FB_exit` methods for initialization logic.
- `{attribute 'TcLinkTo' := '...'}` for I/O links declared in code.
- Conditional compilation: `{IF defined (MY_DEFINE)} ... {END_IF}`.

## Comments

`// line` and `(* block *)`. Comment intent, not mechanics.
