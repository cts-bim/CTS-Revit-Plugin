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
        description: "Adjust the horizontal struc channel/bearer length of fabrication hangers.",
        usage: "Select one or more fabrication hangers and enter a positive or negative imperial adjustment.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "Uses the same hanger dimension logic as the CTS Hangers Adjuster pane."
    )]
    public class StrucChannelAdjusterCommand : IExternalCommand
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

            List<FabricationPart> hangers = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as FabricationPart)
                .Where(h => h != null && h.IsAHanger())
                .ToList();

            if (hangers.Count == 0)
            {
                RevitUtils.Alert("Select one or more fabrication hangers first.", "Struc Channel");
                return Result.Cancelled;
            }

            string input = SimpleForms.AskString(
                "Struc Channel",
                "Enter value to adjust (+ increase, - decrease), e.g. 2\" or -1 1/2\":",
                "2\"");
            if (string.IsNullOrWhiteSpace(input)) return Result.Cancelled;

            double delta;
            if (!TryParseImperial(input, out delta) || Math.Abs(delta) < 1e-12)
            {
                RevitUtils.Alert("Invalid input value. Example: 2\", -1 1/2\", 0.25\".", "Struc Channel");
                return Result.Failed;
            }

            int changed = 0;
            int unsupported = 0;
            int minimumReached = 0;

            // Use one transaction per hanger. This is intentional: Revit owns the
            // actual legal range of fabrication dimensions. If a requested value is
            // below that range, Revit reports HangerWidthOutOfRange during commit.
            // We roll back only that hanger instead of poisoning the whole batch or
            // showing the native Revit failure dialog.
            foreach (FabricationPart hanger in hangers)
            {
                FabricationDimensionDefinition dim = null;
                try
                {
                    dim = RoundStrutChannelCommand.FindStrutDimension(hanger);
                    if (dim == null || !dim.IsModifiable)
                    { unsupported++; continue; }

                    double current = hanger.GetDimensionValue(dim);
                    double target = current + delta;
                    if (target <= 0)
                    {
                        minimumReached++;
                        continue;
                    }

                    using (Transaction t = new Transaction(doc, "Adjust Struc Channel"))
                    {
                        t.Start();

                        FailureHandlingOptions failureOptions = t.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new StrutRangeFailurePreprocessor());
                        t.SetFailureHandlingOptions(failureOptions);

                        try
                        {
                            hanger.SetDimensionValue(dim, target);
                            doc.Regenerate();

                            double verified = hanger.GetDimensionValue(dim);
                            if (Math.Abs(verified - target) < 1e-7)
                            {
                                TransactionStatus status = t.Commit();
                                if (status == TransactionStatus.Committed) changed++;
                                else minimumReached++;
                            }
                            else
                            {
                                t.RollBack();
                                unsupported++;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                            if (IsRangeException(ex)) minimumReached++;
                            else unsupported++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (IsRangeException(ex)) minimumReached++;
                    else unsupported++;
                }
            }

            string result = changed + " hanger(s) adjusted by " + (delta * 12.0).ToString("0.##", CultureInfo.InvariantCulture) + "\".";
            if (minimumReached > 0)
                result += "\n" + minimumReached + " hanger(s) are already at Revit's minimum allowed value.";
            if (unsupported > 0)
                result += "\n" + unsupported + " hanger(s) could not be adjusted.";

            RevitUtils.Alert(result, "Struc Channel");
            return Result.Succeeded;
        
        }

        private sealed class StrutRangeFailurePreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                if (failuresAccessor == null) return FailureProcessingResult.Continue;

                foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
                {
                    if (failure.GetFailureDefinitionId() == BuiltInFailures.MEPFabricationFailures.HangerWidthOutOfRange)
                        return FailureProcessingResult.ProceedWithRollBack;
                }

                return FailureProcessingResult.Continue;
            }
        }

        private static bool IsRangeException(Exception ex)
        {
            if (ex == null) return false;
            string text = ex.Message ?? "";
            return text.IndexOf("out of range", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("cannot be set to the value", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("hanger width", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryParseImperial(string input, out double feet)
        {
            feet = 0;
            string s = input == null ? "" : input.Trim();
            if (s.Length == 0) return false;
            bool negative = s.StartsWith("-");
            if (negative || s.StartsWith("+")) s = s.Substring(1).Trim();
            try
            {
                double f = 0, inches = 0;
                if (s.Contains("'"))
                {
                    string[] parts = s.Split(new[] { '\'' }, 2);
                    if (!string.IsNullOrWhiteSpace(parts[0])) f = double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                    s = parts.Length > 1 ? parts[1] : "";
                }
                s = s.Replace("\"", "").Trim();
                foreach (string part in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Contains("/"))
                    {
                        string[] frac = part.Split('/');
                        if (frac.Length != 2) return false;
                        inches += double.Parse(frac[0], CultureInfo.InvariantCulture) / double.Parse(frac[1], CultureInfo.InvariantCulture);
                    }
                    else inches += double.Parse(part, CultureInfo.InvariantCulture);
                }
                feet = f + inches / 12.0;
                if (negative) feet = -feet;
                return true;
            }
            catch { return false; }
        }
    }
}
