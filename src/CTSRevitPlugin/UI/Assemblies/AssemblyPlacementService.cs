using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Fabrication;
using Autodesk.Revit.UI.Selection;
using CTSRevitPlugin.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace CTSRevitPlugin.UI.Assemblies
{
    public class PlacementResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public List<string> Warnings { get; private set; }
        public List<ElementId> CreatedIds { get; private set; }

        /// <summary>
        /// Set when placement found product entries the assembly did not know about.
        /// The caller saves the assembly so the builder can offer them next time.
        /// </summary>
        public bool ProductEntriesDiscovered { get; set; }

        public PlacementResult()
        {
            Warnings = new List<string>();
            CreatedIds = new List<ElementId>();
        }
    }

    /// <summary>Only lets the user click parts an assembly can start from (olets and open ends).</summary>
    public class FabricationAnchorFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return AssemblyPlacementService.IsAnchor(element);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }

    /// <summary>
    /// Builds an assembly from an olet the user already modeled on the pipe.
    ///
    /// The free connector of the olet is where the sequence starts and its direction is the
    /// direction the sequence grows, so there is no "up / down" setting. Every step is created,
    /// aligned to the free connector of the previous part and connected
    /// (FabricationPart.ConnectAndCouple).
    ///
    /// Call only inside an open Transaction, from a valid Revit API context.
    /// </summary>
    public static class AssemblyPlacementService
    {
        // 1/32" in feet: connector radii closer than this are considered equal.
        private const double RadiusTolerance = 1.0 / 12.0 / 32.0;

        // ------------------------------------------------------------
        // ANCHORS (the olets the user selects)
        // ------------------------------------------------------------

        /// <summary>
        /// A fabrication pipework part with a free connector. Taps (olets, sockolets...) always
        /// qualify; any other part qualifies only when it has exactly one free end.
        /// </summary>
        public static bool IsAnchor(Element element)
        {
            FabricationPart part = element as FabricationPart;
            if (part == null) return false;

            try
            {
                if (part.Category == null ||
                    part.Category.Id.IdValue() != (long)BuiltInCategory.OST_FabricationPipework)
                    return false;

                List<Connector> free = FreeConnectors(part);
                if (free.Count == 0) return false;

                return free.Count == 1 || part.IsATap();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// A tap (olet, sockolet...) with a free connector. Used for the current selection, so a
        /// pipe that happens to be selected together with its olet is never used as a start.
        /// </summary>
        public static bool IsOlet(Element element)
        {
            FabricationPart part = element as FabricationPart;
            if (part == null || !IsAnchor(element)) return false;

            try { return part.IsATap(); }
            catch { return false; }
        }

        /// <summary>The free connector the sequence starts from (null when everything is connected).</summary>
        public static Connector GetAnchorExit(FabricationPart anchor)
        {
            return FreeConnectors(anchor).OrderBy(c => c.Id).FirstOrDefault();
        }

        private static List<Connector> FreeConnectors(FabricationPart part)
        {
            return RevitUtils.PhysicalConnectors(part).Where(c => !c.IsConnected).ToList();
        }

        // ------------------------------------------------------------
        // DATABASE RESOLUTION
        // ------------------------------------------------------------

        /// <summary>
        /// Resolves every step against the fabrication database of the project, looking in the
        /// service of the selected olet first. Returns the list of problems (empty = ready).
        /// </summary>
        public static List<string> ResolveButtons(
            Document doc,
            AssemblyDefinition assembly,
            FabricationService preferredService,
            out List<FabricationServiceButton> buttons)
        {
            buttons = new List<FabricationServiceButton>();
            List<string> problems = new List<string>();

            if (assembly.Steps == null || assembly.Steps.Count == 0)
            {
                problems.Add("The assembly has no steps.");
                return problems;
            }

            for (int i = 0; i < assembly.Steps.Count; i++)
            {
                AssemblyStep step = assembly.Steps[i];
                string problem;
                FabricationServiceButton button = FabricationCatalogService.ResolveButton(doc, step.Item, preferredService, out problem);

                if (button == null)
                {
                    buttons.Add(null);
                    problems.Add("Step " + (i + 1) + " (" + step.DisplayName + "): " + problem + ".");
                    continue;
                }

                // An explicit variation (x1.5, x3...) must exist on the button of this project.
                string wanted = step.Item.ConditionName;
                if (!string.IsNullOrWhiteSpace(wanted) &&
                    FabricationCatalogService.FindConditionIndex(button, wanted) < 0)
                {
                    buttons.Add(null);
                    problems.Add("Step " + (i + 1) + " (" + step.DisplayName + "): the variation '" + wanted +
                                 "' does not exist on this button in the current database.");
                    continue;
                }

                buttons.Add(button);
            }

            return problems;
        }

        // ------------------------------------------------------------
        // MAIN ENTRY
        // ------------------------------------------------------------

        public static PlacementResult BuildFromAnchor(
            Document doc,
            AssemblyDefinition assembly,
            IList<FabricationServiceButton> buttons,
            FabricationPart anchor)
        {
            PlacementResult result = new PlacementResult();

            try
            {
                Connector previousExit = GetAnchorExit(anchor);
                if (previousExit == null)
                {
                    result.Error = "The selected part has no free connector: something is already connected to it.";
                    return result;
                }

                ElementId levelId = ResolveLevelId(doc, anchor, previousExit.Origin);
                if (levelId == null || levelId.Equals(ElementId.InvalidElementId))
                {
                    result.Error = "No level was found in the project to host the new parts.";
                    return result;
                }

                for (int i = 0; i < assembly.Steps.Count; i++)
                {
                    AssemblyStep step = assembly.Steps[i];
                    string tag = "Step " + (i + 1) + " (" + step.DisplayName + ")";

                    if (previousExit == null)
                    {
                        result.Warnings.Add("The sequence ended after step " + i + " because that part has no free connector. " +
                                            (assembly.Steps.Count - i) + " step(s) were not placed.");
                        break;
                    }

                    FabricationPart part;
                    try
                    {
                        part = CreateStepPart(doc, buttons[i], step, previousExit, levelId, result.Warnings, tag);
                    }
                    catch (Exception ex)
                    {
                        result.Error = tag + ": could not create the part. " + ex.Message;
                        return result;
                    }
                    result.CreatedIds.Add(part.Id);

                    // Before aligning: the product entry can change the part's ends,
                    // and the alignment below measures those ends.
                    ApplyProductEntry(part, step, result, tag);

                    int usedId;
                    if (!AlignToPrevious(doc, part, previousExit, step, result.Warnings, tag, out usedId))
                    {
                        result.Error = tag + ": the part could not be aligned to the previous one.";
                        return result;
                    }

                    // Read the connector again: the handle can be refreshed by a regeneration.
                    Connector used = GetConnector(part, usedId);
                    if (used == null)
                    {
                        result.Error = tag + ": the connection end was lost after alignment.";
                        return result;
                    }

                    bool connected;
                    try
                    {
                        connected = FabricationPart.ConnectAndCouple(doc, used, previousExit);
                    }
                    catch (Exception ex)
                    {
                        result.Error = tag + ": could not connect to the previous part. " + ex.Message;
                        return result;
                    }

                    if (!connected)
                    {
                        result.Error = tag + ": Revit refused the connection to the previous part.";
                        return result;
                    }

                    doc.Regenerate();

                    if (Math.Abs(used.Radius - previousExit.Radius) > RadiusTolerance)
                        result.Warnings.Add(tag + ": connection sizes differ (" +
                            AssemblyUnits.FormatInches(used.Radius * 24.0) + " vs " +
                            AssemblyUnits.FormatInches(previousExit.Radius * 24.0) + ").");

                    previousExit = FindExit(part, usedId);
                }

                doc.Regenerate();
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                return result;
            }
        }

        // ------------------------------------------------------------
        // PART CREATION
        // ------------------------------------------------------------

        /// <summary>
        /// Creates the part of a step. An explicit variation (the little arrow of the database
        /// button: x1.5, x3...) is used exactly as picked. Without one, buttons that have several
        /// variations are created for the size of the connection they will meet.
        /// </summary>
        private static FabricationPart CreateStepPart(
            Document doc,
            FabricationServiceButton button,
            AssemblyStep step,
            Connector previousExit,
            ElementId levelId,
            List<string> warnings,
            string tag)
        {
            string wanted = step.Item.ConditionName;
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                int index = FabricationCatalogService.FindConditionIndex(button, wanted);
                if (index < 0)
                    throw new InvalidOperationException("The variation '" + wanted + "' does not exist on '" + button.Name + "'.");

                return FabricationPart.Create(doc, button, index, levelId);
            }

            int conditionCount = 0;
            try { conditionCount = button.ConditionCount; } catch { }

            if (conditionCount <= 1)
                return FabricationPart.Create(doc, button, 0, levelId);

            double size = ResolveSize(step, previousExit);

            try
            {
                // Round parts: width == depth == diameter. Revit picks the variation for that size.
                return FabricationPart.Create(doc, button, size, size, levelId);
            }
            catch
            {
                for (int c = 0; c < conditionCount; c++)
                {
                    try
                    {
                        FabricationPart part = FabricationPart.Create(doc, button, c, levelId);
                        warnings.Add(tag + ": no variation matched " + AssemblyUnits.FormatInches(size * 12.0) +
                                     "; '" + FabricationCatalogService.ConditionLabel(button, c) + "' was used. " +
                                     "Pick the variation you want with the arrow in the builder.");
                        return part;
                    }
                    catch
                    {
                        // try the next variation
                    }
                }

                throw;
            }
        }

        // ------------------------------------------------------------
        // PRODUCT ENTRY
        //
        // A reducing part (a bushing, a reducer, a swage) carries a product
        // entry such as 3/4x1/8. The entry decides the SMALL end, and the small
        // end is what the next step has to connect to - so leaving it to Revit
        // is fine on a plain nipple and wrong on a bushing.
        //
        // Revit only exposes a product list through a part that exists. The
        // builder works around that by creating and rolling back a sample part,
        // but that sample is built at a guessed size. Here the part is the real
        // one at the real connection size, so whatever is found overwrites the
        // step's cached list and the builder's dropdown becomes exact.
        // ------------------------------------------------------------

        private static void ApplyProductEntry(
            FabricationPart part,
            AssemblyStep step,
            PlacementResult result,
            string tag)
        {
            List<string> entries = FabricationCatalogService.ReadProductEntries(part);

            if (step.Item != null && entries.Count > 0 && !SameList(step.Item.ProductEntryNames, entries))
            {
                step.Item.ProductEntryNames = entries;
                result.ProductEntriesDiscovered = true;
            }

            string wanted = step.ProductEntry == null ? "" : step.ProductEntry.Trim();
            if (string.IsNullOrWhiteSpace(wanted)) return;

            int index = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.Equals(entries[i], wanted, StringComparison.OrdinalIgnoreCase)) continue;
                index = i;
                break;
            }

            if (index < 0)
            {
                result.Warnings.Add(tag + ": product entry '" + wanted + "' is not available on this part. " +
                    (entries.Count == 0
                        ? "Revit did not report any product entry for it."
                        : "Available: " + string.Join(", ", entries.ToArray()) + "."));
                return;
            }

            string error;
            if (!TrySetProductEntry(part, index, out error))
            {
                result.Warnings.Add(tag + ": Revit refused product entry '" + wanted + "'." +
                    (string.IsNullOrWhiteSpace(error) ? "" : " " + error));
            }
        }

        private static bool TrySetProductEntry(FabricationPart part, int index, out string error)
        {
            error = null;

            try
            {
                part.ProductListEntry = index;
                part.Document.Regenerate();
                return part.ProductListEntry == index;
            }
            catch (Exception ex)
            {
                // Revit refuses the change when it would resize an end that is
                // already connected. That is why this runs before the alignment
                // and the connection, not after.
                error = ex.Message;
                return false;
            }
        }

        private static bool SameList(List<string> a, List<string> b)
        {
            if (a == null) return b == null || b.Count == 0;
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }

            return true;
        }

        private static double ResolveSize(AssemblyStep step, Connector previousExit)
        {
            if (step.SizeSource == StepSizeSource.Fixed)
            {
                double inches;
                if (AssemblyUnits.TryParseInches(step.FixedSize, out inches) && inches > 0)
                    return inches / 12.0;
            }

            return previousExit.Radius * 2.0;
        }

        // ------------------------------------------------------------
        // ALIGN / CONNECT
        // ------------------------------------------------------------

        /// <summary>
        /// Aligns the new part to the previous free connector using one of its ends.
        /// In Auto mode the ends whose size already matches are tried first, and the first end
        /// that Revit validates without a coupling wins. Returns the Id of the end that was used.
        /// </summary>
        private static bool AlignToPrevious(
            Document doc,
            FabricationPart part,
            Connector previousExit,
            AssemblyStep step,
            List<string> warnings,
            string tag,
            out int usedConnectorId)
        {
            usedConnectorId = -1;

            List<Connector> connectors = RevitUtils.PhysicalConnectors(part).OrderBy(c => c.Id).ToList();
            if (connectors.Count == 0) return false;

            List<int> candidates;
            if (step.Connector == StepConnector.First)
            {
                candidates = new List<int> { connectors[0].Id };
            }
            else if (step.Connector == StepConnector.Second)
            {
                candidates = new List<int> { connectors.Count > 1 ? connectors[1].Id : connectors[0].Id };
            }
            else
            {
                double target = previousExit.Radius;
                candidates = connectors
                    .OrderBy(c => Math.Abs(c.Radius - target) > RadiusTolerance ? 1 : 0)
                    .ThenBy(c => c.Id)
                    .Select(c => c.Id)
                    .ToList();
            }

            double rotation = step.RotationDegrees * Math.PI / 180.0;
            int firstAligned = -1;

            foreach (int id in candidates)
            {
                if (!TryAlign(doc, part, id, previousExit, rotation)) continue;

                Connector aligned = GetConnector(part, id);
                if (aligned == null) continue;

                bool valid = false;
                try { valid = FabricationUtils.ValidateConnectivity(doc, aligned, previousExit); }
                catch { }

                if (valid)
                {
                    usedConnectorId = id;
                    return true;
                }

                if (step.Connector != StepConnector.Auto)
                {
                    warnings.Add(tag + ": the chosen end does not connect directly (a coupling may be added).");
                    usedConnectorId = id;
                    return true;
                }

                if (firstAligned < 0) firstAligned = id;
            }

            if (firstAligned >= 0 && TryAlign(doc, part, firstAligned, previousExit, rotation))
            {
                warnings.Add(tag + ": no end of this part connects directly to the previous part " +
                             "(different connection types). Revit may add a coupling.");
                usedConnectorId = firstAligned;
                return true;
            }

            return false;
        }

        private static bool TryAlign(Document doc, FabricationPart part, int connectorId, Connector fixedConnector, double rotation)
        {
            try
            {
                Connector connector = GetConnector(part, connectorId);
                if (connector == null) return false;

                // Product-list parts are created with their default size: ask for the size of the
                // connection they will meet. Ends that already match are left untouched.
                if (Math.Abs(connector.Radius - fixedConnector.Radius) > RadiusTolerance)
                {
                    try
                    {
                        connector.Radius = fixedConnector.Radius;
                        doc.Regenerate();
                    }
                    catch
                    {
                        // The part has no size list: keep the size it was created with.
                    }

                    // The connector handle can be refreshed by the regeneration above.
                    connector = GetConnector(part, connectorId);
                    if (connector == null) return false;
                }

                bool ok = FabricationPart.AlignPartByConnectorToConnector(
                    doc,
                    connector,
                    fixedConnector,
                    rotation,
                    0.0,
                    FabricationPartJustification.Middle);

                if (ok) doc.Regenerate();
                return ok;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------
        // CONNECTORS / LEVEL
        // ------------------------------------------------------------

        private static Connector GetConnector(FabricationPart part, int id)
        {
            return RevitUtils.PhysicalConnectors(part).FirstOrDefault(c => c.Id == id);
        }

        /// <summary>The free end farthest from the end that was used: where the sequence continues.</summary>
        private static Connector FindExit(FabricationPart part, int usedConnectorId)
        {
            List<Connector> all = RevitUtils.PhysicalConnectors(part);
            Connector used = all.FirstOrDefault(c => c.Id == usedConnectorId);

            IEnumerable<Connector> free = all.Where(c => c.Id != usedConnectorId && !c.IsConnected);
            if (used != null)
                free = free.OrderByDescending(c => c.Origin.DistanceTo(used.Origin));

            return free.FirstOrDefault();
        }

        private static ElementId ResolveLevelId(Document doc, FabricationPart anchor, XYZ point)
        {
            try
            {
                ElementId anchorLevel = anchor.LevelId;
                if (anchorLevel != null && !anchorLevel.Equals(ElementId.InvalidElementId) && doc.GetElement(anchorLevel) is Level)
                    return anchorLevel;
            }
            catch { }

            Level nearest = null;
            double best = double.MaxValue;
            foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                double distance = Math.Abs(level.ProjectElevation - point.Z);
                if (distance < best)
                {
                    best = distance;
                    nearest = level;
                }
            }

            return nearest == null ? ElementId.InvalidElementId : nearest.Id;
        }
    }
}
