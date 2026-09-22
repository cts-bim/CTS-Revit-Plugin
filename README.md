# CTS Revit Plugin

Native C# add-in (no pyRevit runtime) that adds the **CTS Tools** ribbon tab, six dockable
panes and a set of MEP Fabrication tools to Revit. One code base, **Revit 2023 / 2024 / 2025 / 2026**.

| Revit | Target framework          | API references         |
|-------|---------------------------|------------------------|
| 2023  | .NET Framework 4.8        | local install or NuGet |
| 2024  | .NET Framework 4.8        | local install or NuGet |
| 2025  | .NET 8 (`net8.0-windows`) | local install or NuGet |
| 2026  | .NET 8 (`net8.0-windows`) | local install or NuGet |

---

## Contents

1. [The ribbon at a glance](#the-ribbon-at-a-glance)
2. [Hanger tools](#hanger-tools)
3. [Annotation tools](#annotation-tools)
4. [Modeling tools](#modeling-tools)
5. [QC tools](#qc-tools)
6. [Utilities](#utilities)
7. [Dockable panes](#dockable-panes)
8. [Build](#build)
9. [Install](#install)
10. [Repository layout](#repository-layout)
11. [Notes for developers](#notes-for-developers)

---

## The ribbon at a glance

Everything lives on one tab: **CTS Tools**.

| Panel      | What is on it                                                                     |
|------------|-----------------------------------------------------------------------------------|
| Annotation | Smart Concat, and a *Views By Scope Box* pulldown with four view tools             |
| Hanger     | Two split buttons: **Host** and **Adjuster**                                       |
| Modeling   | Flip Elements, MEP Face Aligner, Rotate 90° CW, and stacked Align By X / Y / Z     |
| QC         | Duplicate Finder                                                                  |
| Utilities  | Parameter Cleaner, Format Painter, and a *User Interface* pulldown for the panes   |
| Support    | GitHub                                                                            |

Most tools work on the **current selection** and run inside a single Revit transaction, so
one run is one `Ctrl+Z`.

Hovering any ribbon button shows the tool's description, usage, author and version — the same
text you find below. It comes from the `[ToolDocumentation]` attribute on the command class,
so the tooltip and this file cannot drift apart.

---

## Hanger tools

### Host ▸ Hanger Without Host

Finds fabrication hangers that have no valid host.

1. Open a model view (3D or plan).
2. Run **CTS Tools ▸ Hanger ▸ Host ▸ Hanger Without Host**.
3. Every unhosted hanger visible in the view is temporarily isolated so you can see what
   needs fixing. Use Revit's *Reset Temporary Hide/Isolate* when you are done.

No selection is needed — the tool scans the active view.

### Host ▸ Connect Hanger

Connects a fabrication hanger to a compatible straight fabrication host.

1. Select **exactly two** elements: one fabrication hanger and one straight fabrication
   pipe or duct.
2. Run **CTS Tools ▸ Hanger ▸ Host ▸ Connect Hanger**.
3. The hanger is attached to the straight. If the pair is not compatible the tool says so and
   changes nothing.

### Adjuster ▸ Rod Length Adjuster

Lengthens or shortens the rods of fabrication hangers.

1. Select one or more fabrication hangers.
2. Run **CTS Tools ▸ Hanger ▸ Adjuster ▸ Rod Length Adjuster**.
3. Type an imperial adjustment. It is a *delta*, not an absolute value:
   `2"` adds two inches, `-1 1/2"` removes an inch and a half, `1' - 3"` adds fifteen inches.
4. Confirm. The status line reports how many hangers changed.

### Adjuster ▸ Struc Channel

Lengthens or shortens the horizontal strut/bearer dimension of fabrication hangers.

1. Select one or more fabrication hangers.
2. Run **CTS Tools ▸ Hanger ▸ Adjuster ▸ Struc Channel**.
3. Enter a positive or negative imperial adjustment, same format as above.

When you shorten past what the pattern allows, CTS does **not** silently give up and leave the
hanger at its original size: it finds the lowest value the Revit fabrication definition actually
accepts and moves there, then tells you which hangers hit that floor.

### Adjuster ▸ Round Strut Channel

Rounds the horizontal strut/bearer dimension of the selected hangers to the nearest whole inch.

1. Select one or more elements. Non-hangers are skipped automatically, so a rough selection is fine.
2. Run **CTS Tools ▸ Hanger ▸ Adjuster ▸ Round Strut Channel**.

Hosted bearer hangers are temporarily disconnected, resized and re-hosted, because Revit will not
validate the new dimension while the hanger is constrained to its host. If the re-host fails the
whole operation rolls back — a hanger is never left floating.

---

## Annotation tools

### Smart Concat

Concatenates text values from the selection using CTS matching rules.

1. Select the elements whose values you want to join.
2. Run **CTS Tools ▸ Annotation ▸ Smart Concat**.
3. Follow the prompt to choose what is joined and where the result is written.

### Views By Scope Box

Creates and manages the views tied to a scope box.

1. Select a scope box.
2. Run **CTS Tools ▸ Annotation ▸ Views By Scope Box ▸ Views By Scope Box**.

### Center The View

Centres the active view on what you have selected.

1. Select one or more elements.
2. Run **CTS Tools ▸ Annotation ▸ Views By Scope Box ▸ Center The View**.

### Grids 3D to 2D

Converts grid extents from 3D to 2D in the active view, so editing a grid bubble here stops
moving it everywhere else.

1. Open the view you want to fix. Select specific grids, or leave nothing selected to take
   every grid visible in the view.
2. Run **CTS Tools ▸ Annotation ▸ Views By Scope Box ▸ Grids 3D to 2D**.

### Views To Sheet

Places views onto a sheet.

1. Run **CTS Tools ▸ Annotation ▸ Views By Scope Box ▸ Views To Sheet**.
2. Pick the target sheet and the views when prompted.

---

## Modeling tools

### Flip Elements

Flips every selected element.

1. Select one or more elements. Non-fabrication elements are skipped automatically.
2. Run **CTS Tools ▸ Modeling ▸ Flip Elements**.

### MEP Face Aligner

Aligns compatible MEP fabrication faces.

1. Select the MEP elements involved.
2. Run **CTS Tools ▸ Modeling ▸ MEP Face Aligner** and follow the prompt.

### Rotate 90° CW

Rotates every selected element 90° clockwise.

1. Select one or more elements. Non-fabrication elements are skipped automatically.
2. Run **CTS Tools ▸ Modeling ▸ Rotate 90° CW**.

### Align By X / Align By Y / Align By Z

Lines the selection up on one axis. The **first** element you pick is the reference; everything
else moves to it.

1. Select two or more elements, starting with the one that is already in the right place.
2. Run **CTS Tools ▸ Modeling ▸ Align By X** (or **Y**, or **Z**).

---

## QC tools

### Duplicate Finder

Finds overlapping elements in the active model view, by category.

1. Open the view you want to check.
2. Run **CTS Tools ▸ QC ▸ Duplicate Finder**.
3. Tick one or more categories present in the view.
4. The overlaps found are temporarily isolated for review.

Detection compares transformed element bounding boxes. Treat it as a visual duplicate/overlap
detector, **not** a geometric clash engine — two elements that merely share a bounding box will
be reported.

---

## Utilities

### Parameter Cleaner

Clears all editable parameters on the selected elements.

1. Select one or more elements. Non-fabrication elements are skipped automatically.
2. Run **CTS Tools ▸ Utilities ▸ Parameter Cleaner**.

### Format Painter

Copies safe, editable text and numeric *instance* data from one element to others.

1. Run **CTS Tools ▸ Utilities ▸ Format Painter**.
2. Click the **source** element.
3. Click each **target** element in turn.
4. Press `ESC` when you are finished.

Geometry, ElementId, host, location and the fabrication values that drive the model are
deliberately excluded — the tool copies annotation-style data, not shape.

### GitHub

**CTS Tools ▸ Support ▸ GitHub** opens the repository in your default browser.

---

## Dockable panes

Open them from **CTS Tools ▸ Utilities ▸ User Interface**. Each is a normal Revit dockable
pane: dock it, float it, or close it with the × in its corner.

### CTS Mechanical Properties

The fabrication data that the Revit Properties palette does not show. Select an MEP Fabrication
element and the pane fills in five sections:

| Section                  | What it holds                                                          |
|--------------------------|------------------------------------------------------------------------|
| General Part Information | Fitting Type, Pattern Number, Service Type, Service Abbreviation, Service Name, Buy-Out, Status |
| Connectors               | Every connector on the part                                            |
| Dimensions               | Every fabrication dimension, editable where the pattern allows it       |
| Ancillaries              | The extra hardware that completes the part — nuts, bolts, rods          |
| Custom Data              | The custom data entries defined in the loaded Fabrication Configuration |

**How to use it**

1. Open the pane, then click an MEP Fabrication element in the model.
2. Click a section header to collapse or expand it. Use the search box to filter across all sections.
3. Edit any field that is not greyed out. **Nothing is written as you type** — changes stack up
   and **APPLY** lights up.
4. Press **APPLY**. The whole set is written in one transaction, so it is one undo step. The pane
   then re-reads the element, because Revit may snap a dimension to the nearest size its
   definition accepts.
5. Press **REFRESH** to throw away pending changes and re-read the model. This also drops the
   cached Fabrication Configuration, which is what you want after loading a different one.

**Multiple elements.** Select several parts and the pane shows only what they have in common;
where they disagree the value reads `<varies>`. Editing a shared row applies it to all of them.

**Ancillaries load on demand.** The section holds a `...` button instead of data, because that
lookup is the slowest call in the pane. Click it when you need it.

**Element limit.** A selection is capped at **100 elements** by default. Anything beyond the cap
is reported in the status line rather than silently ignored. Change
`MechanicalPropertiesElementLimit` in the settings file if you need more — and expect it to get
slower as you raise it.

### CTS Element Filter

Rule-based select, isolate, hide, halftone and transparency.

1. Open the pane and press **+** to create a filter, or **EDIT** on an existing one.
2. In the designer: name the filter, pick the categories it applies to, choose **Match ALL (AND)**
   or **Match ANY (OR)**, and set **Look in** to the active view or the whole model.
3. Add one or more conditions. The parameter box is a *suggestion list*, not a limit — you can
   type any parameter name. Tick **Ask when run** to be prompted for the value each time.
4. Pick a **Default action**, then **SAVE**.
5. Back in the palette: select a filter and press an action button, or double-click it to run
   its default action.
6. **RESET VIEW** undoes isolate, hide, halftone and transparency.

Filters are stored per Revit version and can be exported and imported to share across a project.

### CTS Assemblies

Pre-built sequences of MEP Fabrication parts (High Point, Low Point, gauge sets) modelled onto a
pipe in one click.

1. Pick an assembly from the list.
2. Type the diameter.
3. Press **PLACE ON PIPE**, then click a point on a fabrication pipe.
4. Use **+** or **EDIT** to build your own sequences from the fabrication database.

Assemblies are saved per Revit version in
`%APPDATA%\CTSRevitPlugin\<version>\Assemblies.xml` and load automatically when Revit starts.

### CTS Fabrication Assemblies

Builds and places reusable assemblies straight from the Revit fabrication database.

1. Open the pane and create an assembly sequence.
2. Choose service, diameter and direction.
3. Place it on a fabrication pipe.

Templates are stored locally in the CTS Revit Plugin application data folder.

### CTS Hangers

The hanger adjustment tools in pane form, so you can nudge rod and strut values repeatedly
without going back to the ribbon each time.

### CTS Parameters

Shows the parameters of the current selection and lets you pin the ones you care about so they
stay at the top.

### CTS Shortcuts

A launcher for the CTS tools and for native Revit commands, so your most-used commands sit in
one place.

---

## Build

Requirements: Windows, [.NET SDK 8 or newer](https://dotnet.microsoft.com/download), PowerShell 5.1+.

Revit does **not** need to be installed for every version: if a Revit version is not found in
`C:\Program Files\Autodesk\Revit <year>\`, its API is restored from NuGet
(`Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI`).

```powershell
.\Build.bat                                 # all versions, Release, + dist\CTSRevitPlugin.bundle
.\build.ps1                                 # all versions, Release, no bundle
.\build.ps1 -Versions 2025,2026             # only some versions
.\build.ps1 -Versions 2024 -Configuration Debug -Install   # dev loop, see below
```

Single version straight from `dotnet`:

```powershell
dotnet build src\CTSRevitPlugin -p:RevitVersion=2025
```

Visual Studio: open `CTSRevitPlugin.sln`. It builds the version set in `Directory.Build.props`
(default 2024). Change that one line, or set the `RevitVersion` environment variable, to switch.

---

## Install

### A) Bundle (recommended for other machines)

1. Run `Build.bat`.
2. Copy `dist\CTSRevitPlugin.bundle` to `%AppData%\Autodesk\ApplicationPlugins\` (per user) or
   `%ProgramData%\Autodesk\ApplicationPlugins\` (all users).
   `.\build.ps1 -Bundle -InstallBundle` does the per-user copy for you.
3. Start Revit — the **CTS Tools** tab appears. `PackageContents.xml` makes each Revit version
   load only its own `Contents\<year>\` folder.

### B) Single Setup.exe (for colleagues)

Install [Inno Setup 6.3+](https://jrsoftware.org/isdl.php), then:

```powershell
.\build.ps1 -Installer
```

Produces `dist\CTSRevitPlugin_Setup_5.0.0.exe`. It installs the bundle to
`%ProgramData%\Autodesk\ApplicationPlugins\CTSRevitPlugin.bundle` (needs admin, all users) and
registers an uninstaller in Windows "Apps". The script is `installer\CTSRevitPlugin.iss`.

The first time Revit starts with the plugin it asks whether to load the (unsigned) add-in:
choose **Always Load**.

### C) Dev loop (your own machine)

```powershell
.\build.ps1 -Versions 2024 -Configuration Debug -Install
```

Writes `CTSRevitPlugin.addin` to `%ProgramData%\Autodesk\Revit\Addins\2024\` pointing at
`artifacts\bin\2024\Debug\CTSRevitPlugin.dll`. Close Revit before rebuilding — Revit locks the DLL.

> If you used the old `build-and-install.ps1`, an add-in file from it already exists in
> `%ProgramData%\Autodesk\Revit\Addins\2024\`. The new script overwrites the same file name, so
> there is no duplicate. If you also install the bundle, delete the dev `.addin` first, otherwise
> Revit loads the plugin twice.

---

## Repository layout

```
CTSRevitPlugin/
|-- build.ps1                 build / install / package script (all versions)
|-- Build.bat                 double-click: Release build of all versions + bundle
|-- Directory.Build.props     default RevitVersion + artifacts\ output folders
|-- CTSRevitPlugin.sln
|-- deploy/
|   `-- CTSRevitPlugin.addin.template
|-- docs/
|   |-- MULTI_VERSION.md      how the multi-version build works, API differences
|   |-- ACTIONS.md            change log / action items
|   `-- ASSEMBLIES.md         CTS Assemblies (O-Let builder) documentation
|-- legacy/OriginalPython/    the original pyRevit scripts, for side-by-side comparison
`-- src/CTSRevitPlugin/
    |-- CTSRevitPlugin.csproj
    |-- App.cs                ribbon, dockable panes, theme
    |-- Compat/               Revit-version differences (ElementId, ...)
    |-- Commands/             IExternalCommand classes (Annotation, Hanger, Modeling, QC, Utilities)
    |-- UI/                   WPF panes / windows (Assemblies, ElementFilter, ElementTools, Utilities)
    |-- Utilities/            shared helpers and dialogs
    |-- Resources/Icons/      ribbon and pane icons
    `-- Samples/              MyCommand.cs - hello-world test command (not wired to the ribbon)
```

Generated folders (ignored by git):

```
artifacts/bin/<2023|2024|2025|2026>/<Configuration>/   CTSRevitPlugin.dll + Resources\Icons
artifacts/obj/...                                      intermediate files, one set per Revit version
dist/CTSRevitPlugin.bundle/                            ready-to-install package (+ .bundle.zip)
```

---

## Notes for developers

* User settings live per user in `%AppData%\CTSRevitPlugin\` and are shared by all Revit versions.
* Revit 2023 has no dark UI theme, so the panes always use the light theme there.
* Read `docs/MULTI_VERSION.md` before touching anything involving `ElementId`, the UI theme API,
  or the Revit API in general.
* **Documenting a tool.** Put a `[ToolDocumentation]` attribute on the command class. The ribbon
  tooltip, the Element Tools pane and the Shortcuts pane all read it by reflection — there is no
  second place to update.
* **Theming.** `Themes/DarkTheme.xaml` and `Themes/LightTheme.xaml` are structurally identical and
  differ only in colour. Anything that can host text is templated explicitly, because the WPF
  defaults paint parts of a control with system colours that ignore the theme brushes. Two traps
  worth knowing: a `CheckBox` needs a full template or its tick keeps the system colour and
  disappears on the dark theme, and a custom `ComboBox` template must contain a part named
  `PART_EditableTextBox` or an `IsEditable` combo renders no text at all.
* **Fabrication API.** Parts of the fabrication surface differ between releases, so the code
  reaches them by reflection with a fallback (`GetRodInfo`, `GetHostedInfo`, part statuses, custom
  data definitions, ancillaries). These paths must be validated against real fabrication content
  on every Revit version — a missing member degrades gracefully instead of throwing, which means a
  silent empty section is a possible failure mode.
* **Performance.** CTS Mechanical Properties reads a fixed field list and caches the Fabrication
  Configuration per document. If you add a section, keep both properties: enumerating every
  parameter or re-reading the configuration per click is what made the first version stutter.
