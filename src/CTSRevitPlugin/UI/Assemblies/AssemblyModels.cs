using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Serialization;

namespace CTSRevitPlugin.UI.Assemblies
{
    // ============================================================
    // ENUMS
    // ============================================================

    /// <summary>Where a step takes its size from (only used when the step has no explicit variation).</summary>
    public enum StepSizeSource
    {
        /// <summary>Legacy value from the first version. Treated as Previous.</summary>
        Assembly,
        /// <summary>Same size as the free connector of the previous part (the olet outlet for step 1).</summary>
        Previous,
        /// <summary>A fixed size stored in the step (e.g. 1/2").</summary>
        Fixed
    }

    /// <summary>Which end of the step's part is connected to the previous part.</summary>
    public enum StepConnector
    {
        /// <summary>Try each end and keep the first one Revit accepts without a coupling.</summary>
        Auto,
        First,
        Second
    }

    // ============================================================
    // ITEM REFERENCE (a button of the fabrication database)
    // ============================================================

    /// <summary>
    /// Reference to a button of a fabrication service. Items are stored by NAME
    /// (service / palette / button) because every project can load a different
    /// fabrication database. The indexes are only hints.
    /// </summary>
    public class CatalogItemRef
    {
        /// <summary>
        /// Only a hint of where the item was picked. Assemblies do not depend on a service: at
        /// placement the item is looked up in the service of the selected olet first.
        /// </summary>
        public string ServiceName { get; set; }
        public string PaletteName { get; set; }
        public string ButtonName { get; set; }
        public string ButtonCode { get; set; }
        public int PaletteIndex { get; set; }
        public int ButtonIndex { get; set; }

        /// <summary>
        /// The variation picked with the little arrow of the database button (e.g. "x3").
        /// Null = let Revit choose the variation that fits the size of the connection.
        /// </summary>
        public string ConditionName { get; set; }

        /// <summary>Variations the button had when it was picked (lets the builder offer them offline).</summary>
        public List<string> ConditionNames { get; set; }

        /// <summary>
        /// Product entries seen on this item the last time an assembly using it was
        /// placed (a bushing: 3/4x1/8, 3/4x1/4, ...).
        ///
        /// The list cannot be read in the builder: Revit only exposes a part's
        /// product list once the part exists in the model, and the builder is a
        /// dialog that creates nothing. So it is captured during placement and
        /// stored here, which is what fills the PRODUCT ENTRY dropdown from the
        /// second use of the assembly onwards.
        /// </summary>
        public List<string> ProductEntryNames { get; set; }

        /// <summary>File name of the cached thumbnail inside the icon folder.</summary>
        public string IconFile { get; set; }

        public CatalogItemRef()
        {
            PaletteIndex = -1;
            ButtonIndex = -1;
            ConditionNames = new List<string>();
            ProductEntryNames = new List<string>();
        }

        [XmlIgnore]
        public bool IsAssigned
        {
            get { return !string.IsNullOrWhiteSpace(ButtonName); }
        }

        [XmlIgnore]
        public bool HasVariations
        {
            get { return ConditionNames != null && ConditionNames.Count > 1; }
        }

        /// <summary>Button name plus the chosen variation, e.g. "Nipple [x3]".</summary>
        [XmlIgnore]
        public string FullName
        {
            get
            {
                if (!IsAssigned) return "No item assigned";
                return string.IsNullOrWhiteSpace(ConditionName) ? ButtonName : ButtonName + " [" + ConditionName + "]";
            }
        }

        public CatalogItemRef Clone()
        {
            CatalogItemRef copy = (CatalogItemRef)MemberwiseClone();
            copy.ConditionNames = ConditionNames == null ? new List<string>() : new List<string>(ConditionNames);
            copy.ProductEntryNames = ProductEntryNames == null ? new List<string>() : new List<string>(ProductEntryNames);
            return copy;
        }

        public string Describe()
        {
            if (!IsAssigned) return "No item assigned";
            return (string.IsNullOrWhiteSpace(PaletteName) ? "?" : PaletteName) + "  >  " + FullName;
        }
    }

    // ============================================================
    // STEP
    // ============================================================

    public class AssemblyStep
    {
        /// <summary>Free text label (e.g. "Nipple"). Used as a hint while the item is unassigned.</summary>
        public string Label { get; set; }

        public CatalogItemRef Item { get; set; }

        public StepSizeSource SizeSource { get; set; }

        /// <summary>Used when SizeSource is Fixed. Imperial text, e.g. 3/4" or 1-1/2".</summary>
        public string FixedSize { get; set; }

        public StepConnector Connector { get; set; }

        /// <summary>Rotation around the connection axis (valve handle orientation, etc.).</summary>
        public double RotationDegrees { get; set; }

        /// <summary>
        /// Exact product entry to force on the placed part, as Revit spells it in
        /// the Properties palette (a bushing: 3/4x1/8). Empty = leave whatever
        /// Revit picks.
        ///
        /// This matters for reducing parts: the entry decides the SMALL end, and
        /// the small end is what the next step has to connect to. Without it a
        /// bushing can come out 3/4x1/2 when the assembly needed 3/4x1/8, and the
        /// step after it fails or silently gets a coupling.
        /// </summary>
        public string ProductEntry { get; set; }

