---
name: twincat-visu
description: TwinCAT 3 PLC visualizations (TcVIS, also served as web visu) - reading and changing visualization elements through CodeTwin, the TcVIS XML structure, element property IDs for position, text, variables and visibility, and safe editing rules. Use when reading, explaining, creating or changing a PLC visualization.
---

# PLC visualizations (TcVIS)

A PLC visualization is stored as TcVIS XML, a serialization of the visualization editor's objects. The Automation Interface does not expose the elements, so CodeTwin works on that XML:

- `plc_read_visualization` returns the XML.
- `plc_write_visualization` replaces the visualization with changed XML. If TwinCAT cannot load it, the original is restored and the tool returns an error.
- `plc_create_element` with `kind` `visualization` creates an empty one.

Never edit `.TcVIS` files with file tools.

Look up element types, their properties and web visu behavior in InfoSys ("TwinCAT 3 PLC > Visualization") via the `infosys-search` skill before relying on the property IDs below.

## Workflow

1. Read the visualization. Large ones (alarm or diagnostics pages) can be several MB: work on the elements you need, don't reprint the file.
2. Change values in place. Keep everything else byte for byte, including GUIDs, `Id` values and the `GeneratedLMMDescriptions` block.
3. Write it back with the full XML. `<Visu Name="...">` must stay the element's name; rename with `plc_rename_element`.
4. Run `plc_build`. Variables referenced by the visualization are compiled; a wrong variable path is a build error.
5. Tell the user to check the result in the visualization editor. Layout cannot be verified from the XML alone.

## Structure

```xml
<TcPlcObject Version="1.1.0.1">
  <Visu Name="Visu1" Id="{guid}">
    <XmlArchive><Data>
      <o xml:space="preserve" t="VisualObject">
        <v n="UniqueIdGenerator">"3"</v>
        <o n="VisualElemList" t="VisualElemList">
          <l n="VisualElementList" t="VisualElemCollection" cet="GenericVisualElem">
            <o> ... one element ... </o>
          </l>
          <v n="BackgroundColor">16777215</v> ...
        </o>
        <o n="GeneratedLMMDescriptions" ...> ... </o>
```

Serialization tags: `<o>` object, `<v>` value, `<l>` list, `<a>` array, `<d>` dictionary, `<n>` null. `n` is the member name, `t` the type. Strings keep their quotes inside the element (`<v n="Value">"Start"</v>`); `t="Int16"` marks a short; `L` / `U` suffixes mark long / unsigned values.

Each element has:

| Member | Meaning |
| --- | --- |
| `VisualElementTypeName` | `VISUFBELEMSIMPLE` (rectangle, rounded rectangle, ellipse, line), `VISUFBELEMBUTTON`, `VISUFBELEMPOLYGON`, `VisuFbGroup`, ... |
| `VisualElementName` | Display name in the editor (`"Rectangle"`, `"Button"`). |
| `VisualElementIdentifier` | Unique instance name, `"GenElemInst_<n>"`. |
| `VisualElementIdentification` | Unique GUID of the element. |
| `VisualElementId` | Unique number within the visualization. |
| `VisualElemMemberList` | The element's properties as `Id` / `Value` pairs (below). |
| `ConfiguredComplexInputs` | Input configuration (`SignatureName` `Visu_ToggleInput`, `Visu_TapInput`, ...), with its own `VisualElemMemberList`. |

## Property IDs

Properties are keyed by a numeric hash, not a name. Confirmed from projects (geometry by arithmetic, the rest from consistent use):

| Id | Property | Example |
| --- | --- | --- |
| `1649127785` | X (left) | `t="Int16">40` |
| `357335551` | Y (top) | `t="Int16">20` |
| `2422045748` | Width | `t="Int16">400` |
| `2134141914` | Height | `t="Int16">90` |
| `550940142` | Center X (X + Width / 2) | `t="Int16">240` |
| `1473355128` | Center Y (Y + Height / 2) | `t="Int16">65` |
| `564465120` | Shape | `"VISU_ST_RECTANGLE"`, `"VISU_ST_ROUNDRECT"`, `"VISU_ST_CIRCLE"`, `"VISU_PT_POLYLINE"` |
| `390574330` | Text (static text, may hold `%s`-style format placeholders) | `"Start"` |
| `2340015797` | Horizontal text alignment | `"LEFT"`, `"HCENTER"`, `"RIGHT"` |
| `2565699834` | Vertical text alignment | `"TOP"`, `"VCENTER"` |
| `3729828405` | Font (list: name, size, flags, charset, color) | `"Arial"`, `t="Single">12` |
| `1782330054` | Fill style | `"BS_SOLID"`, `"BS_HOLLOW"` |
| `2827249010` | Line style | `"PS_SOLID"`, `"PS_HOLLOW"` |
| `2678395525` | Line width | `t="Int16">1` |
| `1186196937` | Variable of an input (inside `ConfiguredComplexInputs`, toggle or tap) | `"MAIN.bStart"` |

Likely, verify in the editor before relying on them:

| Id | Property |
| --- | --- |
| `2477733581` | Text variable (value shown in the text) |
| `2880254039` | Invisible (expression, e.g. `"NOT MAIN.bExpert"`) |
| `1999528970` | Color variable / toggle color |
| `571893170` | Tooltip |
| `494569607`, `2812299069`, `135947015`, `493260384` | Frame and fill colors, normal and alarm state |

Colors are ARGB as unsigned 32-bit numbers in a list: `4278190080` = `0xFF000000` black, `4294967295` = `0xFFFFFFFF` white, `4294901760` = `0xFFFF0000` red.

## Changing elements

- **Text, position, size, colors, variables:** change the `Value` next to the `Id`. When you move or resize, update the center values as well.
- **Variables** use the full instance path the PLC compiles (`MAIN.fbAxis.bReady`, `GVL_Io.bDoorClosed`).
- **New element:** copy an existing element of the same type from the same or another visualization. Give the copy a new `VisualElementIdentification` GUID, the next free `VisualElementIdentifier` (`GenElemInst_<n>`) and `VisualElementId`, and raise `UniqueIdGenerator` above every used number. Build afterwards; if TwinCAT rejects the file, the original stays in place.
- **Delete an element:** remove its `<o>` from `VisualElementList`.
- Don't hand-write elements from scratch: unknown members get default values that differ from the editor's.
