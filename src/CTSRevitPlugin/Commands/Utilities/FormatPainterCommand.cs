using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Utilities
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Copy safe, editable text and numeric instance data from one element to one or more target elements.",
        usage: "Run the tool, pick the source element, then pick one or more target elements. Press ESC when finished.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "Geometry, ElementId, host, location, fabrication and other structural/mechanical-driving values are intentionally excluded."
    )]
    public class FormatPainterCommand : IExternalCommand
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
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;

            try
            {
                Reference sourceRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new SafeElementSelectionFilter(),
                    "CTS Format Painter — select the SOURCE element.");

                if (sourceRef == null) return Result.Cancelled;

                Element source = uidoc.Document.GetElement(sourceRef.ElementId);
                if (source == null) return Result.Cancelled;

                // PickObjects was the wrong call here, for two reasons.
                //
                // 1. It puts Revit's "Multiple / Finish / Cancel" options bar on
                //    screen, which is what showed up floating in the middle of the
                //    ribbon area.
                // 2. It only returns on Finish. The prompt told the user to press
                //    ESC, but ESC makes PickObjects throw OperationCanceledException,
                //    so every element they had just picked was thrown away and the
                //    tool ended without copying anything.
                //
                // Picking one at a time has neither problem: no options bar, and ESC
                // simply ends the loop and applies whatever was collected.
                List<Element> targets = new List<Element>();
                HashSet<long> seen = new HashSet<long> { source.Id.IdValue() };

                while (true)
                {
                    Reference targetRef;

                    try
                    {
                        targetRef = uidoc.Selection.PickObject(
                            ObjectType.Element,
                            new SafeElementSelectionFilter(),
                            targets.Count == 0
                                ? "CTS Format Painter — select a TARGET element (ESC when finished)."
                                : "CTS Format Painter — " + targets.Count +
                                  " target(s) picked. Select another, or press ESC to apply.");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        break;
                    }

                    if (targetRef == null) break;

                    Element target = uidoc.Document.GetElement(targetRef.ElementId);
                    if (target == null) continue;

                    if (seen.Add(target.Id.IdValue()))
                        targets.Add(target);
                }

                if (targets.Count == 0)
                {
                    RevitUtils.Alert("No target elements were selected.", "Format Painter");
                    return Result.Cancelled;
                }

                Dictionary<string, ParameterSnapshot> sourceValues =
                    ReadSafeParameters(source);

                int changed = 0;
                int skipped = 0;

                using (Transaction t = new Transaction(uidoc.Document, "CTS Format Painter"))
                {
                    t.Start();

                    foreach (Element target in targets)
                    {
                        foreach (ParameterSnapshot snapshot in sourceValues.Values)
                        {
                            try
                            {
                                Parameter p = target.LookupParameter(snapshot.Name);
                                if (p == null || p.IsReadOnly || !IsSafe(p))
                                {
                                    skipped++;
                                    continue;
                                }

                                if (ApplyValue(p, snapshot))
                                    changed++;
                                else
                                    skipped++;
                            }
                            catch
                            {
                                skipped++;
                            }
                        }
                    }

                    t.Commit();
                }

                RevitUtils.Alert(
                    "Format Painter completed.\n\n" +
                    "Source: " + source.Id.IdValue() + "\n" +
                    "Targets: " + targets.Count + "\n" +
                    "Values copied: " + changed +
                    (skipped > 0 ? "\nSkipped: " + skipped : ""),
                    "Format Painter");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                // Run() has no ref-message to fill in; the dialog is the report.
                RevitUtils.Alert("Format Painter failed:\n\n" + ex.Message, "Format Painter");
                return Result.Failed;
            }
        }

        private sealed class SafeElementSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) { return elem != null; }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class ParameterSnapshot
        {
            public string Name;
            public StorageType StorageType;
            public string StringValue;
            public double DoubleValue;
            public int IntegerValue;
        }

        private static Dictionary<string, ParameterSnapshot> ReadSafeParameters(Element element)
        {
            var result = new Dictionary<string, ParameterSnapshot>(StringComparer.OrdinalIgnoreCase);

            foreach (Parameter p in element.Parameters)
            {
                try
                {
                    if (p == null || p.Definition == null || p.IsReadOnly || !IsSafe(p))
                        continue;

                    string name = p.Definition.Name;
                    if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(name))
                        continue;

                    ParameterSnapshot snapshot = new ParameterSnapshot
                    {
                        Name = name,
                        StorageType = p.StorageType
                    };

                    if (p.StorageType == StorageType.String)
                        snapshot.StringValue = p.AsString() ?? "";
                    else if (p.StorageType == StorageType.Double)
                        snapshot.DoubleValue = p.AsDouble();
                    else if (p.StorageType == StorageType.Integer)
                        snapshot.IntegerValue = p.AsInteger();
                    else
                        continue;

                    result.Add(name, snapshot);
                }
                catch { }
            }

            return result;
        }

        private static bool ApplyValue(Parameter p, ParameterSnapshot s)
        {
            if (p.StorageType != s.StorageType) return false;

            if (s.StorageType == StorageType.String)
                return p.Set(s.StringValue);

            if (s.StorageType == StorageType.Double)
                return p.Set(s.DoubleValue);

            if (s.StorageType == StorageType.Integer)
                return p.Set(s.IntegerValue);

            return false;
        }

        private static bool IsSafe(Parameter p)
        {
            if (p == null || p.Definition == null) return false;

            string name = p.Definition.Name ?? "";
            string n = name.ToLowerInvariant();

            // Safe user-data families: text, notes, comments, marks, identifiers and
            // ordinary numeric metadata. Anything that can drive geometry, connectivity,
            // fabrication logic or placement is deliberately excluded.
            string[] blocked =
            {
                "length", "width", "height", "depth", "diameter", "radius",
                "size", "offset", "elevation", "slope", "angle", "rotation",
                "location", "position", "coordinate", "host", "connector",
                "rod", "strut", "bearer", "fabrication", "mechanical",
                "system", "level", "workset", "material", "type", "family",
                "shape", "profile", "insulation", "lining", "service",
                "specification", "product", "dimension", "geometry"
            };

            if (blocked.Any(x => n.Contains(x)))
                return false;

            return p.StorageType == StorageType.String ||
                   p.StorageType == StorageType.Double ||
                   p.StorageType == StorageType.Integer;
        }
    }
}