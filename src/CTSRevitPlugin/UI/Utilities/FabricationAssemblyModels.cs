using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.UI.Utilities
{
    [Serializable]
    public class FabricationAssemblyTemplate
    {
        public string Name { get; set; }
        public string Description { get; set; }
        // A read-only report of the modeled reference, not an executable recipe.
        public string CapturedReferencePath { get; set; }
        // True only after the entire modeled reference was resolved, in pick order,
        // to unique service buttons AND unique button conditions by part type.
        public bool CapturedRecipeVerified { get; set; }
        public int DefaultServiceId { get; set; }
        public string DefaultServiceName { get; set; }
        public string Orientation { get; set; }
        // Compatibility: old XML still deserializes; new templates default to AUTO.
        public bool UseAnchorDiameter { get; set; }
        public double DefaultDiameterInches { get; set; }
        public List<FabricationAssemblyPartDefinition> Parts { get; set; }

        public FabricationAssemblyTemplate()
        {
            Name = "New Assembly";
            Description = "";
            Orientation = "Up";
            UseAnchorDiameter = true;
            DefaultDiameterInches = 2.0;
            Parts = new List<FabricationAssemblyPartDefinition>();
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? "Unnamed Assembly" : Name;
        }
    }

    [Serializable]
    public class FabricationAssemblyPartDefinition
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public int ServiceId { get; set; }
        public int PaletteIndex { get; set; }
        public int ButtonIndex { get; set; }
        public int ConditionIndex { get; set; }
        public string ConditionName { get; set; }
        // Read-only reference identity: labels may differ from service BUTTON labels.
        public string ReferenceFamilyName { get; set; }
        public string ReferenceProductSize { get; set; }
        public string ReferenceProductSpecification { get; set; }
        public string ReferenceProductEntryName { get; set; }
        public double ReferenceLengthInches { get; set; }
        // False for v11 legacy templates: clicking a main tile used to save index 0.
        // True only for an explicitly selected ▼ variation, including index 0.
        public bool ConditionExplicit { get; set; }
        public string PaletteName { get; set; }

        public override string ToString()
        {
            return Name ?? "Fabrication Part";
        }
    }

    public sealed class FabricationServiceInfo
    {
        public int ServiceId { get; set; }
        public string Name { get; set; }
        public string Abbreviation { get; set; }

        public override string ToString()
        {
            return Name ?? "Service";
        }
    }

    public sealed class FabricationPaletteInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name ?? "Palette";
        }
    }

    public sealed class FabricationPartConditionInfo
    {
        public int ConditionIndex { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double LowerValue { get; set; }
        public double UpperValue { get; set; }
        public BitmapSource Image { get; set; }

        public string DisplayName
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(Name) ? "Condition " + (ConditionIndex + 1) : Name;
                return name;
            }
        }

        public string RangeText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Description)) return Description;
                if (LowerValue < 0 && UpperValue < 0) return "Unrestricted";
                if (LowerValue < 0) return "< " + ImperialLength.FormatInches(UpperValue);
                if (UpperValue < 0) return "≥ " + ImperialLength.FormatInches(LowerValue);
                return ImperialLength.FormatInches(LowerValue) + " – " + ImperialLength.FormatInches(UpperValue);
            }
        }
    }

    public sealed class FabricationPartButtonInfo
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public int ServiceId { get; set; }
        public int PaletteIndex { get; set; }
        public int ButtonIndex { get; set; }
        public int ConditionCount { get; set; }
        public string PaletteName { get; set; }
        public string ServiceName { get; set; }
        public BitmapSource Image { get; set; }
        public List<FabricationPartConditionInfo> Conditions { get; set; } = new List<FabricationPartConditionInfo>();

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return "Fabrication Part";
                return Name;
            }
        }
    }

    public static class FabricationAssemblyTemplateStore
    {
        private static readonly object Sync = new object();

        private static string FilePath
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CTSRevitPlugin");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "FabricationAssemblies.xml");
            }
        }

        public static List<FabricationAssemblyTemplate> Load()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(FilePath))
                        return CreateDefaults();

                    XmlSerializer serializer = new XmlSerializer(
                        typeof(List<FabricationAssemblyTemplate>));
                    using (FileStream stream = File.OpenRead(FilePath))
                    {
                        List<FabricationAssemblyTemplate> result =
                            serializer.Deserialize(stream) as List<FabricationAssemblyTemplate>;
                        return result ?? CreateDefaults();
                    }
                }
                catch
                {
                    return CreateDefaults();
                }
            }
        }

        public static void Save(List<FabricationAssemblyTemplate> templates)
        {
            lock (Sync)
            {
                string temporary = FilePath + ".tmp";
                XmlSerializer serializer = new XmlSerializer(typeof(List<FabricationAssemblyTemplate>));
                try
                {
                    // Never truncate the only copy before successful XML serialization.
                    using (FileStream stream = File.Create(temporary))
                        serializer.Serialize(stream, templates ?? new List<FabricationAssemblyTemplate>());

                    if (File.Exists(FilePath))
                        File.Replace(temporary, FilePath, FilePath + ".bak");
                    else File.Move(temporary, FilePath);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
        }

        private static List<FabricationAssemblyTemplate> CreateDefaults()
        {
            return new List<FabricationAssemblyTemplate>
            {
                new FabricationAssemblyTemplate
                {
                    Name = "High Point",
                    Description = "Reusable high-point fabrication assembly template.",
                    Orientation = "Up"
                },
                new FabricationAssemblyTemplate
                {
                    Name = "Low Point",
                    Description = "Reusable low-point fabrication assembly template.",
                    Orientation = "Down"
                },
                new FabricationAssemblyTemplate
                {
                    Name = "Pressure Gauge",
                    Description = "Reusable pressure gauge assembly template.",
                    Orientation = "Up"
                },
                new FabricationAssemblyTemplate
                {
                    Name = "Temperature Indicator",
                    Description = "Reusable temperature indicator assembly template.",
                    Orientation = "Up"
                }
            };
        }
    }
}
