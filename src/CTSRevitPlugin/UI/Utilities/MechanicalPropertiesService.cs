using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace CTSRevitPlugin.UI.Utilities
{
    // ================================================================
    // WHAT THIS PANE SHOWS - AND WHY IT IS A SHORT, FIXED LIST
    //
    // The first version walked element.Parameters, called AsValueString on
    // every one of them, then reflected over FabricationPart, RodInfo and
    // HostedInfo. That is several hundred values per click, and the pane
    // stuttered on every selection.
    //
    // It also showed the wrong thing. Everything in the Revit Properties
    // palette is already one click away; what is NOT reachable there is the
    // fabrication data. So the pane now reads a fixed set of sections:
    //
    //   General Part Information .. seven named values
    //   Connectors ............... ConnectorManager, read-only in v1
    //   Dimensions ............... FabricationPart.GetDimensions(), editable
    //   Ancillaries .............. loaded on demand, never on selection
    //   Custom Data .............. the configuration's own custom data list
    //
    // Out of scope for v1 (agreed): User-Defined parameters and Carry Over.
    //
    // The cost that remains is dominated by FabricationConfiguration lookups,
    // so those are cached per document (see FabricationConfigurationCache) and
    // not repeated per click.
    // ================================================================

    public enum MechanicalPropertyKind
    {
        /// <summary>Plain read-only text.</summary>
        Text,

        /// <summary>Revit parameter, written back through the parameter path.</summary>
        Parameter,

        /// <summary>Fabrication dimension, written back with SetDimensionValue.</summary>
        Dimension,

        /// <summary>Row that triggers an on-demand load instead of holding a value.</summary>
        Action
    }

    public sealed class MechanicalPropertyItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Group { get; set; }

        public MechanicalPropertyKind Kind { get; set; }

        /// <summary>
        /// Parameter name for Kind.Parameter, dimension name for Kind.Dimension.
        /// Used as the write-back key, so it is kept separate from the display
        /// label for the cases where the two differ.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// True only when the value can actually be written back. Non-editable
        /// rows render greyed, the way the reference browser does it.
        /// </summary>
        public bool IsEditable { get; set; }

        /// <summary>Fixed set of accepted values, when the configuration supplies one.</summary>
        public List<string> Choices { get; set; }
    }

    /// <summary>
    /// One staged change, carried from the pane to the external event. The pane
    /// collects these while the user types and sends the whole set on APPLY, so
    /// one press is one Revit transaction and one undo step.
    /// </summary>
    public sealed class MechanicalEdit
    {
        /// <summary>Dimension name when IsDimension, otherwise the parameter name.</summary>
        public string Name { get; set; }

        public string Value { get; set; }

        public bool IsDimension { get; set; }
    }

    public sealed class MechanicalPropertiesSnapshot
    {
        /// <summary>Every element the snapshot was built from, in selection order.</summary>
        public List<ElementId> SourceElementIds { get; set; } = new List<ElementId>();

        /// <summary>Convenience for the single-element case.</summary>
        public ElementId SourceElementId
        {
            get { return SourceElementIds.Count == 0 ? null : SourceElementIds[0]; }
        }

        public int ElementCount { get; set; }

        /// <summary>Elements in the selection that the limit left out.</summary>
        public int SkippedByLimit { get; set; }

        public string ElementId { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }

        public List<MechanicalPropertyItem> Items { get; set; } = new List<MechanicalPropertyItem>();
    }

    // ================================================================
    // CONFIGURATION CACHE
    // ================================================================

    /// <summary>
    /// The service list, the status list and the custom data definitions all
    /// come from the loaded Fabrication Configuration and are identical for
    /// every part in the document. Reading them per click was most of the cost
    /// that survived the rest of the rewrite, so they are read once per
    /// document and reused.
    /// </summary>
    internal sealed class FabricationConfigurationCache
    {
        private static FabricationConfigurationCache _current;
        private static string _currentKey;

        private readonly Dictionary<int, string> _serviceNames = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _serviceAbbreviations = new Dictionary<int, string>();

        public FabricationConfiguration Configuration { get; private set; }

        /// <summary>Display names of the configuration's part statuses, or empty.</summary>
        public List<string> StatusNames { get; private set; }

        /// <summary>Display names of the configuration's custom data entries, or empty.</summary>
        public List<string> CustomDataNames { get; private set; }

        /// <summary>Call when the loaded configuration could have changed.</summary>
        public static void Invalidate()
        {
            _current = null;
            _currentKey = null;
        }

        public static FabricationConfigurationCache For(Document doc)
        {
            if (doc == null) return null;

            string key = DocumentKey(doc);
            if (_current != null && string.Equals(_currentKey, key, StringComparison.Ordinal))
                return _current;

            FabricationConfigurationCache cache = new FabricationConfigurationCache();
            cache.Load(doc);

            _current = cache;
            _currentKey = key;
            return cache;
        }

        private static string DocumentKey(Document doc)
        {
            try { return doc.PathName + "|" + doc.Title; }
            catch { return "unknown"; }
        }

        private void Load(Document doc)
        {
            StatusNames = new List<string>();
            CustomDataNames = new List<string>();

            try { Configuration = FabricationConfiguration.GetFabricationConfiguration(doc); }
            catch { Configuration = null; }

            if (Configuration == null) return;

            StatusNames = ReadNameList(
                Configuration, "GetAllPartStatuses", "GetPartStatusCount", "GetPartStatusName");

            CustomDataNames = ReadNameList(
                Configuration, "GetAllPartCustomData", "GetPartCustomDataCount", "GetPartCustomDataName");
        }

        // ------------------------------------------------------------
        // The exact shape of the status / custom-data API differs between
        // Revit releases: some expose a collection, some a count plus an
        // indexed getter. Both shapes are tried through reflection so one DLL
        // keeps working on 2023 through 2026 - the same defensive approach the
        // rest of the fabrication code already uses. This runs once per
        // document, not per click.
        // ------------------------------------------------------------

        private static List<string> ReadNameList(
            object source,
            string collectionMethod,
            string countMethod,
            string nameMethod)
        {
            List<string> names = ReadFromCollection(source, collectionMethod);
            if (names.Count > 0) return names;

            return ReadFromCountAndIndex(source, countMethod, nameMethod);
        }

        private static List<string> ReadFromCollection(object source, string methodName)
        {
            List<string> names = new List<string>();

            try
            {
                MethodInfo method = source.GetType().GetMethod(methodName, Type.EmptyTypes);
                if (method == null) return names;

                System.Collections.IEnumerable items =
                    method.Invoke(source, null) as System.Collections.IEnumerable;

                if (items == null) return names;

                foreach (object item in items)
                {
                    string name = ReadNameProperty(item);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
            }
            catch { names.Clear(); }

            return names;
        }

        private static List<string> ReadFromCountAndIndex(object source, string countMethod, string nameMethod)
        {
            List<string> names = new List<string>();

            try
            {
                MethodInfo count = source.GetType().GetMethod(countMethod, Type.EmptyTypes);
                MethodInfo name = source.GetType().GetMethod(nameMethod, new[] { typeof(int) });
                if (count == null || name == null) return names;

                int total = Convert.ToInt32(count.Invoke(source, null), CultureInfo.InvariantCulture);

                for (int i = 0; i < total; i++)
                {
                    try
                    {
                        string value = name.Invoke(source, new object[] { i }) as string;
                        if (!string.IsNullOrWhiteSpace(value)) names.Add(value);
                    }
                    catch { }
                }
            }
            catch { names.Clear(); }

            return names;
        }

        private static string ReadNameProperty(object item)
        {
            if (item == null) return null;

            string text = item as string;
            if (text != null) return text;

            try
            {
                PropertyInfo property = item.GetType().GetProperty("Name");
                return property == null ? null : property.GetValue(item, null) as string;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------
        // SERVICE LOOKUPS
        // ------------------------------------------------------------

        public string ServiceName(int serviceId)
        {
            string cached;
            if (_serviceNames.TryGetValue(serviceId, out cached)) return cached;

            string name = "";
            try
            {
                FabricationService service = Configuration == null ? null : Configuration.GetService(serviceId);
                if (service != null) name = service.Name ?? "";
            }
            catch { }

            _serviceNames[serviceId] = name;
            return name;
        }

        public string ServiceAbbreviation(int serviceId)
        {
            string cached;
            if (_serviceAbbreviations.TryGetValue(serviceId, out cached)) return cached;

            string abbreviation = "";
            try
            {
                FabricationService service = Configuration == null ? null : Configuration.GetService(serviceId);
                if (service != null)
                {
                    // Not present on every API build, so it is read by name.
                    PropertyInfo property = service.GetType().GetProperty("Abbreviation");
                    if (property != null)
                        abbreviation = property.GetValue(service, null) as string ?? "";
                }
            }
            catch { }

            _serviceAbbreviations[serviceId] = abbreviation;
            return abbreviation;
        }
    }

    // ================================================================
    // INSPECTOR
    // ================================================================

    public static class MechanicalPropertiesInspector
    {
        public const string GroupGeneral = "GENERAL PART INFORMATION";
        public const string GroupConnectors = "CONNECTORS";
        public const string GroupDimensions = "DIMENSIONS";
        public const string GroupAncillaries = "ANCILLARIES";
        public const string GroupCustomData = "CUSTOM DATA";

        public const string AncillariesRowName = "View Fabrication Ancillary Data";

        /// <summary>Shown when the selection disagrees on a value.</summary>
        public const string VariesText = "<varies>";

        /// <summary>
        /// The seven General Part Information values, in display order. Index 0
        /// is the label; the rest are the parameter names to try, in order.
        ///
        /// They are read by name rather than by BuiltInParameter because the
        /// fabrication built-ins are not stable across the four Revit versions
        /// this one DLL targets, whereas the palette labels are. LookupParameter
        /// is a targeted read, so this stays cheap - the point of the rewrite
        /// was never to stop reading parameters, only to stop reading ALL of them.
        /// </summary>
        private static readonly string[][] GeneralFields =
        {
            new[] { "Fitting Type",         "Fitting Type", "Part Type" },
            new[] { "Pattern Number",       "Pattern Number", "Pattern" },
            new[] { "Service Type",         "Fabrication Service Type", "Service Type" },
            new[] { "Service Abbreviation", "Fabrication Service Abbreviation", "Service Abbreviation" },
            new[] { "Service Name",         "Fabrication Service", "Service Name" },
            new[] { "Buy-Out",              "Buy-Out", "Bought Out", "Buy Out" },
            new[] { "Status",               "Status", "Fabrication Status" }
        };

        // ------------------------------------------------------------
        // ENTRY POINTS
        // ------------------------------------------------------------

        public static MechanicalPropertiesSnapshot Inspect(Element element)
        {
            if (element == null) return new MechanicalPropertiesSnapshot();
            return Inspect(new List<Element> { element });
        }

        /// <summary>
        /// With more than one element the pane shows the values the whole
        /// selection agrees on and marks the rest as &lt;varies&gt;. Rows one
        /// element has and another does not are dropped, so whatever is left is
        /// always safe to edit across the selection.
        /// </summary>
        public static MechanicalPropertiesSnapshot Inspect(IList<Element> elements)
        {
            MechanicalPropertiesSnapshot result = new MechanicalPropertiesSnapshot();

            List<FabricationPart> parts = (elements ?? new List<Element>())
                .OfType<FabricationPart>()
                .Where(p => p.IsValidObject)
                .ToList();

            if (parts.Count == 0) return result;

            result.ElementCount = parts.Count;
            result.SourceElementIds = parts.Select(p => p.Id).ToList();

            FabricationConfigurationCache cache =
                FabricationConfigurationCache.For(parts[0].Document);

            List<List<MechanicalPropertyItem>> perElement =
                parts.Select(part => Read(part, cache)).ToList();

            result.Items = Intersect(perElement);

            // Ancillaries never load with the selection: the reference browser
            // puts them behind a button for the same reason - the lookup is the
            // most expensive call in the whole pane.
            result.Items.Add(new MechanicalPropertyItem
            {
                Group = GroupAncillaries,
                Name = AncillariesRowName,
                Value = "...",
                Kind = MechanicalPropertyKind.Action
            });

            result.ElementId = parts.Count == 1
                ? parts[0].Id.IdValue().ToString(CultureInfo.InvariantCulture)
                : parts.Count.ToString(CultureInfo.InvariantCulture) + " selected";

            result.Category = Common(parts, p => p.Category == null ? "-" : p.Category.Name);
            result.Family = Common(parts, GetFamily);
            result.Type = Common(parts, GetTypeName);

            return result;
        }

        // ------------------------------------------------------------
        // ANCILLARIES - ON DEMAND ONLY
        // ------------------------------------------------------------

        /// <summary>
        /// Called when the user opens the Ancillaries row, never from the
        /// selection path. An ancillary is the extra hardware needed to complete
        /// the host element - the nuts, bolts and rods of a hanger.
        /// </summary>
        public static List<MechanicalPropertyItem> ReadAncillaries(Element element)
        {
            List<MechanicalPropertyItem> items = new List<MechanicalPropertyItem>();

            FabricationPart part = element as FabricationPart;
            if (part == null || !part.IsValidObject) return items;

            System.Collections.IEnumerable usage =
                InvokeEnumerable(part, "GetPartAncillaryUsage") ??
                InvokeEnumerable(part, "GetAncillaryUsage");

            if (usage == null)
            {
                items.Add(Note("Ancillary data is not exposed by this Revit API build."));
                return items;
            }

            int index = 0;
            foreach (object entry in usage)
            {
                index++;
                items.Add(new MechanicalPropertyItem
                {
                    Group = GroupAncillaries,
                    Name = ReadString(entry, "Name") ??
                           ("Ancillary " + index.ToString(CultureInfo.InvariantCulture)),
                    Value = DescribeAncillary(entry),
                    Kind = MechanicalPropertyKind.Text
                });
            }

            if (items.Count == 0) items.Add(Note("This part has no ancillaries."));

            return items;
        }

        private static MechanicalPropertyItem Note(string text)
        {
            return new MechanicalPropertyItem
            {
                Group = GroupAncillaries,
                Name = "Ancillary data",
                Value = text,
                Kind = MechanicalPropertyKind.Text
            };
        }

        // ------------------------------------------------------------
        // MULTI-SELECTION
        // ------------------------------------------------------------

        private static string Common(IList<FabricationPart> parts, Func<FabricationPart, string> read)
        {
            string first = null;

            foreach (FabricationPart part in parts)
            {
                string value;
                try { value = read(part) ?? ""; }
                catch { value = ""; }

                if (first == null) { first = value; continue; }
                if (!string.Equals(first, value, StringComparison.Ordinal)) return VariesText;
            }

            return string.IsNullOrWhiteSpace(first) ? "-" : first;
        }

        private static List<MechanicalPropertyItem> Intersect(List<List<MechanicalPropertyItem>> perElement)
        {
            if (perElement.Count == 0) return new List<MechanicalPropertyItem>();
            if (perElement.Count == 1) return perElement[0];

            List<Dictionary<string, MechanicalPropertyItem>> lookups = perElement
                .Select(list => list
                    .GroupBy(RowKey, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal))
                .ToList();

            List<MechanicalPropertyItem> result = new List<MechanicalPropertyItem>();

            foreach (MechanicalPropertyItem template in perElement[0])
            {
                string key = RowKey(template);

                bool presentEverywhere = true;
                bool varies = false;

                for (int i = 1; i < lookups.Count; i++)
                {
                    MechanicalPropertyItem other;
                    if (!lookups[i].TryGetValue(key, out other)) { presentEverywhere = false; break; }
                    if (!string.Equals(other.Value, template.Value, StringComparison.Ordinal)) varies = true;
                }

                if (!presentEverywhere) continue;

                result.Add(new MechanicalPropertyItem
                {
                    Group = template.Group,
                    Name = template.Name,
                    Key = template.Key,
                    Kind = template.Kind,
                    Choices = template.Choices,
                    IsEditable = template.IsEditable,
                    Value = varies ? VariesText : template.Value
                });
            }

            return result;
        }

        private static string RowKey(MechanicalPropertyItem item)
        {
            return item.Group + "\u0001" + item.Name;
        }

        // ------------------------------------------------------------
        // ONE ELEMENT
        // ------------------------------------------------------------

        private static List<MechanicalPropertyItem> Read(FabricationPart part, FabricationConfigurationCache cache)
        {
            List<MechanicalPropertyItem> items = new List<MechanicalPropertyItem>();

            AddGeneral(part, cache, items);
            AddConnectors(part, items);
            AddDimensions(part, items);
            AddCustomData(part, cache, items);

            return items;
        }

        private static void AddGeneral(
            FabricationPart part,
            FabricationConfigurationCache cache,
            List<MechanicalPropertyItem> items)
        {
            foreach (string[] field in GeneralFields)
            {
                string label = field[0];

                Parameter parameter = null;
                for (int i = 1; i < field.Length && parameter == null; i++)
                {
                    try { parameter = part.LookupParameter(field[i]); }
                    catch { parameter = null; }
                }

                string value = parameter == null ? "" : ParameterValue(parameter, part.Document);

                // Service name and abbreviation also live on the configuration.
                // Use that when the parameter is missing or empty, which is the
                // usual case for Service Abbreviation.
                if (string.IsNullOrWhiteSpace(value) && cache != null)
                {
                    if (string.Equals(label, "Service Name", StringComparison.Ordinal))
                        value = Safe(() => cache.ServiceName(part.ServiceId));
                    else if (string.Equals(label, "Service Abbreviation", StringComparison.Ordinal))
                        value = Safe(() => cache.ServiceAbbreviation(part.ServiceId));
                }

                bool editable = parameter != null && IsWritable(parameter);

                List<string> choices = null;
                if (string.Equals(label, "Status", StringComparison.Ordinal) &&
                    cache != null && cache.StatusNames.Count > 0)
                {
                    choices = cache.StatusNames;
                }

                items.Add(new MechanicalPropertyItem
                {
                    Group = GroupGeneral,
                    Name = label,
                    Key = parameter == null ? null : parameter.Definition.Name,
                    Value = value ?? "",
                    Kind = editable ? MechanicalPropertyKind.Parameter : MechanicalPropertyKind.Text,
                    IsEditable = editable,
                    Choices = choices
                });
            }
        }

        // ------------------------------------------------------------
        // CONNECTORS
        //
        // Read-only in v1. Changing a connector needs the configuration's
        // connector definition list and a forced re-read of the dimensions
        // afterwards, which is a separate piece of work.
        // ------------------------------------------------------------

        private static void AddConnectors(FabricationPart part, List<MechanicalPropertyItem> items)
        {
            try
            {
                if (part.ConnectorManager == null) return;

                int index = 0;
                foreach (Connector connector in part.ConnectorManager.Connectors)
                {
                    index++;
                    items.Add(new MechanicalPropertyItem
                    {
                        Group = GroupConnectors,
                        Name = "Connector " + index.ToString(CultureInfo.InvariantCulture),
                        Value = DescribeConnector(part, connector),
                        Kind = MechanicalPropertyKind.Text
                    });
                }
            }
            catch { }
        }

        private static string DescribeConnector(FabricationPart part, Connector connector)
        {
            if (connector == null) return "-";

            // The friendly name ("S&D", "Bead & Slip") lives on the fabrication
            // connector info, which is not exposed the same way on every API
            // build, so it is read by name and falls back to shape and size.
            string name = TryFabricationConnectorName(part, connector);
            if (!string.IsNullOrWhiteSpace(name)) return name;

            List<string> pieces = new List<string>();

            try { pieces.Add(connector.Shape.ToString()); }
            catch { }

            try
            {
                if (connector.Shape == ConnectorProfileType.Round)
                    pieces.Add(ImperialLength.FormatInches(connector.Radius * 2.0 * 12.0));
                else
                    pieces.Add(ImperialLength.FormatInches(connector.Width * 12.0) + " x " +
                               ImperialLength.FormatInches(connector.Height * 12.0));
            }
            catch { }

            try { pieces.Add(connector.IsConnected ? "connected" : "open"); }
            catch { }

            return pieces.Count == 0 ? "-" : string.Join(" - ", pieces.ToArray());
        }

        private static string TryFabricationConnectorName(FabricationPart part, Connector connector)
        {
            try
            {
                MethodInfo method = typeof(FabricationPart).GetMethod(
                    "GetFabricationConnectorInfo", new[] { typeof(Connector) });

                object info = method == null ? null : method.Invoke(part, new object[] { connector });
                if (info == null) return null;

                PropertyInfo property = info.GetType().GetProperty("Name") ??
                                        info.GetType().GetProperty("BodyName");

                return property == null ? null : property.GetValue(info, null) as string;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------
        // DIMENSIONS
        // ------------------------------------------------------------

        private static void AddDimensions(FabricationPart part, List<MechanicalPropertyItem> items)
        {
            try
            {
                foreach (FabricationDimensionDefinition dimension in part.GetDimensions())
                {
                    if (dimension == null) continue;

                    string value = "-";
                    try { value = ImperialLength.FormatInches(part.GetDimensionValue(dimension) * 12.0); }
                    catch { }

                    bool editable = false;
                    try { editable = dimension.IsModifiable; }
                    catch { }

                    items.Add(new MechanicalPropertyItem
                    {
                        Group = GroupDimensions,
                        Name = dimension.Name,
                        Key = dimension.Name,
                        Value = value,
                        Kind = editable ? MechanicalPropertyKind.Dimension : MechanicalPropertyKind.Text,
                        IsEditable = editable
                    });
                }
            }
            catch { }
        }

        // ------------------------------------------------------------
        // CUSTOM DATA
        //
        // The configuration supplies the list of entries; the values are read
        // and written as ordinary Revit parameters, which keeps the write path
        // identical to the rest of the plugin.
        // ------------------------------------------------------------

        private static void AddCustomData(
            FabricationPart part,
            FabricationConfigurationCache cache,
            List<MechanicalPropertyItem> items)
        {
            if (cache == null || cache.CustomDataNames.Count == 0) return;

            foreach (string name in cache.CustomDataNames)
            {
                Parameter parameter = null;
                try { parameter = part.LookupParameter(name); }
                catch { }

                if (parameter == null) continue;

                bool editable = IsWritable(parameter);

                items.Add(new MechanicalPropertyItem
                {
                    Group = GroupCustomData,
                    Name = name,
                    Key = parameter.Definition.Name,
                    Value = ParameterValue(parameter, part.Document),
                    Kind = editable ? MechanicalPropertyKind.Parameter : MechanicalPropertyKind.Text,
                    IsEditable = editable
                });
            }
        }

        // ------------------------------------------------------------
        // SHARED HELPERS
        // ------------------------------------------------------------

        private static string DescribeAncillary(object entry)
        {
            List<string> pieces = new List<string>();

            string quantity = ReadString(entry, "Quantity") ?? ReadString(entry, "Count");
            if (!string.IsNullOrWhiteSpace(quantity)) pieces.Add("qty " + quantity);

            string group = ReadString(entry, "Group") ?? ReadString(entry, "GroupName");
            if (!string.IsNullOrWhiteSpace(group)) pieces.Add(group);

            string description = ReadString(entry, "Description") ?? ReadString(entry, "Comments");
            if (!string.IsNullOrWhiteSpace(description)) pieces.Add(description);

            return pieces.Count == 0 ? "-" : string.Join(" - ", pieces.ToArray());
        }

        private static System.Collections.IEnumerable InvokeEnumerable(object target, string methodName)
        {
            try
            {
                MethodInfo method = target.GetType().GetMethod(methodName, Type.EmptyTypes);
                if (method == null) return null;
                return method.Invoke(target, null) as System.Collections.IEnumerable;
            }
            catch { return null; }
        }

        private static string ReadString(object source, string propertyName)
        {
            if (source == null) return null;

            try
            {
                PropertyInfo property = source.GetType().GetProperty(propertyName);
                if (property == null) return null;

                object value = property.GetValue(source, null);
                if (value == null) return null;

                if (value is double)
                    return ((double)value).ToString("0.###", CultureInfo.InvariantCulture);

                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        private static string Safe(Func<string> read)
        {
            try { return read() ?? ""; }
            catch { return ""; }
        }

        private static bool IsWritable(Parameter p)
        {
            if (p == null || p.IsReadOnly) return false;

            return p.StorageType == StorageType.String ||
                   p.StorageType == StorageType.Double ||
                   p.StorageType == StorageType.Integer;
        }

        private static string ParameterValue(Parameter p, Document doc)
        {
            if (p == null) return "";

            string value = null;
            try { value = p.AsValueString(); }
            catch { }

            if (!string.IsNullOrWhiteSpace(value)) return value;

            try
            {
                if (p.StorageType == StorageType.String)
                    return p.AsString() ?? "";

                if (p.StorageType == StorageType.Integer)
                    return p.AsInteger().ToString(CultureInfo.InvariantCulture);

                if (p.StorageType == StorageType.Double)
                    return p.AsDouble().ToString("0.######", CultureInfo.InvariantCulture);

                if (p.StorageType == StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    if (id == null || id == ElementId.InvalidElementId) return "";

                    Element referenced = doc == null ? null : doc.GetElement(id);
                    return referenced == null
                        ? id.IdValue().ToString(CultureInfo.InvariantCulture)
                        : referenced.Name;
                }
            }
            catch { }

            return "";
        }

        private static string GetFamily(Element e)
        {
            try { return e?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? ""; }
            catch { return ""; }
        }

        private static string GetTypeName(Element e)
        {
            try
            {
                ElementType type = e?.Document.GetElement(e.GetTypeId()) as ElementType;
                return type?.Name ?? "";
            }
            catch { return ""; }
        }
    }
}
