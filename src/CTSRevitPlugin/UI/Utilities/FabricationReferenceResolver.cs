using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CTSRevitPlugin.UI.Utilities
{
    // Runs only in the ExternalEvent API context. No model modifications.
    // The BUTTON label is not the family name; match the FabricationPartType,
    // then determine the exact condition that contains that type.
    internal static class FabricationReferenceResolver
    {
        private static readonly MethodInfo LookupMethod = typeof(FabricationPartType).GetMethod(
            "Lookup", new[] { typeof(Document), typeof(FabricationServiceButton), typeof(int) });

        internal static bool TryResolve(Document doc, FabricationPart part,
            out FabricationAssemblyPartDefinition result, out string error)
        {
            result = null; error = "";
            if (LookupMethod == null)
            {
                error = "This Revit API build has no FabricationPartType.Lookup. " +
                    "Condition/ITM identity cannot be certified; capture report remains available.";
                return false;
            }
            FabricationConfiguration config = FabricationConfiguration.GetFabricationConfiguration(doc);
            FabricationService service = config == null ? null : config.GetService(part.ServiceId);
            FabricationPartType partType = doc.GetElement(part.GetTypeId()) as FabricationPartType;
            if (service == null || !service.IsValidObject || partType == null || !partType.IsValidObject)
            {
                error = "The selected part's service/type is unavailable.";
                return false;
            }

            var matches = new List<FabricationAssemblyPartDefinition>();
            for (int palette = 0; palette < service.PaletteCount; palette++)
            {
                string paletteName = service.GetPaletteName(palette);
                for (int index = 0; index < service.GetButtonCount(palette); index++)
                {
                    FabricationServiceButton button = null;
                    try
                    {
                        button = service.GetButton(palette, index);
                        if (button == null || !button.IsValid() || button.IsAHanger ||
                            !button.ContainsFabricationPartType(partType)) continue;
                        for (int condition = 0; condition < button.ConditionCount; condition++)
                        {
                            ElementId id;
                            try
                            {
                                id = LookupMethod.Invoke(null,
                                    new object[] { doc, button, condition }) as ElementId;
                            }
                            catch { continue; }
                            if (id == null || id == ElementId.InvalidElementId ||
                                !id.Equals(part.GetTypeId())) continue;
                            string label = button.GetConditionName(condition);
                            if (string.IsNullOrWhiteSpace(label)) label = "Condition " + (condition + 1);
                            matches.Add(new FabricationAssemblyPartDefinition
                            {
                                Name = button.Name,
                                Code = button.Code,
                                ServiceId = part.ServiceId,
                                PaletteIndex = palette,
                                PaletteName = paletteName,
                                ButtonIndex = index,
                                ConditionIndex = condition,
                                ConditionName = label,
                                ConditionExplicit = true,
                                ReferenceFamilyName = GetFamilyName(doc, part),
                                ReferenceProductSize = Safe(() => part.ProductSizeDescription),
                                ReferenceProductSpecification = Safe(() => part.ProductSpecificationDescription),
                                ReferenceProductEntryName = part.ProductListEntry >= 0
                                    ? Safe(() => part.GetProductListEntryName(part.ProductListEntry)) : "",
                                ReferenceLengthInches = GetLengthInches(part)
                            });
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidObjectException) { }
                    finally { if (button != null) { try { button.Dispose(); } catch { } } }
                }
            }
            if (matches.Count != 1)
            {
                error = matches.Count == 0
                    ? "No service button/condition resolves to the selected family's FabricationPartType."
                    : matches.Count + " button/condition matches; ambiguous. Do not guess an ITM.";
                return false;
            }
            result = matches[0];
            return true;
        }

        internal static string GetFamilyName(Document doc, FabricationPart part)
        {
            try
            {
                ElementType type = doc.GetElement(part.GetTypeId()) as ElementType;
                if (type != null && !string.IsNullOrWhiteSpace(type.FamilyName)) return type.FamilyName;
            }
            catch { }
            return Safe(() => part.ProductLongDescription);
        }

        private static string Safe(Func<string> read)
        {
            try { return read() ?? ""; } catch { return ""; }
        }

        private static double GetLengthInches(FabricationPart part)
        {
            try
            {
                foreach (FabricationDimensionDefinition d in part.GetDimensions())
                {
                    if (string.Equals(d.Name, "Length", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d.Type.ToString(), "Length", StringComparison.OrdinalIgnoreCase))
                        return part.GetDimensionValue(d) * 12.0;
                }
            }
            catch { }
            return 0;
        }

        private static bool IsJointLikeIntermediate(Document doc, FabricationPart part)
        {
            if (part == null || !part.IsValidObject) return false;
            string text = (GetFamilyName(doc, part) + " " +
                Safe(() => part.ProductLongDescription) + " " +
                Safe(() => part.ProductName)).ToLowerInvariant();
            return text.Contains("joint") || text.Contains("coupl") ||
                text.Contains("socket weld") || text.Contains("socketweld") ||
                text.Contains("weld gap") || text.Contains("thread joint") ||
                text.Contains("union") || text.Contains("gasket") ||
                text.Contains("flange gap");
        }

        // Traverse a directly-connected joint/coupling without assuming that two
        // explicit recipe parts are adjacent. Limit to three hops so the main
        // pipe network cannot silently replace the user's ordered sequence.
        internal static bool TryFindChain(Document doc, ElementId from, ElementId to,
            out string chain)
        {
            chain = "";
            var queue = new Queue<Tuple<ElementId, List<ElementId>>>();
            queue.Enqueue(Tuple.Create(from, new List<ElementId> { from }));
            var visited = new HashSet<ElementId> { from };
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                ElementId current = state.Item1;
                if (current.Equals(to))
                {
                    chain = string.Join(" -> ", state.Item2.Select(x => x.IdValue().ToString()));
                    return true;
                }
                if (state.Item2.Count >= 4) continue;
                FabricationPart part = doc.GetElement(current) as FabricationPart;
                if (part == null || !part.IsValidObject) continue;
                if (!current.Equals(from) && !IsJointLikeIntermediate(doc, part)) continue;
                foreach (Connector connector in part.ConnectorManager.Connectors)
                {
                    if (!connector.IsConnected) continue;
                    foreach (Connector neighbor in connector.AllRefs)
                    {
                        if (neighbor == null || neighbor.Owner == null ||
                            neighbor.Owner.Id.Equals(current) ||
                            !(neighbor.Owner is FabricationPart) ||
                            visited.Contains(neighbor.Owner.Id)) continue;
                        // Do not certify a false path via the host pipe or a
                        // second unrelated branch. Only genuine joints/couplings
                        // may be intermediate between the selected steps.
                        if (!neighbor.Owner.Id.Equals(to) &&
                            !IsJointLikeIntermediate(doc, neighbor.Owner as FabricationPart))
                            continue;
                        visited.Add(neighbor.Owner.Id);
                        var route = new List<ElementId>(state.Item2) { neighbor.Owner.Id };
                        queue.Enqueue(Tuple.Create(neighbor.Owner.Id, route));
                    }
                }
            }
            return false;
        }
    }
}
