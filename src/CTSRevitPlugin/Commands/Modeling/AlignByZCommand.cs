using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Modeling
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Align selected elements using their Z-axis position.",
        usage: "Select two or more elements and run the command.",
        author: "Bruno Silva",
        version: "1.0",
        notes: "CTS standard command documentation."
    )]
    public class AlignByZCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData data,
            ref string message,
            ElementSet elements)
        {
            // Thin wrapper. The real work lives in Run(UIApplication) so the
            // dockable panes can call it directly from their ExternalEvent
            // instead of resolving a ribbon control id through PostCommand.
            return Run(data.Application);
        }

        /// <summary>Runs the tool against a live UIApplication.</summary>
        public static Result Run(UIApplication uiapp)
        {

            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            var selected = RevitUtils.SelectedElements(uidoc);

            if (selected.Count != 1)
            {
                RevitUtils.Alert("Select exactly one element to move first.");
                return Result.Cancelled;
            }

            var el = selected[0];

            var reference =
                RevitUtils.PickElement(uidoc, "Pick the reference element");

            if (reference == null)
                return Result.Cancelled;

            var p1 = RevitUtils.RepresentativePoint(el);
            var p2 = RevitUtils.RepresentativePoint(reference);

            if (p1 == null || p2 == null)
            {
                RevitUtils.Alert("Could not determine a reference point.");
                return Result.Failed;
            }

            double delta = p2.Z - p1.Z;

            if (Math.Abs(delta) < 1e-9)
                return Result.Succeeded;

            using (var t = new Transaction(doc, "Align by Z"))
            {
                t.Start();

                ElementTransformUtils.MoveElement(
                    doc,
                    el.Id,
                    new XYZ(0, 0, delta)
                );

                t.Commit();
            }

            return Result.Succeeded;
        
        }
    }
}