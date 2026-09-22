using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTSRevitPlugin
{
    [Transaction(TransactionMode.Manual)]
    public class MyCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            TaskDialog.Show(
                "CTS Revit Plugin",
                "Hello! Meu primeiro Add-in C# está funcionando!"
            );

            return Result.Succeeded;
        }
    }
}