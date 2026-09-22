using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.QC
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Clean All Parameters on Selected elements.",
        usage: "Select one or more elements and run the tool.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "Non-Fabrication elements are automatically skipped."
    )]
    
    public class ParameterCleanerCommand : IExternalCommand
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

            if (!selected.Any())
            {
                RevitUtils.Alert(
                    "No elements selected. Please select the elements to clean.",
                    "Empty Selection"
                );

                return Result.Cancelled;
            }

            int count = 0;

            using (var t = new Transaction(doc, "Clean instance parameters"))
            {
                t.Start();

                foreach (var e in selected)
                {
                    foreach (Parameter p in e.Parameters)
                    {
                        try
                        {
                            if (
                                p.StorageType == StorageType.String &&
                                !p.IsReadOnly &&
                                !string.IsNullOrEmpty(p.AsString())
                            )
                            {
                                var internalDef =
                                    p.Definition as InternalDefinition;

                                var bip =
                                    internalDef == null
                                        ? BuiltInParameter.INVALID
                                        : internalDef.BuiltInParameter;

                                // "Item Number" on a fabrication part is a real
                                // built-in (FABRICATION_PART_ITEM_NUMBER), so the
                                // INVALID / COMMENTS / MARK test below never let it
                                // through and the tool silently left it filled in.
                                // Matching on the definition name as well keeps this
                                // working whether or not that enum member exists in
                                // the Revit version being built against.
                                if (
                                    bip == BuiltInParameter.INVALID ||
                                    bip == BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS ||
                                    bip == BuiltInParameter.ALL_MODEL_MARK ||
                                    IsClearableByName(p)
                                )
                                {
                                    p.Set("");
                                    count++;
                                }
                            }
                        }
                        catch
                        {
                            // Ignora parâmetros que não possam ser alterados
                        }
                    }
                }

                t.Commit();
            }

            RevitUtils.Alert(
                count + " parameter value(s) cleared on " +
                selected.Count + " element(s).",
                "Parameter Cleaner");

            return Result.Succeeded;
        }

        /// <summary>
        /// Built-in parameters that are safe to clear even though Revit reports a
        /// real BuiltInParameter for them. Matched by name so the list survives the
        /// enum differences between Revit 2023 and 2026.
        /// </summary>
        private static readonly string[] ClearableNames =
        {
            "Item Number"
        };

        private static bool IsClearableByName(Parameter p)
        {
            if (p == null || p.Definition == null) return false;

            string name = p.Definition.Name;
            if (string.IsNullOrWhiteSpace(name)) return false;

            return ClearableNames.Any(
                x => string.Equals(x, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}