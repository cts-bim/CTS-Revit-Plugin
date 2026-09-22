# CTS Revit Plugin – Action Items Fix Pack

This revision addresses the latest PDF error list:

1. Round Strut Channel is silent when all selected elements are hangers; mixed selections report skipped elements. The WPF action uses the same dimension resolver and verifies the resulting fabrication dimension after regeneration.
2. Added CTS GitHub button using `CTS Icon.png`.
3. Annotation now uses a Views By Scope Box pulldown; Modeling uses three large tools plus stacked Align X/Y/Z tools.
4. Removed the orange backing square behind the CTS pane header icons so Parameters/Hangers/Shortcuts display their actual resource icons.
5. Modeling ribbon layout matches the requested three-large + three-stacked arrangement.
6. Added a fourth dockable pane: CTS Mechanical Properties.
7. CTS Shortcuts can now catalog native Revit `PostableCommand` commands plus external add-in ribbon commands exposed by the Revit ribbon API. Selected commands can be executed through `PostCommand`.
8. Hangers keeps a consistent adjustment layout for Rod Length and Strut Channel, with minus/plus controls adjacent to the value field.
9. Search remains in CTS Shortcuts and is not present in CTS Hangers. The settings dialog also has a shortcut-command search field.
10. CTS WPF dialogs/windows use `CTS Icon.png` as the window icon instead of the generic icon.
11. WPF text colors are theme resources and adapt to Revit Light/Dark mode.
12. Format Painter is placed in the Utilities panel at the right side of the CTS ribbon.
13. Tooltips no longer repeat the tool name; the hover body starts with the description and usage information.

The project targets Revit 2023 - 2026 (.NET Framework 4.8 for 2023/2024, .NET 8 for 2025/2026); see `MULTI_VERSION.md`.

## CTS Assemblies (new)

14. Added a fifth dockable pane, **CTS Assemblies** (Utilities pulldown > Assemblies). The user models the olet on the pipe as usual, selects it (or several) and the pane builds a pre-defined sequence of MEP Fabrication parts (High Point, Low Point, gauges...) from its free end. See `ASSEMBLIES.md`.

---

# Fix Pack – Mechanical Properties rescope, theme contrast, docs

1. **Theme: text and glyphs now follow the background.** Two separate defects, both in
   `UI/Utilities/Themes/*.xaml` and fixed in both themes:
   * `CheckBox` and `RadioButton` had brush setters only, so the WPF default template kept
     painting the tick and the bullet with system colours. On the dark theme that mark sits on
     `InputBrush` and disappears — the Filter Designer reported "1 category(ies) selected" with
     no visible check. Both controls now have a full `ControlTemplate`: the box fills with
     `PrimaryBrush` and the mark is drawn in `AccentTextBrush`, legible on light and dark.
   * The custom `ComboBox` template had no `PART_EditableTextBox`. WPF resolves that part by
     name, and without it an `IsEditable` combo renders no text at all. The Filter Designer
     condition rows use `IsEditable = true`, which is why the parameter box looked empty. The
     part was added, and the drop-down toggle now moves to the arrow column while the box is
     editable so it no longer swallows clicks meant for the editor.

2. **CTS Mechanical Properties rescoped to the fabrication data (v1).** The pane used to walk
   every parameter, call `AsValueString` on each, and reflect over `FabricationPart`, `RodInfo`
   and `HostedInfo` — hundreds of values per click, which is what made selection stutter. It now
   reads a fixed set of sections: General Part Information, Connectors, Dimensions, Ancillaries
   and Custom Data. Everything else is already one click away in the Revit Properties palette.
   * The Fabrication Configuration (services, statuses, custom data definitions) is cached per
     document instead of being read per click. **REFRESH** drops the cache, which is what you
     want after loading a different configuration into the same model.
   * Ancillaries load only when the user opens that section.
   * Multi-selection shows the intersection; disagreeing values read `<varies>`.
   * A selection is capped at `MechanicalPropertiesElementLimit` (default 100) and the overflow
     is reported in the status line rather than dropped silently.
   * Edits are staged and written by **APPLY** in one transaction — one undo step per press.
     Dimensions go through the hanger tools' existing `TrySetDimensionValue`; everything else is
     an ordinary parameter write. The pane re-reads afterwards because Revit may snap a
     dimension to the nearest size its definition accepts.
   * Out of scope for v1, by agreement: User-Defined parameters and Carry Over.

3. **New `README.md`** explaining what every tool does and how to use it step by step, plus the
   dockable panes, build, install and the developer notes.

4. **Authorship.** `Hanger Without Host` and `Connect Hanger` are now credited to Bruno Silva.

---

# Fix Pack – Assembly Builder: taller lists, product entry

1. **SERVICE and PALETTE lists are two rows taller** (124 → 178). They were showing four
   entries, which is not enough to tell similar service names apart without scrolling.

2. **PRODUCT ENTRY per step.** A reducing part (bushing, reducer, swage) carries a product entry
   such as `3/4x1/8`, and that entry decides the **small end** — the end the next step has to
   connect to. Letting Revit choose is harmless on a nipple and wrong on a bushing.
   * This is *not* the arrow on the database tile. That arrow picks the button's
     `FabricationServiceButton` conditions; the product entry is a separate catalogue that lives
     on the part.
   * Revit exposes a product list only through a part that exists (`IsProductList`,
     `GetProductListEntryCount`, `GetProductListEntryName`). The builder therefore creates one
     sample part inside a transaction that is **always rolled back**, reads the list and discards
     it. Nothing reaches the model.
   * The list is size-dependent — a bushing built at 3/4" only offers `3/4x...` — so the probe
     uses the step's fixed size when it has one and 3/4" otherwise, and the hint under the box
     says which size the list was read at.
   * Placement re-reads the list from the real part at the real connection size, overwrites the
     step's cached list and saves the assembly, so the dropdown becomes exact after the first run.
   * `ProductListEntry` is applied **before** alignment and connection: Revit refuses the change
     once it would resize an end that is already connected.
   * A value that does not exist on the part is not a silent failure — the run warns and lists
     the entries that are actually available.
