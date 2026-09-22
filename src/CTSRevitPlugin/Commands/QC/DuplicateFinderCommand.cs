using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.QC
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Find overlapping elements in the active model view by category.",
        usage: "Run the tool, choose one or more categories present in the view, then the detected overlaps are temporarily isolated.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "Detection uses transformed element bounding boxes. It is intended as a visual duplicate/overlap detector, not a geometric clash engine."
    )]
    public class DuplicateFinderCommand : IExternalCommand
    {
        private sealed class Box
        {
            public XYZ Min;
            public XYZ Max;
        }

        private sealed class CategoryGroup
        {
            public string Name;
            public List<Element> Elements;
        }

        private static readonly BuiltInCategory[] TargetCategories =
        {
            BuiltInCategory.OST_FabricationPipework,
            BuiltInCategory.OST_FabricationHangers,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_GenericModel
        };

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
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view.IsTemplate ||
                view.ViewType == ViewType.Schedule ||
                view.ViewType == ViewType.DrawingSheet ||
                view.ViewType == ViewType.Legend ||
                view.ViewType == ViewType.ProjectBrowser ||
                view.ViewType == ViewType.SystemBrowser)
            {
                RevitUtils.Alert("Open a model view (plan, section, elevation or 3D) before running.", "Duplicate Finder");
                return Result.Cancelled;
            }

            List<Element> visible = new List<Element>();

            foreach (BuiltInCategory category in TargetCategories)
            {
                try
                {
                    visible.AddRange(
                        new FilteredElementCollector(doc, view.Id)
                            .OfCategory(category)
                            .WhereElementIsNotElementType()
                            .ToElements());
                }
                catch { }
            }

            visible = visible
                .Where(e => e != null)
                .Where(e => !(e is FamilyInstance fi && fi.SuperComponent != null))
                .GroupBy(e => e.Id.IdValue())
                .Select(g => g.First())
                .ToList();

            List<CategoryGroup> categories = visible
                .GroupBy(e => e.Category == null ? "Unknown" : e.Category.Name)
                .OrderBy(g => g.Key)
                .Select(g => new CategoryGroup
                {
                    Name = g.Key,
                    Elements = g.ToList()
                })
                .Where(g => g.Elements.Count > 1)
                .ToList();

            if (categories.Count == 0)
            {
                RevitUtils.Alert("No supported categories with multiple visible elements were found.", "Duplicate Finder");
                return Result.Cancelled;
            }

            List<CategoryGroup> chosen = SimpleForms.ChooseMany(
                "Duplicate Finder",
                "Choose the category or categories to analyze for overlap:",
                categories,
                x => x.Name + "  (" + x.Elements.Count + ")");

            if (chosen == null || chosen.Count == 0)
                return Result.Cancelled;

            List<ElementId> overlapIds = new List<ElementId>();
            int pairCount = 0;

            // Spatial hashing avoids an O(n²) comparison across large views.
            Dictionary<(int, int, int), List<Tuple<Element, Box>>> buckets =
                new Dictionary<(int, int, int), List<Tuple<Element, Box>>>();

            const double cell = 2.0; // feet
            foreach (Element element in chosen.SelectMany(x => x.Elements))
            {
                Box box = GetBox(element);
                if (box == null) continue;

                int minX = (int)Math.Floor(box.Min.X / cell);
                int minY = (int)Math.Floor(box.Min.Y / cell);
                int minZ = (int)Math.Floor(box.Min.Z / cell);
                int maxX = (int)Math.Floor(box.Max.X / cell);
                int maxY = (int)Math.Floor(box.Max.Y / cell);
                int maxZ = (int)Math.Floor(box.Max.Z / cell);

                for (int x = minX; x <= maxX; x++)
                    for (int y = minY; y <= maxY; y++)
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            var key = (x, y, z);
                            List<Tuple<Element, Box>> list;
                            if (!buckets.TryGetValue(key, out list))
                            {
                                list = new List<Tuple<Element, Box>>();
                                buckets[key] = list;
                            }
                            list.Add(Tuple.Create(element, box));
                        }
            }

            HashSet<string> testedPairs = new HashSet<string>();
            foreach (List<Tuple<Element, Box>> bucket in buckets.Values)
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    for (int j = i + 1; j < bucket.Count; j++)
                    {
                        Element a = bucket[i].Item1;
                        Element b = bucket[j].Item1;
                        long aId = a.Id.IdValue();
                        long bId = b.Id.IdValue();
                        string key = aId < bId ? aId + ":" + bId : bId + ":" + aId;
                        if (!testedPairs.Add(key)) continue;

                        if (Overlaps(bucket[i].Item2, bucket[j].Item2))
                        {
                            overlapIds.Add(a.Id);
                            overlapIds.Add(b.Id);
                            pairCount++;
                        }
                    }
                }
            }

            overlapIds = overlapIds.Distinct().ToList();

            if (overlapIds.Count == 0)
            {
                RevitUtils.Alert("No bounding-box overlaps were found in the selected categories.", "Duplicate Finder");
                return Result.Succeeded;
            }

            using (Transaction t = new Transaction(doc, "Isolate Overlapping Elements"))
            {
                t.Start();
                view.IsolateElementsTemporary(overlapIds);
                t.Commit();
            }

            RevitUtils.Alert(
                overlapIds.Count + " element(s) are involved in " + pairCount +
                " overlapping pair(s).\n\nThe active view has been temporarily isolated to show them.",
                "Duplicate Finder");

            return Result.Succeeded;
        
        }

        private static Box GetBox(Element element)
        {
            try
            {
                BoundingBoxXYZ bb = element.get_BoundingBox(null);
                if (bb == null) return null;

                XYZ[] points =
                {
                    bb.Transform.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z)),
                    bb.Transform.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z)),
                    bb.Transform.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)),
                    bb.Transform.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z)),
                    bb.Transform.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z)),
                    bb.Transform.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z)),
                    bb.Transform.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z)),
                    bb.Transform.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z))
                };

                return new Box
                {
                    Min = new XYZ(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
                    Max = new XYZ(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z))
                };
            }
            catch { return null; }
        }

        private static bool Overlaps(Box a, Box b)
        {
            const double tolerance = 1.0 / 192.0; // 1/16"
            return a.Min.X <= b.Max.X + tolerance && a.Max.X + tolerance >= b.Min.X &&
                   a.Min.Y <= b.Max.Y + tolerance && a.Max.Y + tolerance >= b.Min.Y &&
                   a.Min.Z <= b.Max.Z + tolerance && a.Max.Z + tolerance >= b.Min.Z;
        }
    }
}