using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTSRevitPlugin.UI.Utilities;

namespace CTSRevitPlugin.Commands.Utilities
{
    [Transaction(TransactionMode.Manual)]
    public class ShowParametersPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            if (ParametersPane.Instance != null) ParametersPane.Instance.RefreshFromUIApplication(uiapp);
            uiapp.GetDockablePane(ParametersPane.PaneId).Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ShowShortcutsPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            if (ShortcutsPane.Instance != null) ShortcutsPane.Instance.RefreshFromUIApplication(uiapp);
            uiapp.GetDockablePane(ShortcutsPane.PaneId).Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ShowHangersPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            if (HangersPane.Instance != null) HangersPane.Instance.RefreshFromUIApplication(uiapp);
            uiapp.GetDockablePane(HangersPane.PaneId).Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ShowMechanicalPropertiesPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            if (MechanicalPropertiesPane.Instance != null) MechanicalPropertiesPane.Instance.RefreshFromUIApplication(uiapp);
            uiapp.GetDockablePane(MechanicalPropertiesPane.PaneId).Show();
            return Result.Succeeded;
        }
    }
}
