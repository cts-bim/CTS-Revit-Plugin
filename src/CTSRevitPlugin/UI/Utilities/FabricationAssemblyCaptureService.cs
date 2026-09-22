using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CTSRevitPlugin.UI.Utilities
{
    /// <summary>
    /// READ-ONLY reference capture. The model is never changed and no template
    /// is inferred from a catalog index, connector order, or an ITM label.
    /// The user picks the actual modeled components in sequence: O-Let first.
    /// Escape finishes the sequence. At least two components are required.
    /// </summary>
    internal static class FabricationAssemblyCaptureService
    {
        private sealed class FabricationPartFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                FabricationPart part = element as FabricationPart;
                return part != null && part.IsValidObject;
            }
            public bool AllowReference(Reference reference, XYZ position) { return true; }
        }

        internal static string Capture(UIApplication app, string templateName)
        {
            UIDocument uiDocument = app.ActiveUIDocument;
            Document doc = uiDocument.Document;
            List<ElementId> orderedIds = new List<ElementId>();
            ICollection<ElementId> initialIds = uiDocument.Selection.GetElementIds();
            // Preselection is only unambiguous if it contains exactly one anchor.
            if (initialIds.Count == 1)
            {
                ElementId selected = initialIds.First();
                FabricationPart part = doc.GetElement(selected) as FabricationPart;
                if (part != null && part.IsValidObject && IsOLet(part)) orderedIds.Add(selected);
            }

            if (orderedIds.Count == 0)
            {
                try
                {
                    Reference reference = uiDocument.Selection.PickObject(ObjectType.Element,
                        new FabricationPartFilter(), "CTS CAPTURE 1: pick the existing O-Let anchor");
                    FabricationPart part = doc.GetElement(reference.ElementId) as FabricationPart;
                    if (part == null || !IsOLet(part))
                        return "Reference not captured: the first item must be a Fabrication O-Let.";
                    orderedIds.Add(reference.ElementId);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return "Reference capture canceled. No model elements or templates changed.";
                }
            }

            // Explicit pick order. Revit's GetElementIds() is a SET, not the order
            // of the user's clicks. Never infer order from ElementId or XYZ height.
            while (orderedIds.Count < 40)
            {
                try
                {
                    Reference reference = uiDocument.Selection.PickObject(ObjectType.Element,
                        new FabricationPartFilter(),
                        "CTS CAPTURE " + (orderedIds.Count + 1) +
                        ": pick NEXT Fabrication part in sequence; press ESC when finished");
                    if (orderedIds.Contains(reference.ElementId)) continue;
                    FabricationPart part = doc.GetElement(reference.ElementId) as FabricationPart;
                    if (part == null || !part.IsValidObject) continue;
                    orderedIds.Add(reference.ElementId);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
            }
            if (orderedIds.Count < 2)
                return "Reference capture canceled: pick the O-Let and at least one following part. No changes made.";

            StringBuilder report = new StringBuilder();
            report.AppendLine("CTS FABRICATION ASSEMBLIES - READ-ONLY REFERENCE CAPTURE");
            report.AppendLine("Template: " + templateName);
            report.AppendLine("Captured: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            report.AppendLine("Revit document: " + doc.Title);
            report.AppendLine("Model modifications: NONE. This report does NOT certify automated replay.");
            report.AppendLine("Pick order: " + orderedIds.Count + " pieces (anchor first).");
            report.AppendLine("NOTE: joint/coupling pieces installed automatically may be between selected items.");
            report.AppendLine("NOTE: Product List entries and Conditions are NOT equivalent to diameter.");
            report.AppendLine();
            for (int i = 0; i < orderedIds.Count; i++)
            {
                FabricationPart part = doc.GetElement(orderedIds[i]) as FabricationPart;
                if (part == null || !part.IsValidObject)
                {
                    report.AppendLine("STEP " + (i + 1) + " INVALIDATED DURING CAPTURE");
                    continue;
                }
                try { AppendPart(report, part, i + 1, orderedIds); }
                catch (Exception ex)
                {
                    report.AppendLine("STEP " + (i + 1) + " partial read failure: " + ex.Message);
                    report.AppendLine("Continuing to the next selected part.");
                }
            }
            // The diagnostic always remains available. The operational recipe is
            // replaced ONLY when every part maps to a UNIQUE button + condition
            // and the selected sequence is connected through real Revit joints.
            List<FabricationAssemblyPartDefinition> captured =
                new List<FabricationAssemblyPartDefinition>();
            List<string> problems = new List<string>();
            for (int i = 0; i < orderedIds.Count; i++)
            {
                FabricationPart part = doc.GetElement(orderedIds[i]) as FabricationPart;
                if (part == null || !part.IsValidObject)
                {
                    problems.Add("step " + (i + 1) + ": element was invalidated");
                    continue;
                }
                if (i > 0)
                {
                    string chain;
                    if (!FabricationReferenceResolver.TryFindChain(doc, orderedIds[i - 1],
                        orderedIds[i], out chain))
                        problems.Add("step " + (i + 1) +
                            ": not connected to previous step (through at most two joints)");
                    else report.AppendLine("CAPTURE PATH " + i + " -> " + (i + 1) + ": " + chain);
                }
                FabricationAssemblyPartDefinition definition;
                string error;
                if (!FabricationReferenceResolver.TryResolve(doc, part, out definition, out error))
                {
                    problems.Add("step " + (i + 1) + " " + FamilyName(part) + ": " + error);
                    continue;
                }
                report.AppendLine("CAPTURE MAPPING " + (i + 1) + ": " +
                    definition.Name + " | service=" + part.ServiceName +
                    " | palette=" + definition.PaletteName +
                    " | condition=" + definition.ConditionName +
                    " | reference family=" + definition.ReferenceFamilyName +
                    " | length=" + (definition.ReferenceLengthInches > 0
                        ? ImperialLength.FormatInches(definition.ReferenceLengthInches) : "n/a") +
                    " | product list=" + definition.ReferenceProductEntryName);
                captured.Add(definition);
            }
            report.AppendLine("CAPTURE VALIDATION: " +
                (problems.Count == 0 && captured.Count == orderedIds.Count
                    ? "ALL STEPS RESOLVED" : "INCOMPLETE - old recipe preserved"));
            foreach (string problem in problems) report.AppendLine("  - " + problem);
            report.AppendLine("END OF READ-ONLY REFERENCE CAPTURE");

            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CTSRevitPlugin", "CapturedAssemblies");
            Directory.CreateDirectory(folder);
            string safe = new string((templateName ?? "Assembly").Select(
                c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            if (safe.Length > 45) safe = safe.Substring(0, 45);
            string path = Path.Combine(folder, safe + "_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(true));

            List<FabricationAssemblyTemplate> templates = FabricationAssemblyTemplateStore.Load();
            FabricationAssemblyTemplate template = templates.FirstOrDefault(
                t => string.Equals(t.Name, templateName, StringComparison.OrdinalIgnoreCase));
            if (template != null)
            {
                template.CapturedReferencePath = path;
                if (problems.Count == 0 && captured.Count == orderedIds.Count)
                {
                    template.Parts = captured;
                    template.UseAnchorDiameter = true;
                    template.CapturedRecipeVerified = true;
                }
                FabricationAssemblyTemplateStore.Save(templates);
            }
            if (problems.Count > 0 || captured.Count != orderedIds.Count)
                return "CAPTURE REPORT saved; old recipe NOT changed. " +
                    string.Join(" | ", problems.Take(3)) + ". VIEW REFERENCE: " + path;
            return "CAPTURED " + captured.Count + " exact parts + conditions from the modeled assembly. " +
                "Saved recipe updated (AUTO O-Let diameter); Revit model unchanged. " +
                "Open CONFIGURE to edit type / length. Report: " + path;
        }

        private static bool IsOLet(FabricationPart part)
        {
            try { if (part.IsATap()) return true; } catch { }
            string name = FamilyName(part);
            return name.IndexOf("o-let", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("olet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FamilyName(FabricationPart part)
        {
            try
            {
                ElementType type = part.Document.GetElement(part.GetTypeId()) as ElementType;
                if (type != null && !string.IsNullOrWhiteSpace(type.FamilyName))
                    return type.FamilyName;
            }
            catch { }
            return Safe(() => part.Name);
        }

        private static string Safe(Func<string> get)
        {
            try { return get() ?? ""; } catch (Exception e) { return "<not available: " + e.Message + ">"; }
        }

        private static void AppendPart(StringBuilder report, FabricationPart part,
            int sequenceNumber, List<ElementId> selectedIds)
        {
            report.AppendLine(new string('=', 66));
            report.AppendLine("STEP " + sequenceNumber + (sequenceNumber == 1 ? " [MANUAL O-LET ANCHOR]" : ""));
            report.AppendLine("ElementId: " + part.Id.IdValue() + " / UniqueId: " + part.UniqueId);
            report.AppendLine("Family (Properties): " + FamilyName(part));
            report.AppendLine("Element.Name: " + Safe(() => part.Name));
            report.AppendLine("Service: " + Safe(() => part.ServiceName) + " | ServiceId: " + part.ServiceId);
            report.AppendLine("Product name: " + Safe(() => part.ProductName));
            report.AppendLine("Product code: " + Safe(() => part.ProductCode));
            report.AppendLine("Product size: " + Safe(() => part.ProductSizeDescription));
            report.AppendLine("Product spec: " + Safe(() => part.ProductSpecificationDescription));
            report.AppendLine("Product description: " + Safe(() => part.ProductLongDescription));
            report.AppendLine("ItemCustomId: " + part.ItemCustomId);
            report.AppendLine("Part type id: " + part.GetTypeId().IdValue());
            report.AppendLine("ProductListEntry: " + part.ProductListEntry);
            if (part.ProductListEntry >= 0)
                report.AppendLine("ProductListEntryName: " + Safe(() =>
                    part.GetProductListEntryName(part.ProductListEntry)));
            try
            {
                int productCount = part.GetProductListEntryCount();
                report.AppendLine("Product list entries available: " + productCount);
                for (int entry = 0; entry < Math.Min(productCount, 60); entry++)
                {
                    int captureIndex = entry;
                    report.AppendLine("  product[" + entry + "] " +
                        Safe(() => part.GetProductListEntryName(captureIndex)) +
                        (part.ProductListEntry == entry ? " <-- SELECTED" : ""));
                }
                if (productCount > 60) report.AppendLine("  ... list truncated after 60 entries");
            }
            catch (Exception ex) { report.AppendLine("  <product list read failed> " + ex.Message); }
            report.AppendLine("SIZE/LENGTH DIMENSIONS (Revit internal units; check dimension type):");
            try
            {
                foreach (FabricationDimensionDefinition dimension in part.GetDimensions())
                {
                    try
                    {
                        double value = part.GetDimensionValue(dimension);
                        report.AppendLine("  " + dimension.Name + ": " +
                            value.ToString("R", CultureInfo.InvariantCulture) +
                            " [internal units]; type=" + dimension.Type +
                            "; units=" + dimension.UnitType +
                            "; modifiable=" + dimension.IsModifiable);
                    }
                    catch (Exception e) { report.AppendLine("  <dimension read error> " + e.Message); }
                }
            }
            catch (Exception e) { report.AppendLine("  <GetDimensions failed> " + e.Message); }
            report.AppendLine("CONNECTORS (includes any Revit-created intermediate joints):");
            try
            {
                ConnectorSetIterator iterator = part.ConnectorManager.Connectors.ForwardIterator();
                while (iterator.MoveNext())
                {
                    Connector connector = iterator.Current as Connector;
                    if (connector == null) continue;
                    string size = connector.Shape == ConnectorProfileType.Round
                        ? "diameter " + ImperialLength.FormatInches(connector.Radius * 24.0)
                        : "non-round";
                    report.AppendLine("  connector id=" + connector.Id +
                        " / " + size + " / connected=" + connector.IsConnected +
                        " / origin=" + Point(connector.Origin) +
                        " / axis=" + Point(connector.CoordinateSystem.BasisZ));
                    try
                    {
                        ConnectorSetIterator refs = connector.AllRefs.ForwardIterator();
                        while (refs.MoveNext())
                        {
                            Connector neighbor = refs.Current as Connector;
                            if (neighbor == null || neighbor.Owner == null ||
                                neighbor.Owner.Id == part.Id) continue;
                            string flagged = selectedIds.Contains(neighbor.Owner.Id) ? " [SELECTED STEP]" : " [AUTO/OTHER]";
                            report.AppendLine("    --> element " + neighbor.Owner.Id.IdValue() +
                                " connector " + neighbor.Id + " " +
                                Safe(() => neighbor.Owner.Name) + flagged);
                        }
                    }
                    catch (Exception e) { report.AppendLine("    <connection read error> " + e.Message); }
                }
            }
            catch (Exception e) { report.AppendLine("  <connector read error> " + e.Message); }
            report.AppendLine();
        }

        private static string Point(XYZ value)
        {
            if (value == null) return "(null)";
            return string.Format(CultureInfo.InvariantCulture, "({0:R}, {1:R}, {2:R})", value.X, value.Y, value.Z);
        }
    }
}
