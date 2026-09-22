using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.UI.Assemblies
{
    // ============================================================
    // PLAIN DATA (safe to use outside a Revit API context)
    // ============================================================

    public class CatalogPalette
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int ButtonCount { get; set; }
    }

    public class CatalogService
    {
        public string Name { get; set; }
        public List<CatalogPalette> Palettes { get; private set; }

        public CatalogService()
        {
            Palettes = new List<CatalogPalette>();
        }
    }

    public class CatalogButton
    {
        public CatalogButton()
        {
            ConditionNames = new List<string>();
        }

        public string ServiceName { get; set; }
        public string PaletteName { get; set; }
        public int PaletteIndex { get; set; }
        public int ButtonIndex { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public int ConditionCount { get; set; }

        /// <summary>
        /// The variations behind the little arrow of the database button (nipple: x1.5, x3...).
        /// Empty when the button has a single variation.
        /// </summary>
        public List<string> ConditionNames { get; private set; }

        /// <summary>Thumbnail exactly as stored by the fabrication database (may be null).</summary>
        public byte[] PngBytes { get; set; }
        public BitmapSource Thumbnail { get; set; }
    }

    // ============================================================
    // REVIT API SIDE - call ONLY from a valid API context
    // (ExternalEvent handler, command...).
    // ============================================================

    public static class FabricationCatalogService
    {
        /// <summary>Services loaded in the project, with their palettes.</summary>
        public static List<CatalogService> GetServices(Document doc)
        {
            List<CatalogService> result = new List<CatalogService>();

            FabricationConfiguration config = FabricationConfiguration.GetFabricationConfiguration(doc);
            if (config == null) return result;

            IList<FabricationService> services = config.GetAllLoadedServices();
            if (services == null) return result;

            foreach (FabricationService service in services)
            {
                if (service == null) continue;

                CatalogService entry = new CatalogService { Name = service.Name };

                int paletteCount = 0;
                try { paletteCount = service.PaletteCount; } catch { }

                for (int p = 0; p < paletteCount; p++)
                {
                    string paletteName = null;
                    int buttonCount = 0;
                    try { paletteName = service.GetPaletteName(p); } catch { }
                    try { buttonCount = service.GetButtonCount(p); } catch { }

                    entry.Palettes.Add(new CatalogPalette
                    {
                        Index = p,
                        Name = string.IsNullOrWhiteSpace(paletteName) ? "Palette " + (p + 1) : paletteName,
                        ButtonCount = buttonCount
                    });
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>Buttons (with their database thumbnails) of one palette. Hanger buttons are skipped.</summary>
        public static List<CatalogButton> GetPaletteButtons(Document doc, string serviceName, int paletteIndex)
        {
            List<CatalogButton> result = new List<CatalogButton>();

            FabricationConfiguration config = FabricationConfiguration.GetFabricationConfiguration(doc);
            FabricationService service = FindService(config, serviceName);
            if (service == null) return result;

            string paletteName = null;
            try { paletteName = service.GetPaletteName(paletteIndex); } catch { }

            int buttonCount = 0;
            try { buttonCount = service.GetButtonCount(paletteIndex); } catch { }

            for (int i = 0; i < buttonCount; i++)
            {
                try
                {
                    FabricationServiceButton button = service.GetButton(paletteIndex, i);
                    if (button == null) continue;
                    if (button.IsAHanger) continue;

                    CatalogButton entry = new CatalogButton
                    {
                        ServiceName = service.Name,
                        PaletteName = paletteName,
                        PaletteIndex = paletteIndex,
                        ButtonIndex = i,
                        Name = button.Name,
                        Code = button.Code
                    };

                    try { entry.ConditionCount = button.ConditionCount; } catch { }

                    if (entry.ConditionCount > 1)
                    {
                        for (int c = 0; c < entry.ConditionCount; c++)
                            entry.ConditionNames.Add(ConditionLabel(button, c));
                    }

                    try
                    {
                        System.Drawing.Bitmap bitmap = button.GetImage();
                        if (bitmap != null)
                        {
                            using (bitmap)
                            {
                                entry.PngBytes = AssemblyIconCache.ToPng(bitmap);
                            }
                            entry.Thumbnail = AssemblyIconCache.FromPng(entry.PngBytes);
                        }
                    }
                    catch { }

                    result.Add(entry);
                }
                catch { }
            }

            return result;
        }

        /// <summary>
        /// Finds the button of the current project that matches a stored item.
        /// Assemblies do not depend on a service: the service of the selected olet is searched
        /// first, then the service where the item was picked, then every other loaded service.
        /// Inside a service the palette name is tried first, then any palette.
        /// </summary>
        public static FabricationServiceButton ResolveButton(
            Document doc,
            CatalogItemRef item,
            FabricationService preferredService,
            out string problem)
        {
            problem = null;

            if (item == null || !item.IsAssigned)
            {
                problem = "no database item assigned";
                return null;
            }

            FabricationConfiguration config = FabricationConfiguration.GetFabricationConfiguration(doc);
            if (config == null)
            {
                problem = "this project has no fabrication configuration";
                return null;
            }

            IList<FabricationService> loaded = config.GetAllLoadedServices();
            List<FabricationService> order = new List<FabricationService>();

            if (preferredService != null) order.Add(preferredService);

            if (loaded != null)
            {
                foreach (FabricationService service in loaded)
                {
                    if (service != null && SameText(service.Name, item.ServiceName) && !ContainsService(order, service))
                        order.Add(service);
                }

                foreach (FabricationService service in loaded)
                {
                    if (service != null && !ContainsService(order, service))
                        order.Add(service);
                }
            }

            foreach (FabricationService service in order)
            {
                FabricationServiceButton button = FindButtonInService(service, item);
                if (button != null) return button;
            }

            problem = "'" + item.ButtonName + "' was not found in any loaded fabrication service";
            return null;
        }

        /// <summary>
        /// The product entries available for an item, e.g. a bushing: 3/4x1/8,
        /// 3/4x1/4, 3/4x3/8, 3/4x1/2.
        ///
        /// Revit exposes a product list only through a part that exists, so the
        /// only way to answer this before placing anything is to create one part,
        /// read its list and roll the transaction back. Nothing reaches the model.
        ///
        /// The list depends on the size the part was created at - a bushing built
        /// at 3/4" offers 3/4x... entries only - so <paramref name="probeSizeFeet"/>
        /// decides which catalogue is read. Callers pass the step's fixed size when
        /// it has one.
        /// </summary>
        public static List<string> GetProductEntries(
            Document doc,
            CatalogItemRef item,
            double probeSizeFeet,
            out string problem)
        {
            List<string> entries = new List<string>();
            problem = null;

            if (doc == null || item == null || !item.IsAssigned)
            {
                problem = "no database item assigned";
                return entries;
            }

            FabricationServiceButton button = ResolveButton(doc, item, null, out problem);
            if (button == null) return entries;

            ElementId levelId = FirstLevelId(doc);
            if (levelId == null || levelId == ElementId.InvalidElementId)
            {
                problem = "this project has no level to create a sample part on";
                return entries;
            }

            using (Transaction transaction = new Transaction(doc, "CTS - read product list"))
            {
                try
                {
                    transaction.Start();

                    FabricationPart part = CreateProbePart(doc, button, item, probeSizeFeet, levelId);

                    if (part == null)
                    {
                        problem = "Revit could not create a sample of '" + item.ButtonName + "' at that size";
                    }
                    else
                    {
                        entries = ReadProductEntries(part);
                        if (entries.Count == 0) problem = "this item has no product list";
                    }
                }
                catch (Exception ex)
                {
                    problem = ex.Message;
                }
                finally
                {
                    // Always rolled back: this is a read, and the sample part must
                    // never survive into the model.
                    try { if (transaction.HasStarted()) transaction.RollBack(); }
                    catch { }
                }
            }

            return entries;
        }

        private static FabricationPart CreateProbePart(
            Document doc,
            FabricationServiceButton button,
            CatalogItemRef item,
            double sizeFeet,
            ElementId levelId)
        {
            int conditionIndex = 0;

            if (!string.IsNullOrWhiteSpace(item.ConditionName))
            {
                int found = FindConditionIndex(button, item.ConditionName);
                if (found >= 0) conditionIndex = found;
            }
            else if (sizeFeet > 0)
            {
                // Round parts: width == depth == diameter, and Revit picks the
                // variation that fits. This is the overload that yields the
                // product list the user will actually see at that size.
                try { return FabricationPart.Create(doc, button, sizeFeet, sizeFeet, levelId); }
                catch { }
            }

            try { return FabricationPart.Create(doc, button, conditionIndex, levelId); }
            catch { return null; }
        }

        /// <summary>Names of a part's product entries, or empty when it has no product list.</summary>
        public static List<string> ReadProductEntries(FabricationPart part)
        {
            List<string> names = new List<string>();
            if (part == null) return names;

            int count;
            try { count = part.GetProductListEntryCount(); }
            catch { return names; }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    string name = part.GetProductListEntryName(i);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
                }
                catch { }
            }

            return names;
        }

        private static ElementId FirstLevelId(Document doc)
        {
            try
            {
                Level level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(x => x.Elevation)
                    .FirstOrDefault();

                return level == null ? ElementId.InvalidElementId : level.Id;
            }
            catch { return ElementId.InvalidElementId; }
        }

        /// <summary>The service the part was modeled from (used to look items up in the same service).</summary>
        public static FabricationService GetServiceOfPart(Document doc, FabricationPart part)
        {
            try
            {
                FabricationConfiguration config = FabricationConfiguration.GetFabricationConfiguration(doc);
                return config == null ? null : config.GetService(part.ServiceId);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Name shown for variation c of a button (the database name, or "Variation N" when empty).</summary>
        public static string ConditionLabel(FabricationServiceButton button, int index)
        {
            string name = null;
            try { name = button.GetConditionName(index); } catch { }
            return string.IsNullOrWhiteSpace(name) ? "Variation " + (index + 1) : name.Trim();
        }

        /// <summary>Index of a variation by its name, or -1.</summary>
        public static int FindConditionIndex(FabricationServiceButton button, string conditionName)
        {
            if (button == null || string.IsNullOrWhiteSpace(conditionName)) return -1;

            int count = 0;
            try { count = button.ConditionCount; } catch { }

            for (int c = 0; c < count; c++)
            {
                if (SameText(ConditionLabel(button, c), conditionName)) return c;
            }
            return -1;
        }

        private static FabricationServiceButton FindButtonInService(FabricationService service, CatalogItemRef item)
        {
            int paletteCount = 0;
            try { paletteCount = service.PaletteCount; } catch { }

            // 1) the palette the item was picked from
            for (int p = 0; p < paletteCount; p++)
            {
                string paletteName = null;
                try { paletteName = service.GetPaletteName(p); } catch { }
                if (!SameText(paletteName, item.PaletteName)) continue;

                FabricationServiceButton found = FindButtonInPalette(service, p, item);
                if (found != null) return found;
            }

            // 2) any palette of the service (palettes can be organized differently per service)
            for (int p = 0; p < paletteCount; p++)
            {
                FabricationServiceButton found = FindButtonInPalette(service, p, item);
                if (found != null) return found;
            }

            return null;
        }

        private static FabricationServiceButton FindButtonInPalette(FabricationService service, int paletteIndex, CatalogItemRef item)
        {
            int buttonCount = 0;
            try { buttonCount = service.GetButtonCount(paletteIndex); } catch { }

            FabricationServiceButton sameNameOnly = null;
            for (int i = 0; i < buttonCount; i++)
            {
                FabricationServiceButton button = null;
                try { button = service.GetButton(paletteIndex, i); } catch { }
                if (button == null) continue;
                if (!SameText(button.Name, item.ButtonName)) continue;

                if (string.IsNullOrWhiteSpace(item.ButtonCode) || SameText(button.Code, item.ButtonCode))
                    return button;

                if (sameNameOnly == null) sameNameOnly = button;
            }

            return sameNameOnly;
        }

        private static bool ContainsService(List<FabricationService> list, FabricationService service)
        {
            return list.Any(s => SameText(s.Name, service.Name));
        }

        private static FabricationService FindService(FabricationConfiguration config, string name)
        {
            if (config == null || string.IsNullOrWhiteSpace(name)) return null;

            IList<FabricationService> services = config.GetAllLoadedServices();
            if (services == null) return null;

            foreach (FabricationService service in services)
            {
                if (service != null && SameText(service.Name, name)) return service;
            }
            return null;
        }

        private static bool SameText(string a, string b)
        {
            return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