        public AssemblyStep()
        {
            Item = new CatalogItemRef();
            SizeSource = StepSizeSource.Previous;
            FixedSize = "3/4\"";
            Connector = StepConnector.Auto;
        }

        [XmlIgnore]
        public string DisplayName
        {
            get
            {
                if (Item != null && Item.IsAssigned) return Item.FullName;
                return string.IsNullOrWhiteSpace(Label) ? "Unassigned" : Label;
            }
        }

        public AssemblyStep Clone()
        {
            return new AssemblyStep
            {
                Label = Label,
                Item = Item == null ? new CatalogItemRef() : Item.Clone(),
                SizeSource = SizeSource,
                FixedSize = FixedSize,
                Connector = Connector,
                RotationDegrees = RotationDegrees,
                ProductEntry = ProductEntry
            };
        }
    }

    // ============================================================
    // ASSEMBLY
    // ============================================================

    public class AssemblyDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Up to three letters shown on the tile (HP, LP, PG...).</summary>
        public string ShortName { get; set; }

        /// <summary>Items in the order they are modeled, starting at the free end of the olet.</summary>
        public List<AssemblyStep> Steps { get; set; }

        public AssemblyDefinition()
        {
            Id = Guid.NewGuid().ToString("N");
            Steps = new List<AssemblyStep>();
        }

        [XmlIgnore]
        public int UnassignedCount
        {
            get
            {
                if (Steps == null) return 0;
                return Steps.Count(s => s == null || s.Item == null || !s.Item.IsAssigned);
            }
        }

        public AssemblyDefinition Clone()
        {
            var copy = new AssemblyDefinition
            {
                Id = Id,
                Name = Name,
                ShortName = ShortName,
                Steps = new List<AssemblyStep>()
            };
            if (Steps != null)
            {
                foreach (AssemblyStep step in Steps)
                    copy.Steps.Add(step == null ? new AssemblyStep() : step.Clone());
            }
            return copy;
        }
    }

    public class AssemblyLibrary
    {
        /// <summary>1 = first version (olet was step 1), 2 = the olet is the user's input.</summary>
        public int Version { get; set; }

        public List<AssemblyDefinition> Assemblies { get; set; }
        public string SelectedId { get; set; }

        public AssemblyLibrary()
        {
            Assemblies = new List<AssemblyDefinition>();
        }
    }

    // ============================================================
    // IMPERIAL SIZE HELPERS
    // ============================================================

    public static class AssemblyUnits
    {
        /// <summary>
        /// Parses sizes such as 3/4", 1-1/2", 1 1/2, 0.5, 2, 1' 6".
        /// A bare number is read as inches.
        /// </summary>
        public static bool TryParseInches(string input, out double inches)
        {
            inches = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string s = input.Trim()
                .Replace('\u201D', '"')
                .Replace('\u2033', '"')
                .Replace('\u2019', '\'')
                .Replace('\u2032', '\'');

            try
            {
                double feet = 0;
                int quote = s.IndexOf('\'');
                if (quote >= 0)
                {
                    string feetText = s.Substring(0, quote).Trim();
                    if (feetText.Length > 0)
                        feet = double.Parse(feetText, CultureInfo.InvariantCulture);
                    s = s.Substring(quote + 1);
                }

                s = s.Replace("\"", " ").Replace("-", " ").Trim();
                if (s.EndsWith("in", StringComparison.OrdinalIgnoreCase))
                    s = s.Substring(0, s.Length - 2).Trim();

                double total = 0;
                bool any = false;
                foreach (string part in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Contains("/"))
                    {
                        string[] fraction = part.Split('/');
                        if (fraction.Length != 2) return false;
                        double numerator = double.Parse(fraction[0], CultureInfo.InvariantCulture);
                        double denominator = double.Parse(fraction[1], CultureInfo.InvariantCulture);
                        if (Math.Abs(denominator) < 1e-12) return false;
                        total += numerator / denominator;
                    }
                    else
                    {
                        total += double.Parse(part, CultureInfo.InvariantCulture);
                    }
                    any = true;
                }

                if (!any && quote < 0) return false;
                inches = feet * 12.0 + total;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Formats inches as 3/4", 1-1/2", 2" (nearest 1/16).</summary>
        public static string FormatInches(double inches)
        {
            if (inches <= 0) return "0\"";

            double rounded = Math.Round(inches * 16.0) / 16.0;
            int whole = (int)Math.Floor(rounded + 1e-9);
            int sixteenths = (int)Math.Round((rounded - whole) * 16.0);
            if (sixteenths == 16) { whole++; sixteenths = 0; }

            if (sixteenths == 0) return whole + "\"";

            int numerator = sixteenths;
            int denominator = 16;
            while (numerator % 2 == 0)
            {
                numerator /= 2;
                denominator /= 2;
            }

            string fraction = numerator + "/" + denominator;
            return whole > 0 ? whole + "-" + fraction + "\"" : fraction + "\"";
        }
    }
}
