# Spike: PLC visualizations (TcVIS) through the Automation Interface

Tested on TwinCAT 3.1.4026 in a hidden TcXaeShell (`TcXaeShell.DTE.17.0`), new solution, Standard PLC Template.

## Results

| Test | Result |
| --- | --- |
| `CreateChild(name, 619, "", null)` on the PLC project | Creates the visualization. The first one also adds `Visualization Manager` (620) and `GlobalTextList` (625). |
| `ProduceXml` on the visualization | Only the tree item and its VS properties (file name, `FullPath`, size mode). No elements. |
| `ITcXmlDocument.DocumentXml` | Not implemented by visualization items. |
| `ITcPlcIECProject.PlcOpenExport` of a visualization | Fails: `Couldn't create ExportObjects`. |
| `.TcVIS` file on disk | CODESYS `XmlArchive` serialization: `<o t="VisualObject">` with `VisualElemList`, per-element objects, GUIDs and a generated FB description. |
| `CreateChild(name, 58, "", "<path>.TcVIS")` on the PLC project | Imports the file as a new visualization. The name comes from the file (`<Visu Name=...>`), not from `name`. |

## Changing one visualization without reloading the project

Marker: `BgUseColor` changed in the `.TcVIS` (`BackgroundColor` is recomputed on load and is no marker). A rename forces the shell to write the object again.

| Test | Result |
| --- | --- |
| Edit the `.TcVIS` on disk, then rename and save | The edit survives, also when the object was loaded before the edit. No dialog with `SuppressUI`. |
| Import 58 with a name that already exists | Fails: `An object with the specified name already exists`. |
| `DeleteChild`, then import 58 of the changed file | Works; the content is kept. `DeleteChild` also deletes the file, so the changed copy must live outside the project folder. |

## Conclusion

The only route to the visualization's content is the `.TcVIS` XML. Two ways to apply a change, both without reloading the project:

- Delete and import (sub-type 58) through the Automation Interface: verified and explicit.
- Write the file in place: worked in the hidden shell, but not tested with the visualization open in an editor, with the UI showing "modified outside" prompts, or for whether a build uses the new content.

## Implementation (delete and import)

`plc_read_visualization` / `plc_write_visualization` in `PlcTreeReader`. Verified in the hidden shell with a real 22 KB visualization, at project level and in a folder, written twice in a row; the build passes.

- `LookupTreeItem` does not find a freshly imported object; enumerating the parent's children does (CodeTwin always enumerates).
- Content TwinCAT cannot load does not make the import fail: it adds a plain file item `<name>.TcVIS` (item type 506) and the visualization is gone. CodeTwin checks for a visualization with the name after the import; if there is none, it deletes the file item, imports the original and returns an error.

## Still open

1. Replacing a visualization that is open in an editor in the visible shell.
2. Whether references to it (frames, the Visualization Manager start visualization) survive delete and import.
3. Adding elements: whether new element GUIDs and `GeneratedLMMDescriptions` need more than the rules in the `twincat-visu` skill.
4. Property IDs marked "likely" in the skill.
