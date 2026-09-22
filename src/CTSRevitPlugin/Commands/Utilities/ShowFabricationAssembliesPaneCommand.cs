using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTSRevitPlugin.UI.Utilities;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Utilities
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        "Build and place reusable MEP Fabrication assemblies from the Revit fabrication database.",
        "Open the pane, create an assembly sequence, choose service/diameter/direction, then place it on a fabrication pipe.",
        "Pedro Oliveira",
        "1.0",
        "Templates are stored locally in the CTS Revit Plugin application data folder.")]
    public class ShowFabricationAssembliesPaneCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;

            if (FabricationAssembliesPane.Instance != null)
                FabricationAssembliesPane.Instance.RefreshFromUIApplication(uiapp);

            uiapp.GetDockablePane(FabricationAssembliesPane.PaneId).Show();
            return Result.Succeeded;
        }
    }
}
