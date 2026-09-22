using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTSRevitPlugin.UI.Assemblies;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Utilities
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Open CTS Assemblies: pre-built sequences of MEP Fabrication parts (High Point, Low Point, gauges...) that are modeled on a pipe with one click.",
        usage: "Pick an assembly, type the diameter and press PLACE ON PIPE, then click a point on a fabrication pipe. Use + or EDIT to build your own sequences from the fabrication database.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "Assemblies are saved per Revit version in %APPDATA%\\CTSRevitPlugin\\<version>\\Assemblies.xml and load automatically when Revit starts."
    )]
    public class ShowAssembliesPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            uiapp.GetDockablePane(AssembliesPane.PaneId).Show();
            return Result.Succeeded;
        }
    }
}
