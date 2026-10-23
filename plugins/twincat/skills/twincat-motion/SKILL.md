---
name: twincat-motion
description: PLC motion control with TwinCAT NC PTP and the Tc2_MC2 library (PLCopen) - AXIS_REF, MC_Power, MC_Reset, MC_Home, MC_Move*, MC_Halt vs. MC_Stop, Execute/Busy/Done/Error handshake, buffer modes, axis state machines and safety boundaries. Use when writing or changing code that moves axes.
---

# Motion with Tc2_MC2

Most TwinCAT 3 projects move axes through NC PTP (TF50x0) and the PLCopen-style library `Tc2_MC2`. Newer projects may use MC3 (TF55xx) with similar block names but different details; check which library the project references (`plc_search_elements` for `AXIS_REF` usage, or read an existing axis FB) before writing code.

Reference: InfoSys "PLC Library: Tc2_MC2" (https://infosys.beckhoff.com/content/1033/tcplclib_tc2_mc2/index.html). Look up each block's inputs with `infosys-search` before using an unfamiliar one.

## Safety boundaries

- The agent **writes and builds** motion code. It never activates, logs in, downloads, starts or jogs anything. Moving real axes is the user's decision.
- Code never replaces safety functions (STO, safe limits, TwinSAFE). Don't touch safety projects.
- Stop paths must stay reachable: never gate `MC_Stop`/`MC_Halt` or `MC_Power.Enable := FALSE` behind conditions that can block them.
- Point out to the user when a change alters velocities, limits, homing or direction.

## Axis reference

- One `AXIS_REF` per axis (e.g. `axis : AXIS_REF;` in an axis FB or GVL), linked to the NC axis in the System Manager (user action).
- Call `axis.ReadStatus();` once per cycle **before** the motion blocks so `axis.Status` (e.g. `Status.Moving`, `Status.Error`, `Status.ErrorID`, `Status.Homed`) is current.
- Pass the axis to every block as `Axis := axis`.

## Core blocks

| Block | Purpose | Notes |
| --- | --- | --- |
| `MC_Power` | Enable controller and feed | Level-triggered `Enable`, plus `Enable_Positive`, `Enable_Negative`, `Override` (percent, `100.0` = full speed). `Status` = ready. |
| `MC_Reset` | Clear axis error | Rising edge on `Execute`. Find and fix the cause first. |
| `MC_Home` | Reference the axis | Homing mode is mostly configured in the NC; `Position` sets the reference value. |
| `MC_MoveAbsolute` / `MC_MoveRelative` | Positioning | `Position`/`Distance`, `Velocity`, optional `Acceleration`, `Deceleration`, `Jerk` (0 = NC defaults), `BufferMode`. |
| `MC_MoveVelocity` | Endless move | `Direction` (`MC_Positive_Direction`, ...). `InVelocity` instead of `Done`. Stop it with `MC_Halt`. |
| `MC_MoveModulo` | Rotary axes | Read the modulo notes in InfoSys first. |
| `MC_Jog` | Manual moves | For HMI jogging; keep separate from automatic sequences. |
| `MC_Halt` | Normal stop | Axis can be commanded again immediately. Use for regular stopping. |
| `MC_Stop` | Exceptional stop | Locks the axis against other commands until `Execute` is `FALSE` and the axis stands still. Don't use it for normal sequence stops. |

## The handshake

All `Execute` blocks start on a **rising edge** and report `Busy`, `Done` (or `InVelocity`), `CommandAborted`, `Error`, `ErrorID`.

- Call every motion block instance **every cycle**, unconditionally, with `Execute` driven by your state machine. Calling it only inside one `CASE` branch freezes its outputs.
- Keep `Execute` `TRUE` until `Done`, `Error` or `CommandAborted`, then set it `FALSE` for at least one cycle before the next command.
- Use one instance per command type per axis; don't share an instance between states that may overlap.
- `CommandAborted` is normal when another command takes over (e.g. halt during move); handle it explicitly.
- Read `ErrorID` from the block **and** `axis.Status.ErrorID`; report both. Look up NC error codes in InfoSys ("NC error codes").
- `BufferMode` (`MC_Aborting` default, `MC_Buffered`, `MC_BlendingLow`, ...) chains moves without stopping; only use it when the sequence needs it.

## Axis FB pattern

Wrap each axis in an FB (`FB_Axis`) that owns `MC_Power`, `MC_Reset`, `MC_Home`, the move blocks and `MC_Halt`, calls `ReadStatus`, and exposes commands as methods (`Enable`, `Home`, `MoveAbs(position, velocity)`, `Halt`, `Reset`) plus status properties (`Ready`, `Busy`, `Error`, `ErrorId`, `ActPosition`). The FB body runs a state machine (`E_AxisState`: `Disabled`, `Standstill`, `Homing`, `Moving`, `Stopping`, `Error`). Machine sequences command the FB, never the MC blocks directly.

Use `LREAL` for positions and velocities, and units from the NC axis scaling (usually mm, mm/s, deg).
