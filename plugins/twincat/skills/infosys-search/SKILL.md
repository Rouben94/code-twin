---
name: infosys-search
description: Query the Beckhoff Information System (InfoSys) live for TwinCAT, PLC library, motion, I/O and compiler-message documentation. Use before answering any TwinCAT question from memory.
---

# InfoSys live search

Beckhoff InfoSys (https://infosys.beckhoff.com) is the primary reference for TwinCAT. Its content is Beckhoff copyright: query it live, quote only the short passage you need and always link the page. Never download it in bulk or keep a local copy.

## Search

Run the script that lives next to this file (this skill's directory):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "<skill directory>\Search-InfoSys.ps1" -Query "MC_MoveAbsolute"
powershell -NoProfile -ExecutionPolicy Bypass -File "<skill directory>\Search-InfoSys.ps1" -Query "Namenskonventionen" -Language de -First 5
```

It submits the InfoSys search form and prints title, URL and snippet for each hit of the first result page (default 10, `-First` up to 50). Language `en` (default) or `de`.

Then fetch the best hit with the web fetch tool and read the relevant section.

## Query tips

- Use exact identifiers: `MC_MoveAbsolute`, `FB_FileOpen`, `C0077`, `qualified_only`, `ADSERR_DEVICE_SRVNOTSUPP`.
- Add the library name to narrow results: `Tc2_MC2 MC_Power`, `Tc2_System FB_FileOpen`.
- Compiler messages have their own pages titled `Compiler Error Cxxxx` / `Compiler Warning Cxxxx`.
- If English returns nothing, try German (`-Language de`) or a shorter query.

## Useful entry points

URLs follow `https://infosys.beckhoff.com/content/<lcid>/<topic>/<id>.html` with `1033` for English and `1031` for German. Library manuals start at `https://infosys.beckhoff.com/content/1033/tcplclib_<library in lower case>/index.html`, for example `tcplclib_tc2_mc2`, `tcplclib_tc2_system`, `tcplclib_tc2_standard`, `tcplclib_tc2_utilities`. These index pages are title pages only; use the search to find the topic pages.

## If the search fails

If the script fails (network, changed page layout), say so, then fall back to a web search restricted to `infosys.beckhoff.com`, and only then to other official sources.
