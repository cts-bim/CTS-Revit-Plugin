using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;
using System;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Modeling
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Flip All Selected elements.",
        usage: "Select one or more elements and run the tool.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "Non-Fabrication elements are automatically skipped."
    )]

    public class FlipElementsCommand : IExternalCommand
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

            var selected =
                RevitUtils.SelectedElements(uidoc);


            // =========================================================
            // EMPTY SELECTION
            // =========================================================

            if (!selected.Any())
            {
                RevitUtils.Alert(
                    "No elements selected. Please select MEP Fabrication Pipes first.",
                    "Empty Selection");

                return Result.Cancelled;
            }


            // =========================================================
            // SEPARATE FABRICATION PIPES FROM INVALID ELEMENTS
            // =========================================================

            var parts =
                selected
                    .Where(IsFabricationPipe)
                    .ToList();

            int skipped =
                selected.Count - parts.Count;


            // =========================================================
            // NO VALID PIPES
            // =========================================================

            if (!parts.Any())
            {
                RevitUtils.Alert(
                    "No selected elements are MEP Fabrication Pipes.\n\n" +
                    skipped +
                    " element(s) were skipped.",
                    "Nothing to Flip");

                return Result.Cancelled;
            }


            // =========================================================
            // FLIP VALID PIPES
            // =========================================================

            int ok = 0;

            using (
                var t =
                    new Transaction(
                        doc,
                        "Flip Fabrication Pipes in Batch"))
            {
                t.Start();

                foreach (var p in parts)
                {
                    try
                    {
                        if (RevitUtils.InvokeBool(p, "Flip"))
                        {
                            ok++;
                        }
                    }
                    catch
                    {
                        // Se um elemento individual não puder ser
                        // flipped, simplesmente continua.
                    }
                }

                t.Commit();
            }


            // =========================================================
            // USER INFORMATION
            // =========================================================
            //
            // Só mostra popup se algum elemento da seleção original
            // não era MEP Fabrication Pipe.
            // =========================================================

            if (skipped > 0)
            {
                RevitUtils.Alert(
                    skipped +
                    " selected element(s) were not MEP Fabrication Pipes " +
                    "and were skipped.\n\n" +
                    ok +
                    " Fabrication Pipe(s) flipped successfully.",
                    "Flip Elements");
            }


            return Result.Succeeded;
        
        }


        // =============================================================
        // CHECK MEP FABRICATION PIPE
        // =============================================================

        private static bool IsFabricationPipe(Element element)
        {
            if (element == null)
                return false;


            // Precisa ser FabricationPart
            if (element.GetType().Name != "FabricationPart")
                return false;


            // Precisa pertencer à categoria
            // MEP Fabrication Pipework
            if (element.Category == null)
                return false;


            return
                element.Category.Id.IdValue() ==
                (long)BuiltInCategory.OST_FabricationPipework;
        }
    }
}