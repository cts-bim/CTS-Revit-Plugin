# Multi-version notes (Revit 2023 - 2026)

## How the build picks a version

`RevitVersion` (2023 | 2024 | 2025 | 2026) is an MSBuild property. `Directory.Build.props` defaults it to
2024; `build.ps1` passes `-p:RevitVersion=<year>` for each version.

The csproj derives everything else from it:

* target framework: `net48` for 2023/2024, `net8.0-windows` for 2025/2026 (Revit 2025 moved to .NET 8);
* preprocessor symbols: `REVIT2023` ... `REVIT2026`, plus `REVIT2024_OR_GREATER`,
  `REVIT2025_OR_GREATER`, `REVIT2026_OR_GREATER`;
* API references: the installed `RevitAPI.dll` / `RevitAPIUI.dll` if found, otherwise the
  `Nice3point.Revit.Api.*` NuGet packages (`-p:UseRevitNuGet=true|false` forces one or the other);
* output: `artifacts\bin\<year>\<Configuration>\`, intermediates in `artifacts\obj\CTSRevitPlugin\<year>\`.

## Differences already handled in the code

| Area | Revit 2023 | Revit 2024+ | What the code does |
|------|-----------|-------------|--------------------|
| ElementId number | `IntegerValue` (int), `new ElementId(int)` | `Value` (long), `new ElementId(long)`; `IntegerValue` and the int constructor are **removed in 2026** | `Compat/ElementIdCompat.cs`: `id.IdValue()` and `number.ToElementId()`. All former `.Value` uses on ElementId go through them. |
| UI theme API | not available | `UIThemeManager.CurrentTheme`, `UIControlledApplication.ThemeChanged` (dark theme arrived in 2024) | `#if REVIT2024_OR_GREATER` in `App.cs` and `UI/Utilities/RevitThemeService.cs`. On 2023 the light theme is always used. |
| Type names | - | 2025+ adds `Autodesk.Revit.UI.ContextMenu` / `MenuItem`, which clash with the WPF types | `using ContextMenu = System.Windows.Controls.ContextMenu;` (and `MenuItem`) aliases in `FabricationAssembliesEditorWindow.xaml.cs`. Use the same trick for any new CS0104 "ambiguous reference". |
| .NET runtime | .NET Framework 4.8 | 2025+: .NET 8 | csproj target frameworks; `UseWindowsForms` on .NET 8; explicit desktop references only on net48. |

## Rules for new code

1. **Never** write `element.Id.Value` or `.IntegerValue` on an `ElementId`. Use `element.Id.IdValue()`;
   to build one from a number use `((long)n).ToElementId()`.
2. Anything that exists only in newer Revit versions goes inside `#if REVIT20xx_OR_GREATER`, with an
   `#else` branch (or a graceful no-op) for older ones.
3. Do not put version-specific code in the WPF/XAML files; keep it in `.cs` files under `#if`.
4. Keep `Resources\Icons` next to the DLL - icons are located through `Assembly.Location`.

## What was checked, and how

Checked against the Autodesk Revit API documentation (member exists in the versions concerned):

* `FabricationDimensionDefinition.UnitType` (used by the fabrication capture report) - present 2021 through 2026.
* `FabricationPart.PlaceFittingAsCutIn` - present in 2023 through 2026, same signature.
* `SelectionChangedEventArgs` (used by the dockable panes' selection events) - class present in the 2023 docs.
* `UIThemeManager.CurrentTheme` and `ThemeChanged` - not present in 2023, present from 2024.

Searches of the source for other well-known breaking changes between 2023 and 2026 (units / `ParameterType`
API, `IntegerValue`, .NET-Framework-only classes such as `JavaScriptSerializer`, `Process.Start`
without `UseShellExecute`) turned up nothing else that needs `#if`. That is a text search, not a substitute
for compiling.

## Known unknowns - verify on the first real build

The port was prepared **without a compiler or Revit available**, so it has not been built yet. Expect
the first build of each version to be the real test:

* **2023**: the Fabrication API is large and the code was written against 2024. A missing member would show up
  as a compile error on 2023 only.
* **2025 / 2026**: same idea for anything removed or changed in the newer API; plus .NET 8 behaviour differences
  at runtime (WPF resource loading, WinForms dialogs, reflection on `FabricationRodInfo` / `HostedInfo`).
* **Reflection code** (`GetRodInfo`, `RodCount`, `GetRodLength`, `SetRodLength`, `HostId`, `ServiceId`, ...)
  compiles on every version but can fail silently at runtime if a member changed name or signature.
  Test the hanger tools on real fabrication content in each version.
* NuGet floating versions (`2026.*`) resolve to the newest stable `Nice3point.Revit.Api.*` package of that year.
  Pin an exact version in the csproj if you need reproducible builds.

## Adding another Revit version

1. `build.ps1`: add the year to the `ValidateSet` and to the default `$Versions`.
2. `CTSRevitPlugin.csproj`: add the year to the right target-framework `PropertyGroup` and, if it is 2024+ / 2025+ /
   2026+, make sure the `_OR_GREATER` symbols cover it (they use `!= '2023'` / explicit lists).
3. Build it, fix whatever the new API breaks under `#if`, and document it in the table above.

## Troubleshooting

* *"The process cannot access the file ... CTSRevitPlugin.dll"* - Revit is running and holds the DLL. Close Revit.
* *Restore fails / package not found* - the machine has no Revit of that year installed and no NuGet access.
  Install Revit, point to it with `-p:RevitInstallDir="D:\...\Revit 2025\"`, or allow nuget.org.
* *Plugin loads twice* - the dev `.addin` in `%ProgramData%\Autodesk\Revit\Addins\<year>\` and the bundle are both
  installed. Remove one of them.
* *Buttons have no icons* - `Resources\Icons` did not get copied next to the DLL; rebuild.
