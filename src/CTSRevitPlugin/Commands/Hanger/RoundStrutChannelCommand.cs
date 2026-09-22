using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Hanger
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Rounds the horizontal strut/bearer dimension of selected MEP Fabrication Hangers to the nearest whole inch.",
        usage: "Select one or more elements and run the command. Fabrication Hangers are adjusted; non-hangers are skipped.",
        author: "Pedro Oliveira",
        version: "1.2",
        notes: "Hosted bearer hangers are temporarily disconnected, adjusted, and re-hosted so Revit can validate the fabrication dimension."
    )]
    public class RoundStrutChannelCommand : IExternalCommand
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
                RevitUtils.Alert("Select one or more elements first.", "Round Strut Channel");
                return Result.Cancelled;
            }

            List<FabricationPart> hangers = new List<FabricationPart>();
            int skippedNonHangers = 0;
            foreach (ElementId id in selectedIds)
            {
                FabricationPart part = doc.GetElement(id) as FabricationPart;
                if (part != null && part.IsAHanger()) hangers.Add(part);
                else skippedNonHangers++;
            }

            if (hangers.Count == 0)
            {
                RevitUtils.Alert("No selected elements are MEP Fabrication Hangers.", "Round Strut Channel");
                return Result.Cancelled;
            }

            int changed = 0;
            int skippedHangers = 0;

            using (Transaction t = new Transaction(doc, "Round Strut Channel"))
            {
                t.Start();
                foreach (FabricationPart hanger in hangers)
                {
                    try
                    {
                        FabricationDimensionDefinition dim = FindStrutDimension(hanger);
                        if (dim == null || !dim.IsModifiable)
                        {
                            skippedHangers++;
                            continue;
                        }

                        double current = hanger.GetDimensionValue(dim);
                        double rounded = Math.Round(current * 12.0, MidpointRounding.AwayFromZero);
                        double target = rounded / 12.0;

                        if (target <= 0)
                        {
                            skippedHangers++;
                            continue;
                        }

                        if (Math.Abs(target - current) < 1e-9)
                        {
                            changed++;
                            continue;
                        }

                        if (TrySetStrutDimensionValue(doc, hanger, dim, target))
                            changed++;
                        else
                            skippedHangers++;
                    }
                    catch
                    {
                        skippedHangers++;
                    }
                }
                t.Commit();
            }

            int skippedTotal = skippedNonHangers + skippedHangers;
            if (skippedTotal > 0)
            {
                RevitUtils.Alert(
                    changed + " hanger(s) adjusted to the nearest whole inch.\n" +
                    skippedNonHangers + " non-hanger element(s) skipped.\n" +
                    skippedHangers + " hanger(s) could not be adjusted.",
                    "Round Strut Channel");
            }

            return Result.Succeeded;
        
        }

        internal static FabricationDimensionDefinition FindStrutDimension(FabricationPart hanger)
        {
            try
            {
                // The dimension representing the bearer/strut can be reported by
                // Revit as Length or Width depending on the fabrication pattern.
                // The previous implementation filtered to Length only, which could
                // select/fail on the wrong definition for some hanger ITMs.
                IEnumerable<FabricationDimensionDefinition> dimensions = hanger.GetDimensions()
                    .Cast<FabricationDimensionDefinition>()
                    .Where(d => d != null && d.IsModifiable &&
                           (d.Type == FabricationDimensionType.Length ||
                            d.Type == FabricationDimensionType.Width));

                FabricationDimensionDefinition named = dimensions
                    .OrderByDescending(d => ExactPriority(d.Name))
                    .ThenBy(d => d.Name)
                    .FirstOrDefault(d =>
                        Contains(d.Name, "hanger width") ||
                        Contains(d.Name, "bearer width") ||
                        Contains(d.Name, "strut width") ||
                        Contains(d.Name, "channel width") ||
                        Contains(d.Name, "bearer") ||
                        Contains(d.Name, "strut") ||
                        Contains(d.Name, "channel") ||
                        Contains(d.Name, "support") ||
                        Contains(d.Name, "width"));

                if (named != null) return named;

                List<FabricationDimensionDefinition> generic = dimensions.ToList();
                if (generic.Count == 1) return generic[0];

                return generic.FirstOrDefault(d =>
                    string.Equals(d.Name, "Length", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Name, "Size", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static int ExactPriority(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            if (string.Equals(name, "Hanger Width", StringComparison.OrdinalIgnoreCase)) return 110;
            if (string.Equals(name, "Bearer Width", StringComparison.OrdinalIgnoreCase)) return 105;
            if (string.Equals(name, "Strut Width", StringComparison.OrdinalIgnoreCase)) return 100;
            if (string.Equals(name, "Channel Width", StringComparison.OrdinalIgnoreCase)) return 95;
            if (Contains(name, "hanger width")) return 90;
            if (Contains(name, "bearer width")) return 85;
            if (Contains(name, "strut width")) return 80;
            if (Contains(name, "channel width")) return 75;
            if (Contains(name, "bearer")) return 60;
            if (Contains(name, "strut")) return 55;
            if (Contains(name, "channel")) return 50;
            if (Contains(name, "width")) return 45;
            if (Contains(name, "support")) return 30;
            return 0;
        }

        internal static bool TrySetStrutDimensionValue(
            Document doc,
            FabricationPart hanger,
            FabricationDimensionDefinition dimension,
            double value)
        {
            if (doc == null || hanger == null || dimension == null || value <= 0)
                return false;

            // Plan A: change the dimension with the hanger left exactly as it is.
            // Most fabrication patterns accept this, and it is the only path that
            // cannot disturb the host relationship.
            if (TrySetDirect(doc, hanger, dimension, value))
                return true;

            // Plan B: the pattern refuses to resize while constrained to its host,
            // so disconnect, resize, and put it back on the same host at the same
            // position. Everything happens inside one SubTransaction: if the
            // re-host fails the hanger is rolled back to its original state rather
            // than being left floating.
            return TrySetByRehosting(doc, hanger, dimension, value);
        }

        private static bool TrySetDirect(
            Document doc,
            FabricationPart hanger,
            FabricationDimensionDefinition dimension,
            double value)
        {
            using (SubTransaction sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    hanger.SetDimensionValue(dimension, value);
                    doc.Regenerate();

                    double verified = hanger.GetDimensionValue(dimension);
                    if (Math.Abs(verified - value) > 1e-6)
                    {
                        sub.RollBack();
                        return false;
                    }

                    sub.Commit();
                    return true;
                }
                catch
                {
                    try { sub.RollBack(); } catch { }
                    return false;
                }
            }
        }

        private static bool TrySetByRehosting(
            Document doc,
            FabricationPart hanger,
            FabricationDimensionDefinition dimension,
            double value)
        {
            HostedPlacement placement = CaptureHostedPlacement(doc, hanger);
            if (placement == null)
            {
                // Not hosted: plan A was already the only option.
                return false;
            }

            using (SubTransaction sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    FabricationHostedInfo info = hanger.GetHostedInfo();
                    if (info == null)
                    {
                        sub.RollBack();
                        return false;
                    }

                    info.DisconnectFromHost();
                    doc.Regenerate();

                    hanger.SetDimensionValue(dimension, value);
                    doc.Regenerate();

                    double verified = hanger.GetDimensionValue(dimension);
                    if (Math.Abs(verified - value) > 1e-6)
                    {
                        sub.RollBack();
                        return false;
                    }

                    if (!PlaceBackOnHost(doc, hanger, placement))
                    {
                        sub.RollBack();
                        return false;
                    }

                    doc.Regenerate();
                    sub.Commit();
                    return true;
                }
                catch
                {
                    try { sub.RollBack(); } catch { }
                    return false;
                }
            }
        }

        // Kept as a compatibility helper for the existing command code.
        internal static bool TrySetDimensionValue(
            Document doc,
            FabricationPart hanger,
            FabricationDimensionDefinition dimension,
            double value)
        {
            return TrySetStrutDimensionValue(doc, hanger, dimension, value);
        }

        internal static double FindMinimumValidDimension(
            Document doc,
            FabricationPart hanger,
            FabricationDimensionDefinition dimension,
            double current)
        {
            if (doc == null || hanger == null || dimension == null || current <= 0)
                return current;

            double lowInvalid = 0.0;
            double highValid = current;

            // Discover the real lower boundary by asking Revit. The original
            // hanger value is only the upper bound of the search, never the minimum.
            for (int i = 0; i < 18; i++)
            {
                double mid = (lowInvalid + highValid) / 2.0;
                if (mid <= 1e-8)
                {
                    lowInvalid = mid;
                    continue;
                }

                if (TrySetStrutDimensionValue(doc, hanger, dimension, mid))
                    highValid = mid;
                else
                    lowInvalid = mid;
            }

            // Fabrication patterns commonly use 1/16" increments. Never go below
            // the lowest value that Revit accepted during probing.
            double candidate = Math.Ceiling(highValid * 12.0 * 16.0 - 1e-9) / 16.0 / 12.0;
            if (candidate > current) candidate = current;

            if (!TrySetStrutDimensionValue(doc, hanger, dimension, candidate))
            {
                for (int i = 0; i < 64 && candidate <= current + 1e-9; i++)
                {
                    if (TrySetStrutDimensionValue(doc, hanger, dimension, candidate))
                        return candidate;
                    candidate += 1.0 / 192.0;
                }
            }

            return candidate;
        }

        private sealed class HostedPlacement
        {
            public ElementId HostId { get; set; }
            public Connector HostConnector { get; set; }
            public double Distance { get; set; }
        }

        private static HostedPlacement CaptureHostedPlacement(Document doc, FabricationPart hanger)
        {
            try
            {
                FabricationHostedInfo info = hanger.GetHostedInfo();
                if (info == null || info.HostId == null || info.HostId == ElementId.InvalidElementId)
                    return null;

                Element hostElement = doc.GetElement(info.HostId);
                if (hostElement == null)
                    return null;

                FabricationPart hostPart = hostElement as FabricationPart;
                if (hostPart == null || hostPart.ConnectorManager == null)
                    return null;

                XYZ hangerPoint = hanger.Origin;
                Curve hostCurve = null;
                LocationCurve hostLocation = hostPart.Location as LocationCurve;
                if (hostLocation != null)
                    hostCurve = hostLocation.Curve;

                XYZ targetPoint = hangerPoint;
                if (hostCurve != null)
                {
                    IntersectionResult projection = hostCurve.Project(hangerPoint);
                    if (projection != null)
                        targetPoint = projection.XYZPoint;
                }

                List<Connector> connectors = hostPart.ConnectorManager.Connectors
                    .Cast<Connector>()
                    .Where(c => c != null)
                    .ToList();

                if (connectors.Count == 0)
                    return null;

                Connector hostConnector = connectors
                    .OrderBy(c => c.Origin.DistanceTo(targetPoint))
                    .FirstOrDefault();

                if (hostConnector == null)
                    return null;

                double distance = hostConnector.Origin.DistanceTo(targetPoint);
                if (hostCurve == null)
                    distance = hostConnector.Origin.DistanceTo(hangerPoint);

                return new HostedPlacement
                {
                    HostId = info.HostId,
                    HostConnector = hostConnector,
                    Distance = Math.Max(0.0, distance)
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool PlaceBackOnHost(
            Document doc,
            FabricationPart hanger,
            HostedPlacement placement)
        {
            if (placement == null || placement.HostConnector == null)
                return false;

            try
            {
                FabricationHostedInfo info = hanger.GetHostedInfo();
                if (info == null)
                    return false;

                // The PlaceOnHost overload set is not identical across Revit
                // versions. Pinning the exact (ElementId, Connector, double)
                // signature made this return false on releases that expose a
                // different shape, and the caller then rolled the whole change
                // back — the "could not be adjusted" the panes were reporting.
                // Try every overload whose arguments we can actually supply.
                List<MethodInfo> candidates = info.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => m.Name == "PlaceOnHost")
                    .OrderByDescending(m => m.GetParameters().Length)
                    .ToList();

                foreach (MethodInfo method in candidates)
                {
                    object[] arguments = BuildPlaceOnHostArguments(method, placement);
                    if (arguments == null) continue;

                    try
                    {
                        method.Invoke(info, arguments);
                        return true;
                    }
                    catch
                    {
                        // Wrong overload for this pattern; try the next one.
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Maps a captured placement onto one PlaceOnHost overload. Returns null when
        /// the overload asks for something we did not capture.
        /// </summary>
        private static object[] BuildPlaceOnHostArguments(MethodInfo method, HostedPlacement placement)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] arguments = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;

                if (type == typeof(ElementId)) arguments[i] = placement.HostId;
                else if (type == typeof(Connector)) arguments[i] = placement.HostConnector;
                else if (type == typeof(double)) arguments[i] = placement.Distance;
                else if (type == typeof(bool)) arguments[i] = true;
                else if (parameters[i].IsOptional) arguments[i] = Type.Missing;
                else return null;
            }

            return arguments;
        }

        private static bool Contains(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
