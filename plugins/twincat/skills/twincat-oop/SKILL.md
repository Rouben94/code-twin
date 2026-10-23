---
name: twincat-oop
description: Object-oriented programming in TwinCAT 3 - function blocks with methods and properties, interfaces, inheritance, access modifiers, FB_init, references, and practical design patterns for machine code. Use when designing or changing FB structure.
---

# OOP and patterns in TwinCAT 3

TwinCAT 3 extends IEC 61131-3 with methods, properties, interfaces and inheritance on function blocks. Use them to make machine code modular and testable, not for their own sake. A plain FB with a clear interface is often the right answer.

## Building blocks

| Element | Notes |
| --- | --- |
| `FUNCTION_BLOCK FB_X EXTENDS FB_Base IMPLEMENTS I_A, I_B` | Single inheritance, many interfaces. |
| `METHOD [PUBLIC\|PROTECTED\|PRIVATE\|INTERNAL] Name : ReturnType` | Default `PUBLIC`. Local `VAR` is re-initialized per call; use members or `VAR_INST` for state. |
| `PROPERTY [modifier] Name : Type` with `Get` / `Set` | Remove `Set` for read-only. Keep getters free of side effects. |
| `INTERFACE I_X EXTENDS I_Base` | Only method and property signatures. No variables. |
| `ABSTRACT` / `FINAL` | On FBs, methods and properties (`METHOD ABSTRACT Run`, `FUNCTION_BLOCK FINAL FB_X`). |
| `THIS^`, `SUPER^` | Current instance, base implementation (`SUPER^.Reset();`). |
| `FB_init`, `FB_reinit`, `FB_exit` | Constructor-like hooks. `FB_init(bInitRetains : BOOL; bInCopyCode : BOOL; <own params>)` lets you pass dependencies at declaration: `cylinder : FB_Cylinder(valve := mainValve);`. Keep the two system parameter names exactly as shown. |

Create these elements with `plc_create_element` (`kind`: `method`, `property`, `functionBlock`); `parentPath` of a method or property is the FB's element path. A property starts without accessors: add `Get` and, unless read-only, `Set` with `kind` `propertyGet` / `propertySet` and the property's path as `parentPath`. Edit them at `<property path>/Get` and `<property path>/Set`. Example getter body: `Ready := _ready;`.

## Interfaces and references

- An interface variable (`drive : I_Drive;`) holds a reference to any FB implementing it. Assign an FB instance: `drive := driveA;`. An unassigned interface is `0`; check `drive <> 0` before calling.
- `__QUERYINTERFACE(source, target)` casts between interfaces; returns `FALSE` if not implemented.
- `REFERENCE TO` is a non-null alias: assign with `REF=`, check with `__ISVALIDREF(axisRef)`. Prefer references over pointers.
- Never keep an interface or reference to an FB instance that lives in a method or function `VAR` (stack). It is invalid after the call.
- An online change can move data. Pointers and references stored once (for example `bufferPtr : POINTER TO BYTE := ADR(buffer);` in a declaration) may become invalid; TwinCAT 3.1.4026 updates many of them automatically but not all. Assign them cyclically or right before use. See InfoSys "Pointers, references, interfaces" (https://infosys.beckhoff.com/content/1033/tc3_plc_intro/12050043147.html).

## Patterns that fit PLC code

- **Command + status FB**: `Execute` (rising edge) / `Busy` / `Done` / `Error` / `ErrorID` outputs, like PLCopen motion blocks. Callers can sequence it predictably.
- **State machine inside an FB**: a private `state : E_State` with `CASE`, one method per command (`Start()`, `Stop()`, `Reset()`) that only requests a transition; the body executes the state machine cyclically.
- **Dependency injection via interfaces**: pass `I_Valve`, `I_Logger`, `I_Clock` into `FB_init` or a property. Production code gets real implementations; TcUnit tests get mocks.
- **Template method**: base FB holds the cycle (`Run`) and calls abstract/protected hooks (`OnStart`, `OnStop`) implemented by derived FBs.
- **Composite / module tree**: a module FB owns sub-module instances and calls them each cycle; status aggregates upwards.
- **Observer-lite**: an array of `I_Listener` registered at init, notified on events. Keep array bounds fixed; no dynamic allocation at runtime.

## Rules of thumb

- **Avoid functions (`FUNCTION F_X`).** Put logic into FBs: a method for a calculation or command (`calculator.Add(a := 2, b := 3)`), the FB body for cyclic work. Methods keep related logic together, can be mocked through interfaces in TcUnit, and can use the FB's state and configuration. Write a function only when the user asks for one or the surrounding code already uses one for the same job.
- Inputs and outputs (`VAR_INPUT` / `VAR_OUTPUT`) for cyclic data and HMI; methods for commands; properties for configuration and state queries.
- Keep inheritance shallow (two levels is usually enough). Prefer composition plus interfaces.
- Call every owned FB instance exactly once per cycle from its owner's body; methods should not hide cyclic work.
- Avoid `__NEW` / `__DELETE` in cyclic code. If dynamic memory is unavoidable, allocate at init, check for `0`, and free it in `FB_exit`.
- Mark helper methods `PRIVATE` / `PROTECTED` so the public surface stays small.
- Name methods as verbs (`Reset`, `MoveTo`), properties as state (`Ready`, `ActPosition`).

## Look up

Use `infosys-search` for exact syntax and edge cases: "Object-oriented programming", "FB_init", "__QUERYINTERFACE", "Access modifier", "ABSTRACT".
