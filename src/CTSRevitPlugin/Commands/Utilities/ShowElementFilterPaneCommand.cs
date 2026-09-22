using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTSRevitPlugin.UI.ElementFilter;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Utilities
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        "Rule-based select, isolate, hide, halftone and transparency for model elements.",
        "Open the palette, pick a filter and choose an action. Double-click a filter to run its default action. Use RESET VIEW to undo isolate, hide, halftone and transparency.",
        "Pedro Oliveira",
        "1.0",
        "Build reusable rule sets and apply them to the model with one click. " +
        "Filters are stored per Revit version and can be exported and imported to share across a project.")]
    public class ShowElementFilterPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            if (ElementFilterPane.Instance != null) ElementFilterPane.Instance.RefreshFromUIApplication(uiapp);
            uiapp.GetDockablePane(ElementFilterPane.PaneId).Show();
            return Result.Succeeded;
        }
    }
}
