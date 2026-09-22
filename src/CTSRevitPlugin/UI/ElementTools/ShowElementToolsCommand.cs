using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTSRevitPlugin.UI.ElementTools
{
    [Transaction(TransactionMode.Manual)]
    public class ShowElementToolsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp =
                commandData.Application;

            if (ElementToolsPane.Instance != null)
            {
                ElementToolsPane.Instance
                    .RefreshFromUIApplication(uiapp);
            }

            DockablePane pane =
                uiapp.GetDockablePane(
                    ElementToolsPane.PaneId);

            pane.Show();

            return Result.Succeeded;
        }
    }
}