# CTS Assemblies

Pre-built sequences of MEP Fabrication parts, built from an olet the user already modeled.
Example: **High Point** = 3" nipple > valve > adapter > cap, growing out of the olet.

## How the user works with it

1. Model the olet / sockolet on the pipe wherever it is needed (as usual).
2. Utilities pulldown > **Assemblies** opens the dockable pane. Pick a tile (HP, LP, pressure gauge...).
3. Select one or several olets in the model and press **BUILD ON SELECTED OLETS**.
   If nothing valid is selected, Revit asks you to pick the olets (Finish confirms, Esc cancels).
4. The sequence starts at the free end of each olet and follows its direction and size, so there is no up / down or diameter to type.
   The whole run is one Undo step. If one olet fails, only that one is rolled back and the pop-up says why.

Only taps (olets, sockolets...) are used from the current selection, so a pipe selected together with its olet is ignored.
When you pick with the tool, any part with a single free end is also accepted.

### Assembly Builder (**+** new, **EDIT** existing)

- Left, **Fabrication database**: service > palette > items with the database's own thumbnails. It is only a place to browse:
  assemblies **do not depend on a service**. Items are stored by palette + button name, and at build time they are looked up in
  the service of the selected olet first, then in every other loaded service.
- Items that have the little arrow in the Revit palette (nipples x1.5, x3...) show the same arrow in the builder. Click the item
  to add it with the variation chosen by size, or use the arrow to pick the exact variation. Variations are the button's
  *conditions* in the Revit API.
- Right, **Assembly**: **+ ADD ITEM** adds an empty step, **DUPLICATE** copies the selected one, **+** on a row inserts a step after it,
  the arrows reorder, the swap button replaces an item, x removes it, and the arrow on a row changes its variation later.
- Per step: size (only used when no variation is picked), rotation (valve handle), and which end connects (Auto tries each end).
- Name and 3-letter tile. Saved in `%APPDATA%\CTSRevitPlugin\Assemblies.xml` (thumbnails in `AssemblyIcons\`) and ready at every Revit start.

High Point and Low Point are created on the first run with the order nipple > valve > adapter > cap but **without database items**
(every project names its buttons differently). Open EDIT once and click the item for each step.
Files saved by the first version of the tool are migrated automatically (the old first step, the olet, is removed and a `.v1.bak` copy is kept).

## Files

| File | Role |
| --- | --- |
| `UI/Assemblies/AssemblyModels.cs` | Data model (assembly, step, item + variation) and the imperial size parser |
| `UI/Assemblies/AssemblyStore.cs` | XML persistence, migration, default HP/LP, thumbnail cache |
| `UI/Assemblies/FabricationCatalogService.cs` | Reads services / palettes / buttons / variations / thumbnails; resolves stored items |
| `UI/Assemblies/AssemblyPlacementService.cs` | The engine: chains the parts from the olet |
| `UI/Assemblies/AssembliesController.cs` | ExternalEvent queue: catalog requests and "build on selected olets" |
| `UI/Assemblies/AssembliesPane.xaml(.cs)` | Dockable pane |
| `UI/Assemblies/AssemblyBuilderWindow.xaml(.cs)` | Visual builder (modeless window) |
| `Utilities/CtsConfirmWindow.cs` | Yes/No pop-up in the CTS style |
| `Commands/Utilities/ShowAssembliesPaneCommand.cs` | Ribbon command |

## How the engine works (Revit API)

For every step: create the part (`FabricationPart.Create(document, button, condition, levelId)` for a picked variation, or by size
when the button has several variations and none was picked), align it to the free connector of the previous part
(`AlignPartByConnectorToConnector`) and connect it (`ConnectAndCouple`). In Auto mode the ends whose size already matches are tried
first and `FabricationUtils.ValidateConnectivity` picks the one that connects without a coupling. The free end left on the part is where
the next step continues; a cap has none, so the sequence ends there.

## Things to check first in a real project (not compiled / run here)

The code was written against the Revit 2024 API documentation but **was not compiled or run inside Revit**:

- **Build**: `dotnet build` (the project references `System.Drawing` for the database thumbnails).
- **Variation names**: the names shown in the builder come from `FabricationServiceButton.GetConditionName`. If your nipples show something
  other than x1.5 / x3, tell me what they show.
- **Size of parts created from a variation**: a part created from a picked variation is resized to the connection it meets by setting the connector radius
  (works for parts that have a size list). Sizes that still differ are reported as notes after the build.
- **Connection ends**: in Auto mode Revit's own connectivity check decides the end of nipples / adapters. Use *First end* / *Second end* on a step to force one.
- **Olet with several free connectors**: the first free one (lowest connector id) is used.
- Hanger buttons are hidden in the builder (they need `CreateHanger`, not supported here).

## Ideas for later

- Import / export of `Assemblies.xml` to share sequences between the team.
- Assemblies that also add a hanger or a second branch.
- A live "N olets selected" counter in the pane.
