using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Modeling
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Rotate All Selected elements.",
        usage: "Select one or more elements and run the tool.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "Non-Fabrication elements are automatically skipped."
    )]
    
    public class Rotate90CWCommand : IExternalCommand
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

            if (!selected.Any())
            {
                RevitUtils.Alert(
                    "No elements selected.",
                    "Nothing to Rotate");

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
                    "Nothing to Rotate");

                return Result.Cancelled;
            }


            // =========================================================
            // ROTATE VALID PIPES
            // =========================================================

            int ok = 0;

            using (
                var t =
                    new Transaction(
                        doc,
                        "Rotate Fabrication Pipes 90° CW"))
            {
                t.Start();

                foreach (var e in parts)
                {
                    try
                    {
                        Line axis = BuildRotationAxis(e);

                        if (axis == null)
                            continue;

                        ElementTransformUtils.RotateElement(
                            doc,
                            e.Id,
                            axis,
                            -Math.PI / 2);

                        ok++;
                    }
                    catch
                    {
                        // Mantém o comportamento original:
                        // se um elemento individual não puder ser
                        // rotacionado, simplesmente continua.
                    }
                }

                t.Commit();
            }


            // =========================================================
            // USER INFORMATION
            // =========================================================
            //
            // Não mostra popup quando tudo foi válido.
            //
            // Só informa se algum elemento da seleção original
            // não era MEP Fabrication Pipe.
            // =========================================================

            if (skipped > 0)
            {
                RevitUtils.Alert(
                    skipped +
                    " selected element(s) were not MEP Fabrication Pipes " +
                    "and were skipped.\n\n" +
                    ok +
                    " Fabrication Pipe(s) rotated successfully.",
                    "Rotate 90° CW");
            }


            return Result.Succeeded;
        
        }



        // =============================================================
        // ROTATION AXIS
        //
        // The old code always rotated about a vertical Z line through the
        // midpoint of the location curve (or the bounding-box centre). For a
        // fabrication part that is the wrong axis: the connectors sweep to a
        // new place, so an o-let comes off its pipe and Revit raises
        // "The family is connected in a network and can no longer keep the
        // connectivity".
        //
        // Revit's own rotate for fabrication parts spins the part about its
        // connection axis, which leaves every connector origin exactly where it
        // was. That is what is rebuilt here:
        //
        //   2+ connectors -> the line through the two furthest apart, i.e. the
        //                    part's own centreline (valves, straights, fittings)
        //   1 connector   -> the connector origin along its own direction
        //                    (o-lets, taps, caps)
        //   none          -> fall back to vertical Z through the part origin
        // =============================================================

        private static Line BuildRotationAxis(Element element)
        {
            try
            {
                List<Connector> connectors = RevitUtils.PhysicalConnectors(element);

                if (connectors != null && connectors.Count >= 2)
                {
                    Connector a = null;
                    Connector b = null;
                    double best = -1.0;

                    for (int i = 0; i < connectors.Count; i++)
                    {
                        for (int j = i + 1; j < connectors.Count; j++)
                        {
                            double distance =
                                connectors[i].Origin.DistanceTo(connectors[j].Origin);

                            if (distance > best)
                            {
                                best = distance;
                                a = connectors[i];
                                b = connectors[j];
                            }
                        }
                    }

                    if (a != null && b != null && best > 1e-7)
                        return Line.CreateUnbound(a.Origin, (b.Origin - a.Origin).Normalize());
                }

                if (connectors != null && connectors.Count == 1)
                {
                    Connector only = connectors[0];
                    XYZ direction = only.CoordinateSystem == null
                        ? null
                        : only.CoordinateSystem.BasisZ;

                    if (direction != null && direction.GetLength() > 1e-9)
                        return Line.CreateUnbound(only.Origin, direction.Normalize());
                }
            }
            catch
            {
                // Fall through to the vertical axis below.
            }

            XYZ point = RevitUtils.RepresentativePoint(element);
            if (point == null) return null;

            return Line.CreateUnbound(point, XYZ.BasisZ);
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