using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Hanger
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Increase or decrease the horizontal strut/bearer dimension of fabrication hangers.",
        usage: "Select one or more fabrication hangers and enter a positive or negative imperial adjustment.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "When decreasing past the requested value, CTS uses the lowest value accepted by the Revit fabrication definition rather than the hanger's original size."
    )]
    public class StrucChannelCommand : IExternalCommand
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
            Document doc = uidoc.Document;

            IList<ElementId> selectedIds = uidoc.Selection.GetElementIds().ToList();
            if (selectedIds.Count == 0)
            {
                RevitUtils.Alert("Select one or more elements first.", "Struc Channel");
                return Result.Cancelled;
            }

            double delta;
            string input = SimpleForms.AskString(
                "Struc Channel",
                "Enter value to adjust (+ increase, - decrease), e.g. 2\" or -2\":");

            if (string.IsNullOrWhiteSpace(input))
                return Result.Cancelled;

            if (!TryParseImperial(input, out delta) || Math.Abs(delta) < 1e-12)
            {
                RevitUtils.Alert(
                    "Invalid input value. Accepted examples: 2\", -2\", 0.50\".",
                    "Struc Channel");
                return Result.Failed;
            }

            List<FabricationPart> hangers = new List<FabricationPart>();
            int skippedNonHangers = 0;

            foreach (ElementId id in selectedIds)
            {
                FabricationPart part = doc.GetElement(id) as FabricationPart;
                if (part != null && part.IsAHanger())
                    hangers.Add(part);
                else
                    skippedNonHangers++;
            }

            if (hangers.Count == 0)
            {
                RevitUtils.Alert("No selected elements are MEP Fabrication Hangers.", "Struc Channel");
                return Result.Cancelled;
            }

            int changed = 0;
            int atMinimum = 0;
            int unsupported = 0;

            using (Transaction transaction = new Transaction(doc, "Adjust Struc Channel"))
            {
                transaction.Start();

                foreach (FabricationPart hanger in hangers)
                {
                    try
                    {
                        FabricationDimensionDefinition dimension =
                            RoundStrutChannelCommand.FindStrutDimension(hanger);

                        if (dimension == null || !dimension.IsModifiable)
                        {
                            unsupported++;
                            continue;
                        }

                        double current = hanger.GetDimensionValue(dimension);
                        double target = current + delta;

                        if (delta > 0)
                        {
                            if (RoundStrutChannelCommand.TrySetDimensionValue(doc, hanger, dimension, target))
                                changed++;
                            else
                                unsupported++;
                            continue;
                        }

                        // First try the exact requested reduction.
                        if (target > 0 &&
                            RoundStrutChannelCommand.TrySetDimensionValue(doc, hanger, dimension, target))
                        {
                            changed++;
                            continue;
                        }

                        // The requested value is below Revit's valid range.
                        // Discover the actual lower boundary from Revit itself.
                        double minimum =
                            RoundStrutChannelCommand.FindMinimumValidDimension(
                                doc,
                                hanger,
                                dimension,
                                current);

                        if (minimum < current - 1e-7 &&
                            RoundStrutChannelCommand.TrySetDimensionValue(doc, hanger, dimension, minimum))
                        {
                            changed++;
                        }

                        atMinimum++;
                    }
                    catch
                    {
                        unsupported++;
                    }
                }

                transaction.Commit();
            }

            string result = changed + " hanger(s) adjusted.";
            if (atMinimum > 0)
                result += " " + atMinimum + " hanger(s) reached the Revit minimum.";
            if (skippedNonHangers > 0)
                result += " " + skippedNonHangers + " non-hanger element(s) skipped.";
            if (unsupported > 0)
                result += " " + unsupported + " hanger(s) could not be adjusted.";

            RevitUtils.Alert(result, "Struc Channel");
            return Result.Succeeded;
        
        }

        private static bool TryParseImperial(string input, out double feet)
        {
            feet = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string s = input.Trim();
            bool negative = s.StartsWith("-");
            if (negative || s.StartsWith("+"))
                s = s.Substring(1).Trim();

            try
            {
                // First use Revit's own project units parser when possible.
                // The manual parser below handles the common CTS imperial forms.
                double wholeFeet = 0;
                double inches = 0;

                if (s.Contains("'"))
                {
                    string[] parts = s.Split(new[] { '\'' }, 2);
                    if (!string.IsNullOrWhiteSpace(parts[0]))
                        wholeFeet = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                    s = parts[1].Trim();
                }

                s = s.Replace("\"", "").Trim();

                if (s.Length > 0)
                {
                    string[] parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string part in parts)
                    {
                        if (part.Contains("/"))
                        {
                            string[] fraction = part.Split('/');
                            inches += double.Parse(fraction[0], CultureInfo.InvariantCulture) /
                                      double.Parse(fraction[1], CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            inches += double.Parse(part, CultureInfo.InvariantCulture);
                        }
                    }
                }

                feet = wholeFeet + inches / 12.0;
                if (negative) feet = -feet;
                return true;
            }
            catch
            {
                feet = 0;
                return false;
            }
        }
    }
}
