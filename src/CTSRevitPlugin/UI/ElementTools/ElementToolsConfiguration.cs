using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using Autodesk.Revit.UI;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.UI.ElementTools
{
    [DataContract]
    [XmlRoot("ElementToolsConfiguration")]
    public class ElementToolsConfiguration
    {
        [DataMember]
        public bool ShowParameters { get; set; } = true;

        [DataMember]
        public bool ShowShortcuts { get; set; } = true;

        [DataMember]
        public bool ShowHangers { get; set; } = true;

        [DataMember]
        public List<string> PinnedParameters { get; set; }
            = new List<string>();

        [DataMember]
        public List<string> ShortcutTools { get; set; }
            = new List<string>();

        [DataMember]
        public List<string> HangerTools { get; set; }
            = new List<string>();

        [DataMember]
        public bool ShowHangerRodAdjustment { get; set; } = true;

        /// <summary>
        /// Property names the user starred in CTS Mechanical Properties. They are
        /// listed in a FAVORITES group at the top of that pane. Kept separate from
        /// PinnedParameters so starring something here does not silently add a row
        /// to the CTS Parameters pane.
        /// </summary>
        [DataMember]
        public List<string> FavoriteMechanicalProperties { get; set; }
            = new List<string>();

        /// <summary>
        /// How many elements CTS Mechanical Properties reads from one selection.
        ///
        /// Reading fabrication data is not free even with a fixed field list, so a
        /// large selection is capped instead of being allowed to freeze Revit.
        /// Anything beyond the cap is reported in the pane's status line rather
        /// than silently dropped. 0 or less falls back to the default.
        /// </summary>
        [DataMember]
        public int MechanicalPropertiesElementLimit { get; set; } = 100;

        [DataMember]
        public string HangerRodAdjustmentDefault { get; set; }
            = "2\"";


        // ============================================================
        // LEGACY COMPATIBILITY
        // ============================================================

        [DataMember]
        public bool ShowElementId { get; set; } = true;

        [DataMember]
        public bool ShowMark { get; set; } = true;

        [DataMember]
        public bool ShowCategory { get; set; } = true;

        [DataMember]
        public bool ShowFamily { get; set; } = true;

        [DataMember]
        public bool ShowType { get; set; } = true;

        [DataMember]
        public List<string> EnabledTools { get; set; }
            = new List<string>();


        // ============================================================
        // XML SETTINGS
        // ============================================================

        [XmlIgnore]
        public static string SettingsPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CTSRevitPlugin", "ElementToolsSettings.xml");
            }
        }


        // ============================================================
        // LOAD
        // ============================================================

        public static ElementToolsConfiguration Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    var configuration =
                        new ElementToolsConfiguration();

                    configuration.Save();

                    return configuration;
                }

                var serializer =
                    new XmlSerializer(
                        typeof(ElementToolsConfiguration));

                using (var stream =
                    File.OpenRead(SettingsPath))
                {
                    var configuration =
                        serializer.Deserialize(stream)
                        as ElementToolsConfiguration;

                    if (configuration == null)
                    {
                        configuration =
                            new ElementToolsConfiguration();

                        configuration.Save();

                        return configuration;
                    }

                    // ------------------------------------------------
                    // SAFETY CHECKS
                    // ------------------------------------------------

                    if (configuration.PinnedParameters == null)
                    {
                        configuration.PinnedParameters =
                            new List<string>();
                    }

                    if (configuration.ShortcutTools == null)
                    {
                        configuration.ShortcutTools =
                            new List<string>();
                    }

                    if (configuration.HangerTools == null)
                    {
                        configuration.HangerTools =
                            new List<string>();
                    }

                    if (configuration.EnabledTools == null)
                    {
                        configuration.EnabledTools =
                            new List<string>();
                    }

                    return configuration;
                }
            }
            catch (Exception ex)
            {
                CtsMessageWindow.Show(
                    "CTS Revit Plugin - Settings Error",
                    "Could not load Element Tools settings.\n\n" +
                    "Path:\n" +
                    SettingsPath +
                    "\n\nError:\n" +
                    ex.Message);

                return new ElementToolsConfiguration();
            }
        }


        // ============================================================
        // SAVE
        // ============================================================

        public void Save()
        {
            try
            {
                string directory =
                    Path.GetDirectoryName(SettingsPath);

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var serializer =
                    new XmlSerializer(
                        typeof(ElementToolsConfiguration));

                using (var stream =
                    File.Create(SettingsPath))
                {
                    serializer.Serialize(
                        stream,
                        this);
                }
            }
            catch (Exception ex)
            {
                CtsMessageWindow.Show(
                    "CTS Revit Plugin - Settings Error",
                    "Could not save Element Tools settings.\n\n" +
                    "Path:\n" +
                    SettingsPath +
                    "\n\nError:\n" +
                    ex.Message);
            }
        }
    }
}