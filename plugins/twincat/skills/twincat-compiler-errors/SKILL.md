---
name: twincat-compiler-errors
description: Diagnose and fix TwinCAT 3 PLC compiler errors and warnings (Cxxxx codes) after plc_build - triage order, frequent codes and their usual causes, and how to look up any code on Beckhoff InfoSys. Use whenever plc_build or plc_get_errors reports problems.
---

# Fixing TwinCAT compiler errors

## Workflow

1. `plc_build`, then `plc_get_errors` if the build result does not list everything.
2. Fix the **first error of each element first**. One syntax error (missing `;`, `END_IF`, `THEN`) often causes a cascade of follow-up errors in the same element; rebuild after fixing it instead of chasing the rest.
3. `plc_read_element` the element named in the error before changing it. The error may originate elsewhere (a changed type, a renamed method, a missing library); search with `plc_search_elements`.
4. Look up any code you do not know: run `infosys-search` with `Compiler Error C0xxx` (or `Compiler Warning C0xxx`). The pages give the message, cause and a minimal example.
5. Fix the cause with the smallest change, rebuild, repeat. Stop after 5 rounds without progress and report what is left.
6. Treat warnings as work: fix them unless they existed before your change. Do not silence them with `{warning disable ...}` unless the user asks.

## Frequent codes

| Code | Message (short) | Usual cause and fix |
| --- | --- | --- |
| C0004 | 'x' is not a component of 'y' | Wrong member, method or property name; typo; member is `PRIVATE`. Read the type. |
| C0006 | 'x' expected instead of 'y' | Syntax: missing `THEN`, `DO`, `OF`, `;`, `:=`, or an unclosed block. Look at the line before. |
| C0018 | 'x' is no valid assignment target | Writing a constant, an input of a called FB from outside, a property without `Set`, or an undeclared name. |
| C0027 | Size of string expected | `STRING()` without a length. |
| C0032 | Type 'A' cannot be converted to 'B' | Incompatible assignment; use an explicit conversion (`TO_INT`, `LREAL_TO_DINT`, ...) or fix the types. |
| C0035 | Program name, function or FB instance expected | Calling something that is not callable, or a misspelled call. |
| C0046 | Identifier 'x' not defined | Undeclared variable; missing namespace for `qualified_only` enums or GVLs (`E_State.Idle`, `GVL_X.someVar`); missing library reference. |
| C0076 | Unexpected structure initialization | Struct initializer `(a := 1)` on a non-struct type, or syntax error inside it. |
| C0077 | Unknown type: 'x' | Typo in a type name, DUT not created yet, or the type lives in a library that is not referenced. |
| C0195 | Implicit conversion from signed to unsigned (warning) | Mixed signed/unsigned arithmetic; convert explicitly. |

Codes and messages can change between TwinCAT versions; trust InfoSys over this table.

## Causes the compiler reports indirectly

- **Missing library**: unknown types or functions from `Tc2_MC2`, `Tc2_Standard`, `TcUnit`, etc. CodeTwin cannot add library references; ask the user to add it in the Library Manager.
- **Changed signature**: after changing an FB's inputs, a method's parameters or a return type, every caller fails. Search for callers and update them in the same change.
- **Function return value**: a `FUNCTION F_X : INT` must assign `F_X := ...;`. A missing assignment may compile cleanly and return 0; check it yourself.
- **Element body and declaration**: errors in the declaration (`VAR ... END_VAR`) are fixed with `plc_write_element` `declaration`, logic with `implementation`. Do not move code between them.

## When stuck

- Rebuild once more: stale errors sometimes clear after the first fix.
- Reduce: comment the new code out, rebuild, then re-add it piece by piece.
- Report to the user with the exact error text, the element path and what you tried. Never "fix" by deleting the user's code.
