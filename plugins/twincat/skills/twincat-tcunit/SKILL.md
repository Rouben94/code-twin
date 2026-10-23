---
name: twincat-tcunit
description: Write and maintain TcUnit unit tests for TwinCAT 3 PLC code - test suite structure, TEST/TEST_FINISHED, typed asserts, multi-cycle tests, test program setup, and what the agent can and cannot do (build yes, run no). Use when adding or fixing PLC unit tests.
---

# Unit tests with TcUnit

TcUnit (https://tcunit.org, MIT license) is the de-facto unit test framework for TwinCAT 3. Tests are PLC code: they are built with the project and run when the PLC runs. The API reference is https://github.com/TcUnit/TcUnit/blob/master/docs/api.md; check it when unsure about an assert's parameters.

## Prerequisites

- The PLC project references the `TcUnit` library. If `plc_build` reports unknown `TcUnit.*` types, ask the user to add the reference in the Library Manager. CodeTwin cannot add references.
- Running tests is an online action (activate configuration, login, start). The agent **creates tests and builds them**; the user runs them. Results appear in the error list / ADS log as TcUnit messages.

## Structure

- One test suite per unit under test: `FB_<Unit>_Test EXTENDS TcUnit.FB_TestSuite`, placed in a `Test` folder (or a separate test PLC project if the user prefers).
- One `PRIVATE` method per test case. The suite body calls every test method each cycle.
- A test program instantiates all suites and calls `TcUnit.RUN();` every cycle. It is called from its own task or from `MAIN` only in the test configuration.

```st
FUNCTION_BLOCK FB_Calculator_Test EXTENDS TcUnit.FB_TestSuite
VAR
END_VAR
// body
AddsTwoPositiveNumbers();
AddsNegativeNumber();
```

```st
METHOD PRIVATE AddsTwoPositiveNumbers
VAR
    fbCalc : FB_Calculator;
END_VAR
// implementation
TEST('AddsTwoPositiveNumbers');
fbCalc(nA := 2, nB := 3);
AssertEquals_INT(Expected := 5, Actual := fbCalc.nSum, Message := 'Sum of 2 and 3');
TEST_FINISHED();
```

```st
PROGRAM PRG_Test
VAR
    fbCalculatorTest : FB_Calculator_Test;
END_VAR
// implementation
TcUnit.RUN();
```

With CodeTwin: create the `Test` folder if missing (`kind` `folder`), create the FB with `plc_create_element` (`kind` `functionBlock`, declaration with `EXTENDS TcUnit.FB_TestSuite`), add each test with `kind` `method`, write bodies with `plc_write_element`, then `plc_build`.

## Asserts

- Prefer the typed variants: `AssertEquals_BOOL`, `_INT`, `_DINT`, `_UDINT`, `_STRING`, `_TIME`, ... and `AssertTrue` / `AssertFalse`.
- Floating point: `AssertEquals_REAL` / `AssertEquals_LREAL` with a `Delta` tolerance.
- Arrays: `AssertArrayEquals_<TYPE>` (for example `_INT`, `_BOOL`, `_BYTE`).
- Generic `AssertEquals(Expected, Actual, Message)` takes `ANY`; never pass a **property** to it (crashes or errors depending on TwinCAT version). Copy into a local variable or use a typed assert.
- Every assert has a `Message`; make it say what was expected in domain terms.

## Test design

- One behaviour per test; name tests after the behaviour (`StopsWhenLimitReached`), not the method.
- Arrange - act - assert inside `TEST(...)` ... `TEST_FINISHED();`.
- Inject dependencies through interfaces so a test can use a mock FB instead of hardware (see `twincat-oop`).
- Multi-cycle tests: keep the unit under test and any timer in `VAR_INST` of the test method, and call `TEST_FINISHED()` only when the condition is reached.
- To run tests in order use `TEST_ORDERED('Name')` (returns `TRUE` when it is that test's turn) and/or `TcUnit.RUN_IN_SEQUENCE()`.
- Disable a test temporarily by prefixing its name with `DISABLED_`; say so in your report.
- Hardware I/O (`AT %I*`, `AT %Q*`) and protected variables can be set with TcUnit's `WRITE_PROTECTED_<TYPE>` helpers.
- Default limit is 100 tests per suite (`GVL_Param_TcUnit.MaxNumberOfTestsForEachTestSuite`); split large suites.

## CI

TcUnit-Runner (https://github.com/TcUnit/TcUnit-Runner) runs tests from a build server and produces xUnit results. Mention it if the user asks about pipelines; setting it up is out of scope for the agent.
